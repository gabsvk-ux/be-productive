using BeProductive.Core.Ipc;
using BeProductive.Core.Recovery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BeProductive.Core.Sessions;

/// <summary>
/// Owns the session engine inside the service: runs it, answers commands from the UI and CLI,
/// and saves its state (straight after every change, and every few seconds while a block runs).
/// </summary>
public sealed class SessionService(
    ISessionStore store,
    IRulesStore rules,
    RecoveryCodeService recoveryCodes,
    IOptions<SessionOptions> options,
    TimeProvider time,
    ILogger<SessionService> logger)
{
    private static readonly TimeSpan CheckpointInterval = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim sync = new(1, 1);
    private readonly RuleEditor editor = new(rules, options.Value);
    private SessionEngine? engine;
    private int savedRevision = -1;
    private DateTimeOffset savedAt;

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        await sync.WaitAsync(cancellationToken);
        try
        {
            var now = time.GetUtcNow();
            var current = Prepare(now);
            Save(current, now);
        }
        finally
        {
            sync.Release();
        }
    }

    public async Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken cancellationToken)
    {
        await sync.WaitAsync(cancellationToken);
        SessionEngine? current = null;
        try
        {
            var now = time.GetUtcNow();
            current = Prepare(now);

            string? message = null;
            switch (request)
            {
                case StatusRequest:
                    break;
                case PresetsRequest:
                    return new IpcResponse(true, null, current.Snapshot(now), rules.All());
                case SettingsRequest:
                    return new IpcResponse(true, null, current.Snapshot(now), Settings: Settings());
                case SavePresetRequest savePreset:
                    message = editor.SavePreset(savePreset.Preset, current.State.Day);
                    return new IpcResponse(true, null, current.Snapshot(now), Message: message, Settings: Settings());
                case DeletePresetRequest deletePreset:
                    message = editor.DeletePreset(deletePreset.PresetId, PresetInUse(current));
                    return new IpcResponse(true, null, current.Snapshot(now), Message: message, Settings: Settings());
                case SaveMinimumsRequest saveMinimums:
                    (message, var todaysMinimum) = editor.SaveMinimums(saveMinimums.Minimums, current.State.Day);
                    current.RaiseTodaysMinimum(todaysMinimum);
                    return new IpcResponse(true, null, current.Snapshot(now), Message: message, Settings: Settings());
                case PlanRequest plan:
                    current.Plan(now, plan.PresetId, plan.Minutes, plan.Block, plan.Note);
                    break;
                case PostponeRequest postpone:
                    current.Postpone(now, postpone.Until);
                    break;
                case PauseRequest:
                    current.Pause(now);
                    break;
                case ResumeRequest:
                    current.Resume(now);
                    break;
                case StartNextRequest next:
                    current.StartNext(now, next.Block, next.Note);
                    break;
                case SwitchPresetRequest switchPreset:
                    current.SwitchPreset(now, switchPreset.PresetId);
                    break;
                case ExtendRequest extend:
                    current.Extend(now, extend.Minutes, extend.Block, extend.PresetId, extend.Note);
                    break;
                case StopExtraRequest:
                    current.StopExtra(now);
                    break;
                case RecoveryExitRequest exit:
                    message = await RecoveryExitAsync(current, exit.Code, cancellationToken);
                    break;
                default:
                    throw new SessionRuleException("Unknown command.");
            }

            var after = time.GetUtcNow();
            return new IpcResponse(true, null, current.Snapshot(after), Message: message);
        }
        catch (SessionRuleException exception)
        {
            return new IpcResponse(false, exception.Message, current?.Snapshot(time.GetUtcNow()));
        }
        finally
        {
            if (current is not null)
                Save(current, time.GetUtcNow());
            sync.Release();
        }
    }

    private async Task<string> RecoveryExitAsync(SessionEngine current, string code, CancellationToken cancellationToken)
    {
        // Checked first, so a code isn't used up when there's nothing to end.
        current.EnsureRecoveryExitPossible();

        var result = await recoveryCodes.RedeemAsync(code, cancellationToken);
        switch (result.Outcome)
        {
            case RedeemOutcome.NoCodes:
                throw new SessionRuleException("No recovery codes are set up.");
            case RedeemOutcome.Rejected:
                throw new SessionRuleException("That code isn't valid or has already been used.");
        }

        current.EndWithRecoveryCode(time.GetUtcNow());
        logger.LogWarning("The lock was ended with a recovery code; today counts as failed");

        var message = $"The lock is over for today, and today counts as failed. {result.Remaining} recovery code(s) left.";
        if (result.ShouldOfferNewSet)
            message += " Running low: run `bp codes regenerate` to email your friend a new set.";
        if (result.NewSetSent)
            message += " That was the last one, so a new set was emailed to your friend.";
        if (result.NewSetError is not null)
            message += $" That was the last one, and sending a new set failed ({result.NewSetError}). The service keeps retrying.";
        return message;
    }

    /// <summary>Applies rule changes that have become due, then brings the engine up to date.</summary>
    private SessionEngine Prepare(DateTimeOffset now)
    {
        editor.ApplyDue(SessionEngine.DayOf(now, options.Value, time.LocalTimeZone));
        var current = Engine(now);
        current.Tick(now);
        return current;
    }

    private SettingsSnapshot Settings() =>
        new(
            rules.All(),
            editor.Minimums,
            rules.PendingChanges().Select(change => new PendingRuleChange(
                change.Kind,
                change.Target,
                change.EffectiveOn,
                change.Description,
                change.Kind == PendingChangeKinds.Preset ? IpcProtocol.Deserialize<Preset>(change.Data) : null,
                change.Kind == PendingChangeKinds.Minimums ? IpcProtocol.Deserialize<WeekdayMinimums>(change.Data) : null))
            .ToList());

    /// <summary>The preset today's lock depends on, which mustn't be deleted mid-day.</summary>
    private static string? PresetInUse(SessionEngine current) =>
        current.State.Block?.PresetId ?? (current.AdminToolsBlocked ? current.State.PresetId : null);

    private SessionEngine Engine(DateTimeOffset now)
    {
        if (engine is not null)
            return engine;

        SessionState? saved;
        try
        {
            saved = store.LoadState();
        }
        catch (Exception exception)
        {
            // Starting over means a fresh day at the gate: locked rather than free.
            logger.LogError(exception, "The saved session state couldn't be read; starting the day over");
            saved = null;
        }

        engine = new SessionEngine(options.Value, time.LocalTimeZone, store, rules, saved, now);
        return engine;
    }

    private void Save(SessionEngine current, DateTimeOffset now)
    {
        if (current.Revision == savedRevision && now - savedAt < CheckpointInterval)
            return;

        store.SaveState(current.State);
        savedRevision = current.Revision;
        savedAt = now;
    }
}
