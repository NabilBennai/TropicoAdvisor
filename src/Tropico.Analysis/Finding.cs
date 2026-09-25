namespace Tropico.Analysis;

public enum Severity { Info, Warning, Critical }

/// <summary>How much the underlying save data can be trusted (see the reverse-engineering report).</summary>
public enum Confidence { Uncertain, Probable, Verified }

public sealed record Finding(string Code, Severity Severity, Confidence Confidence, string Message);

public sealed record IslandReport(IslandSnapshot Snapshot, IReadOnlyList<Finding> Findings);
