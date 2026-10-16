using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnvKit.Core.DotEnv;
using EnvKit.Core.Plans;
using EnvKit.Core.Security;
using EnvKit.Core.Vault;
using Wpf.Ui.Controls;

namespace EnvKit.App.ViewModels;

public sealed record SecretFileRow(ProjectSecretFile File, string Root)
{
    public string Title => File.Format == FileFormat.UserSecrets
        ? $"User secrets · {System.IO.Path.GetFileName(File.Project)}"
        : System.IO.Path.GetRelativePath(Root, File.Path);

    public string Detail => File.Format == FileFormat.UserSecrets
        ? System.IO.Path.GetRelativePath(Root, File.Project!)
        : File.IsTemplate ? "template" : string.Empty;

    public string Warnings => string.Join(" · ", File.WarningList.Select(w => w == SecretFileWarning.Committed
        ? "committed to git"
        : "not in .gitignore"));

    public bool HasWarnings => File.WarningList.Count > 0;
}

public sealed record SecretEntryRow(string Key, string Value, bool IsSecret, bool Reveal)
{
    public bool IsVaultReference => VaultReference.HasReferences(Value);

    public string DisplayValue => IsSecret && !Reveal && !IsVaultReference ? SecretDetector.Mask : Value;

    /// <summary>
    /// WPF uses this as the row's UI Automation name; the record default would include
    /// <see cref="Value"/> and hand masked secrets to screen readers and automation clients.
    /// </summary>
    public override string ToString() => Key;
}

/// <summary>.env files and .NET user secrets in a folder tree.</summary>
public sealed partial class ProjectSecretsViewModel : PageViewModel
{
    private readonly AppServices _services;
    private readonly MainViewModel _main;

    public ProjectSecretsViewModel(AppServices services, MainViewModel main)
        : base("Project secrets", SymbolRegular.DocumentLock24)
    {
        _services = services;
        _main = main;
    }

    public ObservableCollection<SecretFileRow> Files { get; } = [];

    public ObservableCollection<SecretEntryRow> Entries { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    public partial string Folder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial string ScanSummary { get; set; } = "Choose a folder to find .env files and user secrets.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDotEnv), nameof(HasFile))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(DeleteCommand), nameof(MoveToVaultCommand))]
    public partial SecretFileRow? SelectedFile { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(MoveToVaultCommand))]
    public partial SecretEntryRow? SelectedEntry { get; set; }

    [ObservableProperty]
    public partial bool RevealSecrets { get; set; }

