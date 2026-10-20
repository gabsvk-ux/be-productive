using BeProductive.Core.Sessions;

namespace BeProductive.Core.Enforcement;

/// <summary>
/// Turns a <see cref="SessionStatus"/> and the active preset into an <see cref="EnforcementPlan"/>.
/// Pure: the same inputs always give the same plan, so it's easy to test. It does not re-decide the
/// rules — it reads the decision the session engine already made (the restriction and the admin-tools flag).
/// </summary>
public sealed class EnforcementPlanner(EnforcementOptions options)
{
    public EnforcementPlan Build(SessionStatus status, Preset? preset)
    {
        switch (status.Restriction)
        {
            case Restriction.Focus:
                return Focus(status, preset);
            case Restriction.Break or Restriction.Gate:
                return BreakOrGate(status, preset);
            default:
                // Postponed or Free: nothing is locked.
                return EnforcementPlan.Unlocked;
        }
    }

    private EnforcementPlan Focus(SessionStatus status, Preset? preset)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in options.AlwaysAllowedApps)
            allowed.Add(Normalize(app));
        allowed.Add(Normalize(options.BrowserApp));
        foreach (var app in preset?.AllowedApps ?? [])
            allowed.Add(Normalize(app));
        if (preset is { AllowTerminal: true })
            foreach (var terminal in options.TerminalApps)
                allowed.Add(Normalize(terminal));

        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var domain in options.GlobalBlockedDomains)
            domains.Add(domain.Trim().ToLowerInvariant());
        foreach (var domain in preset?.BlockedDomains ?? [])
            domains.Add(domain.Trim().ToLowerInvariant());

        return new EnforcementPlan(
            Locked: true,
            BlockedDomains: domains,
            Policies: Groups(status),
            KillMode: KillMode.AllowlistExceptAllowed,
            AllowedApps: allowed,
            BlockedApps: new HashSet<string>());
    }

    private EnforcementPlan BreakOrGate(SessionStatus status, Preset? preset)
    {
        // Breaks allow everything except the hard blocklist (empty in v1). Admin tools stay blocked, so
        // terminals the preset doesn't allow are still killed, and the admin-tool policies stay on.
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (status.AdminToolsBlocked && preset is not { AllowTerminal: true })
            foreach (var terminal in options.TerminalApps)
                blocked.Add(Normalize(terminal));

        return new EnforcementPlan(
            Locked: true,
            BlockedDomains: new HashSet<string>(),
            Policies: Groups(status),
            KillMode: blocked.Count > 0 ? KillMode.BlocklistOnly : KillMode.None,
            AllowedApps: new HashSet<string>(),
            BlockedApps: blocked);
    }

    private static HashSet<PolicyGroup> Groups(SessionStatus status)
    {
        // Chrome policies stay on the whole time the laptop is locked, so browsing can't be loosened
        // mid-session; the admin-tool policies track the admin-tools flag.
        var groups = new HashSet<PolicyGroup> { PolicyGroup.Chrome };
        if (status.AdminToolsBlocked)
            groups.Add(PolicyGroup.AdminTools);
        return groups;
    }

    /// <summary>Lowercase, trimmed, without a trailing ".exe", so "Code.exe" and the live process "Code" match.</summary>
    public static string Normalize(string app)
    {
        var name = app.Trim().ToLowerInvariant();
        return name.EndsWith(".exe", StringComparison.Ordinal) ? name[..^4] : name;
    }
}
