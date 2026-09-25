namespace Tropico.SaveParser;

public sealed class T6SaveFile
{
    public required T6SaveHeader Header { get; init; }
    public required byte[] DecompressedData { get; init; }
    public required T6NameTable NameTable { get; init; }
    public required T6ObjectTable ObjectTable { get; init; }

    private T6PropertyReader? _propertyReader;

    /// <summary>Decodes the property blob of an object. Never throws for undecodable blobs: see <see cref="T6ObjectData.Status"/>.</summary>
    public T6ObjectData ReadObject(T6ObjectRecord record) =>
        (_propertyReader ??= new T6PropertyReader(DecompressedData, NameTable, ObjectTable)).Read(record);
}