    /// <summary>Duplicate keys, invalid lines, git warnings or why the file cannot be read.</summary>
    [ObservableProperty]
    public partial string Problems { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string EditKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditValue { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveToVaultCommand))]
    public partial string VaultName { get; set; } = string.Empty;

    /// <summary>Set when the selected file cannot be edited (invalid UTF-8 or JSON).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(DeleteCommand), nameof(MoveToVaultCommand))]
    public partial bool IsReadOnly { get; set; }

    public bool HasFile => SelectedFile is not null;

    public bool IsDotEnv => SelectedFile?.File.Format == FileFormat.DotEnv;

    public override void Refresh() => LoadEntries();

    private bool CanScan() => Folder.Trim().Length > 0 && !IsScanning;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        var root = Folder.Trim();
        if (!Directory.Exists(root))
        {
            ScanSummary = $"{root} does not exist.";
            return;
        }

        IsScanning = true;
        ScanSummary = "Scanning…";
        try
        {
            // git is asked once per .env file, which takes a moment on large trees.
            var found = await Task.Run(() => ProjectScanner.Scan(root, _services.Git));
            Files.Clear();
            foreach (var file in found)
            {
                Files.Add(new SecretFileRow(file, Path.GetFullPath(root)));
            }

            var warned = found.Count(f => f.WarningList.Count > 0);
            ScanSummary = $"{found.Count} found" + (warned > 0 ? $", {warned} with git warnings" : string.Empty);
            SelectedFile = Files.FirstOrDefault();
        }
        finally
        {
            IsScanning = false;
        }
    }

    partial void OnSelectedFileChanged(SecretFileRow? value) => LoadEntries();

    partial void OnRevealSecretsChanged(bool value) => LoadEntries();

    partial void OnSelectedEntryChanged(SecretEntryRow? value)
    {
        EditKey = value?.Key ?? string.Empty;
        EditValue = value is null || (value.IsSecret && !RevealSecrets && !value.IsVaultReference) ? string.Empty : value.Value;
        VaultName = value is null ? string.Empty : SuggestVaultName(value.Key);
    }

    [RelayCommand]
    private void NewEntry() => SelectedEntry = null;

    private bool CanEdit() => SelectedFile is not null && !IsReadOnly;

    private bool CanSave() => CanEdit() && EditKey.Trim().Length > 0;

    /// <summary>For a masked secret, an empty value field means "keep the current value".</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var key = EditKey.Trim();
        var keepCurrent = EditValue.Length == 0 && SelectedEntry is { IsSecret: true } entry && entry.Key == key && !RevealSecrets;
        if (keepCurrent)
        {
            _main.ShowStatus(InfoBarSeverity.Informational, "Nothing to change", "Type a new value to replace the hidden one.");
            return;
        }

        Review(builder => Edit(builder, d => d.Set(key, EditValue), s => s.Set(key, EditValue)), $"Set {key}");
    }

    private bool CanChangeEntry() => CanEdit() && SelectedEntry is not null;

    [RelayCommand(CanExecute = nameof(CanChangeEntry))]
    private void Delete()
    {
        var key = SelectedEntry!.Key;
        Review(builder => Edit(builder, d => d.Remove(key), s => s.Remove(key)), $"Remove {key}");
    }

    private bool CanMoveToVault() =>
        CanChangeEntry() && IsDotEnv && !SelectedEntry!.IsVaultReference && VaultNames.IsValid(VaultName.Trim());

    /// <summary>Puts the value in the vault and leaves <c>${vault:NAME}</c> in the file.</summary>
    [RelayCommand(CanExecute = nameof(CanMoveToVault))]
    private void MoveToVault()
    {
        var (key, value, name) = (SelectedEntry!.Key, SelectedEntry.Value, VaultName.Trim());
        if (_services.Vault.Get(name) is { } existing && existing != value)
        {
            _main.ShowStatus(InfoBarSeverity.Error, "Name taken", $"The vault already has a different '{name}'.");
            return;
        }

        var plan = Plan(builder => builder.EditDotEnv(SelectedFile!.File.Path, d => d.Set(key, VaultReference.For(name))), $"Move {key} into the vault as {name}");
        if (plan is not null)
        {
            _main.RequestReview(plan, beforeApply: () => _services.Vault.Set(name, value));
        }
    }

    private void Edit(PlanBuilder builder, Action<DotEnvDocument> dotEnv, Action<UserSecretsDocument> userSecrets)
    {
        var path = SelectedFile!.File.Path;
        if (IsDotEnv)
        {
            builder.EditDotEnv(path, dotEnv);
        }
        else
        {
            builder.EditUserSecrets(path, d => userSecrets(d));
        }
    }

    private void Review(Action<PlanBuilder> build, string description)
    {
        if (Plan(build, description) is { } plan)
        {
            _main.RequestReview(plan);
        }
    }

    private ChangePlan? Plan(Action<PlanBuilder> build, string description)
    {
        try
        {
            var builder = _services.NewPlan();
            build(builder);
            return builder.Build($"{description} in {SelectedFile!.Title}");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or JsonException)
        {
            _main.ShowStatus(InfoBarSeverity.Error, "Cannot edit", ex.Message);
            return null;
        }
    }

    private void LoadEntries()
    {
        var keep = SelectedEntry?.Key;
        Entries.Clear();
        Problems = string.Empty;
        IsReadOnly = false;
        if (SelectedFile is not { } row)
        {
            return;
        }

        var problems = new List<string>();
        if (row.HasWarnings)
        {
            problems.Add(row.Warnings);
        }

        IReadOnlyList<KeyValuePair<string, string>> values = [];
        try
        {
            var bytes = _services.Files.Read(row.File.Path);
            if (row.File.Format == FileFormat.DotEnv)
            {
                var document = bytes is null ? DotEnvFile.Empty().Document : DotEnvFile.Parse(bytes).Document;
                values = document.Values();
                problems.AddRange(document.DuplicateKeys.Select(k => $"{k} is assigned more than once (the last one wins)"));
                problems.AddRange(document.InvalidLines.Select(l => $"line {l.LineNumber} is not KEY=VALUE"));
            }
            else
            {
                values = UserSecretsDocument.Parse(bytes).Values();
                if (bytes is null)
                {
                    problems.Add("no secrets.json yet: saving a key creates it");
                }
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            problems.Add($"read-only: {ex.Message}");
            IsReadOnly = true;
        }

        foreach (var (key, value) in values)
        {
            Entries.Add(new SecretEntryRow(key, value, SecretDetector.LooksSecret(key, value), RevealSecrets));
        }

        Problems = string.Join(Environment.NewLine, problems);
        SelectedEntry = Entries.FirstOrDefault(e => e.Key == keep);
        if (SelectedEntry is null)
        {
            OnSelectedEntryChanged(null);
        }
    }

    private static string SuggestVaultName(string key)
    {
        var name = new string([.. key.Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-' ? char.ToLowerInvariant(c) : '-')]);
        return VaultNames.IsValid(name) ? name : string.Empty;
    }
}
