using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Tropico.Analysis;
using Tropico.Localization;

namespace Tropico.Data;

public interface ISnapshotStore
{
    /// <summary>Inserts the snapshot, or replaces the one of the same island and game day. Returns its id.</summary>
    long Save(StoredSnapshot snapshot);

    /// <summary>The snapshots of an island, most recent game day first.</summary>
    IReadOnlyList<SnapshotInfo> List(string islandKey);

    StoredSnapshot? Get(long id);

    /// <summary>The latest snapshot of the island that is strictly earlier than <paramref name="gameDay"/>.</summary>
    StoredSnapshot? GetPrevious(string islandKey, int gameDay);
}

/// <summary>
/// Snapshots in a local SQLite file. Every call opens its own short-lived connection (no pooling), so the file is never kept locked.
/// The schema version is kept in <c>PRAGMA user_version</c>.
/// </summary>
public sealed class SqliteSnapshotStore : ISnapshotStore
{
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Json = new();

    private readonly string _connectionString;
    private bool _initialised;

    public SqliteSnapshotStore(string? path = null)
    {
        path ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TropicoAdvisor", "history.db");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
    }

    private sealed record FindingRow(string Code, string Severity, string Confidence, string Category, string Message);

    public long Save(StoredSnapshot snapshot)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO snapshots (island_key, save_name, game_day, game_year, game_month, analyzed_at, metrics_json, findings_json)
            VALUES ($island, $name, $day, $year, $month, $at, $metrics, $findings)
            ON CONFLICT (island_key, game_day) DO UPDATE SET
                save_name = excluded.save_name, game_year = excluded.game_year, game_month = excluded.game_month,
                analyzed_at = excluded.analyzed_at, metrics_json = excluded.metrics_json, findings_json = excluded.findings_json
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$island", snapshot.IslandKey);
        command.Parameters.AddWithValue("$name", snapshot.SaveName);
        command.Parameters.AddWithValue("$day", snapshot.GameDay);
        command.Parameters.AddWithValue("$year", (object?)snapshot.GameYear ?? DBNull.Value);
        command.Parameters.AddWithValue("$month", (object?)snapshot.GameMonth ?? DBNull.Value);
        command.Parameters.AddWithValue("$at", snapshot.AnalyzedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$metrics", JsonSerializer.Serialize(snapshot.Metrics, Json));
        command.Parameters.AddWithValue("$findings", JsonSerializer.Serialize(
            snapshot.Findings.Select(f => new FindingRow(f.Code, f.Severity.ToString(), f.Confidence.ToString(), f.Category, LocalizedTextJson.Serialize(f.Message))), Json));
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public IReadOnlyList<SnapshotInfo> List(string islandKey)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, save_name, game_day, game_year, game_month, analyzed_at FROM snapshots WHERE island_key = $island ORDER BY game_day DESC;";
        command.Parameters.AddWithValue("$island", islandKey);

        var result = new List<SnapshotInfo>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new SnapshotInfo(
                reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3), reader.IsDBNull(4) ? null : reader.GetInt32(4),
                DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture)));
        }

        return result;
    }

    public StoredSnapshot? Get(long id) => QuerySingle("WHERE id = $a", id, null);

    public StoredSnapshot? GetPrevious(string islandKey, int gameDay) =>
        QuerySingle("WHERE island_key = $a AND game_day < $b ORDER BY game_day DESC", islandKey, gameDay);

    private StoredSnapshot? QuerySingle(string filter, object a, object? b)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT id, island_key, save_name, game_day, game_year, game_month, analyzed_at, metrics_json, findings_json FROM snapshots {filter} LIMIT 1;";
        command.Parameters.AddWithValue("$a", a);
        if (b is not null) command.Parameters.AddWithValue("$b", b);

        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;

        var metrics = JsonSerializer.Deserialize<SnapshotMetrics>(reader.GetString(7), Json)
            ?? throw new InvalidDataException("Stored snapshot has no metrics.");
        var findings = (JsonSerializer.Deserialize<List<FindingRow>>(reader.GetString(8), Json) ?? [])
            .Select(f => new StoredFinding(
                f.Code, Enum.Parse<Severity>(f.Severity), Enum.Parse<Confidence>(f.Confidence), f.Category, LocalizedTextJson.Deserialize(f.Message)))
            .ToList();

        return new StoredSnapshot(
            reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetInt32(5),
            DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture), metrics, findings);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        if (!_initialised)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                CREATE TABLE IF NOT EXISTS snapshots (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    island_key TEXT NOT NULL,
                    save_name TEXT NOT NULL,
                    game_day INTEGER NOT NULL,
                    game_year INTEGER,
                    game_month INTEGER,
                    analyzed_at TEXT NOT NULL,
                    metrics_json TEXT NOT NULL,
                    findings_json TEXT NOT NULL,
                    UNIQUE (island_key, game_day)
                );
                PRAGMA user_version = {SchemaVersion};
                """;
            command.ExecuteNonQuery();
            _initialised = true;
        }

        return connection;
    }
}
