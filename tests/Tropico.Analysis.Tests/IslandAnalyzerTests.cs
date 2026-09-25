namespace Tropico.Analysis.Tests;

public class IslandAnalyzerTests
{
    private static IslandSnapshot Snapshot(
        Dictionary<string, int>? states = null,
        double[]? treasury = null,
        double[]? months = null,
        int? adults = 1000,
        double[]? unemployed = null,
        double[]? homeless = null) => new(
            "test", "t6-test",
            new BuildingSummary(10, new Dictionary<string, int>(), states ?? new Dictionary<string, int> { ["Built"] = 10 }),
            new PopulationSummary(1300, 200, adults, 0, 0),
            treasury?.LastOrDefault(), 0,
            (treasury ?? []).Select((v, i) => new TimePoint(i, v)).ToList(),
            months ?? [], unemployed ?? [], homeless ?? []);

    private static IReadOnlyList<Finding> Run(IslandSnapshot s) => new IslandAnalyzer().Analyze(s).Findings;

    [Fact]
    public void HealthyIsland_ProducesNoWarning()
    {
        Assert.DoesNotContain(Run(Snapshot(treasury: [100, 200], months: [5, 6])), f => f.Severity != Severity.Info);
    }

    [Fact]
    public void BuildingCondition_ReportsDamagedBrokenRubble()
    {
        var findings = Run(Snapshot(new() { ["Built"] = 7, ["Damaged"] = 2, ["Broken"] = 1 }));

        var finding = Assert.Single(findings, f => f.Code == "buildings.condition");
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(Confidence.Verified, finding.Confidence);
        Assert.Contains("3 of 10", finding.Message);
        Assert.Contains("2 Damaged", finding.Message);
    }

    [Fact]
    public void BuildingCondition_IgnoresUnknownState()
    {
        Assert.DoesNotContain(
            Run(Snapshot(new() { ["Built"] = 9, [IslandSnapshotBuilder.UnknownState] = 1 })),
            f => f.Code == "buildings.condition");
    }

    [Fact]
    public void TreasuryTrend_FallingTreasury_IsWarningOverTheWindow()
    {
        // 30 samples falling by 10 each: compares the last with the sample 12 earlier
        var treasury = Enumerable.Range(0, 30).Select(i => 1000.0 - i * 10).ToArray();

        var finding = Assert.Single(Run(Snapshot(treasury: treasury)), f => f.Code == "treasury.trend");

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(Confidence.Probable, finding.Confidence);
        Assert.Contains("decreased by 120", finding.Message);
        Assert.Contains("last 12 samples", finding.Message);
    }

    [Fact]
    public void TreasuryTrend_ShortHistory_UsesAvailableSamples()
    {
        var finding = Assert.Single(Run(Snapshot(treasury: [100, 150, 200])), f => f.Code == "treasury.trend");

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Contains("increased by 100", finding.Message);
        Assert.Contains("last 2 samples", finding.Message);
    }

    [Fact]
    public void MonthlyBalance_HalfNegative_IsWarning()
    {
        var finding = Assert.Single(Run(Snapshot(months: [-1, 5, -2, 6])), f => f.Code == "balance.negative-months");

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains("2 of the last 4", finding.Message);
    }

    [Fact]
    public void Unemployment_IsAlwaysLowConfidence_AndWarnsAboveThreshold()
    {
        var high = Assert.Single(Run(Snapshot(unemployed: [60, 70, 1])), f => f.Code == "population.unemployment"); // 131 / 1000
        var low = Assert.Single(Run(Snapshot(unemployed: [20, 10, 0])), f => f.Code == "population.unemployment");

        Assert.Equal((Severity.Warning, Confidence.Uncertain), (high.Severity, high.Confidence));
        Assert.Equal((Severity.Info, Confidence.Uncertain), (low.Severity, low.Confidence));
    }

    [Fact]
    public void Unemployment_WithoutAdults_IsSkipped()
    {
        Assert.DoesNotContain(Run(Snapshot(adults: null, unemployed: [5])), f => f.Code == "population.unemployment");
    }

    [Fact]
    public void HomelessFamilies_ReportedOnlyWhenPresent()
    {
        Assert.Contains(Run(Snapshot(homeless: [45, 3])), f => f.Code == "housing.homeless-families");
        Assert.DoesNotContain(Run(Snapshot(homeless: [0])), f => f.Code == "housing.homeless-families");
    }

    [Fact]
    public void Findings_AreOrderedBySeverityThenConfidence()
    {
        var findings = Run(Snapshot(new() { ["Built"] = 9, ["Rubble"] = 1 }, treasury: [300, 200], unemployed: [200, 0, 0]));

        Assert.Equal(findings.OrderByDescending(f => f.Severity).ThenByDescending(f => f.Confidence), findings);
        Assert.Equal("buildings.condition", findings[0].Code);
    }
}
