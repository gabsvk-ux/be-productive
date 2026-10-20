using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BeProductive.App.Services;
using BeProductive.App.ViewModels;

namespace BeProductive.App;

public sealed partial class App : Application
{
    private WindowCoordinator? coordinator;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new AppViewModel(new PipeServiceClient(), TimeProvider.System);
            coordinator = new WindowCoordinator(this, desktop, viewModel);
            coordinator.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
