using System.Globalization;
using BeProductive.Cli;
using BeProductive.Core;
using BeProductive.Core.Enforcement;
using BeProductive.Core.Ipc;
using BeProductive.Core.Recovery;
using BeProductive.Core.SafetyNet;
using BeProductive.Core.Sessions;
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
        ["status"] => await Status(),
        ["presets"] => await Presets(),
        ["stats"] => await Stats(7),
        ["stats", var days] => await Stats(Minutes(days)),
        ["plan", var preset, var minutes, .. var rest] => await Send(new PlanRequest(preset, Minutes(minutes), Block(rest, 0), Note(rest, 1))),
        ["postpone", var until] => await Send(new PostponeRequest(TodayAt(until))),
        ["pause"] => await Send(new PauseRequest()),
        ["resume"] => await Send(new ResumeRequest()),
        ["next", .. var rest] => await Send(new StartNextRequest(Block(rest, 0), Note(rest, 1))),
        ["switch", var preset] => await Send(new SwitchPresetRequest(preset)),
        ["extend", var minutes, .. var rest] => await Send(new ExtendRequest(Minutes(minutes), Block(rest, 0), At(rest, 1), Note(rest, 2))),
        ["stop"] => await Send(new StopExtraRequest()),
        ["exit", .. var parts] when parts.Length > 0 => await Send(new RecoveryExitRequest(string.Join("", parts))),
        ["codes", "init"] => await CodesInit(),
        ["codes", "status"] => CodesStatus(),
        ["codes", "regenerate"] => await CodesRegenerate(),
        ["restore"] => Restore(),
        _ => Usage(),
    };
}
catch (UsageException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Error: {exception.Message}");
    return 1;
}

async Task<int> Status()
{
    var gate = services.GetRequiredService<EnforcementGate>();
    var applied = services.GetRequiredService<SystemChanges>().Describe();
    var codes = services.GetRequiredService<RecoveryCodeService>().GetStatus();

    try
    {
        var response = await IpcProtocol.SendAsync(new StatusRequest(), CancellationToken.None);
        if (response.Status is not null)
            StatusPrinter.Print(response.Status);
    }
    catch (ServiceUnavailableException exception)
    {
        Console.WriteLine(exception.Message);
    }

    Console.WriteLine();
    Console.WriteLine($"Enforcement:  {gate.Mode}");
    Console.WriteLine(applied.Count == 0
        ? "Applied:      no changes"
        : $"Applied:      {applied.Count} change(s), undone at {applied.ExpiresAt?.ToLocalTime():t} unless renewed");
    Console.WriteLine(codes.HasCodes
        ? $"Codes:        {codes.Remaining} of {codes.Total} recovery codes left"
        : "Codes:        not set up (run: bp codes init)");
    Console.WriteLine($"Data:         {paths.DataDirectory}");
    return 0;
}

async Task<int> Presets()
{
    var response = await IpcProtocol.SendAsync(new PresetsRequest(), CancellationToken.None);
    foreach (var preset in response.Presets ?? [])
    {
        Console.WriteLine($"{preset.Id,-14} {preset.Name} (default {preset.DefaultBlock.Label()})");
        if (preset.AllowedApps.Count > 0)
            Console.WriteLine($"{"",-14} apps: {string.Join(", ", preset.AllowedApps)}");
    }

    return 0;
}

async Task<int> Stats(int days)
{
    var response = await IpcProtocol.SendAsync(new StatsRequest(days), CancellationToken.None);
    if (response.Stats is { } stats)
        StatsPrinter.Print(stats);
    return 0;
}

async Task<int> Send(IpcRequest request)
{
    var response = await IpcProtocol.SendAsync(request, CancellationToken.None);
    if (!response.Ok)
    {
        Console.Error.WriteLine(response.Error);
        return 1;
    }

    if (response.Message is not null)
        Console.WriteLine(response.Message);
    if (response.Status is not null)
        StatusPrinter.Print(response.Status);
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
    Console.WriteLine("Each code ends a focus lock once (bp exit <code>). This PC keeps only hashed copies.");
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
        Console.WriteLine("A new set may have been sent but isn't confirmed yet. Codes from either set work.");
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

        Today
          status                                 Where the day stands
          presets                                List presets
          stats [days]                           Focus per day and per preset (default: the last 7 days)
          plan <preset> <minutes> [15|25|45] [note]
                                                 Plan the day at the start screen and start the first block
          postpone <HH:mm>                       Postpone the start (3 per week, until 20:00 at the latest)
          pause | resume                         Pause the running focus block (10 min at most)
          next [15|25|45] [note]                 Start the next block after (or during) a break
          switch <preset>                        Switch preset during a break
          extend <minutes> [15|25|45] [preset] [note]
                                                 Add extra time once the planned time is done
          stop                                   Stop extra time
          exit <recovery code>                   End today's lock with a recovery code (the day counts as failed)

        Setup and safety
          codes init                             Create the first set of recovery codes (shown once)
          codes status                           How many codes are left
          codes regenerate                       Email your friend a new set
          restore                                Undo every change the app has made to this PC
        """);
    return 2;
}

static int Minutes(string text) =>
    int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
        ? minutes
        : throw new UsageException($"'{text}' isn't a number of minutes.");

static BlockKind Block(string[] rest, int index)
{
    if (rest.Length <= index)
        return BlockKind.Classic25;
    return BlockKinds.TryParse(rest[index], out var kind)
        ? kind
        : throw new UsageException($"'{rest[index]}' isn't a block length. Use 15, 25 or 45.");
}

static string? At(string[] rest, int index) => rest.Length > index ? rest[index] : null;

static string? Note(string[] rest, int index) => rest.Length > index ? string.Join(' ', rest[index..]) : null;

static DateTimeOffset TodayAt(string text)
{
    if (!TimeOnly.TryParseExact(text, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        throw new UsageException($"'{text}' isn't a time. Use HH:mm, e.g. 16:30.");
    return new DateTimeOffset(DateTime.Today + time.ToTimeSpan(), TimeZoneInfo.Local.GetUtcOffset(DateTime.Today + time.ToTimeSpan()));
}

internal sealed class UsageException(string message) : Exception(message);
