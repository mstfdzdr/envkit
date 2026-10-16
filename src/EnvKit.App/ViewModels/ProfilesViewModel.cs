using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnvKit.Core.Profiles;
using EnvKit.Core.Vault;
using Wpf.Ui.Controls;

namespace EnvKit.App.ViewModels;

public enum ActionKind
{
    Set,
    Unset,
    PrependPath,
    AppendPath,
    RemovePath,
}

/// <summary>One editable row; path actions use only <see cref="Value"/> (the directory).</summary>
public sealed partial class ActionRow : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesName))]
    public partial ActionKind Kind { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;

    public bool UsesName => Kind is ActionKind.Set or ActionKind.Unset;

    public static ActionRow From(ProfileAction action) => action switch
    {
        SetVariableAction a => new ActionRow { Kind = ActionKind.Set, Name = a.Name, Value = a.Value },
        UnsetVariableAction a => new ActionRow { Kind = ActionKind.Unset, Name = a.Name },
        PrependPathAction a => new ActionRow { Kind = ActionKind.PrependPath, Value = a.Directory },
        AppendPathAction a => new ActionRow { Kind = ActionKind.AppendPath, Value = a.Directory },
        RemovePathAction a => new ActionRow { Kind = ActionKind.RemovePath, Value = a.Directory },
        _ => throw new NotSupportedException(action.GetType().Name),
    };

    public ProfileAction ToAction() => Kind switch
    {
        ActionKind.Set => new SetVariableAction(Name.Trim(), Value),
        ActionKind.Unset => new UnsetVariableAction(Name.Trim()),
        ActionKind.PrependPath => new PrependPathAction(Value.Trim()),
        ActionKind.AppendPath => new AppendPathAction(Value.Trim()),
        _ => new RemovePathAction(Value.Trim()),
    };
}

public sealed partial class ProfilesViewModel : PageViewModel
{
    private readonly AppServices _services;
    private readonly MainViewModel _main;

    public ProfilesViewModel(AppServices services, MainViewModel main)
        : base("Profiles", SymbolRegular.Layer24)
    {
        _services = services;
        _main = main;
        Refresh();
    }

    public ObservableCollection<Profile> Profiles { get; } = [];

    public ObservableCollection<ActionRow> Actions { get; } = [];

    public IReadOnlyList<ActionKind> ActionKinds { get; } = Enum.GetValues<ActionKind>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNew), nameof(RunCommandText))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(DeleteCommand))]
    public partial Profile? Selected { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string EditName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ActionRow? SelectedAction { get; set; }

    /// <summary>Vault secrets the profile references that are not in the vault yet.</summary>
    [ObservableProperty]
    public partial string MissingSecrets { get; set; } = string.Empty;

    public bool IsNew => Selected is null;

    public string RunCommandText => Selected is null ? string.Empty : $"envkit run -p {Selected.Name} -- ";

    public override void Refresh()
    {
        var keep = Selected?.Name;
        Profiles.Clear();
        foreach (var profile in _services.Profiles.List())
        {
            Profiles.Add(profile);
        }

        Selected = Profiles.FirstOrDefault(p => string.Equals(p.Name, keep, StringComparison.OrdinalIgnoreCase));
        OnSelectedChanged(Selected);
    }

    partial void OnSelectedChanged(Profile? value)
    {
        Actions.Clear();
        EditName = value?.Name ?? string.Empty;
        EditDescription = value?.Description ?? string.Empty;
        foreach (var action in value?.Actions ?? [])
        {
            Actions.Add(ActionRow.From(action));
        }

        UpdateMissingSecrets(value);
    }

    [RelayCommand]
    private void New() => Selected = null;

    [RelayCommand]
    private void AddAction()
    {
        var row = new ActionRow { Kind = Actions.Count == 0 ? ActionKind.Set : ActionKind.PrependPath };
        Actions.Add(row);
        SelectedAction = row;
    }

    [RelayCommand]
    private void RemoveAction()
    {
        if (SelectedAction is not null)
        {
            Actions.Remove(SelectedAction);
        }
    }

    [RelayCommand]
    private void MoveActionUp() => MoveAction(-1);

    [RelayCommand]
    private void MoveActionDown() => MoveAction(+1);

    private bool CanSave() => EditName.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        Profile profile;
        try
        {
            profile = new Profile(
                EditName.Trim(),
                [.. Actions.Select(a => a.ToAction())],
                EditDescription.Trim().Length == 0 ? null : EditDescription.Trim());
            profile.Validate();
            if (profile.Actions.Count == 0)
            {
                throw new ArgumentException("A profile needs at least one action.");
            }
        }
        catch (ArgumentException ex)
        {
            _main.ShowStatus(InfoBarSeverity.Error, "Invalid profile", ex.Message);
            return;
        }

        var renamedFrom = Selected is { } current && !string.Equals(current.Name, profile.Name, StringComparison.OrdinalIgnoreCase) ? current.Name : null;
        if (IsNew || renamedFrom is not null)
        {
            if (_services.Profiles.Get(profile.Name) is not null)
            {
                _main.ShowStatus(InfoBarSeverity.Error, "Name taken", $"A profile named '{profile.Name}' already exists.");
                return;
            }
        }

        _services.Profiles.Save(profile);
        if (renamedFrom is not null)
        {
            _services.Profiles.Delete(renamedFrom);
        }

        Refresh();
        Selected = Profiles.FirstOrDefault(p => p.Name == profile.Name);
        _main.ShowStatus(InfoBarSeverity.Success, "Profile saved", $"Saved '{profile.Name}'. Nothing changes until you apply or run it.");
    }

    private bool HasSelection() => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Apply()
    {
        try
        {
            _main.RequestReview(_services.NewPlan().Apply(Selected!).Build($"Apply profile {Selected!.Name}"));
        }
        catch (InvalidOperationException ex)
        {
            _main.ShowStatus(InfoBarSeverity.Warning, "Run only", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        var name = Selected!.Name;
        _services.Profiles.Delete(name);
        Selected = null;
        Refresh();
        _main.ShowStatus(InfoBarSeverity.Informational, "Profile deleted", $"Deleted '{name}'. Changes it already applied stay; undo them from History.");
    }

    private void MoveAction(int offset)
    {
        if (SelectedAction is null)
        {
            return;
        }

        var from = Actions.IndexOf(SelectedAction);
        var to = from + offset;
        if (to >= 0 && to < Actions.Count)
        {
            Actions.Move(from, to);
        }
    }

    private void UpdateMissingSecrets(Profile? profile)
    {
        var missing = profile?.Actions.OfType<SetVariableAction>()
            .SelectMany(a => VaultReference.Names(a.Value))
            .Where(n => _services.Vault.Get(n) is null)
            .Distinct(VaultNames.Comparer)
            .ToList() ?? [];
        MissingSecrets = missing.Count == 0 ? string.Empty : $"Not in the vault yet: {string.Join(", ", missing)}";
    }
}
