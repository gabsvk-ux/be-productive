using System.Runtime.Versioning;
using BeProductive.Core.Enforcement;
using BeProductive.Core.SafetyNet;

namespace BeProductive.Windows;

/// <summary>
/// The concrete registry policies for each group. Chrome policies are machine-wide; the admin-tool
/// policies are per-user, so they go under the signed-in user's hive (<c>HKEY_USERS\{sid}</c>).
/// Tier 1 of the tamper-resistance research note; the boot-risky Tier 3 is deliberately left out.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsEnforcementPolicies : IEnforcementPolicies
{
    private const string Chrome = @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Google\Chrome";

    public IReadOnlyList<PolicyValue> For(PolicyGroup group) => group switch
    {
        PolicyGroup.Chrome =>
        [
            new(Chrome, "DnsOverHttpsMode", PolicyValueKind.String, "off"),
            new(Chrome, "IncognitoModeAvailability", PolicyValueKind.DWord, "1"),
            new(Chrome, "DeveloperToolsAvailability", PolicyValueKind.DWord, "2"),
        ],
        PolicyGroup.AdminTools => AdminTools(),
        _ => [],
    };

    private static IReadOnlyList<PolicyValue> AdminTools()
    {
        // No signed-in user means no hive to write; skip rather than guess.
        if (WindowsUserSession.ActiveUserSid() is not { } sid)
            return [];

        var system = $@"HKEY_USERS\{sid}\Software\Microsoft\Windows\CurrentVersion\Policies\System";
        return
        [
            new(system, "DisableTaskMgr", PolicyValueKind.DWord, "1"),
            new(system, "DisableRegistryTools", PolicyValueKind.DWord, "1"),
            new(system, "DisableCMD", PolicyValueKind.DWord, "1"),
        ];
    }
}
