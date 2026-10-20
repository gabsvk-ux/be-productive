using BeProductive.Core.SafetyNet;

namespace BeProductive.Core.Enforcement;

/// <summary>A group of machine policies the enforcer can turn on. The concrete registry values per group
/// are supplied by <see cref="IEnforcementPolicies"/>, which is OS-specific.</summary>
public enum PolicyGroup
{
    /// <summary>Chrome: Secure DNS off (so the hosts file can't be skipped), no Incognito, no DevTools.</summary>
    Chrome,

    /// <summary>Admin tools: Task Manager, cmd, regedit and the MMC snap-ins.</summary>
    AdminTools,
}

/// <summary>How running programs are policed this tick.</summary>
public enum KillMode
{
    /// <summary>Nothing is killed.</summary>
    None,

    /// <summary>Allowlist: kill every program in the user's session that isn't in <see cref="EnforcementPlan.AllowedApps"/>.</summary>
    AllowlistExceptAllowed,

    /// <summary>Blocklist: kill only the programs in <see cref="EnforcementPlan.BlockedApps"/> (used during breaks, for terminals).</summary>
    BlocklistOnly,
}

/// <summary>A single registry policy value to apply.</summary>
public sealed record PolicyValue(string KeyPath, string Name, PolicyValueKind Kind, string Data);

/// <summary>
/// What the machine should look like right now, worked out from the session status by
/// <see cref="EnforcementPlanner"/>. Program names are normalized (lowercase, no ".exe").
/// </summary>
public sealed record EnforcementPlan(
    bool Locked,
    IReadOnlySet<string> BlockedDomains,
    IReadOnlySet<PolicyGroup> Policies,
    KillMode KillMode,
    IReadOnlySet<string> AllowedApps,
    IReadOnlySet<string> BlockedApps)
{
    public static readonly EnforcementPlan Unlocked = new(
        false, new HashSet<string>(), new HashSet<PolicyGroup>(), KillMode.None,
        new HashSet<string>(), new HashSet<string>());

    /// <summary>True when there are reversible machine changes (hosts or registry) to keep applied.</summary>
    public bool HasMachineChanges => BlockedDomains.Count > 0 || Policies.Count > 0;
}

/// <summary>The concrete registry values for a policy group. OS-specific (Windows resolves the user's hive).</summary>
public interface IEnforcementPolicies
{
    IReadOnlyList<PolicyValue> For(PolicyGroup group);
}

/// <summary>No policies: for systems without an implementation yet, so the enforcer still runs (and logs) in dry run.</summary>
public sealed class NoEnforcementPolicies : IEnforcementPolicies
{
    public IReadOnlyList<PolicyValue> For(PolicyGroup group) => [];
}
