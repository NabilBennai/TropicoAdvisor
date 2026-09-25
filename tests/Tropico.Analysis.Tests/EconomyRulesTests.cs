namespace Tropico.Analysis.Tests;

public class EconomyRulesTests
{
    private static GoodSnapshot Good(
        string resource, double stock = 0, double? price = 2, double? average = 2, int exported = 0, int exportOffers = 0, string[]? partners = null) =>
        new(resource, price, average, stock, exported, 0, exportOffers, 0, partners ?? []);

    private static IslandSnapshot Snapshot(
        double? treasury = 100_000,
        long expenses = 120_000,
        long wages = 0,
        long upkeep = 0,
        long imports = 0,
        GoodSnapshot[]? goods = null,
        ClassCost[]? classes = null,
        Dictionary<string, long>? exports = null) => new(
            "test", "t6-test",
            new BuildingSummary(10, new Dictionary<string, int>(), new Dictionary<string, int> { ["Built"] = 10 }),
            new PopulationSummary(1000, 200, 800, 0, 0),
            treasury, 0, [], [], [], [],
            new EconomySnapshot(100, goods ?? [], classes ?? [], expenses * 2, expenses, wages, upkeep, imports, exports?.Values.Sum() ?? 0,
                exports ?? new Dictionary<string, long>()));

    private static IReadOnlyList<Finding> Run(IslandSnapshot s) => new IslandAnalyzer().Analyze(s).Findings;

    private static Finding? Find(IslandSnapshot s, string code) => Run(s).SingleOrDefault(f => f.Code == code);

    [Fact]
    public void Runway_LowTreasury_IsWarning_AndCriticalUnderOneMonth()
    {
        var low = Find(Snapshot(treasury: 20_000, expenses: 120_000), "economy.runway-low")!; // 2 months
        var critical = Find(Snapshot(treasury: 5_000, expenses: 120_000), "economy.runway-low")!; // 0.5 month

        Assert.Equal(Severity.Warning, low.Severity);
        Assert.Contains("2.0 months", low.Message);
        Assert.Equal(Severity.Critical, critical.Severity);
        Assert.NotNull(low.Suggestion);
        Assert.NotEmpty(low.Evidence);
    }

    [Fact]
    public void Runway_HugeTreasury_SuggestsInvesting_AndNormalTreasuryIsSilent()
    {
        var idle = Find(Snapshot(treasury: 1_000_000, expenses: 120_000), "economy.runway-idle")!; // 100 months

        Assert.Equal(Severity.Info, idle.Severity);
        Assert.Contains("100 months", idle.Message);
        Assert.Null(Find(Snapshot(treasury: 100_000, expenses: 120_000), "economy.runway-idle")); // 10 months
        Assert.Null(Find(Snapshot(treasury: 100_000, expenses: 120_000), "economy.runway-low"));
        Assert.DoesNotContain(Run(Snapshot(treasury: null)), f => f.Code.StartsWith("economy.runway"));
        Assert.DoesNotContain(Run(Snapshot(expenses: 0)), f => f.Code.StartsWith("economy.runway"));
    }

    [Fact]
    public void WageBurden_ThresholdsAndEvidence()
    {
        Assert.Null(Find(Snapshot(wages: 50_000), "economy.wage-burden")); // 42 %
        Assert.Equal(Severity.Info, Find(Snapshot(wages: 84_000), "economy.wage-burden")!.Severity); // 70 %

        var heavy = Find(Snapshot(wages: 108_000, upkeep: 12_000), "economy.wage-burden")!; // 90 %
        Assert.Equal(Severity.Warning, heavy.Severity);
        Assert.Contains("90 %", heavy.Message);
        Assert.Contains("Wages: 108,000", heavy.Evidence);
    }

