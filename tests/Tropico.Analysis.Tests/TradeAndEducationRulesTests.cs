namespace Tropico.Analysis.Tests;

public class TradeAndEducationRulesTests
{
    private static GoodSnapshot Good(
        string resource, double stock = 0, double? price = 2, double? average = 2, int exported = 0, int imported = 0) =>
        new(resource, price, average, stock, exported, imported, 1, 0, []);

    private static IslandSnapshot Snapshot(
        long imports = 0, long exportRevenue = 0, GoodSnapshot[]? goods = null, double[]? education = null, double[]? unemployed = null) => new(
            "test", "t6-test",
            new BuildingSummary(10, new Dictionary<string, int>(), new Dictionary<string, int> { ["Built"] = 10 }),
            new PopulationSummary(1000, 200, 800, 0, 0),
            100_000, 0, [], [], [], [])
        {
            EconomyData = new EconomySnapshot(100, goods ?? [], [], 200_000, 120_000, 0, 0, imports, exportRevenue, new Dictionary<string, long>()),
            PopulationData = PopulationDetails.Empty with { Education = education ?? [], Unemployed = unemployed ?? [] },
        };

    private static Finding? Find(IslandSnapshot s, string code) => new IslandAnalyzer().Analyze(s).Findings.SingleOrDefault(f => f.Code == code);

    [Fact]
    public void TradeDeficit_IsInfoWhenSlight_WarningWhenImportsAreOneAndAHalfTimesExports()
    {
        var slight = Find(Snapshot(imports: 12_000, exportRevenue: 10_000), "trade.deficit")!;
        var heavy = Find(Snapshot(imports: 30_000, exportRevenue: 10_000), "trade.deficit")!;

        Assert.Equal(Severity.Info, slight.Severity);
        Assert.Equal(Severity.Warning, heavy.Severity);
        Assert.Contains("trade deficit of 20,000", heavy.Message);
        Assert.NotNull(heavy.Suggestion);
        Assert.Equal(2, heavy.Evidence.Count);
    }

    [Theory]
    [InlineData(20_000, 30_000)] // balanced or in surplus
    [InlineData(3_000, 0)]       // too small to matter
    public void NoTradeDeficit_WhenExportsCoverImports_OrAmountsAreSmall(long imports, long exports) =>
        Assert.Null(Find(Snapshot(imports: imports, exportRevenue: exports), "trade.deficit"));

    [Fact]
    public void PriceDip_NeedsAnExportedResource_StockedAndWellBelowItsAverage_AndIsUncertain()
    {
        var finding = Find(Snapshot(goods: [Good("Iron", stock: 300, price: 1.5, average: 2, exported: 50)]), "trade.price-low.Iron")!;

        Assert.Equal(Confidence.Uncertain, finding.Confidence);
        Assert.Contains("25 % below", finding.Message);

        Assert.Null(Find(Snapshot(goods: [Good("Iron", stock: 300, price: 1.5, average: 2, exported: 0)]), "trade.price-low.Iron")); // never exported
        Assert.Null(Find(Snapshot(goods: [Good("Iron", stock: 300, price: 1.9, average: 2, exported: 50)]), "trade.price-low.Iron")); // only 5 % below
        Assert.Null(Find(Snapshot(goods: [Good("Iron", stock: 50, price: 1.5, average: 2, exported: 50)]), "trade.price-low.Iron")); // no stock to hold
    }

    [Fact]
    public void ImportWhileStocked_FlagsGoodsAlreadyStockedForHalfTheImportedQuantity()
    {
        var finding = Find(Snapshot(goods: [Good("Wood", stock: 600, imported: 1_000)]), "trade.import-while-stocked.Wood")!;

        Assert.Equal(Confidence.Uncertain, finding.Confidence);
        Assert.Contains("1,000 in the last 12 months", finding.Message);

        Assert.Null(Find(Snapshot(goods: [Good("Wood", stock: 400, imported: 1_000)]), "trade.import-while-stocked.Wood"));
        Assert.Null(Find(Snapshot(goods: [Good("Wood", stock: 600, imported: 50)]), "trade.import-while-stocked.Wood"));
    }

    [Fact]
    public void EducationLow_IsInfoAtSixtyPercentUneducated_WarningAtEighty_WithTheThreeLevelsAsEvidence()
    {
        var info = Find(Snapshot(education: [650, 300, 50]), "population.education-low")!;
        var warning = Find(Snapshot(education: [850, 100, 50]), "population.education-low")!;

        Assert.Equal(Severity.Info, info.Severity);
        Assert.Equal(Severity.Warning, warning.Severity);
        Assert.Equal(Confidence.Probable, warning.Confidence);
        Assert.Contains("85 % of citizens are uneducated (850 of 1,000)", warning.Message);
        Assert.Equal(["Uneducated: 850", "High school: 100", "College: 50"], warning.Evidence);
    }

    [Theory]
    [InlineData(500, 300, 200)] // 50 % uneducated
    [InlineData(60, 20, 10)]    // too few citizens to judge
    public void NoEducationFinding_WhenEducationIsAcceptable_OrTheSampleIsTiny(double a, double b, double c) =>
        Assert.Null(Find(Snapshot(education: [a, b, c]), "population.education-low"));

    [Fact]
    public void EducatedJobless_IsUncertain_AndNeedsManyEducatedAmongTheUnemployed()
    {
        var finding = Find(Snapshot(unemployed: [60, 30, 10]), "population.educated-jobless")!;

        Assert.Equal(Confidence.Uncertain, finding.Confidence);
        Assert.Contains("40 of the 100 unemployed", finding.Message);

        Assert.Null(Find(Snapshot(unemployed: [90, 8, 2]), "population.educated-jobless"));  // 10 % educated
        Assert.Null(Find(Snapshot(unemployed: [5, 10, 5]), "population.educated-jobless"));  // large share, but only 15 people
        Assert.Null(Find(Snapshot(unemployed: [0, 0, 0]), "population.educated-jobless"));
    }
}
