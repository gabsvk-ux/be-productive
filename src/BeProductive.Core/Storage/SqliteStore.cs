using System.Globalization;
using System.Text.Json;
using BeProductive.Core.Sessions;
using Microsoft.Data.Sqlite;

namespace BeProductive.Core.Storage;

/// <summary>
/// The app's database: presets, the session state and the tracking records (days, blocks, events).
/// Only the service writes to it. Times are stored as UTC ISO 8601 text, so they sort as text.
/// </summary>
public sealed class SqliteStore : ISessionStore, IRulesStore
{
    private const string StateKey = "session";
    private const string MinimumsKey = "minimums";

    private static readonly string[] Migrations =
    [
        """
        CREATE TABLE presets (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            sort INTEGER NOT NULL,
            data TEXT NOT NULL
        );
        CREATE TABLE days (
            day TEXT PRIMARY KEY,
            minimum_minutes INTEGER NOT NULL,
            binding_minutes INTEGER NOT NULL,
            extra_minutes INTEGER NOT NULL,
            focused_seconds INTEGER NOT NULL,
            completed_blocks INTEGER NOT NULL,
            outcome TEXT NOT NULL
        );
        CREATE TABLE blocks (
            id INTEGER PRIMARY KEY,
            day TEXT NOT NULL,
            preset_id TEXT NOT NULL,
            kind TEXT NOT NULL,
            extra INTEGER NOT NULL,
            note TEXT,
            started_at TEXT NOT NULL,
            ended_at TEXT NOT NULL,
            focused_seconds INTEGER NOT NULL,
            completed INTEGER NOT NULL
        );
        CREATE INDEX blocks_day ON blocks (day);
        CREATE TABLE events (
            id INTEGER PRIMARY KEY,
            at TEXT NOT NULL,
            kind TEXT NOT NULL,
            detail TEXT
        );
        CREATE INDEX events_kind_at ON events (kind, at);
        CREATE TABLE state (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE pending_changes (
            kind TEXT NOT NULL,
            target TEXT NOT NULL,
            effective_on TEXT NOT NULL,
            description TEXT NOT NULL,
            data TEXT NOT NULL,
            PRIMARY KEY (kind, target)
        );
        """,
    ];

    private readonly string connectionString;

