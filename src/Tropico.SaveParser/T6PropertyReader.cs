using System.Text;

namespace Tropico.SaveParser;

/// <summary>
/// Reads the blob of an object as Unreal-style tagged properties.
/// Tag: FName name (u32 index, u32 number) | FName type | u32 size | u32 arrayIndex | extra FNames (Array/Struct/Enum/Set/Byte: 1, Map: 2)
///      | u8 value (BoolProperty only) | u8 guid flag | value[size].
/// A "None" FName ends a section; further sections may follow (super-class data). Placed actors start with a 24-byte transform preamble.
/// Only non-default values are serialized: an absent property means its default, not "unknown".
/// </summary>
public sealed class T6PropertyReader
{
    private const int ActorPreambleSize = 24;
    private const int StructArrayHeaderSize = 8 + 8 + 4 + 4 + 8 + 16 + 1; // name, type, size, index, struct name, guid, flag

    private static readonly HashSet<string> KnownTypes =
    [
        "IntProperty", "FloatProperty", "ObjectProperty", "BoolProperty", "EnumProperty", "ArrayProperty", "StructProperty",
        "ByteProperty", "DoubleProperty", "NameProperty", "StrProperty", "MapProperty", "SetProperty", "UInt32Property",
        "Int64Property", "TextProperty", "Int8Property",
    ];

    private enum WalkStatus { Ok, Incomplete, BadName, BadType, Truncated, Overrun }

    private sealed record WalkResult(List<T6Property> Properties, int End, WalkStatus Status, List<(int Start, int End)> Gaps);

    private readonly byte[] _data;
    private readonly IReadOnlyList<string> _names;
    private readonly int _blobBase;
    private readonly Dictionary<uint, int> _blobEnds = [];

    public T6PropertyReader(byte[] data, T6NameTable nameTable, T6ObjectTable objectTable)
    {
        _data = data;
        _names = nameTable.Names;
        _blobBase = objectTable.BlobBaseOffset;

        // A blob ends where the next distinct blob offset starts; the last one ends at the end of the data.
        var offsets = objectTable.Objects.Where(o => o.HasBlob).Select(o => o.BlobOffset!.Value).Distinct().Order().ToArray();
        for (var i = 0; i < offsets.Length; i++)
        {
            _blobEnds[offsets[i]] = i + 1 < offsets.Length ? (int)offsets[i + 1] : data.Length - _blobBase;
        }
    }

    public T6ObjectData Read(T6ObjectRecord record)
    {
        if (!record.HasBlob) return new T6ObjectData { Record = record, Status = T6ObjectDataStatus.NoBlob };

        var start = _blobBase + (int)record.BlobOffset!.Value;
        var end = _blobBase + _blobEnds[record.BlobOffset.Value];

        var isActor = record.Path.StartsWith("/Game/", StringComparison.Ordinal) && record.Kind is 1 or 2 && record.Flag is 2 or 4 or 8;
        int[] skips = isActor ? [ActorPreambleSize, 0] : [0, ActorPreambleSize];

        // Strict pass first; resynchronisation (heuristic, last resort) only if the strict pass fails everywhere.
        foreach (var resync in (ReadOnlySpan<bool>)[false, true])
        {
            foreach (var skip in skips)
            {
                var walk = Walk(start + skip, end, resync);
                if (walk.Status != WalkStatus.Ok) continue;

                return new T6ObjectData
                {
                    Record = record,
                    Status = walk.Gaps.Count > 0 ? T6ObjectDataStatus.Resynchronized : T6ObjectDataStatus.Complete,
                    Properties = walk.Properties,
                    Transform = ReadTransform(start, skip),
                    Gaps = walk.Gaps,
                };
            }
        }

        // Partial: keep the properties read before the first undecodable byte (agents carry a native, non-tagged segment).
        WalkResult? best = null;
        var bestSkip = 0;
        foreach (var skip in skips)
        {
            var walk = Walk(start + skip, end, resync: false);
            if (walk.Properties.Count > 0 && (best is null || walk.Properties.Count > best.Properties.Count))
            {
                best = walk;
                bestSkip = skip;
            }
        }

        return best is null
            ? new T6ObjectData { Record = record, Status = T6ObjectDataStatus.Failed }
            : new T6ObjectData
            {
                Record = record,
                Status = T6ObjectDataStatus.Partial,
                Properties = best.Properties,
                Transform = ReadTransform(start, bestSkip),
                StopOffset = best.End,
            };
    }

