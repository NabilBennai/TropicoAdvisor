using Tropico.Analysis;
using Tropico.Data;
using Tropico.Desktop.Localization;
using Tropico.Desktop.Services;
using Tropico.Desktop.ViewModels;
using Tropico.Localization;

namespace Tropico.Desktop.Tests;

public sealed class HistoryServiceTests : IDisposable
{
    private readonly string _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tropico-hist-" + Guid.NewGuid())).FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static IslandReport Report(int? gameDay, double treasury = 1000, int buildings = 10, string island = "island-A", params Finding[] findings) => new(
        new IslandSnapshot("save", "t6", new BuildingSummary(buildings, new Dictionary<string, int> { ["BP_T6Mine_C"] = buildings }, new Dictionary<string, int>()),
            new PopulationSummary(1000, 200, 800, 0, 0), treasury, 0, [], [], [], [])
        {
            MapId = island,
            GameDay = gameDay,
            EconomyData = new EconomySnapshot(12, [], [], 3_000, 1_200, 800, 300, 0, 0, new Dictionary<string, long>()) { Year = 1934, Month = 9 },
        },
        findings);

    private HistoryService Service() => new(new SqliteSnapshotStore(Path.Combine(_directory, "h.db")), () => new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task TheFirstAnalysisOfAnIsland_IsTheEarliest_AndIsRecorded()
    {
        var result = await Service().RecordAsync(Report(12_000));

        Assert.Equal(HistoryStatus.Earliest, result.Status);
        Assert.Null(result.Evolution);
        Assert.Single(result.Snapshots);
    }

    [Fact]
    public async Task ALaterAnalysis_IsComparedWithTheEarlierOne()
    {
        var service = Service();
        await service.RecordAsync(Report(12_000, treasury: 1_000, buildings: 10));

        var result = await service.RecordAsync(Report(12_390, treasury: 1_500, buildings: 12));

        Assert.Equal(HistoryStatus.Compared, result.Status);
        Assert.Equal(13, result.Evolution!.MonthsBetween);
        Assert.Equal(500, result.Evolution.Metrics.Single(m => m.Metric == "treasury").Delta);
        Assert.Equal(2, result.Evolution.BuildingChanges.Single().Delta);
        Assert.Equal(2, result.Snapshots.Count);
    }

    [Fact]
    public async Task AnalysingTheSameSaveAgain_DoesNotCompareItWithItself()
    {
        var service = Service();
        await service.RecordAsync(Report(12_000));

        var again = await service.RecordAsync(Report(12_000, treasury: 5_000));

        Assert.Equal(HistoryStatus.Earliest, again.Status);
        Assert.Single(again.Snapshots); // replaced, not duplicated
    }

    [Fact]
    public async Task OpeningAnOlderSaveLater_IsComparedWithItsOwnPredecessor()
    {
        var service = Service();
        await service.RecordAsync(Report(10_000));
        await service.RecordAsync(Report(12_000));

        var old = await service.RecordAsync(Report(11_000));

        Assert.Equal(HistoryStatus.Compared, old.Status);
        Assert.Equal(10_000, old.Evolution!.Previous.GameDay); // never the newer 12,000 one
        Assert.Equal(3, old.Snapshots.Count);
    }

    [Fact]
    public async Task DifferentIslands_AreKeptApart()
    {
        var service = Service();
        await service.RecordAsync(Report(12_000, island: "island-A"));

        var other = await service.RecordAsync(Report(12_500, island: "island-B"));

        Assert.Equal(HistoryStatus.Earliest, other.Status);
        Assert.Single(other.Snapshots);
    }

    [Fact]
    public async Task ASaveWithoutGameDate_CannotBeCompared()
    {
        var result = await Service().RecordAsync(Report(null));

        Assert.Equal(HistoryStatus.NoGameDate, result.Status);
    }

    [Fact]
    public async Task AnUnusableHistoryFile_NeverStopsTheAnalysis()
    {
        var blocked = Path.Combine(_directory, "folder-not-file");
        Directory.CreateDirectory(blocked);
        var service = new HistoryService(new SqliteSnapshotStore(blocked)); // a directory cannot be opened as a database

        var result = await service.RecordAsync(Report(12_000));

        Assert.Equal(HistoryStatus.Unavailable, result.Status);
    }
}

public class EvolutionViewModelTests
{
    private static StoredFinding Stored(string code, string key = "finding.balance.negativeMonths") =>
        new(code, Severity.Warning, Confidence.Probable, "Economy", LocalizedText.Of(key, 3, 12));

