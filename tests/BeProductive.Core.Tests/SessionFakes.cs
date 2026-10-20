using BeProductive.Core.Sessions;

namespace BeProductive.Core.Tests;

internal sealed class InMemorySessionStore : ISessionStore, IRulesStore
{
    private readonly Dictionary<(string Kind, string Target), PendingChange> pending = [];

    public List<Preset> Presets { get; } =
    [
        new() { Id = "uni", Name = "Uni studying", DefaultBlock = BlockKind.Classic25 },
        new() { Id = "programming", Name = "Programming", DefaultBlock = BlockKind.Long45 },
    ];

    public WeekdayMinimums? Minimums { get; set; }

    public SessionState? State { get; private set; }

    public List<BlockRecord> Blocks { get; } = [];

    public Dictionary<DateOnly, DayRecord> Days { get; } = [];

    public List<SessionEvent> Events { get; } = [];

    public IReadOnlyList<Preset> All() => Presets;

    public Preset? Find(string id) => Presets.FirstOrDefault(preset => preset.Id == id);

    public void SavePreset(Preset preset)
    {
        var index = Presets.FindIndex(existing => existing.Id == preset.Id);
        if (index >= 0)
            Presets[index] = preset;
        else
            Presets.Add(preset);
    }

    public void DeletePreset(string id) => Presets.RemoveAll(preset => preset.Id == id);

    public WeekdayMinimums? LoadMinimums() => Minimums;

    public void SaveMinimums(WeekdayMinimums minimums) => Minimums = minimums;

    public IReadOnlyList<PendingChange> PendingChanges() => pending.Values.OrderBy(change => change.EffectiveOn).ToList();

    public void SavePending(PendingChange change) => pending[(change.Kind, change.Target)] = change;

    public void DeletePending(string kind, string target) => pending.Remove((kind, target));

    public SessionState? LoadState() => State;

    public void SaveState(SessionState state) => State = state;

    public void RecordBlock(BlockRecord block) => Blocks.Add(block);

    public void SaveDay(DayRecord day) => Days[day.Day] = day;

    public void RecordEvent(SessionEvent sessionEvent) => Events.Add(sessionEvent);

    public int CountEvents(string kind, DateTimeOffset since) =>
        Events.Count(sessionEvent => sessionEvent.Kind == kind && sessionEvent.At >= since);
}

/// <summary>An engine on a hand-driven clock. Days run in UTC; the test day is Tuesday 20 October 2026.</summary>
internal sealed class EngineHarness
{
    public static readonly DateTimeOffset Tuesday9am = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    public EngineHarness(SessionOptions? options = null, DateTimeOffset? start = null)
    {
        Options = options ?? new SessionOptions();
        Now = start ?? Tuesday9am;
        Engine = new SessionEngine(Options, TimeZoneInfo.Utc, Store, Store, null, Now);
    }

    public SessionOptions Options { get; }

    public InMemorySessionStore Store { get; } = new();

    public SessionEngine Engine { get; private set; }

    public DateTimeOffset Now { get; private set; }

    public SessionState State => Engine.State;

    /// <summary>Lets time pass with the service running, ticking every few seconds.</summary>
    public void Run(TimeSpan duration)
    {
        var end = Now + duration;
        while (Now < end)
        {
            var step = TimeSpan.FromSeconds(5);
            Now = Now + step > end ? end : Now + step;
            Engine.Tick(Now);
        }
    }

    public void RunMinutes(double minutes) => Run(TimeSpan.FromMinutes(minutes));

    /// <summary>Time passes with no ticks at all: the laptop is asleep or off.</summary>
    public void Sleep(TimeSpan duration)
    {
        Now += duration;
        Engine.Tick(Now);
    }

    /// <summary>Simulates the service restarting from the saved state after <paramref name="downtime"/>.</summary>
    public void Restart(TimeSpan downtime)
    {
        Store.SaveState(Engine.State);
        Now += downtime;
        Engine = new SessionEngine(Options, TimeZoneInfo.Utc, Store, Store, Store.LoadState(), Now);
    }

    public void Plan(int minutes, BlockKind kind = BlockKind.Classic25, string preset = "uni") =>
        Engine.Plan(Now, preset, minutes, kind, note: null);

    public void Next(BlockKind kind = BlockKind.Classic25) => Engine.StartNext(Now, kind, note: null);

    /// <summary>Runs a whole block, and its break if one follows, then starts the next block.</summary>
    public void FinishBlockAndBreak(BlockKind next = BlockKind.Classic25)
    {
        Run(State.Block!.Kind.FocusLength() - State.Block.Focused);
        if (State.Phase == Phase.Break)
        {
            Run(State.BreakEndsAt!.Value - Now);
            Next(next);
        }
    }
}
