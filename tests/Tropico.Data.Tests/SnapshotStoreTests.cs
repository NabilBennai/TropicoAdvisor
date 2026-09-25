using Microsoft.Data.Sqlite;
using Tropico.Analysis;
using Tropico.Localization;

namespace Tropico.Data.Tests;

public sealed class SnapshotStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tropico-data-" + Guid.NewGuid())).FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true); // fails if the store kept the file open

    private string DbPath => Path.Combine(_directory, "nested", "history.db");

    private static SnapshotMetrics Metrics(int buildings = 10, double? treasury = 1000) => new(
        buildings, new Dictionary<string, int> { ["BP_T6Mine_C"] = 4, ["BP_T6House_C"] = 6 },
        1000, 800, 200, 50, 12, 41.5, treasury, 3_000, 1_200, 800, 300,
        new Dictionary<string, double> { ["Capitalists"] = -13.5, ["Communists"] = 13.1 },
        new Dictionary<string, double> { ["Coal"] = 33_409.5 });

    private static StoredSnapshot Snapshot(string island = "island-A", int day = 12_000, string name = "save", int buildings = 10) => new(
        null, island, name, day, 1934, 9, new DateTimeOffset(2026, 9, 25, 12, 30, 0, TimeSpan.FromHours(2)), Metrics(buildings),
        [
            new StoredFinding("economy.wage-burden", Severity.Info, Confidence.Probable, "Economy", LocalizedText.Of("finding.wageBurden", 68.5)),
            new StoredFinding("trade.idle-stock.Sugar", Severity.Warning, Confidence.Uncertain, "Trade",
                LocalizedText.Of("suggest.idleStock", 1, TextList.Of([Term.Faction("Capitalists"), "Mexico"]))),
        ]);

    [Fact]
    public void SaveThenGet_RoundTripsEverything()
    {
        var store = new SqliteSnapshotStore(DbPath);

        var id = store.Save(Snapshot());
        var loaded = store.Get(id)!;

        Assert.Equal(id, loaded.Id);
        Assert.Equal(("island-A", "save", 12_000, 1934, 9), (loaded.IslandKey, loaded.SaveName, loaded.GameDay, loaded.GameYear, loaded.GameMonth));
        Assert.Equal(new DateTimeOffset(2026, 9, 25, 12, 30, 0, TimeSpan.FromHours(2)), loaded.AnalyzedAt);
        Assert.Equal(Metrics().Buildings, loaded.Metrics.Buildings);
        Assert.Equal(6, loaded.Metrics.BuildingsByClass["BP_T6House_C"]);
        Assert.Equal(-13.5, loaded.Metrics.FactionStanding["Capitalists"]);
        Assert.Equal(33_409.5, loaded.Metrics.ResourceStock["Coal"]);
        Assert.Equal((41.5, 1000.0, 12.0), (loaded.Metrics.Happiness!.Value, loaded.Metrics.Treasury!.Value, loaded.Metrics.HomelessFamilies!.Value));

        Assert.Equal(["economy.wage-burden", "trade.idle-stock.Sugar"], loaded.Findings.Select(f => f.Code));
        Assert.Equal((Severity.Warning, Confidence.Uncertain, "Trade"), (loaded.Findings[1].Severity, loaded.Findings[1].Confidence, loaded.Findings[1].Category));
    }

    [Fact]
    public void StoredFindings_KeepTheirLocalizableMessage()
    {
        var store = new SqliteSnapshotStore(DbPath);
        var loaded = store.Get(store.Save(Snapshot()))!;

        var french = Localizer.For(Languages.French);
        Assert.Equal("Les salaires représentent 69 % des dépenses.", loaded.Findings[0].Message.Render(french));
        Assert.Equal("Wages are 69 % of expenses.", loaded.Findings[0].Message.Render(Localizer.English));
    }

    [Fact]
    public void SavingTheSameIslandAndGameDayAgain_ReplacesTheSnapshot()
    {
        var store = new SqliteSnapshotStore(DbPath);

        var first = store.Save(Snapshot(buildings: 10, name: "first"));
        var second = store.Save(Snapshot(buildings: 12, name: "second"));

        Assert.Equal(first, second);
        var only = Assert.Single(store.List("island-A"));
        Assert.Equal("second", only.SaveName);
        Assert.Equal(12, store.Get(first)!.Metrics.Buildings);
    }

    [Fact]
    public void List_IsOrderedFromTheMostRecentGameDay_AndIsolatedPerIsland()
    {
        var store = new SqliteSnapshotStore(DbPath);
        store.Save(Snapshot(day: 10_000, name: "old"));
        store.Save(Snapshot(day: 12_000, name: "new"));
        store.Save(Snapshot(day: 11_000, name: "middle"));
        store.Save(Snapshot(island: "island-B", day: 99_999, name: "other island"));

        Assert.Equal(["new", "middle", "old"], store.List("island-A").Select(s => s.SaveName));
        Assert.Equal(["other island"], store.List("island-B").Select(s => s.SaveName));
        Assert.Empty(store.List("nowhere"));
    }

    [Fact]
    public void GetPrevious_ReturnsTheLatestStrictlyEarlierSnapshotOfTheSameIsland()
    {
        var store = new SqliteSnapshotStore(DbPath);
        store.Save(Snapshot(day: 10_000, name: "old"));
        store.Save(Snapshot(day: 11_000, name: "middle"));
        store.Save(Snapshot(day: 12_000, name: "new"));
        store.Save(Snapshot(island: "island-B", day: 11_500, name: "other island"));

        Assert.Equal("middle", store.GetPrevious("island-A", 12_000)!.SaveName);
        Assert.Equal("old", store.GetPrevious("island-A", 11_000)!.SaveName);   // strictly earlier: never itself
        Assert.Null(store.GetPrevious("island-A", 10_000));
        Assert.Equal("new", store.GetPrevious("island-A", 20_000)!.SaveName);
    }

    [Fact]
    public void Get_UnknownId_ReturnsNull() => Assert.Null(new SqliteSnapshotStore(DbPath).Get(12345));

    [Fact]
    public void ANewStoreOnTheSameFile_SeesTheEarlierData_AndTheSchemaVersionIsRecorded()
    {
        new SqliteSnapshotStore(DbPath).Save(Snapshot(name: "kept"));

        Assert.Equal("kept", Assert.Single(new SqliteSnapshotStore(DbPath).List("island-A")).SaveName);

        using var connection = new SqliteConnection($"Data Source={DbPath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        Assert.Equal(1L, command.ExecuteScalar());
    }
}
