using BeProductive.Core;
using BeProductive.Service;
using BeProductive.Windows;

var builder = Host.CreateApplicationBuilder(args);

var paths = AppPaths.FromConfiguration(builder.Configuration);
builder.Configuration.AddJsonFile(paths.SettingsFile, optional: true, reloadOnChange: false);

builder.Services.AddWindowsService(options => options.ServiceName = "BeProductive");
builder.Services.AddBeProductiveCore(builder.Configuration, paths);
if (OperatingSystem.IsWindows())
    builder.Services.AddWindowsPlatform();
else
    builder.Services.AddUnsupportedPlatform();

builder.Services.AddHostedService<SafetyNetWorker>();
builder.Services.AddHostedService<RecoveryCodeWorker>();
builder.Services.AddHostedService<SessionWorker>();
builder.Services.AddHostedService<IpcServerWorker>();

builder.Build().Run();
