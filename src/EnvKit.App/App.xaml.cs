using System.Windows;
using EnvKit.App.ViewModels;
using EnvKit.App.Views;
using Wpf.Ui.Appearance;

namespace EnvKit.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var viewModel = new MainViewModel(AppServices.CreateDefault());
        var window = new MainWindow { DataContext = viewModel };
        ApplicationThemeManager.ApplySystemTheme();
        SystemThemeWatcher.Watch(window);
        window.Show();

        _ = viewModel.CheckForUpdatesAsync();
    }
}
