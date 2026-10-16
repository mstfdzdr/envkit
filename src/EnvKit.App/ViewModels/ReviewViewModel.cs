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

    [RelayCommand]
    private Task ApplyAsync() => main.ApplyAsync(Plan);

    [RelayCommand]
    private void Cancel() => main.Review = null;
}
