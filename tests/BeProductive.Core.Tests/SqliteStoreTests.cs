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
        first.SavePreset(new Preset { Id = "reading", Name = "Reading" }, sort: 10);

        var second = new SqliteStore(directory.AppPaths);

        Assert.Equal(4, second.All().Count);
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
