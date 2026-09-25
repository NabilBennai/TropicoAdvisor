using Tropico.Analysis;
using Tropico.Desktop.Services;
using Tropico.Desktop.ViewModels;

namespace Tropico.Desktop.Tests;

public class MainViewModelTests
{
    private sealed class FakeService(IReadOnlyList<SaveFileItem> saves) : ISaveAnalysisService
    {
        public Dictionary<string, TaskCompletionSource<IslandReport>> Pending { get; } = [];

        public Func<string, Task<IslandReport>>? Analyze { get; set; }

        public IReadOnlyList<SaveFileItem> ListSaves() => saves;

        public Task<IslandReport> AnalyzeAsync(string path, CancellationToken cancellationToken = default)
        {
            if (Analyze is not null) return Analyze(path);

            var source = new TaskCompletionSource<IslandReport>(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending[path] = source;
            return source.Task;
        }
    }

    private static SaveFileItem Save(string name) => new($@"C:\saves\{name}.t6sav", name, new DateTime(2026, 1, 1), 1024 * 1024);

    private static IslandReport Report(string name, params Finding[] findings) => new(
        new IslandSnapshot(
            name, "t6-test",
            new BuildingSummary(3, new Dictionary<string, int> { ["BP_A_C"] = 2, ["BP_B_C"] = 1 }, new Dictionary<string, int> { ["Built"] = 3 }),
            new PopulationSummary(1300, 200, 900, 0, 0),
            1234.5, 0, [], [], [], []),
        findings);

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "condition not reached in time");
    }

    [Fact]
    public async Task Refresh_SelectsNewestSaveAndShowsItsReport()
    {
        var service = new FakeService([Save("newest"), Save("older")]) { Analyze = path => Task.FromResult(Report(Path.GetFileNameWithoutExtension(path))) };
        var viewModel = new MainViewModel(service);

        await viewModel.RefreshCommand.ExecuteAsync(null);
        await WaitUntil(() => viewModel.Report is not null);

        Assert.Equal(2, viewModel.Saves.Count);
        Assert.Equal("newest", viewModel.SelectedSave!.Name);
        Assert.Equal("newest", viewModel.Report!.Title);
        Assert.Equal("3", viewModel.Report.Buildings);
        Assert.Equal("BP_A_C: 2", viewModel.Report.TopBuildings[0]);
        Assert.False(viewModel.IsBusy);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Refresh_WithoutSaves_ShowsStatusMessage()
    {
        var viewModel = new MainViewModel(new FakeService([]));

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Null(viewModel.Report);
        Assert.Equal("No Tropico 6 save found.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Refresh_WhenAnalysisFails_ShowsErrorInsteadOfThrowing()
    {
        var service = new FakeService([Save("broken")]) { Analyze = _ => Task.FromException<IslandReport>(new InvalidDataException("bad zlib")) };
        var viewModel = new MainViewModel(service);

        await viewModel.RefreshCommand.ExecuteAsync(null);
        await WaitUntil(() => viewModel.ErrorMessage is not null);

        Assert.Null(viewModel.Report);
        Assert.Contains("broken", viewModel.ErrorMessage);
        Assert.Contains("bad zlib", viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task SelectingAnotherSave_IgnoresTheStaleResultOfTheFirst()
    {
        var first = Save("first");
        var second = Save("second");
        var service = new FakeService([first, second]);
        var viewModel = new MainViewModel(service);

        await viewModel.RefreshCommand.ExecuteAsync(null); // selects "first", analysis pending
        Assert.True(viewModel.IsBusy);

        viewModel.SelectedSave = second;
        service.Pending[second.Path].SetResult(Report("second"));
        await WaitUntil(() => viewModel.Report is not null);

        service.Pending[first.Path].SetResult(Report("first")); // late result of the superseded load
        await Task.Delay(50);

        Assert.Equal("second", viewModel.Report!.Title);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public void ReportViewModel_MapsFindingsToDisplayFlags()
    {
        var report = new IslandReportViewModel(Report("x",
            new Finding("a", Severity.Warning, Confidence.Uncertain, "warn"),
            new Finding("b", Severity.Info, Confidence.Verified, "info")));

        Assert.True(report.HasFindings);
        Assert.Equal("Warning · Uncertain", report.Findings[0].Label);
        Assert.True(report.Findings[0].IsWarning);
        Assert.True(report.Findings[0].IsUncertain);
        Assert.False(report.Findings[1].IsWarning);
        Assert.False(report.Findings[1].IsUncertain);
        Assert.Equal("?", new IslandReportViewModel(new IslandReport(Report("y").Snapshot with { Treasury = null }, [])).Treasury);
    }
}
