namespace BeProductive.Core.Enforcement;

public sealed class EnforcementOptions
{
    public const string SectionName = "Enforcement";

    /// <summary>
    /// When true (the default), nothing on the machine is blocked or changed: every action is only logged.
    /// </summary>
    public bool DryRun { get; set; } = true;

    /// <summary>
    /// When true, a file named PANIC in the data directory switches all enforcement off and undoes
    /// whatever is applied. Meant for development builds, so a bug can't lock you out while testing.
    /// </summary>
    public bool PanicFileEnabled { get; set; } = true;
}
