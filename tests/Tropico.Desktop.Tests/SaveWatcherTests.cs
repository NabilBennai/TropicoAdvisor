using Tropico.Analysis;
using Tropico.Desktop.Services;
using Tropico.Desktop.ViewModels;

namespace Tropico.Desktop.Tests;

public sealed class SaveWatcherTests : IDisposable
{
    private sealed class ManualWatcher : ISaveWatcher
    {
        public event Action? SaveWritten;

        public void Raise() => SaveWritten?.Invoke();

        public void Dispose() { }
    }

    private sealed class Folder(List<SaveFileItem> saves) : ISaveAnalysisService
    {
        public List<string> Analysed { get; } = [];

        public IReadOnlyList<SaveFileItem> ListSaves() => saves.OrderByDescending(s => s.LastWriteTime).ToList();

        public Task<IslandReport> AnalyzeAsync(string path, CancellationToken cancellationToken = default)
        {
            Analysed.Add(path);
            return Task.FromResult(new IslandReport(
                new IslandSnapshot(Path.GetFileNameWithoutExtension(path), "t6", new BuildingSummary(1, new Dictionary<string, int>(), new Dictionary<string, int>()),
                    new PopulationSummary(10, 2, 8, 0, 0), 1, 0, [], [], [], []), []));
        }
    }

    private readonly string _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tropico-watch-" + Guid.NewGuid())).FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static SaveFileItem Save(string name, int hour) => new($@"C:\saves\{name}.t6sav", name, new DateTime(2026, 1, 1, hour, 0, 0), 1);

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "condition not reached in time");
    }

    [Fact]
    public async Task ANewSave_IsAnalysedAndShown_EvenWhenAnOlderOneWasSelected()
    {
        var saves = new List<SaveFileItem> { Save("old", 1) };
        var folder = new Folder(saves);
        var watcher = new ManualWatcher();
        var vm = new MainViewModel(folder, new Desktop.Localization.Translator(), null, watcher: watcher);
        await vm.RefreshCommand.ExecuteAsync(null);
        await WaitUntil(() => vm.Report is not null);

        saves.Add(Save("new", 2));
        watcher.Raise();

        await WaitUntil(() => vm.Report?.Title == "new");
        Assert.Equal("new", vm.SelectedSave!.Name);
    }

    [Fact]
    public async Task ARewrittenSave_IsAnalysedAgain()
    {
        var folder = new Folder([Save("only", 1)]);
        var watcher = new ManualWatcher();
        var vm = new MainViewModel(folder, new Desktop.Localization.Translator(), null, watcher: watcher);
        await vm.RefreshCommand.ExecuteAsync(null);
        await WaitUntil(() => vm.Report is not null);

        watcher.Raise();

        await WaitUntil(() => folder.Analysed.Count == 2);
    }

    [Fact]
    public async Task TheFileWatcher_MergesTheBurstOfWritesIntoOneSignal_AndIgnoresTheProfile()
    {
        using var watcher = new FileSaveWatcher(_directory, TimeSpan.FromMilliseconds(150));
        var signals = 0;
        watcher.SaveWritten += () => Interlocked.Increment(ref signals);

        await File.WriteAllTextAsync(Path.Combine(_directory, "Trop6_Profile.t6sav"), "x");
        await Task.Delay(500);
        Assert.Equal(0, signals);

        var save = Path.Combine(_directory, "Trop6_Sav_a.t6sav");
        for (var i = 0; i < 5; i++)
        {
            await File.AppendAllTextAsync(save, "chunk");
            await Task.Delay(20);
        }

        await WaitUntil(() => signals > 0);
        await Task.Delay(400);
        Assert.Equal(1, signals);
    }

    [Fact]
    public void AMissingFolder_IsNotAnError()
    {
        using var watcher = new FileSaveWatcher(Path.Combine(_directory, "nope"));
    }
}
