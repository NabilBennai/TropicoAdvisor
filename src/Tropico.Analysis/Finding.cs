namespace Tropico.Analysis;

public enum Severity { Info, Warning, Critical }

/// <summary>How much the underlying save data can be trusted (see the reverse-engineering report).</summary>
public enum Confidence { Uncertain, Probable, Verified }

/// <summary>
/// One observation about the island. <see cref="Suggestion"/> is a general lead, not a precise instruction;
/// <see cref="Evidence"/> lists the figures the finding is based on.
/// </summary>
public sealed record Finding(string Code, Severity Severity, Confidence Confidence, string Message)
{
    public string Category { get; init; } = "General";
    public string? Suggestion { get; init; }
    public IReadOnlyList<string> Evidence { get; init; } = [];
}

public sealed record IslandReport(IslandSnapshot Snapshot, IReadOnlyList<Finding> Findings);
