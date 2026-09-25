using Tropico.SaveParser;

namespace Tropico.Analysis.Tests;

public class IslandSnapshotBuilderTests
{
    private const string SampleName = "Trop6_Sav_urss Oct, 1934.t6sav";

    private static string FindSample()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "samples", "private", SampleName);
            if (File.Exists(candidate)) return candidate;
        }

        Assert.Fail($"Sample save not found: put '{SampleName}' in samples/private/.");
        return "";
    }

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

    [Fact]
    public void Analyze_ReferenceSave_ReportsFallingTreasuryAndLowConfidenceUnemployment()
    {
        var report = new IslandAnalyzer().Analyze(T6SaveReader.Read(FindSample()));

        Assert.DoesNotContain(report.Findings, f => f.Code == "buildings.condition"); // all 297 buildings are Built
        Assert.Contains(report.Findings, f => f.Code == "treasury.trend");
        var unemployment = Assert.Single(report.Findings, f => f.Code == "population.unemployment");
        Assert.Equal(Confidence.Uncertain, unemployment.Confidence);
    }
}
