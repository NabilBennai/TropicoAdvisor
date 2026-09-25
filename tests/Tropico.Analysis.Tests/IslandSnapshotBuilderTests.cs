using Tropico.SaveParser;

namespace Tropico.Analysis.Tests;

public class IslandSnapshotBuilderTests
{
    private const string SampleName = "Trop6_Sav_urss Oct, 1934.t6sav";

    internal static string FindSample()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "samples", "private", SampleName);
            if (File.Exists(candidate)) return candidate;
        }

        var documents = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games", "Tropico6", "Saved", "SaveGames", SampleName);
        Assert.True(File.Exists(documents), $"Sample save not found: put '{SampleName}' in samples/private/.");
        return documents;
    }

    [Trait("Category", "RealSave")]
    [Fact]
    public void Build_ReferenceSave_ExposesParserData()
    {
        var snapshot = IslandSnapshotBuilder.Build(T6SaveReader.Read(FindSample()));

        Assert.Equal("urss Oct, 1934", snapshot.SaveName);
        Assert.Equal(297, snapshot.Buildings.Total);
        Assert.Equal(297, snapshot.Buildings.ByState["Built"]);
        Assert.Equal(85, snapshot.Buildings.ByClass["BP_T6CountryHouse_C"]);
        Assert.Equal(1313, snapshot.Population.Total);
        Assert.Equal(1453750.25, snapshot.Treasury);
        Assert.Equal(61, snapshot.TreasuryHistory.Count);
        Assert.Equal(12, snapshot.MonthlyBalance.Count);
        Assert.Equal([59.0, 72.0, 1.0], snapshot.UnemployedLastSample);
    }

    [Trait("Category", "RealSave")]
    [Fact]
    public void Analyze_ReferenceSave_ReportsFallingTreasuryAndLowConfidenceUnemployment()
    {
        var report = new IslandAnalyzer().Analyze(T6SaveReader.Read(FindSample()));

        Assert.DoesNotContain(report.Findings, f => f.Code == "buildings.condition"); // all 297 buildings are Built
        Assert.Contains(report.Findings, f => f.Code == "treasury.trend");
        var unemployment = Assert.Single(report.Findings, f => f.Code == "population.unemployment");
        Assert.Equal(Confidence.Uncertain, unemployment.Confidence);
    }

    [Trait("Category", "RealSave")]
    [Fact]
    public void Build_ReferenceSave_ExposesEconomyData()
    {
        var economy = IslandSnapshotBuilder.Build(T6SaveReader.Read(FindSample())).Economy;

        Assert.Equal(417, economy.MonthIndex);
        Assert.Equal(56, economy.Goods.Count);
        Assert.Equal(165_044, economy.YearlyExpenses);
        Assert.Equal(111_432, economy.Wages);
        Assert.Equal(41_760, economy.Upkeep);

        var coal = economy.Goods.Single(g => g.Resource == "Coal");
        Assert.Equal(33_409, coal.StockTotal, 0);
        Assert.NotNull(coal.CurrentPrice);
        Assert.NotNull(coal.AveragePrice);

        var mines = economy.ClassCosts.Single(c => c.ClassName == "BP_T6Mine_C");
        Assert.Equal(35, mines.Instances);
        Assert.Equal(27_300, mines.Cost);
    }

    [Trait("Category", "RealSave")]
    [Fact]
    public void Analyze_ReferenceSave_ProducesEconomicSuggestions()
    {
        var findings = new IslandAnalyzer().Analyze(T6SaveReader.Read(FindSample())).Findings;

        Assert.Contains(findings, f => f.Code == "economy.runway-idle");
        Assert.Contains(findings, f => f.Code == "economy.wage-burden");
        var costCenters = Assert.Single(findings, f => f.Code == "economy.cost-centers");
        Assert.StartsWith("Costliest building classes last year: BP_T6Mine_C", costCenters.Message);
        Assert.Contains(findings, f => f.Code == "trade.idle-stock.Sugar");
        Assert.All(findings.Where(f => f.Category is "Economy" or "Trade"), f => Assert.NotEmpty(f.Message));
    }

    [Trait("Category", "RealSave")]
    [Fact]
    public void Build_ReferenceSave_ExposesChartData()
    {
        var economy = IslandSnapshotBuilder.Build(T6SaveReader.Read(FindSample())).Economy;

        Assert.Equal((1934, 9), (economy.Year!.Value, economy.Month!.Value));
        Assert.Equal(61, economy.RevenueHistory.Count);
        Assert.Equal(61, economy.ExpenseHistory.Count);
        Assert.Equal((1929, 9), economy.DateOf(economy.RevenueHistory[0].X));
        Assert.Equal(237_658, economy.RevenueByCategory["Exports"]);
        Assert.Equal(111_432, economy.ExpensesByCategory["Wages"]);
        Assert.Equal(48, economy.Goods.Single(g => g.Resource == "Gold").PriceHistory.Count);
    }
}
