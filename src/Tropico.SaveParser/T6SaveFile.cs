namespace Tropico.SaveParser;

public sealed class T6SaveFile
{
    public required T6SaveHeader Header { get; init; }
    public required byte[] DecompressedData { get; init; }
}
