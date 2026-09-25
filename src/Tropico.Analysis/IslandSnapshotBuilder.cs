using Tropico.SaveParser;

namespace Tropico.Analysis;

public static class IslandSnapshotBuilder
{
    public const string UnknownState = "Unknown";

    public static IslandSnapshot Build(T6SaveFile save)
    {
        var buildings = T6BuildingReader.Read(save);
        var stats = T6IslandStatisticsReader.Read(save);
        var population = stats.Population;

        return new IslandSnapshot(
            save.Header.SaveName,
            save.Header.Build,
            new BuildingSummary(
                buildings.Count,
                buildings.GroupBy(b => b.ClassName).ToDictionary(g => g.Key, g => g.Count()),
                buildings.GroupBy(b => StateName(b.BuildingState)).ToDictionary(g => g.Key, g => g.Count())),
            new PopulationSummary(population.Total, population.Children, population.Adults, population.Retired, population.Prisoners),
            stats.Treasury,
            stats.SwissBank,
            stats.Histories.TryGetValue("TreasuryHistory", out var treasury)
                ? treasury.Where(s => s.Y.Count > 0).Select(s => new TimePoint(s.X, s.Y[0])).ToList()
                : [],
            stats.MonthBalance,
            stats.LastSample("UnemployedHistory")?.Y ?? [],
            stats.LastSample("HomelessFamiliesByWealthHistory")?.Y ?? []);
    }

    // "ET6BuildingState::Built" -> "Built"; null means no construction component was found.
    private static string StateName(string? state) =>
        state is null ? UnknownState : state[(state.LastIndexOf("::", StringComparison.Ordinal) + 2)..];
}
