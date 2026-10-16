using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnvKit.Core.Security;
using EnvKit.Core.Variables;
using Wpf.Ui.Controls;

namespace EnvKit.App.ViewModels;

public sealed record VariableRow(EnvVariable Variable, bool IsSecret, bool Reveal)
{
    public string Name => Variable.Name;

    public string DisplayValue => IsSecret && !Reveal ? SecretDetector.Mask : Variable.Value;

    public EnvValueKind Kind => Variable.Kind;
}

public sealed partial class VariablesViewModel : PageViewModel
{
    private readonly AppServices _services;
    private readonly MainViewModel _main;

    public VariablesViewModel(AppServices services, MainViewModel main)
        : base("Variables", SymbolRegular.BracesVariable24)
    {
        _services = services;
        _main = main;
        Refresh();
    }

    public IReadOnlyList<EnvScope> Scopes { get; } = [EnvScope.User, EnvScope.Machine];

    public ObservableCollection<VariableRow> Variables { get; } = [];

    [ObservableProperty]
    public partial EnvScope Scope { get; set; } = EnvScope.User;

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    /// <summary>Off by default: values that look like credentials are masked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValueHidden))]
    public partial bool RevealSecrets { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(IsValueHidden))]
    public partial VariableRow? Selected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorTitle))]
    public partial bool IsNew { get; set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string EditName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    public partial string EditValue { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    public partial bool EditExpandable { get; set; }

    public string EditorTitle => IsNew ? "New variable" : "Edit variable";

    /// <summary>A masked variable cannot be viewed or edited until secrets are revealed.</summary>
    public bool IsValueHidden => Selected is { IsSecret: true } && !RevealSecrets;

    /// <summary>The expanded value, when it differs from what is stored.</summary>
    public string Preview
    {
        get
        {
            if (!EditExpandable || IsValueHidden)
            {
                return string.Empty;
            }

            var expanded = _services.Lookup().Expand(EditValue, Scope).Value;
            return expanded == EditValue ? string.Empty : $"Expands to: {expanded}";
        }
    }

    public override void Refresh()
    {
        var keep = Selected?.Name;
        Variables.Clear();
        foreach (var variable in _services.Store.GetAll(Scope)
            .Where(Matches)
            .OrderBy(v => v.Name, EnvVariable.NameComparer))
        {
            Variables.Add(new VariableRow(variable, SecretDetector.LooksSecret(variable.Name, variable.Value), RevealSecrets));
        }

        Selected = Variables.FirstOrDefault(v => EnvVariable.NameComparer.Equals(v.Name, keep));
        if (Selected is null)
        {
            New();
        }
    }

    partial void OnScopeChanged(EnvScope value) => Refresh();

    partial void OnSearchChanged(string value) => Refresh();

    partial void OnRevealSecretsChanged(bool value) => Refresh();

    partial void OnSelectedChanged(VariableRow? value)
    {
        if (value is null)
        {
            return;
        }

        IsNew = false;
        EditName = value.Name;
        EditValue = value.Variable.Value;
        EditExpandable = value.Kind == EnvValueKind.Expandable;
    }

    [RelayCommand]
    private void New()
    {
        Selected = null;
        IsNew = true;
        EditName = string.Empty;
        EditValue = string.Empty;
        EditExpandable = false;
    }

    private bool CanSave() => EditName.Trim().Length > 0 && !IsValueHidden;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        try
        {
            var kind = EditExpandable ? EnvValueKind.Expandable : EnvValueKind.Literal;
            var name = EditName.Trim();
            var plan = _services.NewPlan().Set(name, Scope, EditValue, kind).Build($@"Set {Scope}\{name}");
            _main.RequestReview(plan);
        }
        catch (ArgumentException ex)
        {
            _main.ShowStatus(InfoBarSeverity.Error, "Invalid variable", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete() =>
        _main.RequestReview(_services.NewPlan().Delete(Selected!.Name, Scope).Build($@"Delete {Scope}\{Selected.Name}"));

    private bool CanDelete() => Selected is not null;

    // Masked values are not searched, so typing part of a token does not reveal which variable holds it.
    private bool Matches(EnvVariable variable) =>
        Search.Length == 0
        || variable.Name.Contains(Search, StringComparison.OrdinalIgnoreCase)
        || ((RevealSecrets || !SecretDetector.LooksSecret(variable.Name, variable.Value))
            && variable.Value.Contains(Search, StringComparison.OrdinalIgnoreCase));
}
