using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BeProductive.Core.Enforcement;

public enum EnforcementMode
{
    /// <summary>Only log what would happen.</summary>
    DryRun,

    /// <summary>The panic file is present: enforce nothing and undo what's applied.</summary>
    Panic,

    /// <summary>Really block things.</summary>
    Live,
}

/// <summary>The single switch every enforcement action goes through.</summary>
public sealed class EnforcementGate(IOptions<EnforcementOptions> options, EnforcementArming arming, AppPaths paths, ILogger<EnforcementGate> logger)
{
    private int warnedArmed;

    public EnforcementMode Mode
    {
        get
        {
            var current = options.Value;

            // An armed install never lets the config weaken the lock: DryRun and the panic file are ignored,
            // so editing settings.json or dropping a panic file can't turn enforcement off. The only ways out
            // of a live lock are a recovery code or scripts/restore.ps1 (admin) / Safe Mode.
            if (arming.Armed)
            {
                if ((current.DryRun || (current.PanicFileEnabled && paths.PanicFiles.Any(SafeExists))) && Interlocked.Exchange(ref warnedArmed, 1) == 0)
                    logger.LogWarning(
                        "This is an armed (installed) service, so DryRun and the panic file are IGNORED. " +
                        "Enforcement stays live; use a recovery code or restore.ps1 to end a lock.");
                return EnforcementMode.Live;
            }

            if (current.DryRun)
                return EnforcementMode.DryRun;
            if (current.PanicFileEnabled && paths.PanicFiles.Any(SafeExists))
                return EnforcementMode.Panic;
            return EnforcementMode.Live;
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> only when enforcement is live; otherwise logs what would have happened.
    /// Returns whether the action ran.
    /// </summary>
    public bool TryEnforce(string description, Action action)
    {
        var mode = Mode;
        if (mode != EnforcementMode.Live)
        {
            logger.LogInformation("[{Mode}] Would {Action}", mode, description);
            return false;
        }

        action();
        return true;
    }

    /// <summary>A panic check must never throw (e.g. an unreadable path), or enforcement couldn't be stopped.</summary>
    private static bool SafeExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }
}
