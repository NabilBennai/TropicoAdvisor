namespace Tropico.Analysis;

public sealed record TimePoint(double X, double Value);

public sealed record BuildingSummary(int Total, IReadOnlyDictionary<string, int> ByClass, IReadOnlyDictionary<string, int> ByState);

public sealed record PopulationSummary(int? Total, int? Children, int? Adults, int? Retired, int? Prisoners)
{
    public int? Soldiers { get; init; }
    public int? Voters { get; init; }
    public int? NativeTropicans { get; init; }
    public int? Immigrants { get; init; }
}

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
    IReadOnlyList<double> HomelessFamiliesLastSample,
    EconomySnapshot? EconomyData = null)
{
    public EconomySnapshot Economy => EconomyData ?? EconomySnapshot.Empty;

    public PopulationDetails? PopulationData { get; init; }

    public PopulationDetails PopulationInfo => PopulationData ?? PopulationDetails.Empty;

    public ProductionSnapshot? ProductionData { get; init; }

    public ProductionSnapshot Production => ProductionData ?? ProductionSnapshot.Empty;
}
