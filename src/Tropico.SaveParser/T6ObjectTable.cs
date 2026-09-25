using System.Text;

namespace Tropico.SaveParser;

public sealed class T6ObjectRecord
{
    public required int Index { get; init; }

    /// <summary>1/2 = object with a path and (usually) a blob; 3 = class/data-asset reference ("#/Game/...") without blob.</summary>
    public required byte Kind { get; init; }
    public required string Path { get; init; }

    /// <summary>1 = component/sub-object, 2 = placed actor blueprint, 4/8 = singleton managers (meaning partly unverified).</summary>
    public byte? Flag { get; init; }

    /// <summary>Offset of the blob relative to <see cref="T6ObjectTable.BlobBaseOffset"/>.</summary>
    public uint? BlobOffset { get; init; }

    /// <summary>Index of the owning object, when the record carries one (9-byte tail).</summary>
    public int? OwnerIndex { get; init; }

    public bool HasBlob => BlobOffset.HasValue;

    /// <summary>Last segment of the path after the final '/' or '.', e.g. <c>BP_T6Bunkhouse_C</c> or <c>T6Agent</c>.</summary>
    public string ShortName => Path[(Path.LastIndexOfAny(['/', '.']) + 1)..];
}

public sealed class T6ObjectTable
{
    public required IReadOnlyList<T6ObjectRecord> Objects { get; init; }

    /// <summary>Offset (in the decompressed data) of the first blob: the end of the object table.</summary>
    public required int BlobBaseOffset { get; init; }

    /// <summary>
    /// Record: u8 kind | i32 length (incl. NUL) | path\0 | tail, where tail is 0 bytes (kind 3), 5 bytes (u8 flag, u32 blobOffset)
    /// or 9 bytes (flag, blobOffset, i32 ownerIndex). The tail length is not stored, so it is resolved by checking which
    /// length lands on the start of the next record (see <see cref="ResolveTailLength"/>).
    /// </summary>
    public static T6ObjectTable Parse(byte[] data, int offset)
    {
        if (offset + 4 > data.Length) throw new InvalidDataException("Decompressed data too short for an object table.");

        var count = BitConverter.ToUInt32(data, offset);
        if (count > data.Length / 6) throw new InvalidDataException($"Implausible object count: {count}.");

        var objects = new List<T6ObjectRecord>((int)count);
        var pos = offset + 4;

        for (var i = 0; i < count; i++)
        {
            var pathLength = TryReadRecordHeader(data, pos)
                ?? throw new InvalidDataException($"Object #{i}: no valid record header at 0x{pos:X}.");

            var kind = data[pos];
            var path = Encoding.Latin1.GetString(data, pos + 5, pathLength - 1);
            var tailStart = pos + 5 + pathLength;
            var tailLength = ResolveTailLength(data, tailStart, isLast: i == count - 1)
                ?? throw new InvalidDataException($"Object #{i}: cannot resolve tail length at 0x{tailStart:X}.");

            objects.Add(new T6ObjectRecord
            {
                Index = i,
                Kind = kind,
                Path = path,
                Flag = tailLength >= 5 ? data[tailStart] : null,
                BlobOffset = tailLength >= 5 ? BitConverter.ToUInt32(data, tailStart + 1) : null,
                OwnerIndex = tailLength == 9 ? BitConverter.ToInt32(data, tailStart + 5) : null,
            });

            pos = tailStart + tailLength;
        }

        return new T6ObjectTable { Objects = objects, BlobBaseOffset = pos };
    }

    // Resynchronisation: try tail lengths 0, 5, 9 and keep the first one after which a plausible record header follows.
    // The last record has no successor: 9 if its flag byte is 1 or 4, else 5 (observed heuristic, verified on 2 saves).
    private static int? ResolveTailLength(byte[] data, int tailStart, bool isLast)
    {
        if (isLast) return tailStart < data.Length && data[tailStart] is 1 or 4 ? 9 : 5;

        foreach (var length in (ReadOnlySpan<int>)[0, 5, 9])
        {
            if (TryReadRecordHeader(data, tailStart + length) is not null) return length;
        }

        return null;
    }

    /// <returns>The path length (including NUL) when a plausible record starts at <paramref name="pos"/>, otherwise null.</returns>
    private static int? TryReadRecordHeader(byte[] data, int pos)
    {
        if (pos + 6 > data.Length || data[pos] is not (1 or 2 or 3)) return null;

        var length = BitConverter.ToInt32(data, pos + 1);
        if (length is <= 3 or >= 400 || pos + 5 + length > data.Length || data[pos + 4 + length] != 0) return null;

        for (var k = pos + 5; k < pos + 4 + length; k++)
        {
            if (data[k] is < 0x20 or > 0x7E) return null;
        }

        return length;
    }
}