    private static StoredSnapshot Snapshot(int day, SnapshotMetrics metrics, params StoredFinding[] findings) =>
        new(null, "island", "old save", day, 1932, 3, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), metrics, findings);

    private static SnapshotMetrics Metrics(
        int buildings, double treasury, double unemployed, double happiness, Dictionary<string, int>? byClass = null, Dictionary<string, double>? factions = null) => new(
        buildings, byClass ?? new Dictionary<string, int>(), 1000, 800, 200, unemployed, 12, happiness, treasury, 3_000, 1_200, 800, 300,
        factions ?? new Dictionary<string, double>(), new Dictionary<string, double>());

    private static HistoryResult Compared()
    {
        var previous = Snapshot(12_000, Metrics(100, 1_000_000, 50, 40.0,
            new Dictionary<string, int> { ["BP_T6Mine_C"] = 10, ["BP_T6PileOfBS_C"] = 4 }, new Dictionary<string, double> { ["Capitalists"] = -3.0, ["Communists"] = 5.0 }),
            Stored("old.problem"), Stored("still.there"));
        var current = previous with
        {
            SaveName = "new save", GameDay = 12_390, GameYear = 1933, GameMonth = 4,
            Metrics = Metrics(112, 1_250_000, 40, 45.5,
                new Dictionary<string, int> { ["BP_T6Mine_C"] = 14, ["BP_T6PileOfBS_C"] = 3 }, new Dictionary<string, double> { ["Capitalists"] = -13.5, ["Communists"] = 5.0 }),
            Findings = [Stored("still.there"), Stored("new.problem", "finding.wageBurden")],
        };
        return new HistoryResult(HistoryStatus.Compared, SnapshotComparison.Compare(previous, current),
            [new SnapshotInfo(2, "new save", 12_390, 1933, 4, new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero)), new SnapshotInfo(1, "old save", 12_000, 1932, 3, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero))]);
    }

    [Fact]
    public void Metrics_ShowBeforeNowAndASignedChange_ColouredByWhetherItIsAnImprovement()
    {
        var vm = new EvolutionViewModel(Compared());
        var rows = vm.Metrics.ToDictionary(r => r.Name);

        Assert.Equal(("100", "112", "+12"), (rows["Buildings"].Before, rows["Buildings"].Now, rows["Buildings"].Change));
        Assert.True(rows["Buildings"].IsGood);
        Assert.Equal("+250,000", rows["Treasury"].Change);
        Assert.True(rows["Treasury"].IsGood);
        Assert.Equal("-10", rows["Unemployed"].Change);
        Assert.True(rows["Unemployed"].IsGood);            // fewer unemployed is an improvement
        Assert.Equal("+5.5", rows["Happiness"].Change);      // one decimal for happiness
        Assert.Equal("0", rows["Citizens"].Change);
        Assert.False(rows["Citizens"].IsGood || rows["Citizens"].IsBad); // no change: neutral
    }

    [Fact]
    public void AHigherCostIsBad_AndFactionDropsAreBad()
    {
        var current = Compared().Evolution!;
        var worse = current with { Metrics = [new MetricChange("wages", 100, 150), new MetricChange("homelessFamilies", 5, 2)] };

        var rows = new EvolutionViewModel(new HistoryResult(HistoryStatus.Compared, worse, [])).Metrics;

        Assert.True(rows[0].IsBad);   // more wages
        Assert.True(rows[1].IsGood);  // fewer homeless families

        var factions = new EvolutionViewModel(Compared()).FactionChanges;
        var capitalists = Assert.Single(factions);
        Assert.Equal(("Capitalists", "-3.0", "-13.5", "-10.5"), (capitalists.Name, capitalists.Before, capitalists.Now, capitalists.Change));
        Assert.True(capitalists.IsBad);
    }

    [Fact]
    public void BuildingChanges_UseReadableNames_LargestFirst()
    {
        var rows = new EvolutionViewModel(Compared()).BuildingChanges;

        Assert.Equal(["Mine", "Pile Of BS"], rows.Select(r => r.Name));
        Assert.Equal("+4", rows[0].Change);
        Assert.Equal("-1", rows[1].Change);
    }

    [Fact]
    public void NewAndResolvedSuggestions_AreRenderedFromTheirStoredMessages()
    {
        var vm = new EvolutionViewModel(Compared());

        Assert.Equal(["Wages are 3 % of expenses."], vm.NewFindings.Select(f => f.Message)); // args 3, 12 of the wageBurden template
        Assert.Equal(["3 of the last 12 monthly balances are negative."], vm.ResolvedFindings.Select(f => f.Message));
        Assert.True(vm.HasNewFindings && vm.HasResolvedFindings);
    }

    [Fact]
    public void TheComparisonSentence_AndTheSavesList_AreFormatted()
    {
        var vm = new EvolutionViewModel(Compared());

        Assert.True(vm.HasComparison);
        Assert.Equal("Compared with old save (Mar 1932, 13 months earlier)", vm.ComparedWith);
        Assert.Equal(["new save", "old save"], vm.Snapshots.Select(s => s.Save));
        Assert.Equal("Apr 1933", vm.Snapshots[0].GameDate);
    }

    [Theory]
    [InlineData(HistoryStatus.Earliest, "This is the earliest analysed save")]
    [InlineData(HistoryStatus.NoGameDate, "no game date")]
    [InlineData(HistoryStatus.Unavailable, "history file could not be used")]
    public void WithoutAComparison_ExplainsWhy(HistoryStatus status, string expected)
    {
        var vm = new EvolutionViewModel(HistoryResult.Of(status));

        Assert.False(vm.HasComparison);
        Assert.Contains(expected, vm.Message);
        Assert.Empty(vm.Metrics);
    }

    [Fact]
    public void EverythingIsTranslated_IncludingTheStoredFindingsOfTheOlderSave()
    {
        var french = new EvolutionViewModel(Compared(), Localizer.For(Languages.French));

        Assert.Equal("Comparé à old save (mars 1932, 13 mois plus tôt)".Replace("mars", french.ComparedWith.Contains("mars") ? "mars" : "mar"), french.ComparedWith.Replace("mar. ", "mars ").Replace("mars.", "mars"));
        Assert.Equal("Bâtiments", french.Metrics[0].Name);
        Assert.Equal("Salaires", french.Metrics.Single(r => r.Name == "Salaires").Name);
        Assert.Equal(["3 des 12 derniers bilans mensuels sont négatifs."], french.ResolvedFindings.Select(f => f.Message));

        var arabic = new EvolutionViewModel(Compared(), Localizer.For(Languages.Arabic));
        Assert.Equal("المباني", arabic.Metrics[0].Name);
        Assert.Matches(@"[\u0600-\u06FF]", arabic.ComparedWith);
        Assert.Equal("+12", arabic.Metrics[0].Change); // Latin digits and signs in every language
    }
}

