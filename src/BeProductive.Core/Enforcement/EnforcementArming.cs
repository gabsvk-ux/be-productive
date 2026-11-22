namespace BeProductive.Core.Enforcement;

/// <summary>
/// Whether enforcement is "armed". An armed install ignores anything that would weaken the lock from the
/// editable config file — <c>DryRun</c> and the panic file — so editing <c>settings.json</c> can't turn
/// enforcement off. It is deliberately NOT read from config (that would be self-defeating): the service sets
/// it true when it runs as an installed Windows service, and leaves it false for development (console) runs,
/// where DryRun and the panic file stay honoured for safe iteration.
/// </summary>
public sealed class EnforcementArming(bool armed)
{
    public bool Armed { get; } = armed;

    /// <summary>The default: not armed (development / CLI). The service replaces this when installed.</summary>
    public static EnforcementArming Disarmed { get; } = new(false);
}
