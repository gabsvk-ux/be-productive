namespace BeProductive.Core.Sessions;

/// <summary>A command that the rules don't allow right now. The message is shown to the user as is.</summary>
public sealed class SessionRuleException(string message) : Exception(message);

/// <summary>
/// The day's rules: the gate, pomodoro blocks, breaks, pauses, sleep, the day reset, extra time and the
/// recovery exit. It's plain logic driven by the time passed in, so it can be tested without waiting.
/// Call <see cref="Tick"/> about once a second and before every command.
/// </summary>
public sealed class SessionEngine
{
    /// <summary>A gap between ticks longer than this means the laptop was asleep or off (or the service wasn't running).</summary>
    private static readonly TimeSpan GapThreshold = TimeSpan.FromSeconds(15);

    private const int MaxPlanMinutes = 16 * 60;

    private readonly SessionOptions options;
    private readonly TimeZoneInfo zone;
    private readonly ISessionStore store;
    private readonly IRulesStore rules;

    public SessionEngine(SessionOptions options, TimeZoneInfo zone, ISessionStore store, IRulesStore rules, SessionState? saved, DateTimeOffset now)
    {
        this.options = options;
        this.zone = zone;
        this.store = store;
        this.rules = rules;
        State = saved ?? NewDay(DayOf(now), now);
        Tick(now);
    }

    public SessionState State { get; private set; }

    /// <summary>Goes up on every change that should be saved straight away.</summary>
    public int Revision { get; private set; }

    public Restriction Restriction => State.Phase switch
    {
        Phase.Gate => Restriction.Gate,
        Phase.Focus or Phase.AwaitingNext => Restriction.Focus,
        Phase.Break => Restriction.Break,
        _ => Restriction.None,
    };

    /// <summary>Task Manager, terminals and the like stay blocked until the planned time is done, breaks included.</summary>
    public bool AdminToolsBlocked =>
        State.Outcome == DayOutcome.Open && State.Phase is Phase.Gate or Phase.Focus or Phase.Break or Phase.AwaitingNext;

    private TimeSpan Binding => TimeSpan.FromMinutes(State.BindingMinutes);

    /// <summary>Focus total at which the laptop is free again.</summary>
    private TimeSpan Target => State.Outcome == DayOutcome.Open ? Binding : State.ExtraUntil;

    public void Tick(DateTimeOffset now)
    {
        var elapsed = now - State.LastTick;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        var suspended = elapsed > GapThreshold;
        State.LastTick = now;

        var today = DayOf(now);
        if (today != State.Day)
        {
            RollOver(today, now);
            return;
        }

        switch (State.Phase)
        {
            case Phase.Postponed when now >= State.PostponedUntil:
                State.Phase = Phase.Gate;
                State.PostponedUntil = null;
                Changed();
                break;
            case Phase.Focus:
                AdvanceFocus(now, elapsed, suspended);
                break;
            case Phase.Break when now >= State.BreakEndsAt:
                AwaitNextBlock(now);
                break;
            case Phase.AwaitingNext when now >= State.NextBlockDeadline:
                Event(now, SessionEventKinds.AutoStart, null);
                StartBlock(now, DefaultBlock(), note: null);
                break;
        }
    }

    public void Plan(DateTimeOffset now, string presetId, int minutes, BlockKind kind, string? note)
    {
        Require(State.Phase is Phase.Gate or Phase.Postponed, "Today is already planned.");
        RequirePreset(presetId);
        Require(minutes >= State.MinimumMinutes, $"Plan at least {State.MinimumMinutes} minutes of focus.");
        Require(minutes <= MaxPlanMinutes, "That's more than 16 hours.");

        State.BindingMinutes = minutes;
        State.PresetId = presetId;
        State.PostponedUntil = null;
        Event(now, SessionEventKinds.Plan, $"{presetId}, {minutes} min");
        SaveDay();
        StartBlock(now, kind, note);
    }

