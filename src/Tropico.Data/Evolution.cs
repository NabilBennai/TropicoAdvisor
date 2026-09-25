namespace Tropico.Data;

/// <summary>The same figure at two moments. <see cref="Delta"/> is null when either side is missing.</summary>
public sealed record MetricChange(string Metric, double? Before, double? After)
{
    public double? Delta => Before is { } b && After is { } a ? a - b : null;
}

public sealed record CountChange(string Name, int Before, int After)
{
    public int Delta => After - Before;
}

/// <summary>What changed on an island between two of its saves.</summary>
public sealed record Evolution(
    StoredSnapshot Previous,
    StoredSnapshot Current,
    int MonthsBetween,
    IReadOnlyList<MetricChange> Metrics,
    IReadOnlyList<CountChange> BuildingChanges,
    IReadOnlyList<MetricChange> FactionChanges,
    IReadOnlyList<StoredFinding> NewFindings,
    IReadOnlyList<StoredFinding> ResolvedFindings);

public static class SnapshotComparison
{
    public const int DaysPerMonth = 30;
    public const double MinFactionChange = 0.05;

    /// <summary>Compares <paramref name="current"/> with an earlier snapshot of the same island.</summary>
    public static Evolution Compare(StoredSnapshot previous, StoredSnapshot current)
    {
        if (previous.IslandKey != current.IslandKey) throw new ArgumentException("Snapshots belong to different islands.");

        var before = previous.Metrics;
        var after = current.Metrics;

        MetricChange[] metrics =
        [
            new("buildings", before.Buildings, after.Buildings),
            new("citizens", before.Citizens, after.Citizens),
            new("adults", before.Adults, after.Adults),
            new("children", before.Children, after.Children),
            new("unemployed", before.Unemployed, after.Unemployed),
            new("homelessFamilies", before.HomelessFamilies, after.HomelessFamilies),
            new("happiness", before.Happiness, after.Happiness),
            new("treasury", before.Treasury, after.Treasury),
            new("yearlyRevenue", before.YearlyRevenue, after.YearlyRevenue),
            new("yearlyExpenses", before.YearlyExpenses, after.YearlyExpenses),
            new("wages", before.Wages, after.Wages),
            new("upkeep", before.Upkeep, after.Upkeep),
        ];

        var buildings = before.BuildingsByClass.Keys.Union(after.BuildingsByClass.Keys)
            .Select(c => new CountChange(c, before.BuildingsByClass.GetValueOrDefault(c), after.BuildingsByClass.GetValueOrDefault(c)))
            .Where(c => c.Delta != 0)
            .OrderByDescending(c => Math.Abs(c.Delta)).ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();

        var factions = before.FactionStanding.Keys.Union(after.FactionStanding.Keys)
            .Select(f => new MetricChange(f, before.FactionStanding.GetValueOrDefault(f), after.FactionStanding.GetValueOrDefault(f)))
            .Where(f => Math.Abs(f.Delta ?? 0) >= MinFactionChange)
            .OrderBy(f => f.Delta).ThenBy(f => f.Metric, StringComparer.Ordinal)
            .ToList();

        var previousCodes = previous.Findings.Select(f => f.Code).ToHashSet();
        var currentCodes = current.Findings.Select(f => f.Code).ToHashSet();

        return new Evolution(
            previous, current,
            (current.GameDay - previous.GameDay) / DaysPerMonth,
            metrics, buildings, factions,
            current.Findings.Where(f => !previousCodes.Contains(f.Code)).ToList(),
            previous.Findings.Where(f => !currentCodes.Contains(f.Code)).ToList());
    }
}
