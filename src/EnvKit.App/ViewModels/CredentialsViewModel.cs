using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnvKit.Core.Credentials;
using Wpf.Ui.Controls;

namespace EnvKit.App.ViewModels;

public sealed partial class CredentialRow(Credential credential) : ObservableObject
{
    public Credential Credential { get; } = credential;

    public CredentialGroup Group { get; } = CredentialClassifier.Classify(credential.Key);

    public string TargetName => Credential.TargetName;

    public string UserName => Credential.State.UserName ?? string.Empty;

    public string KindLabel => Credential.Kind == CredentialKind.Generic ? string.Empty : Credential.Kind.ToString();

    public DateTimeOffset LastWritten => Credential.LastWritten.ToLocalTime();

    /// <summary>Ticked for bulk delete.</summary>
    [ObservableProperty]
    public partial bool IsChecked { get; set; }
}

/// <param name="Group"><see langword="null"/> for all groups.</param>
public sealed record GroupOption(CredentialGroup? Group, string Label);

public sealed partial class CredentialsViewModel : PageViewModel
{
    private readonly AppServices _services;
    private readonly MainViewModel _main;
    private List<Credential> _all = [];

    public CredentialsViewModel(AppServices services, MainViewModel main)
        : base("Credentials", SymbolRegular.Key24)
    {
        _services = services;
        _main = main;
        Refresh();
    }

    public ObservableCollection<CredentialRow> Rows { get; } = [];

    public ObservableCollection<GroupOption> Groups { get; } = [];

    public IReadOnlyList<CredentialPersistence> Persistences { get; } =
        [CredentialPersistence.LocalMachine, CredentialPersistence.Enterprise, CredentialPersistence.Session];