    public void Postpone(DateTimeOffset now, DateTimeOffset until)
    {
        Require(State.Phase == Phase.Gate, "You can only postpone from the start screen.");
        Require(PostponesLeft() > 0, "No postpones left this week.");
        Require(until > now, "Pick a time later than now.");
        Require(until <= LatestStart(), $"The latest start is {options.LatestStart:hh\\:mm}.");

        State.Phase = Phase.Postponed;
        State.PostponedUntil = until;
        Event(now, SessionEventKinds.Postpone, until.ToString("O"));
        Changed();
    }

    public void Pause(DateTimeOffset now)
    {
        Require(State.Phase == Phase.Focus && State.Block is { PausedSince: null }, "Only a running focus block can be paused.");
        State.Block!.PausedSince = now;
        Event(now, SessionEventKinds.Pause, null);
        Changed();
    }

    public void Resume(DateTimeOffset now)
    {
        Require(State.Block is { PausedSince: not null }, "Nothing is paused.");
        State.Block!.PausedSince = null;
        Event(now, SessionEventKinds.Resume, null);
        Changed();
    }

    /// <summary>Starts the next block when a break has ended, or cuts the break short.</summary>
    public void StartNext(DateTimeOffset now, BlockKind kind, string? note)
    {
        Require(State.Phase is Phase.AwaitingNext or Phase.Break, "The next block starts after a break.");
        if (State.Phase == Phase.Break)
            Event(now, SessionEventKinds.SkipBreak, null);
        StartBlock(now, kind, note);
    }

    public void SwitchPreset(DateTimeOffset now, string presetId)
    {
        Require(State.Phase is Phase.Break or Phase.AwaitingNext, "Presets can be switched during a break.");
        RequirePreset(presetId);
        State.PresetId = presetId;
        Event(now, SessionEventKinds.SwitchPreset, presetId);
        Changed();
    }

    /// <summary>Adds extra focus time once nothing is required. Extra time can be stopped any time.</summary>
    public void Extend(DateTimeOffset now, int minutes, BlockKind kind, string? presetId, string? note)
    {
        Require(State.Phase == Phase.Free, "Extra time can be added once today's planned time is done.");
        Require(minutes is > 0 and <= MaxPlanMinutes, "Add between 1 minute and 16 hours.");
        if (presetId is not null)
        {
            RequirePreset(presetId);
            State.PresetId = presetId;
        }

        Require(State.PresetId is not null, "Pick a preset for the extra time.");

        State.ExtraMinutes += minutes;
        State.ExtraUntil = State.Focused + TimeSpan.FromMinutes(minutes);
        Event(now, SessionEventKinds.Extend, $"{minutes} min");
        SaveDay();
        StartBlock(now, kind, note);
    }

    public void StopExtra(DateTimeOffset now)
    {
        Require(State.Outcome != DayOutcome.Open && State.Phase is Phase.Focus or Phase.Break or Phase.AwaitingNext,
            "Only extra time can be stopped; the planned time has to be finished.");
        if (State.Block is not null)
            EndBlock(now, completed: false);
        State.ExtraUntil = State.Focused;
        Event(now, SessionEventKinds.StopExtra, null);
        BecomeFree();
    }

    /// <summary>
    /// A raised minimum counts today too, as long as today hasn't been planned yet.
    /// Days without a minimum stay free: a settings change shouldn't lock the laptop on the spot.
    /// </summary>
    public void RaiseTodaysMinimum(int minutes)
    {
        if (State.Outcome != DayOutcome.Open || State.Phase is not (Phase.Gate or Phase.Postponed) || minutes <= State.MinimumMinutes)
            return;
        State.MinimumMinutes = minutes;
        SaveDay();
        Changed();
    }

    /// <summary>The day a moment belongs to: days start at <see cref="SessionOptions.DayStartsAt"/>, not at midnight.</summary>
    public static DateOnly DayOf(DateTimeOffset now, SessionOptions options, TimeZoneInfo zone) =>
        DateOnly.FromDateTime((TimeZoneInfo.ConvertTime(now, zone) - options.DayStartsAt).DateTime);

