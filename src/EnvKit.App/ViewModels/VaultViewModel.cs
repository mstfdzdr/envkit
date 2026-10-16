using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnvKit.App.Localization;
using EnvKit.Core.Profiles;
using EnvKit.Core.Vault;
using Wpf.Ui.Controls;

namespace EnvKit.App.ViewModels;

public sealed partial class VaultViewModel : PageViewModel
{
    private readonly AppServices _services;
    private readonly MainViewModel _main;

    public VaultViewModel(AppServices services, MainViewModel main)
        : base("Page_Vault", SymbolRegular.LockClosed24)
    {
        _services = services;
        _main = main;
        Refresh();
    }

    /// <summary>Secret names only; values are read when revealed.</summary>
    public ObservableCollection<string> Names { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNew), nameof(ValueLabel), nameof(Reference), nameof(UsedBy))]
    [NotifyCanExecuteChangedFor(nameof(RevealCommand), nameof(DeleteCommand))]
    public partial string? Selected { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string EditName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string EditValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? RevealedValue { get; set; }

    /// <summary>Deleting cannot be undone, so the first click only arms it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteLabel))]
    public partial bool IsDeleteArmed { get; set; }

    public bool IsNew => Selected is null;

    public string ValueLabel => Loc.Get(IsNew ? "Vault_ValueNew" : "Vault_ValueEdit");

    public string DeleteLabel => Loc.Get(IsDeleteArmed ? "Vault_DeleteConfirm" : "Common_Delete");

    public string Reference => Selected is null ? string.Empty : VaultReference.For(Selected);

    public string UsedBy
    {
        get
        {
            if (Selected is null)
            {
                return string.Empty;
            }

            var profiles = _services.Profiles.List()
                .Where(p => p.Actions.OfType<SetVariableAction>().Any(a => VaultReference.Names(a.Value).Contains(Selected, VaultNames.Comparer)))
                .Select(p => p.Name)
                .ToList();
            return profiles.Count == 0 ? Loc.Get("Vault_NotUsed") : Loc.Format("Vault_UsedBy", string.Join(", ", profiles));
        }
    }

    public override void Refresh()
    {
        var keep = Selected;
        Names.Clear();
        foreach (var name in _services.Vault.Names())
        {
            Names.Add(name);
        }

        Selected = Names.FirstOrDefault(n => VaultNames.Comparer.Equals(n, keep));
        OnSelectedChanged(Selected);
    }

    partial void OnSelectedChanged(string? value)
    {
        EditName = value ?? string.Empty;
        EditValue = string.Empty;
        RevealedValue = null;
        IsDeleteArmed = false;
    }

    [RelayCommand]
    private void New() => Selected = null;

    private bool HasSelection() => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Reveal() => RevealedValue = _services.Vault.Get(Selected!);

    private bool CanSave() => VaultNames.IsValid(EditName.Trim()) && EditValue.Length > 0;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var name = EditName.Trim();
        if (IsNew && _services.Vault.Get(name) is not null)
        {
            _main.ShowStatus(InfoBarSeverity.Error, Loc.Get("Vault_NameTaken"), Loc.Format("Vault_NameTakenMessage", name));
            return;
        }

        _services.Vault.Set(name, EditValue);
        EditValue = string.Empty;
        Refresh();
        Selected = Names.FirstOrDefault(n => VaultNames.Comparer.Equals(n, name));
        _main.ShowStatus(InfoBarSeverity.Success, Loc.Get("Vault_Stored"), Loc.Format("Vault_StoredMessage", VaultReference.For(name)));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        if (!IsDeleteArmed)
        {
            IsDeleteArmed = true;
            return;
        }

        var name = Selected!;
        _services.Vault.Delete(name);
        Selected = null;
        Refresh();
        _main.ShowStatus(InfoBarSeverity.Informational, Loc.Get("Vault_Deleted"), Loc.Format("Vault_DeletedMessage", name));
    }
}
