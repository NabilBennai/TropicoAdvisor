using Tropico.Analysis;
using Tropico.Localization;

namespace Tropico.Data;

/// <summary>Key figures of an island at one moment. Null means the save did not provide the figure.</summary>
public sealed record SnapshotMetrics(
    int Buildings,
    IReadOnlyDictionary<string, int> BuildingsByClass,
    int? Citizens,
    int? Adults,
    int? Children,
    double? Unemployed,
    double? HomelessFamilies,
    double? Happiness,
    double? Treasury,
    long YearlyRevenue,
    long YearlyExpenses,
    long Wages,
    long Upkeep,
    IReadOnlyDictionary<string, double> FactionStanding,
    IReadOnlyDictionary<string, double> ResourceStock);

/// <summary>A finding kept with its localizable message, so it can be shown later in any language.</summary>
public sealed record StoredFinding(string Code, Severity Severity, Confidence Confidence, string Category, LocalizedText Message);

/// <summary>
/// One analysed save of an island. An island is identified by its map id (the same island keeps it across saves); two analyses of the
/// same island at the same game day are the same snapshot.
/// </summary>
public sealed record StoredSnapshot(
    long? Id,
    string IslandKey,
    string SaveName,
    int GameDay,
    int? GameYear,
    int? GameMonth,
    DateTimeOffset AnalyzedAt,
    SnapshotMetrics Metrics,
    IReadOnlyList<StoredFinding> Findings);

/// <summary>Listing entry of a stored snapshot (without its content).</summary>
public sealed record SnapshotInfo(long Id, string SaveName, int GameDay, int? GameYear, int? GameMonth, DateTimeOffset AnalyzedAt);

public static class SnapshotFactory
{
    /// <summary>
    /// The snapshot to keep for an analysis, or null when the save has no game day (snapshots cannot be ordered without it).
    /// </summary>
    public static StoredSnapshot? From(IslandReport report, DateTimeOffset analyzedAt)
    {
        var snapshot = report.Snapshot;
        if (snapshot.GameDay is not { } day) return null;

        var details = snapshot.PopulationInfo;
        var economy = snapshot.Economy;

        var metrics = new SnapshotMetrics(
            snapshot.Buildings.Total,
            snapshot.Buildings.ByClass,
            snapshot.Population.Total, snapshot.Population.Adults, snapshot.Population.Children,
            details.Unemployed.Count > 0 ? details.TotalUnemployed : null,
            details.HomelessFamilies.Count > 0 ? details.TotalHomelessFamilies : null,
            details.HappinessOverall,
            snapshot.Treasury,
            economy.YearlyRevenue, economy.YearlyExpenses, economy.Wages, economy.Upkeep,
            snapshot.Politics.Factions.ToDictionary(f => f.Name, f => f.KnownTotal),
            economy.Goods.Where(g => g.StockTotal > 0).ToDictionary(g => g.Resource, g => g.StockTotal));

        return new StoredSnapshot(
            null,
            snapshot.MapId ?? snapshot.SaveName ?? "unknown",
            snapshot.SaveName ?? "",
            day, economy.Year, economy.Month, analyzedAt, metrics,
            report.Findings.Select(f => new StoredFinding(f.Code, f.Severity, f.Confidence, f.Category, f.MessageText)).ToList());
    }
}
