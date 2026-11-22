using BeProductive.Core;
using BeProductive.Core.Enforcement;
using Microsoft.Extensions.Hosting.WindowsServices;
using BeProductive.Service;
using BeProductive.Windows;

var builder = Host.CreateApplicationBuilder(args);

var paths = AppPaths.FromConfiguration(builder.Configuration);
builder.Configuration.AddJsonFile(paths.SettingsFile, optional: true, reloadOnChange: false);

builder.Services.AddWindowsService(options => options.ServiceName = "BeProductive");

// Arm enforcement when we run as an installed Windows service: the config can then only make the lock
// stricter, never weaker (DryRun and the panic file are ignored). A console/dev run (dotnet run) stays
// disarmed, so DryRun and the panic file keep working for safe development.
builder.Services.AddSingleton(new EnforcementArming(WindowsServiceHelpers.IsWindowsService()));
builder.Services.AddBeProductiveCore(builder.Configuration, paths);
if (OperatingSystem.IsWindows())
    builder.Services.AddWindowsPlatform();
else
    builder.Services.AddUnsupportedPlatform();

builder.Services.AddHostedService<SafetyNetWorker>();
builder.Services.AddHostedService<RecoveryCodeWorker>();
builder.Services.AddHostedService<SessionWorker>();
builder.Services.AddHostedService<IpcServerWorker>();
builder.Services.AddHostedService<WatchdogWorker>();
builder.Services.AddHostedService<ConfigWatchWorker>();

builder.Build().Run();
