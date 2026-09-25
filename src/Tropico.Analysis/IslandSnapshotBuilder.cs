using Tropico.SaveParser;

namespace Tropico.Analysis;

public static class IslandSnapshotBuilder
{
    public const string UnknownState = "Unknown";

    public static IslandSnapshot Build(T6SaveFile save)
    {
        var buildings = T6BuildingReader.Read(save);
        var stats = T6IslandStatisticsReader.Read(save);
        var trade = T6TradeEconomyReader.Read(save);
        var population = stats.Population;

        return new IslandSnapshot(
            save.Header.SaveName,
            save.Header.Build,
            new BuildingSummary(
                buildings.Count,
                buildings.GroupBy(b => b.ClassName).ToDictionary(g => g.Key, g => g.Count()),
                buildings.GroupBy(b => StateName(b.BuildingState)).ToDictionary(g => g.Key, g => g.Count())),
            new PopulationSummary(population.Total, population.Children, population.Adults, population.Retired, population.Prisoners)
            {
                Soldiers = population.Soldiers,
                Voters = population.Voters,
                NativeTropicans = population.NativeTropicans,
                Immigrants = population.Immigrants,
            },
            stats.Treasury,
            stats.SwissBank,
            stats.Histories.TryGetValue("TreasuryHistory", out var treasury)
                ? treasury.Where(s => s.Y.Count > 0).Select(s => new TimePoint(s.X, s.Y[0])).ToList()
                : [],
            stats.MonthBalance,
            stats.LastSample("UnemployedHistory")?.Y ?? [],
            stats.LastSample("HomelessFamiliesByWealthHistory")?.Y ?? [],
            BuildEconomy(trade, stats, buildings))
        {
            PopulationData = BuildPopulation(save, stats, trade.Calendar),
            ProductionData = ProductionSnapshotBuilder.Build(buildings, T6DepositReader.Read(save), trade),
        };
    }

    private static readonly string[] HappinessCategories = ["Food", "Health", "Job", "House", "Faith", "Fun", "Liberty", "Safety"];
    private static readonly string[] HappinessLevelNames = ["Low", "Medium", "High"];
    private const string LookingForHome = "ET6Thought::GoingToFindHome";

    private static PopulationDetails BuildPopulation(T6SaveFile save, T6IslandStatistics stats, T6Calendar? calendar)
    {
        // Each happiness category history stores its value at the slot of its own category (verified: the only non-zero slot).
        var happiness = new Dictionary<string, double>();
        for (var i = 0; i < HappinessCategories.Length; i++)
        {
            if (stats.LastSample(HappinessCategories[i] + "HappinessHistory") is { } sample && i < sample.Y.Count) happiness[HappinessCategories[i]] = sample.Y[i];
        }

        // Level counts: slot 0 is unused (0 in every sample), then low, medium, high (low/medium verified against agents).
        var levels = Pad(stats.LastSample("AverageHappinessLevelHistory")?.Y, 4);
        var census = T6AgentCensusReader.Read(save);

        return new PopulationDetails(
            Pad(stats.LastSample("AgentEducationDistributionHistory")?.Y, 3),
            Pad(stats.LastSample("AgentAgeDistributionHistory")?.Y, 3),
            Pad(stats.LastSample("AgentWealthDistributionHistory")?.Y, 5),
            Pad(stats.LastSample("UnemployedHistory")?.Y, 3),
            Pad(stats.LastSample("HomelessFamiliesByWealthHistory")?.Y, 3),
            Pad(stats.LastSample("VacantHomesByWealthNumberHistory")?.Y, 3),
            stats.LastSample("OpenJobsHistory")?.Y.FirstOrDefault(),
            stats.LastSample("OverallHappinessHistory")?.Y.FirstOrDefault(),
            happiness,
            HappinessLevelNames.Select((n, i) => (n, levels[i + 1])).ToDictionary(x => x.n, x => x.Item2),
            stats.LastSample("AgentAverageAgeHistory")?.Y.FirstOrDefault(),
            census.ThoughtCounts.TryGetValue(LookingForHome, out var looking) ? looking : 0)
        {
            MonthIndex = calendar?.MonthIndex,
            Year = calendar?.Year,
            Month = calendar?.Month,
            PopulationHistory = Series(stats, "AgentPopulationHistory"),
            UnemployedHistory = Series(stats, "UnemployedHistory", sum: true),
            HomelessFamiliesHistory = Series(stats, "HomelessFamiliesByWealthHistory", sum: true),
            VacantHomesHistory = Series(stats, "VacantHomesByWealthNumberHistory", sum: true),
            HappinessHistory = Series(stats, "OverallHappinessHistory"),
        };
    }

