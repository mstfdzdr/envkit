using System.Diagnostics;
using System.Reflection;
using System.Windows;
using EnvKit.App.Localization;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace EnvKit.App.Settings;

public interface IThemeService
{
    void Apply(ThemeChoice theme);
}

/// <summary>Applies the theme to the app and, for <see cref="ThemeChoice.System"/>, keeps following Windows.</summary>
public sealed class WpfUiThemeService(Window window) : IThemeService
{
    private bool _watching;

    public void Apply(ThemeChoice theme)
    {
        if (theme == ThemeChoice.System)
        {
            ApplicationThemeManager.ApplySystemTheme(updateAccent: true);
            if (!_watching)
            {
                SystemThemeWatcher.Watch(window, WindowBackdropType.Mica);
                _watching = true;
            }

            return;
        }

        if (_watching)
        {
            SystemThemeWatcher.UnWatch(window);
            _watching = false;
        }

        ApplicationThemeManager.Apply(theme == ThemeChoice.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.Mica);
    }
}

/// <summary>Opens links and folders with the user's default handler.</summary>
public interface IShell
{
    void Open(string target);
}

public sealed class WindowsShell : IShell
{
    public void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
}

public static class AppInfo
{
    public const string AuthorProfileUrl = "https://github.com/mstfdzdr";
    public const string RepositoryUrl = "https://github.com/mstfdzdr/envkit";
    public const string IssuesUrl = RepositoryUrl + "/issues";

    /// <summary>The release version without the commit hash the build appends.</summary>
    public static string Version { get; } =
        (typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
            .Split('+')[0];

    public static System.Globalization.CultureInfo Resolve(LanguageChoice language) => language switch
    {
        LanguageChoice.English => Loc.English,
        LanguageChoice.Turkish => Loc.Turkish,
        _ => Loc.SystemDefault(),
    };
}
