namespace Tropico.SaveParser;

public sealed class T6SaveFile
{
    public required T6SaveHeader Header { get; init; }
    public required byte[] DecompressedData { get; init; }
    public required T6NameTable NameTable { get; init; }
    public required T6ObjectTable ObjectTable { get; init; }
}
