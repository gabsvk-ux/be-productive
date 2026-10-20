using BeProductive.Core;
using BeProductive.Core.Enforcement;
using BeProductive.Core.Recovery;
using BeProductive.Core.SafetyNet;
using BeProductive.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Command-line arguments are commands here, not configuration, so they aren't passed to the host.
var builder = Host.CreateApplicationBuilder();
builder.Logging.SetMinimumLevel(LogLevel.Error);

var paths = AppPaths.FromConfiguration(builder.Configuration);
builder.Configuration.AddJsonFile(paths.SettingsFile, optional: true, reloadOnChange: false);
builder.Services.AddBeProductiveCore(builder.Configuration, paths);
if (OperatingSystem.IsWindows())
    builder.Services.AddWindowsPlatform();
else
    builder.Services.AddUnsupportedPlatform();

using var host = builder.Build();
var services = host.Services;

try
{
    return args switch
    {
        ["status"] => Status(),
        ["codes", "init"] => await CodesInit(),
        ["codes", "status"] => CodesStatus(),
        ["codes", "redeem", .. var parts] when parts.Length > 0 => await CodesRedeem(string.Join("", parts)),
        ["codes", "regenerate"] => await CodesRegenerate(),
        ["restore"] => Restore(),
        _ => Usage(),
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Error: {exception.Message}");
    return 1;
}

int Status()
{
    var gate = services.GetRequiredService<EnforcementGate>();
    var applied = services.GetRequiredService<SystemChanges>().Describe();
    var codes = services.GetRequiredService<RecoveryCodeService>().GetStatus();

    Console.WriteLine($"Data directory:   {paths.DataDirectory}");
    Console.WriteLine($"Enforcement mode: {gate.Mode}");
    Console.WriteLine(applied.Count == 0
        ? "Applied changes:  none"
        : $"Applied changes:  {applied.Count}, undone at {applied.ExpiresAt?.ToLocalTime():g} unless renewed");
    Console.WriteLine(codes.HasCodes
        ? $"Recovery codes:   {codes.Remaining} of {codes.Total} left"
        : "Recovery codes:   not set up (run: bp codes init)");
    return 0;
}

async Task<int> CodesInit()
{
    var codes = await services.GetRequiredService<RecoveryCodeService>().CreateFirstSetAsync(CancellationToken.None);

    Console.WriteLine("Recovery codes: give these to your friend. They won't be shown again.");
    Console.WriteLine();
    for (var i = 0; i < codes.Count; i++)
        Console.WriteLine($"  {i + 1,2}. {codes[i]}");
    Console.WriteLine();
    Console.WriteLine("Each code ends a focus lock once. This PC keeps only hashed copies.");
    Console.WriteLine("Clear this window once your friend has them.");
    return 0;
}

int CodesStatus()
{
    var status = services.GetRequiredService<RecoveryCodeService>().GetStatus();
    if (!status.HasCodes)
    {
        Console.WriteLine("No recovery codes yet. Run: bp codes init");
        return 0;
    }

    Console.WriteLine($"{status.Remaining} of {status.Total} codes left (set created {status.CreatedAt?.ToLocalTime():g}).");
    if (status.DeliveryPending)
        Console.WriteLine("A new set was being sent and isn't confirmed yet. Codes from either set work.");
    return 0;
}

async Task<int> CodesRedeem(string code)
{
    var result = await services.GetRequiredService<RecoveryCodeService>().RedeemAsync(code, CancellationToken.None);
    switch (result.Outcome)
    {
        case RedeemOutcome.NoCodes:
            Console.WriteLine("No recovery codes are set up.");
            return 1;
        case RedeemOutcome.Rejected:
            Console.WriteLine("That code isn't valid or has already been used.");
            return 1;
    }

    Console.WriteLine($"Code accepted. {result.Remaining} left.");
    if (result.ShouldOfferNewSet)
        Console.WriteLine("Running low: run `bp codes regenerate` to email your friend a new set.");
    if (result.NewSetSent)
        Console.WriteLine("That was the last one, so a new set was emailed to your friend.");
    if (result.NewSetError is not null)
        Console.WriteLine($"That was the last one, and sending a new set failed: {result.NewSetError}. The service keeps retrying every 5 minutes.");
    return 0;
}

async Task<int> CodesRegenerate()
{
    await services.GetRequiredService<RecoveryCodeService>().RegenerateAsync(CancellationToken.None);
    Console.WriteLine("A new set was emailed to your friend. The old codes no longer work.");
    return 0;
}

int Restore()
{
    var report = services.GetRequiredService<SystemChanges>().RestoreAll("`bp restore` was run");
    Console.WriteLine(report.Restored == 0 && report.Failed == 0
        ? "Nothing to undo."
        : $"Undid {report.Restored} change(s); {report.Failed} failed (see the log, or run scripts/restore.ps1).");
    return report.Failed == 0 ? 0 : 1;
}

int Usage()
{
    Console.WriteLine("""
        Usage: bp <command>

          status                 Enforcement mode, applied changes and recovery codes
          codes init             Create the first set of recovery codes (shown once)
          codes status           How many codes are left
          codes redeem <code>    Use a recovery code
          codes regenerate       Email your friend a new set
          restore                Undo every change the app has made
        """);
    return 2;
}
