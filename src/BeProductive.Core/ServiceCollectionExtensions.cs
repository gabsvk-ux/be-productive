using BeProductive.Core.Enforcement;
using BeProductive.Core.Recovery;
using BeProductive.Core.SafetyNet;
using BeProductive.Core.Sessions;
using BeProductive.Core.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BeProductive.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBeProductiveCore(this IServiceCollection services, IConfiguration configuration, AppPaths paths)
    {
        services.AddSingleton(paths);
        services.AddSingleton(TimeProvider.System);

        services.Configure<EnforcementOptions>(configuration.GetSection(EnforcementOptions.SectionName));
        services.Configure<RecoveryOptions>(configuration.GetSection(RecoveryOptions.SectionName));
        services.Configure<RecoveryDeliveryOptions>(configuration.GetSection(RecoveryDeliveryOptions.SectionName));
        services.Configure<SafetyNetOptions>(configuration.GetSection(SafetyNetOptions.SectionName));
        services.Configure<SessionOptions>(configuration.GetSection(SessionOptions.SectionName));

        services.AddSingleton<EnforcementGate>();

        services.AddHttpClient();
        services.AddSingleton<IRecoveryCodeStore, JsonRecoveryCodeStore>();
        services.AddSingleton<IRecoveryCodeDelivery>(provider =>
            string.Equals(provider.GetRequiredService<IOptions<RecoveryDeliveryOptions>>().Value.Provider, "Resend", StringComparison.OrdinalIgnoreCase)
                ? ActivatorUtilities.CreateInstance<ResendDelivery>(provider)
                : new NotConfiguredDelivery());
        services.AddSingleton<RecoveryCodeService>();

        services.AddSingleton<IJournalStore, JsonJournalStore>();
        services.AddSingleton<SystemChanges>();

        services.AddSingleton<SqliteStore>();
        services.AddSingleton<ISessionStore>(provider => provider.GetRequiredService<SqliteStore>());
        services.AddSingleton<IPresetStore>(provider => provider.GetRequiredService<SqliteStore>());
        services.AddSingleton<SessionService>();
        return services;
    }

    /// <summary>For systems that have no platform implementation yet. Dry runs still work.</summary>
    public static IServiceCollection AddUnsupportedPlatform(this IServiceCollection services)
    {
        var platform = new UnsupportedPlatform();
        services.AddSingleton<IRegistry>(platform);
        services.AddSingleton<IHostsFile>(platform);
        services.AddSingleton<IDnsCache>(platform);
        return services;
    }
}
