using System.Windows;
using EnvKit.App.Localization;
using EnvKit.App.Settings;
using EnvKit.App.ViewModels;
using EnvKit.App.Views;

namespace EnvKit.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = AppServices.CreateDefault();
        var settings = services.SettingsStore.Load();

        // Before any view is built, so every text binds in the chosen language from the start.
        Loc.Instance.SetCulture(AppInfo.Resolve(settings.Language));

        var viewModel = new MainViewModel(services);
        var window = new MainWindow { DataContext = viewModel };
        services.Theme = new WpfUiThemeService(window);
        services.Theme.Apply(settings.Theme);
        window.Show();

        if (Program.UpdatedTo is { } version)
        {
            // The updater starts us in the background; come forward and say what happened.
            viewModel.AnnounceUpdate(version);
            window.Activate();
        }
        else
        {
            _ = viewModel.CheckForUpdatesAsync();
        }
    }
}
