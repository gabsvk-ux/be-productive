using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BeProductive.Core.Recovery;

public sealed class RecoveryOptions
{
    public const string SectionName = "Recovery";

    public int CodeCount { get; set; } = 10;

    /// <summary>At this many codes left (or fewer), offer to send a new set.</summary>
    public int PromptThreshold { get; set; } = 3;
}

public enum RedeemOutcome
{
    Accepted,
    Rejected,
    NoCodes,
}

/// <param name="Remaining">Unused codes left in the active set.</param>
/// <param name="ShouldOfferNewSet">Few codes are left: ask whether to send a new set.</param>
/// <param name="NewSetSent">That was the last code and a new set was sent automatically.</param>
/// <param name="NewSetError">That was the last code and sending a new set failed; the service keeps retrying.</param>
public sealed record RedeemResult(RedeemOutcome Outcome, int Remaining, bool ShouldOfferNewSet, bool NewSetSent, string? NewSetError);

public sealed record RecoveryStatus(bool HasCodes, int Remaining, int Total, DateTimeOffset? CreatedAt, bool DeliveryPending);

public sealed class RecoveryCodeService(
    IRecoveryCodeStore store,
    IRecoveryCodeDelivery delivery,
    IOptions<RecoveryOptions> options,
    TimeProvider time,
    ILogger<RecoveryCodeService> logger)
{
    private readonly SemaphoreSlim sync = new(1, 1);

    public RecoveryStatus GetStatus()
    {
        var state = store.Load();
        return state.Active is { } active
            ? new RecoveryStatus(true, active.Remaining, active.Codes.Count, active.CreatedAt, state.Pending is not null)
            : new RecoveryStatus(false, 0, 0, null, state.Pending is not null);
    }

    /// <summary>
    /// Creates the first set at setup and returns it once, so your friend can write it down.
    /// Refused while unused codes exist: after setup, new codes only ever go to your friend.
    /// </summary>
    public async Task<IReadOnlyList<string>> CreateFirstSetAsync(CancellationToken cancellationToken)
    {
        await sync.WaitAsync(cancellationToken);
        try
        {
            var state = store.Load();
            if (state.Active is { Remaining: > 0 })
                throw new InvalidOperationException("Recovery codes already exist. New ones can only be sent to your friend.");

            var (set, codes) = NewSet();
            state.Active = set;
            state.Pending = null;
            store.Save(state);
            logger.LogInformation("Created the first recovery code set ({Count} codes)", codes.Count);
            return codes;
        }
        finally
        {
            sync.Release();
        }
    }

    public async Task<RedeemResult> RedeemAsync(string input, CancellationToken cancellationToken)
    {
        await sync.WaitAsync(cancellationToken);
        try
        {
            var state = store.Load();
            if (state.Active is null && state.Pending is null)
                return new RedeemResult(RedeemOutcome.NoCodes, 0, false, false, null);

            if (!RecoveryCode.TryNormalize(input, out var code))
            {
                logger.LogWarning("Rejected a recovery code attempt (not a valid code)");
                return Rejected(state);
            }

            var match = FindUnused(state.Pending, code);
            if (match is not null)
            {
                // Only the friend could have this code, so the new set did reach them.
                state.Active = state.Pending;
                state.Pending = null;
            }
            else
            {
                match = FindUnused(state.Active, code);
            }

            if (match is null)
            {
                logger.LogWarning("Rejected a recovery code attempt (unknown or already used)");
                return Rejected(state);
            }

            match.UsedAt = time.GetUtcNow();
            store.Save(state);

            var remaining = state.Active!.Remaining;
            logger.LogWarning("Recovery code used; {Remaining} left", remaining);

            if (remaining > 0)
                return new RedeemResult(RedeemOutcome.Accepted, remaining, remaining <= options.Value.PromptThreshold, false, null);

            try
            {
                await SendNewSetAsync(cancellationToken);
                return new RedeemResult(RedeemOutcome.Accepted, 0, false, true, null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "The last recovery code was used and a new set couldn't be sent");
                return new RedeemResult(RedeemOutcome.Accepted, 0, false, false, exception.Message);
            }
        }
        finally
        {
            sync.Release();
        }
    }

    /// <summary>
    /// Emails a new set to your friend. The current codes stay valid until the email is confirmed sent.
    /// Throws if it couldn't be sent.
    /// </summary>
    public async Task RegenerateAsync(CancellationToken cancellationToken)
    {
        await sync.WaitAsync(cancellationToken);
        try
        {
            await SendNewSetAsync(cancellationToken);
        }
        finally
        {
            sync.Release();
        }
    }

    private async Task SendNewSetAsync(CancellationToken cancellationToken)
    {
        var state = store.Load();
        var (set, codes) = NewSet();

        // Saved before sending, so codes that reach the friend are accepted even if we never hear back.
        state.Pending = set;
        store.Save(state);

        try
        {
            await delivery.DeliverAsync(codes, cancellationToken);
        }
        catch (DeliveryNotSentException)
        {
            state.Pending = null;
            store.Save(state);
            throw;
        }

        state.Active = set;
        state.Pending = null;
        store.Save(state);
        logger.LogInformation("Sent a new recovery code set to your friend");
    }

    private (RecoveryCodeSet Set, IReadOnlyList<string> FormattedCodes) NewSet()
    {
        var codes = new HashSet<string>();
        while (codes.Count < options.Value.CodeCount)
            codes.Add(RecoveryCode.Generate());

        var set = new RecoveryCodeSet
        {
            CreatedAt = time.GetUtcNow(),
            Codes = codes.Select(RecoveryCodeHasher.Hash).ToList(),
        };
        return (set, codes.Select(RecoveryCode.Format).ToList());
    }

    private static StoredCode? FindUnused(RecoveryCodeSet? set, string code) =>
        set?.Codes.FirstOrDefault(stored => stored.UsedAt is null && RecoveryCodeHasher.Matches(code, stored));

    private static RedeemResult Rejected(RecoveryCodeState state) =>
        new(RedeemOutcome.Rejected, state.Active?.Remaining ?? 0, false, false, null);
}
