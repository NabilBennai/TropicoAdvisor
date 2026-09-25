namespace Tropico.SaveParser;

/// <summary>Decoded value of a tagged property.</summary>
public abstract record T6Value;

public sealed record T6BoolValue(bool Value) : T6Value;

/// <summary>Int8/Int/UInt32/Int64 properties.</summary>
public sealed record T6IntegerValue(long Value) : T6Value;

/// <summary>Float/Double properties.</summary>
public sealed record T6FloatValue(double Value) : T6Value;

public sealed record T6StringValue(string Value) : T6Value;

/// <summary>Enum, Name and Byte-as-name values, e.g. <c>ET6AgentLifeState::Adult</c>.</summary>
public sealed record T6NameValue(string Value) : T6Value;

/// <summary>Reference to an object-table index (may be negative for "none"-like values).</summary>
public sealed record T6ObjectRefValue(int ObjectIndex) : T6Value;

public sealed record T6ArrayValue(string ElementType, IReadOnlyList<T6Value> Items) : T6Value;

/// <summary>Struct decoded as nested tagged properties. <see cref="Trailing"/> holds non-zero unparsed bytes, if any.</summary>
public sealed record T6StructValue(string StructName, IReadOnlyList<T6Property> Properties, byte[]? Trailing = null) : T6Value
{
    public T6Property? Find(string name) => Properties.FirstOrDefault(p => p.Name == name);
}

/// <summary>Value left undecoded (unknown type, unsupported layout or out-of-bounds): raw bytes are kept.</summary>
public sealed record T6RawValue(string Type, byte[] Bytes) : T6Value;

public sealed record T6Property(
    string Name,
    string Type,
    IReadOnlyList<string> ExtraTypes,
    int ArrayIndex,
    int Offset,
    int ValueOffset,
    int Size,
    T6Value Value);

/// <summary>Placed-actor transform read from the 24-byte blob preamble. Yaw (float at +16) is unverified.</summary>
public sealed record T6Transform(float X, float Y, float Z, float Yaw);

public enum T6ObjectDataStatus
{
    /// <summary>The record has no blob (kind 3 class references).</summary>
    NoBlob,

    /// <summary>The whole blob parsed as tagged properties.</summary>
    Complete,

    /// <summary>Parsed after skipping undecodable native byte ranges (see <see cref="T6ObjectData.Gaps"/>). Heuristic.</summary>
    Resynchronized,

    /// <summary>Only the properties before the first undecodable byte were read (see <see cref="T6ObjectData.StopOffset"/>).</summary>
    Partial,

    /// <summary>Nothing could be decoded.</summary>
    Failed,
}

public sealed class T6ObjectData
{
    public required T6ObjectRecord Record { get; init; }
    public required T6ObjectDataStatus Status { get; init; }
    public IReadOnlyList<T6Property> Properties { get; init; } = [];
    public T6Transform? Transform { get; init; }

    /// <summary>Skipped native ranges [start, end) in decompressed-data offsets (Resynchronized only).</summary>
    public IReadOnlyList<(int Start, int End)> Gaps { get; init; } = [];

    /// <summary>Decompressed-data offset where parsing stopped (Partial only).</summary>
    public int? StopOffset { get; init; }

    /// <summary>First property with this name (array elements share a name and differ by ArrayIndex).</summary>
    public T6Property? Find(string name, int arrayIndex = 0) =>
        Properties.FirstOrDefault(p => p.Name == name && p.ArrayIndex == arrayIndex);
}