    [Fact]
    public void CostCenters_ListTheCostliestClassesWithoutCallingThemLosses()
    {
        var finding = Find(Snapshot(classes:
        [
            new ClassCost("BP_Mine_C", 10, 8_000, 2_000, 0),
            new ClassCost("BP_Farm_C", 5, 3_000, 1_000, 0),
            new ClassCost("BP_House_C", 20, 0, 4_000, 9_000), // earns more than it costs: not a cost center
            new ClassCost("BP_Tiny_C", 1, 100, 0, 0),
            new ClassCost("BP_Small_C", 2, 500, 100, 0),
        ]), "economy.cost-centers")!;

        Assert.Equal("Costliest building classes last year: BP_Mine_C, BP_Farm_C, BP_Small_C.", finding.Message);
        Assert.DoesNotContain("BP_House_C", finding.Message);
        Assert.Contains("BP_Mine_C: 10,000 (", finding.Evidence[0]);
        Assert.Contains("1,000 per building", finding.Evidence[0]);
        Assert.DoesNotContain("loss", finding.Message + finding.Suggestion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImportDependence_ThresholdsAndSeverity()
    {
        Assert.Null(Find(Snapshot(imports: 6_000), "economy.import-dependence")); // 5 %
        Assert.Equal(Severity.Info, Find(Snapshot(imports: 24_000), "economy.import-dependence")!.Severity); // 20 %
        Assert.Equal(Severity.Warning, Find(Snapshot(imports: 48_000), "economy.import-dependence")!.Severity); // 40 %
    }

    [Fact]
    public void ExportConcentration_ReportsDominantResource()
    {
        var finding = Find(Snapshot(exports: new()
        {
            ["ET6ResourceType::Gold"] = 8_000,
            ["ET6ResourceType::Rum"] = 1_500,
            ["ET6ResourceType::Fish"] = 500,
        }), "trade.export-concentration")!;

        Assert.Equal(Severity.Warning, finding.Severity); // 80 %
        Assert.Contains("Gold makes 80 %", finding.Message);
        Assert.Equal("Gold: 8,000 (80 %)", finding.Evidence[0]);

        Assert.Null(Find(Snapshot(exports: new() { ["Gold"] = 4_000, ["Rum"] = 3_000, ["Fish"] = 3_000 }), "trade.export-concentration")); // 40 %
        Assert.Contains("Gold makes 100 %", Find(Snapshot(exports: new() { ["Gold"] = 4_000 }), "trade.export-concentration")!.Message); // names without prefix
        Assert.Null(Find(Snapshot(exports: new()), "trade.export-concentration"));
    }

    [Fact]
    public void IdleStock_RequiresStock_NoExports_AnOfferedRoute_AndValue()
    {
        var findings = Run(Snapshot(goods:
        [
            Good("Coal", stock: 30_000, price: 1.6, exportOffers: 2, partners: ["Mexico", "TheCrown"]), // reported
            Good("Sugar", stock: 4_000, price: 2.5, exportOffers: 1, partners: ["TheCrown"]),           // reported
            Good("Gold", stock: 6_000, price: 9, exported: 500, exportOffers: 1),                        // already exported
            Good("Fish", stock: 5_000, price: 3, exportOffers: 0),                                       // nothing to sell it through
            Good("Rum", stock: 100, price: 5, exportOffers: 1),                                          // stock too small
            Good("Hides", stock: 1_000, price: 0.5, exportOffers: 1),                                    // value 500 < minimum
        ])).Where(f => f.Code.StartsWith("trade.idle-stock.")).ToList();

        Assert.Equal(["trade.idle-stock.Coal", "trade.idle-stock.Sugar"], findings.Select(f => f.Code).Order());
        var coal = findings.Single(f => f.Code.EndsWith("Coal"));
        Assert.Contains("30,000 Coal", coal.Message);
        Assert.Contains("worth about 48,000", coal.Message);
        Assert.Contains("Mexico, TheCrown", coal.Suggestion);
        Assert.Equal(Confidence.Probable, coal.Confidence);
    }

    [Fact]
    public void IdleStock_ReportsAtMostThree_MostValuableFirst()
    {
        var goods = new[] { "A", "B", "C", "D", "E" }
            .Select((r, i) => Good(r, stock: 10_000 * (i + 1), price: 1, exportOffers: 1)).ToArray();

        var codes = Run(Snapshot(goods: goods)).Where(f => f.Code.StartsWith("trade.idle-stock.")).Select(f => f.Code).ToList();

        Assert.Equal(["trade.idle-stock.E", "trade.idle-stock.D", "trade.idle-stock.C"], codes.OrderByDescending(c => c));
        Assert.Equal(3, codes.Count);
    }

    [Fact]
    public void PriceOpportunity_IsAlwaysUncertain_AndNeedsStockAndARoute()
    {
        var findings = Run(Snapshot(goods:
        [
            Good("Rum", stock: 800, price: 6.6, average: 5.0, exportOffers: 1),     // +32 %
            Good("Gold", stock: 800, price: 9.0, average: 8.9, exportOffers: 1),    // +1 %
            Good("Fish", stock: 10, price: 6.6, average: 5.0, exportOffers: 1),     // stock too small
            Good("Meat", stock: 800, price: 6.6, average: 5.0, exportOffers: 0),    // no route
            Good("Corn", stock: 800, price: 6.6, average: null, exportOffers: 1),   // no history
        ])).Where(f => f.Code.StartsWith("trade.price-high.")).ToList();

        var rum = Assert.Single(findings);
        Assert.Equal("trade.price-high.Rum", rum.Code);
        Assert.Equal(Confidence.Uncertain, rum.Confidence);
        Assert.Contains("32 % above", rum.Message);
        Assert.Contains("Price now: 6.60", rum.Evidence);
    }

    [Fact]
    public void Findings_CarryCategories()
    {
        var findings = Run(Snapshot(treasury: 1_000_000, wages: 100_000, goods: [Good("Coal", stock: 30_000, exportOffers: 1, partners: ["Mexico"])]));

        Assert.Equal("Economy", findings.Single(f => f.Code == "economy.wage-burden").Category);
        Assert.Equal("Trade", findings.Single(f => f.Code == "trade.idle-stock.Coal").Category);
    }

    [Fact]
    public void GoodSnapshot_DerivedValues()
    {
        var good = Good("Gold", stock: 100, price: 5, average: 4);

        Assert.Equal(500, good.StockValue);
        Assert.Equal(1.25, good.PriceRatio);
        Assert.Null(Good("Gold", average: null).PriceRatio);
        Assert.Equal(0, Good("Gold", stock: 10, price: null).StockValue);
    }

    [Fact]
    public void DateOf_DerivesCalendarDatesFromTheCurrentMonthIndex()
    {
        var economy = EconomySnapshot.Empty with { MonthIndex = 417 };
        Assert.Null(economy.DateOf(417)); // no calendar

        var dated = new EconomySnapshot(417, [], [], 0, 0, 0, 0, 0, 0, new Dictionary<string, long>()) { Year = 1934, Month = 9 };

        Assert.Equal((1934, 9), dated.DateOf(417));
        Assert.Equal((1934, 8), dated.DateOf(416));
        Assert.Equal((1934, 1), dated.DateOf(409));
        Assert.Equal((1933, 12), dated.DateOf(408)); // crosses the year boundary
        Assert.Equal((1929, 9), dated.DateOf(357));  // first sample of the 61-month history
    }
}
