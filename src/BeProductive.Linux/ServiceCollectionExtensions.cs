using BeProductive.Core.Enforcement;
using BeProductive.Core.SafetyNet;
using Microsoft.Extensions.DependencyInjection;

namespace BeProductive.Linux;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLinuxPlatform(this IServiceCollection services)
    {
        services.AddSingleton(LinuxRoot.Real);
        services.AddSingleton<ProcFs>();
        services.AddSingleton<LinuxUserSession>();
        services.AddSingleton<DesktopApps>();
        services.AddSingleton<IRegistry, JsonPolicyFiles>();
        services.AddSingleton<IHostsFile, LinuxHostsFile>();
        services.AddSingleton<IDnsCache, LinuxDnsCache>();
        services.AddSingleton<IProcessGuard, LinuxProcessGuard>();
        services.AddSingleton<IEnforcementPolicies, LinuxEnforcementPolicies>();
        services.AddSingleton<IAppLauncher, LinuxAppLauncher>();
        return services;
    }
}