    /// <summary>Throws unless there's a lock a recovery code could end, so a code isn't used up for nothing.</summary>
    public void EnsureRecoveryExitPossible() =>
        Require(AdminToolsBlocked, "Nothing is locked right now, so no recovery code is needed.");

    /// <summary>A recovery code was accepted: end the lock and count the day as failed.</summary>
    public void EndWithRecoveryCode(DateTimeOffset now)
    {
        EnsureRecoveryExitPossible();
        if (State.Block is not null)
            EndBlock(now, completed: false);
        State.Outcome = DayOutcome.Failed;
        Event(now, SessionEventKinds.RecoveryExit, null);
        SaveDay();
        BecomeFree();
    }

    public SessionStatus Snapshot(DateTimeOffset now)
    {
        var block = State.Block is { } active
            ? new BlockStatus(
                active.Kind,
                active.Focused,
                active.Kind.FocusLength() - active.Focused,
                active.PausedSince is not null,
                active.PausedSince + options.MaxPause,
                active.Extra,
                active.Note)
            : null;

        return new SessionStatus(
            State.Day,
            State.Phase,
            State.Outcome,
            Restriction,
            AdminToolsBlocked,
            State.PresetId,
            State.PresetId is null ? null : rules.Find(State.PresetId)?.Name,
            State.MinimumMinutes,
            State.BindingMinutes,
            State.ExtraMinutes,
            State.Focused + (State.Block?.Focused ?? TimeSpan.Zero),
            State.CompletedBlocks,
            block,
            State.BreakEndsAt,
            State.LongBreak,
            State.NextBlockDeadline,
            State.PostponedUntil,
            PostponesLeft(),
            LatestStart());
    }

    private void AdvanceFocus(DateTimeOffset now, TimeSpan elapsed, bool suspended)
    {
        var block = State.Block!;
        if (block.PausedSince is { } pausedSince)
        {
            if (now - pausedSince >= options.MaxPause)
            {
                block.PausedSince = null;
                Event(now, SessionEventKinds.AutoResume, null);
                Changed();
            }

            return;
        }

        // Time asleep or switched off never counts as focus. A short gap just carries on.
        if (suspended)
        {
            if (elapsed > options.SleepResumeLimit)
            {
                Event(now, SessionEventKinds.BlockClosedAfterSleep, $"{elapsed.TotalMinutes:0} min");
                var kind = block.Kind;
                EndBlock(now, completed: false);
                AfterBlock(now, kind, completed: false);
            }

            return;
        }

        block.Focused += elapsed;
        if (block.Focused >= block.Kind.FocusLength())
        {
            block.Focused = block.Kind.FocusLength();
            var kind = block.Kind;
            EndBlock(now, completed: true);
            AfterBlock(now, kind, completed: true);
        }
    }

    private void EndBlock(DateTimeOffset now, bool completed)
    {
        var block = State.Block!;
        State.Focused += block.Focused;
        if (completed)
            State.CompletedBlocks++;
        store.RecordBlock(new BlockRecord(
            State.Day, block.PresetId, block.Kind, block.Extra, block.Note, block.StartedAt, now, block.Focused, completed));
        State.Block = null;

        if (State.Outcome == DayOutcome.Open && State.Focused >= Binding)
        {
            State.Outcome = DayOutcome.Done;
            Event(now, SessionEventKinds.BindingDone, null);
        }

        SaveDay();
        Changed();
    }

    private void AfterBlock(DateTimeOffset now, BlockKind kind, bool completed)
    {
        if (State.Focused >= Target)
        {
            BecomeFree();
            return;
        }

        // A block closed after sleep goes straight to choosing the next one.
        if (!completed)
        {
            AwaitNextBlock(now);
            return;
        }

        var longBreak = options.LongBreaks && options.LongBreakEvery > 0 && State.CompletedBlocks % options.LongBreakEvery == 0;
        State.Phase = Phase.Break;
        State.LongBreak = longBreak;
        State.BreakEndsAt = now + (longBreak ? options.LongBreakLength : kind.ShortBreakLength());
        Changed();
    }

