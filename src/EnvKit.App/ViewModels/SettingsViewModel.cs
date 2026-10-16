using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnvKit.App.Localization;
using EnvKit.App.Settings;
using EnvKit.Core;
using Wpf.Ui.Controls;

namespace EnvKit.App.ViewModels;

public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly AppServices _services;
    private readonly MainViewModel _main;
    private bool _loading;

    public SettingsViewModel(AppServices services, MainViewModel main)
        : base("Page_Settings", SymbolRegular.Settings24)
    {
        _services = services;
        _main = main;
        Refresh();
    }

    public IReadOnlyList<ThemeChoice> Themes { get; } = Enum.GetValues<ThemeChoice>();

    public IReadOnlyList<LanguageChoice> Languages { get; } = Enum.GetValues<LanguageChoice>();

    [ObservableProperty]
    public partial ThemeChoice SelectedTheme { get; set; }

    [ObservableProperty]
    public partial LanguageChoice SelectedLanguage { get; set; }

    /// <summary>Result of the last "Check for updates".</summary>
    [ObservableProperty]
    public partial string UpdateStatus { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    public partial bool IsChecking { get; set; }

#pragma warning disable CA1822 // Instance properties so XAML can bind to them.
    public string VersionText => Loc.Format("Settings_Version", AppInfo.Version);

    public string DataFolder => EnvKitPaths.DataDirectory;

    public string AuthorProfileUrl => AppInfo.AuthorProfileUrl;

    public string RepositoryUrl => AppInfo.RepositoryUrl;
#pragma warning restore CA1822

    public override void Refresh()
    {
        _loading = true;
        var settings = _services.SettingsStore.Load();
        SelectedTheme = settings.Theme;
        SelectedLanguage = settings.Language;
        _loading = false;
    }

    partial void OnSelectedThemeChanged(ThemeChoice value)
    {
        if (_loading)
        {
            return;
        }

        Save();
        _services.Theme?.Apply(value);
    }

    partial void OnSelectedLanguageChanged(LanguageChoice value)
    {
        if (_loading)
        {
            return;
        }

        Save();
        Loc.Instance.SetCulture(AppInfo.Resolve(value));
    }

    [RelayCommand]
    private void OpenLink(string url) => _services.Shell?.Open(url);

    [RelayCommand]
    private void OpenDataFolder()
    {
        Directory.CreateDirectory(DataFolder);
        _services.Shell?.Open(DataFolder);
    }

    private bool CanCheck() => !IsChecking;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckForUpdatesAsync()
    {
        if (_services.Updater is not { IsInstalled: true } updater)
        {
            UpdateStatus = Loc.Get("Settings_NotInstalled");
            return;
        }

        IsChecking = true;
        UpdateStatus = Loc.Get("Settings_Checking");
        try
        {
            var version = await updater.DownloadUpdateAsync();
            _main.PendingUpdate = version;
            UpdateStatus = version is null ? Loc.Get("Settings_UpToDate") : Loc.Format("Settings_UpdateReady", version);
        }
#pragma warning disable CA1031 // Shown to the user: network, rate limits, feed problems.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            UpdateStatus = Loc.Format("Settings_UpdateFailed", ex.Message);
        }
        finally
        {
            IsChecking = false;
        }
    }

    private void Save() => _services.SettingsStore.Save(new AppSettings(SelectedTheme, SelectedLanguage));
}