    // History arrays omit trailing zeros: pad to the fixed number of slots.
    private static List<double> Pad(IReadOnlyList<double>? values, int length) =>
        Enumerable.Range(0, length).Select(i => values is not null && i < values.Count ? values[i] : 0.0).ToList();

    private static List<TimePoint> Series(T6IslandStatistics stats, string name, bool sum = false) =>
        stats.Histories.TryGetValue(name, out var samples)
            ? samples.Select(s => new TimePoint(s.X, sum ? s.Y.Sum() : s.Y.FirstOrDefault())).ToList()
            : [];

    private static EconomySnapshot BuildEconomy(T6TradeEconomy trade, T6IslandStatistics stats, IReadOnlyList<T6Building> buildings)
    {
        var stockByResource = trade.Stocks.ToDictionary(s => s.Resource, s => s.Total);
        var instances = buildings.GroupBy(b => b.ClassName).ToDictionary(g => g.Key, g => g.Count());
        var finances = stats.YearlyFinances;

        var goods = trade.Goods.Select(g =>
        {
            var exports = trade.RouteOffers.Where(o => o.Resource == g.Resource && !o.IsImport).ToList();
            var history = g.PriceHistory;
            return new GoodSnapshot(
                g.Resource, g.CurrentPrice,
                history.Count >= 6 ? history.Take(history.Count - 1).Average() : null, // average of the previous samples
                stockByResource.GetValueOrDefault(g.Resource),
                g.ExportedLast12Months, g.ImportedLast12Months,
                exports.Count, trade.RouteOffers.Count(o => o.Resource == g.Resource && o.IsImport),
                exports.Select(o => o.Partner).Distinct().Order().ToList())
            {
                PriceHistory = history,
            };
        }).ToList();

        return new EconomySnapshot(
            trade.Calendar?.MonthIndex,
            goods,
            trade.ClassEconomies
                .Select(e => new ClassCost(e.ClassName, instances.GetValueOrDefault(e.ClassName), e.Wages, e.Upkeep, e.FeesAndRents))
                .ToList(),
            finances.TotalRevenue, finances.TotalExpenses,
            finances.ExpensesByCategory.GetValueOrDefault("Wages"), finances.ExpensesByCategory.GetValueOrDefault("Upkeeps"),
            finances.ExpensesByCategory.GetValueOrDefault("Imports"), finances.RevenueByCategory.GetValueOrDefault("Exports"),
            finances.ExportRevenueByResource)
        {
            Year = trade.Calendar?.Year,
            Month = trade.Calendar?.Month,
            RevenueByCategory = finances.RevenueByCategory,
            ExpensesByCategory = finances.ExpensesByCategory,
            RevenueHistory = History(stats, "RevenueHistory"),
            ExpenseHistory = History(stats, "ExpenseHistory"),
        };
    }

    private static List<TimePoint> History(T6IslandStatistics stats, string name) =>
        stats.Histories.TryGetValue(name, out var samples)
            ? samples.Where(s => s.Y.Count > 0).Select(s => new TimePoint(s.X, s.Y[0])).ToList()
            : [];

    // "ET6BuildingState::Built" -> "Built"; null means no construction component was found.
    private static string StateName(string? state) =>
        state is null ? UnknownState : state[(state.LastIndexOf("::", StringComparison.Ordinal) + 2)..];
}
