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
public sealed class EnforcementGate(IOptions<EnforcementOptions> options, AppPaths paths, ILogger<EnforcementGate> logger)
{
    public EnforcementMode Mode
    {
        get
        {
            var current = options.Value;
            if (current.DryRun)
                return EnforcementMode.DryRun;
            if (current.PanicFileEnabled && File.Exists(paths.PanicFile))
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
}