    private void StartBlock(DateTimeOffset now, BlockKind kind, string? note)
    {
        State.Block = new ActiveBlock
        {
            Kind = kind,
            PresetId = State.PresetId!,
            StartedAt = now,
            Extra = State.Outcome != DayOutcome.Open,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
        State.Phase = Phase.Focus;
        ClearBreak();
        Changed();
    }

    private void AwaitNextBlock(DateTimeOffset now)
    {
        State.Phase = Phase.AwaitingNext;
        State.BreakEndsAt = null;
        State.LongBreak = false;
        State.NextBlockDeadline = now + options.NextBlockDeadline;
        Changed();
    }

    private void BecomeFree()
    {
        State.Phase = Phase.Free;
        ClearBreak();
        Changed();
    }

    private void ClearBreak()
    {
        State.BreakEndsAt = null;
        State.LongBreak = false;
        State.NextBlockDeadline = null;
    }

    private void RollOver(DateOnly today, DateTimeOffset now)
    {
        if (State.Block is not null)
            EndBlock(now, completed: false);
        if (State.Outcome == DayOutcome.Open)
        {
            State.Outcome = DayOutcome.Missed;
            Event(now, SessionEventKinds.DayMissed, State.Day.ToString("yyyy-MM-dd"));
        }

        SaveDay();
        State = NewDay(today, now);
        Changed();
    }

    private SessionState NewDay(DateOnly day, DateTimeOffset now)
    {
        var minimum = (rules.LoadMinimums() ?? options.MinimumMinutes).For(day.DayOfWeek);
        return new SessionState
        {
            Day = day,
            MinimumMinutes = minimum,
            Phase = minimum > 0 ? Phase.Gate : Phase.Free,
            Outcome = minimum > 0 ? DayOutcome.Open : DayOutcome.NoMinimum,
            LastTick = now,
        };
    }

    private int PostponesLeft() =>
        Math.Max(0, options.PostponesPerWeek - store.CountEvents(SessionEventKinds.Postpone, WeekStart()));

    private DateTimeOffset LatestStart() => LocalTime(State.Day, options.LatestStart);

    private DateTimeOffset WeekStart()
    {
        var daysSinceMonday = ((int)State.Day.DayOfWeek + 6) % 7;
        return LocalTime(State.Day.AddDays(-daysSinceMonday), options.DayStartsAt);
    }

    private DateOnly DayOf(DateTimeOffset now) => DayOf(now, options, zone);

    private DateTimeOffset LocalTime(DateOnly day, TimeSpan timeOfDay) => LocalTime(day, timeOfDay, zone);

    /// <summary>The moment a day begins (at <see cref="SessionOptions.DayStartsAt"/>).</summary>
    public static DateTimeOffset DayStart(DateOnly day, SessionOptions options, TimeZoneInfo zone) =>
        LocalTime(day, options.DayStartsAt, zone);

    private static DateTimeOffset LocalTime(DateOnly day, TimeSpan timeOfDay, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue) + timeOfDay;
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private BlockKind DefaultBlock() =>
        State.PresetId is null ? BlockKind.Classic25 : rules.Find(State.PresetId)?.DefaultBlock ?? BlockKind.Classic25;

    private void RequirePreset(string presetId) =>
        Require(rules.Find(presetId) is not null, $"There's no preset called '{presetId}'.");

    private void SaveDay() =>
        store.SaveDay(new DayRecord(
            State.Day, State.MinimumMinutes, State.BindingMinutes, State.ExtraMinutes, State.Focused, State.CompletedBlocks, State.Outcome));

    private void Event(DateTimeOffset now, string kind, string? detail) =>
        store.RecordEvent(new SessionEvent(now, kind, detail));

    private void Changed() => Revision++;

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new SessionRuleException(message);
    }
}
