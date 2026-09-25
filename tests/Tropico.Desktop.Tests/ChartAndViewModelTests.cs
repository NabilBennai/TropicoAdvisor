using Tropico.Analysis;
using Tropico.Desktop.Controls;
using Tropico.Desktop.ViewModels;

namespace Tropico.Desktop.Tests;

public class ChartScaleTests
{
    [Fact]
    public void Range_PadsTheDataRange()
    {
        var (min, max) = ChartScale.Range([100, 200]);

        Assert.Equal(92, min, 6);
        Assert.Equal(208, max, 6);
    }

    [Fact]
    public void Range_ConstantAndEmptySeries_NeverCollapse()
    {
        var (min, max) = ChartScale.Range([50, 50, 50]);
        Assert.True(min < 50 && max > 50);

        Assert.Equal((0.0, 1.0), ChartScale.Range([]));
        Assert.Equal((0.0, 1.0), ChartScale.Range([double.NaN, double.PositiveInfinity]));
    }

    [Fact]
    public void Map_PlacesSamplesAcrossTheWidthAndInvertsY()
    {
        Assert.Equal(0, ChartScale.MapX(0, 5, 100));
        Assert.Equal(100, ChartScale.MapX(4, 5, 100));
        Assert.Equal(50, ChartScale.MapX(0, 1, 100)); // a single sample is centred

        Assert.Equal(200, ChartScale.MapY(0, 0, 10, 200));   // minimum at the bottom
        Assert.Equal(0, ChartScale.MapY(10, 0, 10, 200));    // maximum at the top
        Assert.Equal(100, ChartScale.MapY(5, 5, 5, 200));    // degenerate range does not divide by zero
    }

    [Theory]
    [InlineData(1_453_750, "1.45M")]
    [InlineData(-2_500_000, "-2.5M")]
    [InlineData(12_345, "12.3k")]
    [InlineData(1_234, "1,234")]
    [InlineData(250.4, "250")]
    [InlineData(8.966, "8.97")]
    [InlineData(0, "0")]
    public void FormatValue_IsCompactAndCultureIndependent(double value, string expected) =>
        Assert.Equal(expected, ChartScale.FormatValue(value));

    [Fact]
    public void FormatDate_UsesMonthNames() => Assert.Equal("Sep 1934", ChartScale.FormatDate((1934, 9)));
}

public class DisplayNamesTests
{
    [Theory]
    [InlineData("BP_T6Mine_C", "Mine")]
    [InlineData("BP_T6PileOfBS_C", "Pile Of BS")]
    [InlineData("Building_Palace_C", "Palace")]
    [InlineData("BP_Building_Teamster_C", "Teamster")]
    [InlineData("BP_T6RumDistillery_C", "Rum Distillery")]
    public void Building_RemovesTechnicalPrefixesAndSplitsWords(string className, string expected) =>
        Assert.Equal(expected, DisplayNames.Building(className));

    [Theory]
    [InlineData("TouristFees", "Tourist Fees")]
    [InlineData("ET6ResourceType::Gold", "Gold")]
    [InlineData("Exports", "Exports")]
    public void Category_HumanizesNames(string name, string expected) => Assert.Equal(expected, DisplayNames.Category(name));
}

