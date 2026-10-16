using System.Windows;
using EnvKit.App.ViewModels;
using EnvKit.App.Views;
using EnvKit.Windows.Elevation;
using EnvKit.Windows.Variables;
using Wpf.Ui.Appearance;

namespace EnvKit.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Relaunched through UAC by WindowsElevation: apply the plan and exit without a window.
        if (e.Args is [ElevatedPlanHost.Verb, var planFile, ElevatedPlanHost.HashOption, var hash])
        {
            Shutdown(ElevatedPlanHost.Run(planFile, hash, new RegistryEnvStore()));
            return;
        }

        var window = new MainWindow { DataContext = new MainViewModel(AppServices.CreateDefault()) };
        ApplicationThemeManager.ApplySystemTheme();
        SystemThemeWatcher.Watch(window);
        window.Show();
    }
}
