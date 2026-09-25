using Tropico.Analysis;
using Tropico.Localization;
using Tropico.SaveParser;

namespace Tropico.Data.Tests;

public class SnapshotComparisonTests
{
    private static StoredFinding Finding(string code) => new(code, Severity.Info, Confidence.Probable, "Economy", LocalizedText.Raw(code));

    private static SnapshotMetrics Metrics(
        int buildings, double? treasury, Dictionary<string, int>? byClass = null, double? happiness = 40, Dictionary<string, double>? factions = null) => new(
        buildings, byClass ?? new Dictionary<string, int>(), 1000, 800, 200, 50, 12, happiness, treasury, 3_000, 1_200, 800, 300,
        factions ?? new Dictionary<string, double>(), new Dictionary<string, double>());

    private static StoredSnapshot Snapshot(int day, SnapshotMetrics metrics, params StoredFinding[] findings) =>
        new(null, "island", "save", day, 1934, 9, DateTimeOffset.UnixEpoch, metrics, findings);

    [Fact]
    public void MetricChanges_ReportTheDifferenceAndTheElapsedMonths()
    {
        var evolution = SnapshotComparison.Compare(
            Snapshot(12_000, Metrics(100, 1_000_000)),
            Snapshot(12_390, Metrics(110, 1_250_000, happiness: 45)));

        Assert.Equal(13, evolution.MonthsBetween); // 390 days / 30
        var byName = evolution.Metrics.ToDictionary(m => m.Metric);
        Assert.Equal(10, byName["buildings"].Delta);
        Assert.Equal(250_000, byName["treasury"].Delta);
        Assert.Equal(5, byName["happiness"].Delta);
        Assert.Equal(0, byName["citizens"].Delta);
        Assert.Equal(["buildings", "citizens", "adults", "children", "unemployed", "homelessFamilies", "happiness", "treasury", "yearlyRevenue", "yearlyExpenses", "wages", "upkeep"],
            evolution.Metrics.Select(m => m.Metric));
    }

    [Fact]
    public void AMissingFigure_HasNoDelta()
    {
        var evolution = SnapshotComparison.Compare(Snapshot(1_000, Metrics(1, null)), Snapshot(2_000, Metrics(1, 500)));

        var treasury = evolution.Metrics.Single(m => m.Metric == "treasury");
        Assert.Null(treasury.Before);
        Assert.Equal(500, treasury.After);
        Assert.Null(treasury.Delta);
    }

    [Fact]
    public void BuildingChanges_ListOnlyClassesThatChanged_LargestFirst_TiesByName()
    {
        var before = new Dictionary<string, int> { ["BP_Mine_C"] = 10, ["BP_House_C"] = 20, ["BP_Chapel_C"] = 1, ["BP_Old_C"] = 2 };
        var after = new Dictionary<string, int> { ["BP_Mine_C"] = 12, ["BP_House_C"] = 20, ["BP_Chapel_C"] = 4, ["BP_New_C"] = 3 };

        var changes = SnapshotComparison.Compare(Snapshot(1_000, Metrics(33, 0, before)), Snapshot(2_000, Metrics(39, 0, after))).BuildingChanges;

        Assert.Equal([("BP_Chapel_C", 3), ("BP_New_C", 3), ("BP_Mine_C", 2), ("BP_Old_C", -2)], changes.Select(c => (c.Name, c.Delta)));
        Assert.DoesNotContain(changes, c => c.Name == "BP_House_C"); // unchanged
    }

    [Fact]
    public void FactionChanges_IgnoreNoise_AndListTheBiggestDropsFirst()
    {
        var before = new Dictionary<string, double> { ["Capitalists"] = -3.0, ["Communists"] = 5.0, ["Militarists"] = 2.0, ["Religious"] = 1.0 };
        var after = new Dictionary<string, double> { ["Capitalists"] = -13.5, ["Communists"] = 5.01, ["Militarists"] = 4.0, ["Religious"] = 1.0 };

        var changes = SnapshotComparison.Compare(Snapshot(1_000, Metrics(1, 0, factions: before)), Snapshot(2_000, Metrics(1, 0, factions: after))).FactionChanges;

        Assert.Equal(["Capitalists", "Militarists"], changes.Select(c => c.Metric)); // 0.01 is below the noise threshold
        Assert.Equal(-10.5, changes[0].Delta!.Value, 6);
    }