    [ObservableProperty]
    public partial GroupOption? SelectedGroup { get; set; }

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNew), nameof(EditorTitle), nameof(IsReadOnlyKind), nameof(PasswordHint))]
    [NotifyCanExecuteChangedFor(nameof(RevealPasswordCommand), nameof(SaveCommand))]
    public partial CredentialRow? Selected { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string EditTarget { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditUser { get; set; } = string.Empty;

    /// <summary>Empty keeps the current password when editing.</summary>
    [ObservableProperty]
    public partial string EditPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditComment { get; set; } = string.Empty;

    [ObservableProperty]
    public partial CredentialPersistence EditPersistence { get; set; } = CredentialPersistence.LocalMachine;

    /// <summary>The stored secret, only after the user asked for it; cleared when the selection changes.</summary>
    [ObservableProperty]
    public partial string? RevealedSecret { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyPropertyChangedFor(nameof(DeleteLabel))]
    public partial int DeleteCount { get; set; }

    public string DeleteLabel => DeleteCount <= 1 ? "Delete…" : $"Delete {DeleteCount}…";

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    public bool IsNew => Selected is null;

    public string EditorTitle => IsNew ? "New credential" : "Edit credential";

    /// <summary>Domain credentials: Windows keeps the secret, so they can be deleted but not edited.</summary>
    public bool IsReadOnlyKind => Selected is { Credential.IsEditable: false };

    public string PasswordHint => IsNew ? "Password" : "New password (leave empty to keep the current one)";

    public override void Refresh()
    {
        var keepGroup = SelectedGroup?.Group;
        var keepSelected = Selected?.Credential.Key;
        _all = [.. _services.Credentials.List()];

        Groups.Clear();
        Groups.Add(new GroupOption(null, $"All ({_all.Count})"));
        foreach (var group in _all.GroupBy(c => CredentialClassifier.Classify(c.Key)).OrderBy(g => g.Key))
        {
            Groups.Add(new GroupOption(group.Key, $"{group.Key} ({group.Count()})"));
        }

        SelectedGroup = Groups.FirstOrDefault(g => g.Group == keepGroup) ?? Groups[0];
        Filter(keepSelected);
    }

    partial void OnSelectedGroupChanged(GroupOption? value) => Filter(Selected?.Credential.Key);

    partial void OnSearchChanged(string value) => Filter(Selected?.Credential.Key);

    partial void OnSelectedChanged(CredentialRow? value)
    {
        RevealedSecret = null;
        EditPassword = string.Empty;
        if (value is null)
        {
            EditTarget = string.Empty;
            EditUser = string.Empty;
            EditComment = string.Empty;
            EditPersistence = CredentialPersistence.LocalMachine;
            return;
        }

        var state = value.Credential.State;
        EditTarget = value.TargetName;
        EditUser = state.UserName ?? string.Empty;
        EditComment = state.Comment ?? string.Empty;
        EditPersistence = state.Persistence;
    }

    [RelayCommand]
    private void New() => Selected = null;

    private bool CanReveal() => Selected is { Credential.State.Secret: not null };

    [RelayCommand(CanExecute = nameof(CanReveal))]
    private void RevealPassword() => RevealedSecret = Selected!.Credential.State.SecretText;

    private bool CanSave() => EditTarget.Trim().Length > 0 && !IsReadOnlyKind;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var key = Selected?.Credential.Key ?? new CredentialKey(EditTarget.Trim(), CredentialKind.Generic);
        var existing = Selected?.Credential.State;
        var secret = EditPassword.Length == 0 && existing?.Secret is { } current
            ? current
            : CredentialState.EncodeSecret(EditPassword, existing?.Secret);
        var state = (existing ?? new CredentialState(null, null, CredentialPersistence.LocalMachine)) with
        {
            UserName = EditUser.Length == 0 ? null : EditUser,
            Comment = EditComment.Length == 0 ? null : EditComment,
            Persistence = EditPersistence,
            Secret = secret,
        };

        try
        {
            _main.RequestReview(_services.NewPlan().SetCredential(key, state).Build($"Set credential {key}"));
        }
        catch (ArgumentException ex)
        {
            _main.ShowStatus(InfoBarSeverity.Error, "Invalid credential", ex.Message);
        }
    }

    private bool CanDelete() => DeleteCount > 0;

    /// <summary>Deletes the ticked rows, or the selected one when none is ticked.</summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        var targets = DeleteTargets();
        var builder = _services.NewPlan();
        foreach (var row in targets)
        {
            builder.DeleteCredential(row.Credential.Key);
        }

        _main.RequestReview(builder.Build($"Delete {targets.Count} credential(s)"));
    }

    /// <summary>Writes the visible rows without secrets; the view picks the file.</summary>
    public void ExportTo(string path)
    {
        File.WriteAllText(path, CredentialExport.ToJson(Rows.Select(r => r.Credential)));
        _main.ShowStatus(InfoBarSeverity.Success, "Exported", $"{Rows.Count} credential(s) written to {path}. No secrets are included.");
    }

    private List<CredentialRow> DeleteTargets()
    {
        var ticked = Rows.Where(r => r.IsChecked).ToList();
        return ticked.Count > 0 ? ticked : Selected is null ? [] : [Selected];
    }

    private void Filter(CredentialKey? keepSelected)
    {
        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        Rows.Clear();
        foreach (var credential in _all
            .Where(c => SelectedGroup?.Group is not { } group || CredentialClassifier.Classify(c.Key) == group)
            .Where(Matches)
            .OrderBy(c => CredentialClassifier.Classify(c.Key))
            .ThenBy(c => c.TargetName, CredentialKey.TargetComparer))
        {
            var row = new CredentialRow(credential);
            row.PropertyChanged += OnRowChanged;
            Rows.Add(row);
        }

        Selected = Rows.FirstOrDefault(r => r.Credential.Key == keepSelected);
        Summary = $"{Rows.Count} shown";
        UpdateDeleteCount();
    }

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateDeleteCount();

    partial void OnSelectedChanged(CredentialRow? oldValue, CredentialRow? newValue) => UpdateDeleteCount();

    private void UpdateDeleteCount() => DeleteCount = DeleteTargets().Count;

    // Secrets are never searched.
    private bool Matches(Credential credential) =>
        Search.Length == 0
        || credential.TargetName.Contains(Search, StringComparison.OrdinalIgnoreCase)
        || (credential.State.UserName?.Contains(Search, StringComparison.OrdinalIgnoreCase) ?? false);
}