public class EconomyAndTradeViewModelTests
{
    private static IslandSnapshot Snapshot()
    {
        var economy = new EconomySnapshot(
            MonthIndex: 12,
            Goods:
            [
                new GoodSnapshot("Gold", 9, 8, 100, 50, 0, 2, 1, ["Mexico"]),
                new GoodSnapshot("Coal", 2, 2, 1_000, 0, 0, 0, 0, []),
                new GoodSnapshot("Unused", 5, 5, 0, 0, 0, 1, 1, []),
            ],
            ClassCosts:
            [
                new ClassCost("BP_T6Mine_C", 4, 800, 200, 0),
                new ClassCost("BP_T6House_C", 10, 0, 100, 500),
                new ClassCost("BP_Nothing_C", 1, 0, 0, 0),
            ],
            YearlyRevenue: 3_000, YearlyExpenses: 1_200, Wages: 800, Upkeep: 300, Imports: 0, ExportRevenue: 2_000,
            ExportRevenueByResource: new Dictionary<string, long>())
        {
            Year = 1930,
            Month = 3,
            RevenueByCategory = new Dictionary<string, long> { ["Exports"] = 2_000, ["TouristFees"] = 0, ["Rents"] = 1_000 },
            ExpensesByCategory = new Dictionary<string, long> { ["Wages"] = 800, ["Upkeeps"] = 300, ["Imports"] = 0 },
            RevenueHistory = [new TimePoint(11, 10), new TimePoint(12, 20)],
            ExpenseHistory = [new TimePoint(11, 5), new TimePoint(12, 6)],
        };

        return new IslandSnapshot(
            "test", "t6", new BuildingSummary(15, new Dictionary<string, int>(), new Dictionary<string, int>()),
            new PopulationSummary(1000, 200, 800, 0, 0), 12_000, 0,
            [new TimePoint(11, 11_000), new TimePoint(12, 12_000)], [], [], [], economy);
    }

    private static readonly Finding[] Findings =
    [
        new("a", Severity.Info, Confidence.Probable, "eco") { Category = "Economy" },
        new("b", Severity.Info, Confidence.Probable, "trade") { Category = "Trade" },
        new("c", Severity.Info, Confidence.Probable, "other") { Category = "Population" },
    ];

    [Fact]
    public void Economy_BuildsSeriesLabelsAndKeyFigures()
    {
        var vm = new EconomyViewModel(Snapshot(), Findings);

        Assert.Equal([11_000.0, 12_000.0], vm.TreasurySeries[0].Values);
        Assert.Equal(["Revenue", "Expenses"], vm.FlowSeries.Select(s => s.Name));
        Assert.Equal("Feb 1930", vm.TreasuryLeft); // month index 11 is one month before the current March 1930
        Assert.Equal("Mar 1930", vm.TreasuryRight);
        Assert.StartsWith("120 months", vm.Runway); // 12,000 / (1,200 / 12)
        Assert.Equal(["eco"], vm.Findings.Select(f => f.Message));
    }

    [Fact]
    public void Economy_Breakdowns_SkipZeroAndSortByAmount()
    {
        var vm = new EconomyViewModel(Snapshot(), Findings);

        Assert.Equal(["Exports", "Rents"], vm.RevenueBreakdown.Select(r => r.Name));
        Assert.Equal(["Wages", "Upkeeps"], vm.ExpenseBreakdown.Select(r => r.Name));
        Assert.Contains("67", vm.RevenueBreakdown[0].Share); // 2000 / 3000
    }

    [Fact]
    public void Economy_CostCenters_UseReadableNamesAndSkipEmptyClasses()
    {
        var rows = new EconomyViewModel(Snapshot(), Findings).CostCenters;

        Assert.Equal(["Mine", "House"], rows.Select(r => r.Name));
        Assert.DoesNotContain(rows, r => r.Name == "Nothing");
        Assert.Equal("4", rows[0].Buildings);
    }

    [Fact]
    public void Economy_WithoutCalendar_FallsBackToMonthIndices()
    {
        var snapshot = Snapshot() with { EconomyData = Snapshot().Economy with { Year = null, Month = null } };

        var vm = new EconomyViewModel(snapshot, []);

        Assert.Equal("month 11", vm.TreasuryLeft);
        Assert.Equal("month 12", vm.TreasuryRight);
    }

    [Fact]
    public void Trade_ListsOnlyGoodsWithStockOrTrade_MostValuableFirst()
    {
        var vm = new TradeViewModel(Snapshot(), Findings);

        Assert.Equal(["Coal", "Gold"], vm.Goods.Select(g => g.Resource)); // 2,000 vs 900 stock value
        Assert.DoesNotContain(vm.Goods, g => g.Resource == "Unused");
        Assert.Equal("0 / 0", vm.Goods[0].Routes);
        Assert.Equal("2 / 1", vm.Goods[1].Routes);
        Assert.Contains("12", vm.Goods[1].PriceChange); // 9 / 8 = +12.5 %
        Assert.Equal(["trade"], vm.Findings.Select(f => f.Message));
    }
}
