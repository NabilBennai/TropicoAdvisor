namespace Tropico.SaveParser;

public sealed class T6SaveHeader
{
    public required string Build { get; init; }
    public string? MapId { get; init; }
    public string? SaveName { get; init; }
    public string? SteamId { get; init; }

    /// <summary>Offset of the zlib stream in the .t6sav file (equals the header length).</summary>
    public int CompressedDataOffset { get; init; }
}
