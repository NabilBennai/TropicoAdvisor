namespace Tropico.Analysis;

public sealed record TimePoint(double X, double Value);

public sealed record BuildingSummary(int Total, IReadOnlyDictionary<string, int> ByClass, IReadOnlyDictionary<string, int> ByState);

public sealed record PopulationSummary(int? Total, int? Children, int? Adults, int? Retired, int? Prisoners);

/// <summary>
/// Parser-independent view of an island, the only input of the analysis rules.
/// Series values come from the game's history samples (X unit unverified). Unemployed and homeless series have an unverified
/// meaning (see the parser docs), so rules using them must report low confidence.
/// </summary>
public sealed record IslandSnapshot(
    string? SaveName,
    string GameBuild,
    BuildingSummary Buildings,
    PopulationSummary Population,
    double? Treasury,
    double? SwissBank,
    IReadOnlyList<TimePoint> TreasuryHistory,
    IReadOnlyList<double> MonthlyBalance,
    IReadOnlyList<double> UnemployedLastSample,
    IReadOnlyList<double> HomelessFamiliesLastSample);
