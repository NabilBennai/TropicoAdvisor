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
            stats.LastSample("HomelessFamiliesByWealthHistory")?.Y ?? [],
            BuildEconomy(save, stats, buildings));
    }

    private static EconomySnapshot BuildEconomy(T6SaveFile save, T6IslandStatistics stats, IReadOnlyList<T6Building> buildings)
    {
        var trade = T6TradeEconomyReader.Read(save);
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
