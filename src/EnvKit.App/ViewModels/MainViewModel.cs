using CommunityToolkit.Mvvm.ComponentModel;
using EnvKit.Core;
using EnvKit.Core.Plans;
using Wpf.Ui.Controls;

namespace EnvKit.App.ViewModels;

public abstract partial class PageViewModel(string title, SymbolRegular icon) : ObservableObject
{
    public string Title { get; } = title;

    public SymbolRegular Icon { get; } = icon;

    /// <summary>Reloads from the store, discarding unsaved edits.</summary>
    public abstract void Refresh();
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;

    public MainViewModel(AppServices services)
    {
        _services = services;
        Variables = new VariablesViewModel(services, this);
        Path = new PathViewModel(services, this);
        History = new HistoryViewModel(services, this);
        Pages = [Path, Variables, History];
        SelectedPage = Path;
    }

    public VariablesViewModel Variables { get; }

    public PathViewModel Path { get; }

    public HistoryViewModel History { get; }

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

    public void RequestReview(ChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.IsEmpty)
        {
            ShowStatus(InfoBarSeverity.Informational, "Nothing to change", "The edits match what is already stored.");
            return;
        }

        Review = new ReviewViewModel(plan, this);
    }

    public void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusSeverity = severity;
        StatusTitle = title;
        StatusMessage = message;
        IsStatusOpen = true;
    }

    internal async Task ApplyAsync(ChangePlan plan)
    {
        try
        {
            // The broadcast can wait on unresponsive windows; keep the UI thread free.
            var result = await Task.Run(() => _services.Applier().Apply(plan));
            ShowStatus(
                InfoBarSeverity.Success,
                "Changes applied",
                $"Snapshot {result.Snapshot!.Id} saved; restore it from History. New terminals see the change, open ones keep their old environment.");
        }
        catch (ElevationRequiredException)
        {
            ShowStatus(InfoBarSeverity.Error, "Administrator rights needed", "Machine-scope changes need administrator rights. Nothing was changed.");
        }
        catch (PlanConflictException ex)
        {
            ShowStatus(InfoBarSeverity.Warning, "Changed elsewhere", $"{ex.Message} The view was reloaded; make the edit again.");
        }
        catch (PlanApplyException ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Partially applied", ex.Message);
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
}
