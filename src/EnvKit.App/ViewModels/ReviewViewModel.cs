using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnvKit.Core.Plans;

namespace EnvKit.App.ViewModels;

public sealed partial class ReviewViewModel(ChangePlan plan, MainViewModel main) : ObservableObject
{
    public ChangePlan Plan { get; } = plan;

    public string Description => Plan.Description;

    public IReadOnlyList<ChangeSummary> Changes { get; } = PlanSummary.Summarize(plan);

    public bool RequiresElevation => Plan.RequiresElevation;

    /// <summary>Some deletions (domain credentials) cannot be restored from the snapshot.</summary>
    public bool HasIrreversibleChanges => Plan.Changes.Any(c => c is CredentialChange { After: null, IsIrreversible: true });

    [RelayCommand]
    private Task ApplyAsync() => main.ApplyAsync(Plan);

    [RelayCommand]
    private void Cancel() => main.Review = null;
}
