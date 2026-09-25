using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Tropico.Analysis;
using Tropico.Data;

namespace Tropico.Desktop.Services;

public enum HistoryStatus
{
    /// <summary>An earlier snapshot of the island exists: <see cref="HistoryResult.Evolution"/> is set.</summary>
    Compared,

    /// <summary>This is the earliest analysed save of the island.</summary>
    Earliest,

    /// <summary>The save has no game date, so it cannot be ordered against others.</summary>
    NoGameDate,

    /// <summary>The history file could not be read or written; the analysis itself is unaffected.</summary>
    Unavailable,
}

public sealed record HistoryResult(HistoryStatus Status, Evolution? Evolution, IReadOnlyList<SnapshotInfo> Snapshots)
{
    public static HistoryResult Of(HistoryStatus status) => new(status, null, []);
}

public interface IHistoryService
{
    /// <summary>Keeps a snapshot of the analysis and compares it with the previous one of the same island.</summary>
    Task<HistoryResult> RecordAsync(IslandReport report, CancellationToken cancellationToken = default);
}

public sealed class HistoryService(ISnapshotStore store, Func<DateTimeOffset>? clock = null) : IHistoryService
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.Now);

    public Task<HistoryResult> RecordAsync(IslandReport report, CancellationToken cancellationToken = default) =>
        Task.Run(() => Record(report), cancellationToken);

    private HistoryResult Record(IslandReport report)
    {
        try
        {
            var snapshot = SnapshotFactory.From(report, _clock());
            if (snapshot is null) return HistoryResult.Of(HistoryStatus.NoGameDate);

            store.Save(snapshot);
            var previous = store.GetPrevious(snapshot.IslandKey, snapshot.GameDay);
            var snapshots = store.List(snapshot.IslandKey);

            return previous is null
                ? new HistoryResult(HistoryStatus.Earliest, null, snapshots)
                : new HistoryResult(HistoryStatus.Compared, SnapshotComparison.Compare(previous, snapshot), snapshots);
        }
        catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            // history is a bonus: a broken or locked file must never stop the analysis from being shown
            return HistoryResult.Of(HistoryStatus.Unavailable);
        }
    }
}
