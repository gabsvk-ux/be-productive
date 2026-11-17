using BeProductive.Core.Enforcement;
using BeProductive.Core.SafetyNet;

namespace BeProductive.Linux;

/// <summary>
/// The concrete policies for each group on Linux. Chrome reads managed policies from JSON files (one file of
/// our own per browser, so restore.sh can simply delete it). Linux has no equivalent of the Windows
/// Task Manager / regedit / cmd policies, so the admin-tools group has no policy values: those tools are
/// ended by name instead (<see cref="EnforcementOptions.AdminToolApps"/> and the terminal list).
/// </summary>
public sealed class LinuxEnforcementPolicies : IEnforcementPolicies
{
    /// <summary>Our policy file in each Chromium-family browser's managed-policy directory.</summary>
    public static readonly IReadOnlyList<string> ChromePolicyFiles =
    [
        "/etc/opt/chrome/policies/managed/be-productive.json",
        "/etc/chromium/policies/managed/be-productive.json",
    ];

    public IReadOnlyList<PolicyValue> For(PolicyGroup group) => group switch
    {
        PolicyGroup.Chrome => ChromePolicyFiles
            .Select(JsonPolicyFiles.KeyPathFor)
            .SelectMany(file => (PolicyValue[])
            [
                new(file, "DnsOverHttpsMode", PolicyValueKind.String, "off"),
                new(file, "IncognitoModeAvailability", PolicyValueKind.DWord, "1"),
                new(file, "DeveloperToolsAvailability", PolicyValueKind.DWord, "2"),
            ])
            .ToList(),
        _ => [],
    };
}