    private T6Transform? ReadTransform(int start, int skip) => skip == 0
        ? null
        : new T6Transform(
            BitConverter.ToSingle(_data, start), BitConverter.ToSingle(_data, start + 4),
            BitConverter.ToSingle(_data, start + 8), BitConverter.ToSingle(_data, start + 16));

    // ---- tag walker ---------------------------------------------------------------------------------------------

    private WalkResult Walk(int p, int end, bool resync, bool stopAtNone = false)
    {
        var props = new List<T6Property>();
        var gaps = new List<(int, int)>();

        while (p + 8 <= end)
        {
            var nameIndex = U32(p);

            if (resync && (nameIndex >= _names.Count || (nameIndex != 0 && !TagLooksValid(p, end))))
            {
                var q = p + 1;
                while (q < end - 24 && !(TagLooksValid(q, end) && ChainLooksValid(q, end))) q++;
                if (q >= end - 24) return new WalkResult(props, p, WalkStatus.BadName, gaps);

                gaps.Add((p, q));
                p = q;
                nameIndex = U32(p);
            }
            else if (nameIndex >= _names.Count)
            {
                return new WalkResult(props, p, WalkStatus.BadName, gaps);
            }

            var name = _names[(int)nameIndex];
            if (name == "None")
            {
                p += 8;
                if (stopAtNone) return new WalkResult(props, p, WalkStatus.Ok, gaps);
                continue;
            }

            if (p + 24 > end) return new WalkResult(props, p, WalkStatus.Truncated, gaps);

            var typeIndex = U32(p + 8);
            if (typeIndex >= _names.Count) return new WalkResult(props, p, WalkStatus.BadType, gaps);

            var type = _names[(int)typeIndex];
            var size = U32(p + 16);
            var arrayIndex = (int)U32(p + 20);
            var q2 = p + 24;

            var extras = new List<string>();
            for (var k = 0; k < ExtraCount(type); k++)
            {
                if (q2 + 8 > end || U32(q2) >= _names.Count) return new WalkResult(props, p, WalkStatus.BadType, gaps);
                extras.Add(_names[(int)U32(q2)]);
                q2 += 8;
            }

            var boolValue = false;
            if (type == "BoolProperty")
            {
                if (q2 >= end) return new WalkResult(props, p, WalkStatus.Truncated, gaps);
                boolValue = _data[q2++] != 0;
            }

            q2++; // property-guid flag
            if ((long)q2 + size > end) return new WalkResult(props, p, WalkStatus.Overrun, gaps);

            props.Add(new T6Property(name, type, extras, arrayIndex, p, q2, (int)size,
                DecodeValue(type, extras, q2, (int)size, boolValue)));
            p = q2 + (int)size;
        }

        return new WalkResult(props, p, p == end ? WalkStatus.Ok : WalkStatus.Incomplete, gaps);
    }

    private static int ExtraCount(string type) => type switch
    {
        "ArrayProperty" or "StructProperty" or "EnumProperty" or "SetProperty" or "ByteProperty" => 1,
        "MapProperty" => 2,
        _ => 0,
    };

    // Resync only: does a plausible tag start here?
    private bool TagLooksValid(int p, int end)
    {
        if (p + 25 > end) return false;

        var (name, nameNumber, type, typeNumber, size, arrayIndex) = (U32(p), U32(p + 4), U32(p + 8), U32(p + 12), U32(p + 16), U32(p + 20));
        if (name == 0 || name >= _names.Count || nameNumber >= 64 || type >= _names.Count || typeNumber != 0 || arrayIndex >= 64) return false;

        return KnownTypes.Contains(_names[(int)type]) && size < end - p;
    }

    // Resync only: the candidate tag must be followed by another plausible tag, a None, or the exact end.
    private bool ChainLooksValid(int q, int end)
    {
        var type = _names[(int)U32(q + 8)];
        var next = (long)q + 24 + 8 * ExtraCount(type) + 1 + (type == "BoolProperty" ? 1 : 0) + U32(q + 16);
        if (next + 8 > end) return next + 8 == end;

        return U32((int)next) == 0 || TagLooksValid((int)next, end);
    }

    // ---- value decoding -----------------------------------------------------------------------------------------