public class MainViewModelHistoryTests
{
    private sealed class FakeHistory : IHistoryService
    {
        public int Recorded { get; private set; }

        public Task<HistoryResult> RecordAsync(IslandReport report, CancellationToken cancellationToken = default)
        {
            Recorded++;
            return Task.FromResult(HistoryResult.Of(HistoryStatus.Earliest));
        }
    }

    private sealed class Analyzer(IslandReport report) : ISaveAnalysisService
    {
        public IReadOnlyList<SaveFileItem> ListSaves() => [new(@"C:\saves\a.t6sav", "a", new DateTime(2026, 1, 1), 1)];

        public Task<IslandReport> AnalyzeAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(report);
    }

    private static IslandReport Report() => new(
        new IslandSnapshot("island", "t6", new BuildingSummary(3, new Dictionary<string, int>(), new Dictionary<string, int>()),
            new PopulationSummary(1000, 200, 800, 0, 0), 12_000, 0, [], [], [], []),
        []);

    [Fact]
    public async Task EachAnalysis_IsRecordedOnce_AndTheEvolutionTabFollowsTheLanguage()
    {
        var history = new FakeHistory();
        var vm = new MainViewModel(new Analyzer(Report()), new Translator(), null, history: history);

        await vm.RefreshCommand.ExecuteAsync(null);
        for (var i = 0; i < 100 && vm.Report is null; i++) await Task.Delay(10);

        Assert.Equal(1, history.Recorded);
        Assert.Contains("earliest analysed save", vm.Report!.EvolutionTab.Message);

        vm.SelectedLanguage = Languages.Spanish;

        Assert.Equal(1, history.Recorded); // switching language only re-renders the stored result
        Assert.Contains("partida analizada más antigua", vm.Report!.EvolutionTab.Message);
    }
}