    [Fact]
    public void Findings_AreDiffedByCode_IntoNewAndResolved()
    {
        var evolution = SnapshotComparison.Compare(
            Snapshot(1_000, Metrics(1, 0), Finding("kept"), Finding("resolved.one"), Finding("resolved.two")),
            Snapshot(2_000, Metrics(1, 0), Finding("kept"), Finding("new.one")));

        Assert.Equal(["new.one"], evolution.NewFindings.Select(f => f.Code));
        Assert.Equal(["resolved.one", "resolved.two"], evolution.ResolvedFindings.Select(f => f.Code));
    }

    [Fact]
    public void ComparingTwoIslands_IsRejected()
    {
        var other = Snapshot(2_000, Metrics(1, 0)) with { IslandKey = "another island" };

        Assert.Throws<ArgumentException>(() => SnapshotComparison.Compare(Snapshot(1_000, Metrics(1, 0)), other));
    }
}

[Trait("Category", "RealSave")]
public class SnapshotFactoryTests
{
    private const string SampleName = "Trop6_Sav_urss Oct, 1934.t6sav";

    private static readonly Lazy<IslandReport> Urss = new(() =>
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Tropico6", "Saved", "SaveGames", SampleName);
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "samples", "private", SampleName);
            if (File.Exists(candidate)) { path = candidate; break; }
        }

        Assert.True(File.Exists(path), $"Sample save not found: put '{SampleName}' in samples/private/.");
        return new IslandAnalyzer().Analyze(T6SaveReader.Read(path));
    });

    [Fact]
    public void From_ReferenceSave_CapturesTheKeyFiguresOfTheIsland()
    {
        var snapshot = SnapshotFactory.From(Urss.Value, new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero))!;

        Assert.Equal("#RMG#RMG_Map#4M69I4GGODHF0", snapshot.IslandKey); // the map id identifies the island
        Assert.Equal(("urss Oct, 1934", 12_515, 1934, 9), (snapshot.SaveName, snapshot.GameDay, snapshot.GameYear, snapshot.GameMonth));

        var m = snapshot.Metrics;
        Assert.Equal(297, m.Buildings);
        Assert.Equal(85, m.BuildingsByClass["BP_T6CountryHouse_C"]);
        Assert.Equal((1313, 952, 273), (m.Citizens, m.Adults, m.Children));
        Assert.Equal(132.0, m.Unemployed);
        Assert.Equal(48.0, m.HomelessFamilies);
        Assert.Equal(40.38, m.Happiness!.Value, 2);
        Assert.Equal(1453750.25, m.Treasury);
        Assert.Equal(-13.48, m.FactionStanding["Capitalists"], 2);
        Assert.Equal(33_409, m.ResourceStock["Coal"], 0);
        Assert.Equal(Urss.Value.Findings.Count, snapshot.Findings.Count);
    }

    [Fact]
    public void From_ASaveWithoutGameDay_ReturnsNull()
    {
        var report = new IslandReport(Urss.Value.Snapshot with { GameDay = null }, []);

        Assert.Null(SnapshotFactory.From(report, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void AStoredSnapshot_RendersItsFindingsInAnyLanguage_LikeTheLiveOnes()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tropico-data-" + Guid.NewGuid())).FullName;
        try
        {
            var store = new SqliteSnapshotStore(Path.Combine(directory, "h.db"));
            var stored = store.Get(store.Save(SnapshotFactory.From(Urss.Value, DateTimeOffset.UtcNow)!))!;

            foreach (var language in Languages.All)
            {
                var loc = Localizer.For(language);
                var live = Urss.Value.Findings.Select(f => f.MessageIn(loc)).ToList();
                var restored = stored.Findings.Select(f => f.Message.Render(loc)).ToList();
                Assert.Equal(live, restored);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
