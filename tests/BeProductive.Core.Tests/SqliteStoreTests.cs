using BeProductive.Core.Ipc;
using BeProductive.Core.Sessions;
using BeProductive.Core.Storage;
using Microsoft.Data.Sqlite;

namespace BeProductive.Core.Tests;

public sealed class SqliteStoreTests : IDisposable
{
    private readonly TempDirectory directory = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        directory.Dispose();
    }

    [Fact]
    public void A_new_database_comes_with_the_default_presets()
    {
        var store = new SqliteStore(directory.AppPaths);

        Assert.Equal(["uni", "projects", "programming"], store.All().Select(preset => preset.Id));
        Assert.Equal(BlockKind.Long45, store.Find("programming")!.DefaultBlock);
        Assert.Null(store.Find("games"));
    }

    [Fact]
    public void Opening_an_existing_database_keeps_its_data()
    {
        var first = new SqliteStore(directory.AppPaths);
        first.SavePreset(new Preset { Id = "reading", Name = "Reading" });

        var second = new SqliteStore(directory.AppPaths);

        Assert.Equal(4, second.All().Count);
    }

    [Fact]
    public void Updating_a_preset_keeps_its_place_and_new_ones_go_last()
    {
        var store = new SqliteStore(directory.AppPaths);

        store.SavePreset(new Preset { Id = "uni", Name = "University" });
        store.SavePreset(new Preset { Id = "reading", Name = "Reading" });

        Assert.Equal(["uni", "projects", "programming", "reading"], store.All().Select(preset => preset.Id));
        Assert.Equal("University", store.Find("uni")!.Name);
    }

    [Fact]
    public void Pending_changes_replace_each_other_per_target()
    {
        var store = new SqliteStore(directory.AppPaths);
        var tomorrow = new DateOnly(2026, 10, 21);

        store.SavePending(new PendingChange(PendingChangeKinds.Preset, "uni", tomorrow, "allow Steam.exe", "{}"));
        store.SavePending(new PendingChange(PendingChangeKinds.Preset, "uni", tomorrow, "allow Discord.exe", "{}"));
        store.SavePending(new PendingChange(PendingChangeKinds.Minimums, "", tomorrow, "lower minimums", "{}"));

        Assert.Equal(2, store.PendingChanges().Count);
        Assert.Contains(store.PendingChanges(), change => change.Description == "allow Discord.exe");

        store.DeletePending(PendingChangeKinds.Preset, "uni");
        Assert.Equal(PendingChangeKinds.Minimums, Assert.Single(store.PendingChanges()).Kind);
    }

    [Fact]
    public void Minimums_are_stored_once_edited()
    {
        var store = new SqliteStore(directory.AppPaths);
        Assert.Null(store.LoadMinimums());

        store.SaveMinimums(new WeekdayMinimums { Sunday = 0 });

        Assert.Equal(0, store.LoadMinimums()!.Sunday);
        Assert.Equal(100, store.LoadMinimums()!.Monday);
    }

    [Fact]
    public void Session_state_survives_a_round_trip()
    {
        var store = new SqliteStore(directory.AppPaths);
        var state = new SessionState
        {
            Day = new DateOnly(2026, 10, 20),
            Phase = Phase.Focus,
            BindingMinutes = 120,
            Focused = TimeSpan.FromMinutes(25),
            PresetId = "uni",
            Block = new ActiveBlock { Kind = BlockKind.Long45, PresetId = "uni", Focused = TimeSpan.FromSeconds(90), Note = "Calculus" },
            LastTick = EngineHarness.Tuesday9am,
        };

        store.SaveState(state);
        var loaded = store.LoadState()!;

        Assert.Equal(Phase.Focus, loaded.Phase);
        Assert.Equal(TimeSpan.FromMinutes(25), loaded.Focused);
        Assert.Equal(BlockKind.Long45, loaded.Block!.Kind);
        Assert.Equal("Calculus", loaded.Block.Note);
        Assert.Equal(EngineHarness.Tuesday9am, loaded.LastTick);
    }

    [Fact]
    public void Events_are_counted_by_kind_and_time()
    {
        var store = new SqliteStore(directory.AppPaths);
        var monday = new DateTimeOffset(2026, 10, 19, 4, 0, 0, TimeSpan.Zero);

        store.RecordEvent(new SessionEvent(monday.AddMinutes(-1), SessionEventKinds.Postpone, null));
        store.RecordEvent(new SessionEvent(monday.AddHours(1), SessionEventKinds.Postpone, null));
        store.RecordEvent(new SessionEvent(monday.AddHours(2), SessionEventKinds.Pause, null));

        Assert.Equal(1, store.CountEvents(SessionEventKinds.Postpone, monday));
    }

    [Fact]
    public void Days_are_updated_in_place_and_blocks_are_recorded()
    {
        var store = new SqliteStore(directory.AppPaths);
        var day = new DateOnly(2026, 10, 20);

        store.SaveDay(new DayRecord(day, 100, 100, 0, TimeSpan.FromMinutes(25), 1, DayOutcome.Open));
        store.SaveDay(new DayRecord(day, 100, 100, 0, TimeSpan.FromMinutes(100), 4, DayOutcome.Done));
        store.RecordBlock(new BlockRecord(day, "uni", BlockKind.Classic25, false, null,
            EngineHarness.Tuesday9am, EngineHarness.Tuesday9am.AddMinutes(25), TimeSpan.FromMinutes(25), true));

        using var connection = new SqliteConnection($"Data Source={directory.AppPaths.DatabaseFile}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), MAX(outcome), MAX(focused_seconds) FROM days";
        using var reader = command.ExecuteReader();
        reader.Read();
        Assert.Equal(1, reader.GetInt64(0));
        Assert.Equal("Done", reader.GetString(1));
        Assert.Equal(6000, reader.GetInt64(2));
    }

    [Fact]
    public void Stats_queries_read_days_blocks_and_events_in_range()
    {
        var store = new SqliteStore(directory.AppPaths);
        var day = new DateOnly(2026, 10, 20);
        store.SaveDay(new DayRecord(day.AddDays(-1), 100, 100, 0, TimeSpan.FromMinutes(100), 4, DayOutcome.Done));
        store.SaveDay(new DayRecord(day, 100, 120, 0, TimeSpan.FromMinutes(30), 1, DayOutcome.Open));
        store.SaveDay(new DayRecord(day.AddDays(-9), 100, 100, 0, TimeSpan.Zero, 0, DayOutcome.Missed));
        foreach (var (preset, minutes) in new[] { ("uni", 25), ("uni", 25), ("programming", 45) })
        {
            store.RecordBlock(new BlockRecord(day, preset, BlockKind.Classic25, false, null,
                EngineHarness.Tuesday9am, EngineHarness.Tuesday9am, TimeSpan.FromMinutes(minutes), true));
        }

        store.RecordEvent(new SessionEvent(EngineHarness.Tuesday9am, SessionEventKinds.Pause, null));
        store.RecordEvent(new SessionEvent(EngineHarness.Tuesday9am.AddHours(2), SessionEventKinds.Pause, null));

        var days = store.Days(day.AddDays(-6), day);
        Assert.Equal([day.AddDays(-1), day], days.Select(record => record.Day));
        Assert.Equal(DayOutcome.Done, days[0].Outcome);
        Assert.Equal(TimeSpan.FromMinutes(100), days[0].Focused);

        Assert.Equal(
            [("programming", TimeSpan.FromMinutes(45), 1), ("uni", TimeSpan.FromMinutes(50), 2)],
            store.FocusByPreset(day, day).OrderBy(total => total.PresetId));
        Assert.Equal(1, store.CountEvents(SessionEventKinds.Pause, EngineHarness.Tuesday9am, EngineHarness.Tuesday9am.AddHours(1)));
    }

    [Fact]
    public void Pipe_messages_round_trip_as_single_lines()
    {
        IpcRequest[] requests =
        [
            new StatusRequest(),
            new PlanRequest("uni", 120, BlockKind.Long45, "Calculus"),
            new PostponeRequest(EngineHarness.Tuesday9am),
            new ExtendRequest(30, BlockKind.Short15, null, null),
            new RecoveryExitRequest("ABCD-EFGH-JKMN-PQRS"),
        ];

        foreach (var request in requests)
        {
            var line = IpcProtocol.Serialize(request);
            Assert.DoesNotContain('\n', line);
            Assert.Equal(request, IpcProtocol.Deserialize<IpcRequest>(line));
        }
    }
}