    public SqliteStore(AppPaths paths)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        connectionString = new SqliteConnectionStringBuilder { DataSource = paths.DatabaseFile, Pooling = true }.ToString();
        Migrate();
    }

    public IReadOnlyList<Preset> All()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT data FROM presets ORDER BY sort, name";
        using var reader = command.ExecuteReader();
        var presets = new List<Preset>();
        while (reader.Read())
            presets.Add(Deserialize<Preset>(reader.GetString(0)));
        return presets;
    }

    public Preset? Find(string id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT data FROM presets WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() is string data ? Deserialize<Preset>(data) : null;
    }

    /// <summary>Adds a preset at the end of the list, or updates it in place.</summary>
    public void SavePreset(Preset preset)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO presets (id, name, sort, data)
            VALUES ($id, $name, (SELECT COALESCE(MAX(sort), -1) + 1 FROM presets), $data)
            ON CONFLICT (id) DO UPDATE SET name = excluded.name, data = excluded.data
            """;
        command.Parameters.AddWithValue("$id", preset.Id);
        command.Parameters.AddWithValue("$name", preset.Name);
        command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(preset, Json.Options));
        command.ExecuteNonQuery();
    }

    public void DeletePreset(string id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM presets WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public WeekdayMinimums? LoadMinimums() => LoadValue<WeekdayMinimums>(MinimumsKey);

    public void SaveMinimums(WeekdayMinimums minimums) => SaveValue(MinimumsKey, minimums);

    public IReadOnlyList<PendingChange> PendingChanges()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT kind, target, effective_on, description, data FROM pending_changes ORDER BY effective_on, kind, target";
        using var reader = command.ExecuteReader();
        var changes = new List<PendingChange>();
        while (reader.Read())
        {
            changes.Add(new PendingChange(
                reader.GetString(0),
                reader.GetString(1),
                DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(3),
                reader.GetString(4)));
        }

        return changes;
    }

    public void SavePending(PendingChange change)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO pending_changes (kind, target, effective_on, description, data)
            VALUES ($kind, $target, $effective, $description, $data)
            ON CONFLICT (kind, target) DO UPDATE SET
                effective_on = excluded.effective_on, description = excluded.description, data = excluded.data
            """;
        command.Parameters.AddWithValue("$kind", change.Kind);
        command.Parameters.AddWithValue("$target", change.Target);
        command.Parameters.AddWithValue("$effective", DayText(change.EffectiveOn));
        command.Parameters.AddWithValue("$description", change.Description);
        command.Parameters.AddWithValue("$data", change.Data);
        command.ExecuteNonQuery();
    }

    public void DeletePending(string kind, string target)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM pending_changes WHERE kind = $kind AND target = $target";
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$target", target);
        command.ExecuteNonQuery();
    }

    public SessionState? LoadState() => LoadValue<SessionState>(StateKey);

    public void SaveState(SessionState state) => SaveValue(StateKey, state);

    public void RecordBlock(BlockRecord block)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO blocks (day, preset_id, kind, extra, note, started_at, ended_at, focused_seconds, completed)
            VALUES ($day, $preset, $kind, $extra, $note, $started, $ended, $focused, $completed)
            """;
        command.Parameters.AddWithValue("$day", DayText(block.Day));
        command.Parameters.AddWithValue("$preset", block.PresetId);
        command.Parameters.AddWithValue("$kind", block.Kind.ToString());
        command.Parameters.AddWithValue("$extra", block.Extra);
        command.Parameters.AddWithValue("$note", (object?)block.Note ?? DBNull.Value);
        command.Parameters.AddWithValue("$started", TimeText(block.StartedAt));
        command.Parameters.AddWithValue("$ended", TimeText(block.EndedAt));
        command.Parameters.AddWithValue("$focused", (long)block.Focused.TotalSeconds);
        command.Parameters.AddWithValue("$completed", block.Completed);
        command.ExecuteNonQuery();
    }

    public void SaveDay(DayRecord day)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO days (day, minimum_minutes, binding_minutes, extra_minutes, focused_seconds, completed_blocks, outcome)
            VALUES ($day, $minimum, $binding, $extra, $focused, $blocks, $outcome)
            ON CONFLICT (day) DO UPDATE SET
                minimum_minutes = excluded.minimum_minutes,
                binding_minutes = excluded.binding_minutes,
                extra_minutes = excluded.extra_minutes,
                focused_seconds = excluded.focused_seconds,
                completed_blocks = excluded.completed_blocks,
                outcome = excluded.outcome
            """;
        command.Parameters.AddWithValue("$day", DayText(day.Day));
        command.Parameters.AddWithValue("$minimum", day.MinimumMinutes);
        command.Parameters.AddWithValue("$binding", day.BindingMinutes);
        command.Parameters.AddWithValue("$extra", day.ExtraMinutes);
        command.Parameters.AddWithValue("$focused", (long)day.Focused.TotalSeconds);
        command.Parameters.AddWithValue("$blocks", day.CompletedBlocks);
        command.Parameters.AddWithValue("$outcome", day.Outcome.ToString());
        command.ExecuteNonQuery();
    }

    public void RecordEvent(SessionEvent sessionEvent)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO events (at, kind, detail) VALUES ($at, $kind, $detail)";
        command.Parameters.AddWithValue("$at", TimeText(sessionEvent.At));
        command.Parameters.AddWithValue("$kind", sessionEvent.Kind);
        command.Parameters.AddWithValue("$detail", (object?)sessionEvent.Detail ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public int CountEvents(string kind, DateTimeOffset since)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM events WHERE kind = $kind AND at >= $since";
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$since", TimeText(since));
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private T? LoadValue<T>(string key)
        where T : class
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM state WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() is string value ? Deserialize<T>(value) : null;
    }

    private void SaveValue<T>(string key, T value)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO state (key, value) VALUES ($key, $value)
            ON CONFLICT (key) DO UPDATE SET value = excluded.value
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(value, Json.Options));
        command.ExecuteNonQuery();
    }

    private void Migrate()
    {
        using var connection = Open();
        using (var wal = connection.CreateCommand())
        {
            wal.CommandText = "PRAGMA journal_mode = WAL";
            wal.ExecuteNonQuery();
        }

        long version;
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "PRAGMA user_version";
            version = (long)read.ExecuteScalar()!;
        }

        for (var next = (int)version; next < Migrations.Length; next++)
        {
            using var transaction = connection.BeginTransaction();
            using var migrate = connection.CreateCommand();
            migrate.Transaction = transaction;
            migrate.CommandText = $"{Migrations[next]}\nPRAGMA user_version = {next + 1};";
            migrate.ExecuteNonQuery();
            transaction.Commit();

            if (next == 0)
                SeedPresets();
        }
    }

    private void SeedPresets()
    {
        var defaults = new[]
        {
            new Preset { Id = "uni", Name = "Uni studying", AllowedApps = ["chrome.exe", "WINWORD.EXE", "Acrobat.exe"], DefaultBlock = BlockKind.Classic25 },
            new Preset { Id = "projects", Name = "Projects", AllowedApps = ["chrome.exe"], DefaultBlock = BlockKind.Long45 },
            new Preset { Id = "programming", Name = "Programming", AllowedApps = ["chrome.exe", "Code.exe"], DefaultBlock = BlockKind.Long45, AllowTerminal = true },
        };
        foreach (var preset in defaults)
            SavePreset(preset);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Json.Options) ?? throw new InvalidDataException($"Stored {typeof(T).Name} is empty.");

    private static string DayText(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string TimeText(DateTimeOffset time) => time.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
