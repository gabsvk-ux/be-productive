using System.Runtime.Versioning;
using BeProductive.Core.SafetyNet;
using Microsoft.Extensions.DependencyInjection;

namespace BeProductive.Windows;

public static class ServiceCollectionExtensions
{
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddWindowsPlatform(this IServiceCollection services)
    {
        services.AddSingleton<IRegistry, WindowsRegistry>();
        services.AddSingleton<IHostsFile, WindowsHostsFile>();
        services.AddSingleton<IDnsCache, WindowsDnsCache>();
        return services;
    }
}
