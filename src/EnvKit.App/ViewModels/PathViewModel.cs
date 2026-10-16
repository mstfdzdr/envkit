using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnvKit.Core.Paths;
using EnvKit.Core.Variables;
using Wpf.Ui.Controls;

namespace EnvKit.App.ViewModels;

public sealed record PathEntryItem(int Index, string Raw, string Expanded, string Problems)
{
    public string Display => Raw.Trim().Length == 0 ? "(empty)" : Raw;

    public bool HasProblems => Problems.Length > 0;

    public bool ShowExpanded => Expanded != Raw && Expanded.Trim().Length > 0;
}

/// <summary>
/// Edits a working copy of both scopes' PATH. Problems are re-analyzed after every edit,
/// against the working copy, so they reflect what saving would produce.
/// </summary>
public sealed partial class PathViewModel : PageViewModel
{
    private readonly AppServices _services;
    private readonly MainViewModel _main;
    private readonly Dictionary<EnvScope, List<string>> _original = [];
    private readonly Dictionary<EnvScope, List<string>> _working = [];
    private PathReport _report = new([], []);

    public PathViewModel(AppServices services, MainViewModel main)
        : base("PATH", SymbolRegular.TextBulletListLtr24)
    {
        _services = services;
        _main = main;
        Refresh();
    }

    public IReadOnlyList<EnvScope> Scopes { get; } = [EnvScope.User, EnvScope.Machine];

    public ObservableCollection<PathEntryItem> Entries { get; } = [];

    public ObservableCollection<string> WhichResults { get; } = [];

    [ObservableProperty]
    public partial EnvScope Scope { get; set; } = EnvScope.User;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand), nameof(ReplaceCommand), nameof(MoveUpCommand), nameof(MoveDownCommand))]
    public partial PathEntryItem? SelectedEntry { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand), nameof(ReplaceCommand))]
    public partial string NewEntry { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(RevertCommand))]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial string WhichQuery { get; set; } = string.Empty;

    private List<string> Current => _working[Scope];

    public override void Refresh()
    {
        foreach (var scope in Scopes)
        {
            var value = _services.Store.Get(PathAnalyzer.VariableName, scope)?.Value;
            _original[scope] = value is null ? [] : [.. PathList.Split(value)];
            _working[scope] = [.. _original[scope]];
        }

        Reanalyze();
    }

    /// <summary>Drag and drop.</summary>
    public void Move(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= Current.Count || to >= Current.Count)
        {
            return;
        }

        var entry = Current[from];
        Current.RemoveAt(from);
        Current.Insert(to, entry);
        Reanalyze(selectIndex: to);
    }

    partial void OnScopeChanged(EnvScope value) => Reanalyze();

    partial void OnSelectedEntryChanged(PathEntryItem? value)
    {
        if (value is not null)
        {
            NewEntry = value.Raw;
        }
    }

    private bool HasNewEntry() => NewEntry.Trim().Length > 0;

    private bool HasSelection() => SelectedEntry is not null;

    [RelayCommand(CanExecute = nameof(HasNewEntry))]
    private void Add()
    {
        Current.Add(NewEntry.Trim());
        NewEntry = string.Empty;
        Reanalyze(selectIndex: Current.Count - 1);
    }

    [RelayCommand(CanExecute = nameof(CanReplace))]
    private void Replace()
    {
        var index = SelectedEntry!.Index;
        Current[index] = NewEntry.Trim();
        Reanalyze(selectIndex: index);
    }

    private bool CanReplace() => HasSelection() && HasNewEntry();

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Remove()
    {
        var index = SelectedEntry!.Index;
        Current.RemoveAt(index);
        Reanalyze(selectIndex: Math.Min(index, Current.Count - 1));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveUp() => Move(SelectedEntry!.Index, SelectedEntry.Index - 1);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveDown() => Move(SelectedEntry!.Index, SelectedEntry.Index + 1);

    [RelayCommand]
    private void FixProblems() => Clean(PathFixes.RemoveEmpty | PathFixes.RemoveDuplicates);

    [RelayCommand]
    private void RemoveMissing() => Clean(PathFixes.RemoveMissing);

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Revert() => Refresh();

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Save()
    {
        var builder = _services.NewPlan();
        foreach (var scope in Scopes)
        {
            if (!_working[scope].SequenceEqual(_original[scope], StringComparer.Ordinal))
            {
                builder.SetPath(scope, _working[scope]);
            }
        }

        _main.RequestReview(builder.Build("Update PATH"));
    }

    [RelayCommand]
    private void Which()
    {
        WhichResults.Clear();
        var command = WhichQuery.Trim();
        if (command.Length == 0)
        {
            return;
        }

        try
        {
            var pathExt = _services.Lookup().Resolve("PATHEXT", EnvScope.User);
            var matches = new CommandResolver(_services.FileSystem).FindAll(command, _report, pathExt);
            for (var i = 0; i < matches.Count; i++)
            {
                var notes = $"{matches[i].Entry.Scope}#{matches[i].Entry.Index}"
                    + (matches[i].IsAppExecutionAlias ? ", Store alias" : string.Empty)
                    + (i > 0 ? ", shadowed" : string.Empty);
                WhichResults.Add($"{(i == 0 ? "→" : "  ")} {matches[i].FullPath}   ({notes})");
            }

            if (matches.Count == 0)
            {
                WhichResults.Add($"{command}: not found in PATH");
            }
        }
        catch (ArgumentException ex)
        {
            WhichResults.Add(ex.Message);
        }
    }

    private void Clean(PathFixes fixes)
    {
        if (PathCleanup.Apply(_report, fixes).TryGetValue(Scope, out var cleaned))
        {
            _working[Scope] = [.. PathList.Split(cleaned)];
            Reanalyze();
        }
    }

    private void Reanalyze(int? selectIndex = null)
    {
        var keep = selectIndex ?? SelectedEntry?.Index;
        _report = new PathAnalyzer(_services.FileSystem).Analyze(Working(EnvScope.Machine), Working(EnvScope.User), _services.Lookup());

        Entries.Clear();
        foreach (var entry in _report.EntriesIn(Scope))
        {
            var problems = string.Join(" · ", _report.IssuesFor(entry).Select(PathIssueText.Describe));
            Entries.Add(new PathEntryItem(entry.Index, entry.Raw, entry.Expanded, problems));
        }

        SelectedEntry = keep is { } index && index >= 0 && index < Entries.Count ? Entries[index] : null;

        var problemCount = _report.Issues.Count(i => i.Scope == Scope);
        Summary = $"{Entries.Count} entries · {(problemCount == 0 ? "no problems" : $"{problemCount} problem(s)")}";
        IsDirty = Scopes.Any(s => !_working[s].SequenceEqual(_original[s], StringComparer.Ordinal));
    }

    private EnvVariable? Working(EnvScope scope)
    {
        var stored = _services.Store.Get(PathAnalyzer.VariableName, scope);
        if (stored is null && _working[scope].Count == 0)
        {
            return null;
        }

        var value = PathList.Join(_working[scope]);

        // Mirrors PlanBuilder: references make the value expandable when it is saved.
        var kind = EnvExpander.HasReferences(value) ? EnvValueKind.Expandable : stored?.Kind ?? EnvValueKind.Literal;
        return new EnvVariable(stored?.Name ?? PathAnalyzer.VariableName, value, scope, kind);
    }
}
