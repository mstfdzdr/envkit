using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnvKit.App.Localization;
using EnvKit.Core;
using EnvKit.Core.Plans;
using Wpf.Ui.Controls;

namespace EnvKit.App.ViewModels;

/// <param name="titleKey">Resource key of the navigation title.</param>
public abstract partial class PageViewModel(string titleKey, SymbolRegular icon) : ObservableObject
{
    public string Title => Loc.Get(titleKey);

    public SymbolRegular Icon { get; } = icon;

    /// <summary>Reloads from the store, discarding unsaved edits.</summary>
    public abstract void Refresh();

    /// <summary>
    /// Re-renders texts in the new language. Pages override this to rebuild computed texts
    /// without reloading, so unsaved edits survive a language switch.
    /// </summary>
    public virtual void OnLanguageChanged() => OnPropertyChanged(string.Empty);
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;

    public MainViewModel(AppServices services)
    {
        _services = services;
        Variables = new VariablesViewModel(services, this);
        Path = new PathViewModel(services, this);
        Credentials = new CredentialsViewModel(services, this);
        Profiles = new ProfilesViewModel(services, this);
        Vault = new VaultViewModel(services, this);
        ProjectSecrets = new ProjectSecretsViewModel(services, this);
        History = new HistoryViewModel(services, this);
        Settings = new SettingsViewModel(services, this);
        Pages = [Path, Variables, Credentials, Profiles, Vault, ProjectSecrets, History, Settings];
        SelectedPage = Path;
        Loc.Instance.CultureChanged += (_, _) => OnLanguageChanged();
    }

    public VariablesViewModel Variables { get; }

    public PathViewModel Path { get; }

    public CredentialsViewModel Credentials { get; }

    public ProfilesViewModel Profiles { get; }

    public VaultViewModel Vault { get; }

    public ProjectSecretsViewModel ProjectSecrets { get; }

    public HistoryViewModel History { get; }

    public SettingsViewModel Settings { get; }

    public IReadOnlyList<PageViewModel> Pages { get; }

    [ObservableProperty]
    public partial PageViewModel SelectedPage { get; set; }

    /// <summary>The plan awaiting confirmation; the review overlay is shown while this is set.</summary>
    [ObservableProperty]
    public partial ReviewViewModel? Review { get; set; }

    [ObservableProperty]
    public partial bool IsStatusOpen { get; set; }

    [ObservableProperty]
    public partial string StatusTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity StatusSeverity { get; set; }

    /// <summary>Set once a newer release has been downloaded and is ready to install.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateLabel), nameof(UpdateAction))]
    public partial string? PendingUpdate { get; set; }

    /// <summary>The app is about to exit for the update; the button says so instead of seeming dead.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateAction))]
    [NotifyCanExecuteChangedFor(nameof(RestartToUpdateCommand))]
    public partial bool IsRestarting { get; set; }

    /// <summary>First line of the update button: which version is ready.</summary>
    public string UpdateLabel => Loc.Format("Main_UpdateReady", PendingUpdate);

    /// <summary>Second line: what clicking does, or that it is happening.</summary>
    public string UpdateAction => Loc.Get(IsRestarting ? "Main_Restarting" : "Main_RestartToUpdate");

    /// <summary>Called on the first start after an update.</summary>
    public void AnnounceUpdate(string version) =>
        ShowStatus(InfoBarSeverity.Success, Loc.Get("Status_Updated"), Loc.Format("Status_UpdatedMessage", version));

    /// <summary>Best effort: offline or rate-limited simply means no update this time.</summary>
    public async Task CheckForUpdatesAsync()
    {
        if (_services.Updater is not { } updater)
        {
            return;
        }

        try
        {
            PendingUpdate = await updater.DownloadUpdateAsync();
        }
#pragma warning disable CA1031 // Failures are network or feed problems the user cannot act on.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private bool CanRestartToUpdate() => !IsRestarting;

    [RelayCommand(CanExecute = nameof(CanRestartToUpdate))]
    private async Task RestartToUpdateAsync()
    {
        if (_services.Updater is not { } updater)
        {
            return;
        }

        IsRestarting = true;

        // Let the button show "Restarting…" before the process exits for the updater.
        await Task.Delay(400);
        updater.ApplyAndRestart();
    }

    /// <param name="beforeApply">Runs once the user confirms, right before the plan is applied.</param>
    public void RequestReview(ChangePlan plan, Action? beforeApply = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.IsEmpty)
        {
            ShowStatus(InfoBarSeverity.Informational, Loc.Get("Status_NothingToChange"), Loc.Get("Status_NothingToChangeMessage"));
            return;
        }

        Review = new ReviewViewModel(plan, this, beforeApply);
    }

    public void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusSeverity = severity;
        StatusTitle = title;
        StatusMessage = message;
        IsStatusOpen = true;
    }

    internal async Task ApplyAsync(ChangePlan plan, Action? beforeApply = null)
    {
        try
        {
            beforeApply?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            ShowStatus(InfoBarSeverity.Error, Loc.Get("Status_NothingChanged"), ex.Message);
            Review = null;
            return;
        }

        try
        {
            // The UAC prompt and the broadcast both block; keep the UI thread free.
            var result = await Task.Run(() => _services.Executor().Execute(plan));
            var message = Loc.Format("Status_AppliedMessage", result.Snapshot!.Id);
            if (plan.Changes.Any(c => c is VariableChange))
            {
                message += " " + Loc.Get("Status_Propagation");
            }

            ShowStatus(InfoBarSeverity.Success, Loc.Get("Status_Applied"), message);
        }
        catch (OperationCanceledException)
        {
            ShowStatus(InfoBarSeverity.Warning, Loc.Get("Status_Cancelled"), Loc.Get("Status_ElevationDeclined"));
        }
        catch (ElevationRequiredException)
        {
            ShowStatus(InfoBarSeverity.Error, Loc.Get("Status_AdminNeeded"), Loc.Get("Status_AdminNeededMessage"));
        }
        catch (ElevatedApplyException ex)
        {
            ShowStatus(InfoBarSeverity.Error, Loc.Get("Status_MachineFailed"), Loc.Format("Status_MachineFailedMessage", ex.Message));
        }
        catch (PlanConflictException ex)
        {
            ShowStatus(InfoBarSeverity.Warning, Loc.Get("Status_Conflict"), Loc.Format("Status_ConflictMessage", ex.Message));
        }
        catch (PlanApplyException ex)
        {
            ShowStatus(InfoBarSeverity.Error, Loc.Get("Status_Partial"), ex.Message);
        }
        finally
        {
            Review = null;
            RefreshAll();
        }
    }

    public void RefreshAll()
    {
        foreach (var page in Pages)
        {
            page.Refresh();
        }
    }

    private void OnLanguageChanged()
    {
        foreach (var page in Pages)
        {
            page.OnLanguageChanged();
        }

        OnPropertyChanged(nameof(UpdateLabel));
        OnPropertyChanged(nameof(UpdateAction));
    }
}