    private T6Value DecodeValue(string type, IReadOnlyList<string> extras, int at, int size, bool boolValue)
    {
        try
        {
            switch (type)
            {
                case "BoolProperty": return new T6BoolValue(boolValue);
                case "IntProperty": return new T6IntegerValue(BitConverter.ToInt32(_data, at));
                case "UInt32Property": return new T6IntegerValue(BitConverter.ToUInt32(_data, at));
                case "Int64Property": return new T6IntegerValue(BitConverter.ToInt64(_data, at));
                case "Int8Property": return new T6IntegerValue((sbyte)_data[at]);
                case "FloatProperty": return new T6FloatValue(BitConverter.ToSingle(_data, at));
                case "DoubleProperty": return new T6FloatValue(BitConverter.ToDouble(_data, at));
                case "ObjectProperty": return new T6ObjectRefValue(BitConverter.ToInt32(_data, at));
                case "EnumProperty" or "NameProperty": return new T6NameValue(NameAt(at));
                case "ByteProperty": return size == 1 ? new T6IntegerValue(_data[at]) : new T6NameValue(NameAt(at));
                case "StrProperty": return new T6StringValue(ReadString(at));
                case "ArrayProperty": return DecodeArray(extras[0], at, size);
                case "StructProperty": return DecodeStruct(extras[0], at, size);
            }
        }
        catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException)
        {
            // fall through to raw
        }

        return Raw(type, at, size);
    }

    private T6RawValue Raw(string type, int at, int size) => new(type, _data.AsSpan(at, Math.Min(size, _data.Length - at)).ToArray());

    private string NameAt(int at)
    {
        var index = U32(at);
        return index < _names.Count ? _names[(int)index] : $"<name#{index}>";
    }

    private string ReadString(int at)
    {
        var length = BitConverter.ToInt32(_data, at);
        return length switch
        {
            0 => "",
            > 0 => Encoding.Latin1.GetString(_data, at + 4, length - 1),
            _ => Encoding.Unicode.GetString(_data, at + 4, (-length - 1) * 2),
        };
    }

    private T6Value DecodeStruct(string structName, int at, int size)
    {
        var walk = Walk(at, at + size, resync: false);
        var remaining = at + size - walk.End;

        // Structs are None-prefixed property lists without a terminator; a small unparsed tail is kept as raw bytes.
        if (walk.Properties.Count > 0 && remaining < 64 && walk.Status is WalkStatus.Ok or WalkStatus.Incomplete or WalkStatus.Truncated)
        {
            var trailing = _data.AsSpan(walk.End, remaining);
            return new T6StructValue(structName, walk.Properties, trailing.IndexOfAnyExcept((byte)0) >= 0 ? trailing.ToArray() : null);
        }

        return Raw("StructProperty:" + structName, at, size);
    }

    private T6Value DecodeArray(string inner, int at, int size)
    {
        var count = (int)U32(at);
        var items = new List<T6Value>();

        var width = inner switch
        {
            "ObjectProperty" or "IntProperty" or "FloatProperty" or "UInt32Property" => 4,
            "DoubleProperty" => 8,
            "BoolProperty" or "Int8Property" => 1,
            "EnumProperty" or "NameProperty" or "ByteProperty" => 8,
            _ => 0,
        };

        if (width > 0 && 4 + (long)count * width == size)
        {
            for (var k = 0; k < count; k++)
            {
                var p = at + 4 + k * width;
                items.Add(inner switch
                {
                    "ObjectProperty" => new T6ObjectRefValue(BitConverter.ToInt32(_data, p)),
                    "IntProperty" => new T6IntegerValue(BitConverter.ToInt32(_data, p)),
                    "UInt32Property" => new T6IntegerValue(BitConverter.ToUInt32(_data, p)),
                    "FloatProperty" => new T6FloatValue(BitConverter.ToSingle(_data, p)),
                    "DoubleProperty" => new T6FloatValue(BitConverter.ToDouble(_data, p)),
                    "BoolProperty" => new T6BoolValue(_data[p] != 0),
                    "Int8Property" => new T6IntegerValue((sbyte)_data[p]),
                    _ => new T6NameValue(NameAt(p)),
                });
            }

            return new T6ArrayValue(inner, items);
        }

        // Array of structs: count, one header tag, then `count` None-terminated property lists.
        if (inner == "StructProperty" && at + 4 + StructArrayHeaderSize <= at + size)
        {
            var structName = NameAt(at + 4 + 8 + 8 + 4 + 4);
            var p = at + 4 + StructArrayHeaderSize;
            for (var k = 0; k < count; k++)
            {
                var walk = Walk(p, at + size, resync: false, stopAtNone: true);
                if (walk.Status != WalkStatus.Ok) return Raw("ArrayProperty:StructProperty", at, size);

                items.Add(new T6StructValue(structName, walk.Properties));
                p = walk.End;
            }

            if (p == at + size) return new T6ArrayValue(inner, items);
        }

        return Raw("ArrayProperty:" + inner, at, size);
    }

    private uint U32(int at) => BitConverter.ToUInt32(_data, at);
}
