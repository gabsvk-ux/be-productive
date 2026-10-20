using BeProductive.Core.SafetyNet;
using BeProductive.Core.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BeProductive.Core.Enforcement;

/// <summary>Applies the current session's enforcement plan to the machine. A no-op implementation is the default.</summary>
public interface IEnforcer
{
    void Apply(SessionStatus status, DateTimeOffset now);
}

public sealed class NoOpEnforcer : IEnforcer
{
    public void Apply(SessionStatus status, DateTimeOffset now)
    {
    }
}

/// <summary>
/// The one place a session's decision becomes action. Every machine change still goes through
/// <see cref="SystemChanges"/> (journalled, leased, reversible) and every kill through
/// <see cref="EnforcementGate"/>, so dry run, the panic file and the restore path all keep working.
/// It reconciles: it only re-applies hosts and policies when the plan changes or the re-assert interval
/// passes, and it undoes everything the moment the laptop is unlocked.
/// </summary>
public sealed class Enforcer(
    SystemChanges changes,
    IEnforcementPolicies policies,
    IProcessGuard processes,
    EnforcementGate gate,
    IRulesStore rules,
    ISessionStore store,
    IOptions<EnforcementOptions> enforcementOptions,
    IOptions<SafetyNetOptions> safetyNetOptions,
    ILogger<Enforcer> logger) : IEnforcer
{
    private readonly object sync = new();
    private readonly EnforcementPlanner planner = new(enforcementOptions.Value);
    private readonly Dictionary<string, DateTimeOffset> loggedAttempts = new(StringComparer.OrdinalIgnoreCase);

    private EnforcementPlan lastApplied = EnforcementPlan.Unlocked;
    private bool anyApplied;
    private bool hostsApplied;
    private DateTimeOffset nextReassert = DateTimeOffset.MinValue;

    public void Apply(SessionStatus status, DateTimeOffset now)
    {
        lock (sync)
        {
            // In panic the only safe thing is to let go: undo everything and police nothing. The gate
            // already stops individual changes, but we must also drop what's applied, not keep re-asserting.
            if (gate.Mode == EnforcementMode.Panic)
            {
                StandDown("the panic file is present");
                return;
            }

            var preset = status.PresetId is { } id ? rules.Find(id) : null;
            var plan = planner.Build(status, preset);

            try
            {
                ReconcileMachine(plan, now);
                PoliceProcesses(plan, now);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Enforcement failed this tick");
            }

            lastApplied = plan;
        }
    }

    private void ReconcileMachine(EnforcementPlan plan, DateTimeOffset now)
    {
        if (!plan.HasMachineChanges)
        {
            // Nothing should be applied now. Undo anything we had applied (e.g. focus ended, or a break
            // with no admin policy).
            StandDown("the lock no longer needs machine changes");
            return;
        }

        var changed = !SamePlan(plan, lastApplied);
        if (changed || now >= nextReassert)
        {
            // Only touch the hosts file once there are domains to block, or to clear ones we set earlier.
            if (plan.BlockedDomains.Count > 0 || hostsApplied)
            {
                changes.SetHostsBlocklist(plan.BlockedDomains);
                hostsApplied = plan.BlockedDomains.Count > 0;
            }

            foreach (var group in plan.Policies)
                foreach (var value in policies.For(group))
                    changes.SetRegistryValue(value.KeyPath, value.Name, value.Kind, value.Data);

            anyApplied = true;
            nextReassert = now + enforcementOptions.Value.ReassertInterval;
        }

        // Keep the lease ahead of expiry so a crash or hang still self-heals, but a healthy session never drops.
        changes.RenewLease(now + safetyNetOptions.Value.DefaultLease);
    }

    /// <summary>Undo every applied change and forget our applied state. Safe to call when nothing is applied.</summary>
    private void StandDown(string reason)
    {
        if (anyApplied)
        {
            changes.RestoreAll(reason);
            anyApplied = false;
            hostsApplied = false;
            nextReassert = DateTimeOffset.MinValue;
        }
    }

    private void PoliceProcesses(EnforcementPlan plan, DateTimeOffset now)
    {
        if (plan.KillMode == KillMode.None)
            return;

        foreach (var process in processes.UserProcesses())
        {
            var kill = plan.KillMode switch
            {
                KillMode.AllowlistExceptAllowed => !plan.AllowedApps.Contains(process.Name),
                KillMode.BlocklistOnly => plan.BlockedApps.Contains(process.Name),
                _ => false,
            };
            if (!kill)
                continue;

            var acted = gate.TryEnforce($"end {process.Name} (pid {process.Pid})", () => processes.Kill(process.Pid));
            if (acted || gate.Mode != EnforcementMode.Live)
                RecordAttempt(process.Name, now);
        }
    }

    /// <summary>Logs a blocked attempt at most once a minute per program, so stats aren't flooded.</summary>
    private void RecordAttempt(string name, DateTimeOffset now)
    {
        if (loggedAttempts.TryGetValue(name, out var last) && now - last < TimeSpan.FromMinutes(1))
            return;
        loggedAttempts[name] = now;
        store.RecordEvent(new SessionEvent(now, SessionEventKinds.BlockedApp, name));
    }

    private static bool SamePlan(EnforcementPlan a, EnforcementPlan b) =>
        a.BlockedDomains.SetEquals(b.BlockedDomains) && a.Policies.SetEquals(b.Policies);
}
