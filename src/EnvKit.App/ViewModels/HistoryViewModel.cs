using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnvKit.App.Localization;
using EnvKit.Core.Plans;
using EnvKit.Core.Snapshots;
using Wpf.Ui.Controls;

namespace EnvKit.App.ViewModels;

public sealed partial class HistoryViewModel : PageViewModel
{
    private readonly AppServices _services;
    private readonly MainViewModel _main;
    private ChangePlan? _restorePlan;

    public HistoryViewModel(AppServices services, MainViewModel main)
        : base("Page_History", SymbolRegular.History24)
    {
        _services = services;
        _main = main;
        Refresh();
    }

    public ObservableCollection<SnapshotInfo> Snapshots { get; } = [];

    [ObservableProperty]
    public partial SnapshotInfo? Selected { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ChangeSummary> Preview { get; set; } = [];

    [ObservableProperty]
    public partial string PreviewNote { get; set; } = Loc.Get("History_Select");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    public partial bool CanRestore { get; set; }

    public override void Refresh()
    {
        var keep = Selected?.Id;
        Snapshots.Clear();
        foreach (var info in _services.Snapshots.List())
        {
            Snapshots.Add(info);
        }

        Selected = Snapshots.FirstOrDefault(s => s.Id == keep);
        OnSelectedChanged(Selected);
    }

    public override void OnLanguageChanged()
    {
        // Reloaded rather than re-notified: the list items are records whose texts are read once.
        Refresh();
        base.OnLanguageChanged();
    }

    partial void OnSelectedChanged(SnapshotInfo? value)
    {
        _restorePlan = null;
        Preview = [];
        CanRestore = false;
        if (value is null)
        {
            PreviewNote = Snapshots.Count == 0
                ? Loc.Get("History_None")
                : Loc.Get("History_Select");
            return;
        }

        try
        {
            var snapshot = _services.Snapshots.Load(value.Id);
            _restorePlan = _services.NewPlan().Restore(snapshot).Build(Loc.Format("History_RestoreDescription", value.CreatedAt.ToLocalTime().ToString("g", Loc.Instance.Culture)));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException or KeyNotFoundException)
        {
            PreviewNote = Loc.Format("History_Unreadable", ex.Message);
            return;
        }

        Preview = PlanSummary.Summarize(_restorePlan);
        CanRestore = !_restorePlan.IsEmpty;
        PreviewNote = Loc.Get(_restorePlan.IsEmpty ? "History_Matches" : "History_WouldChange");
    }

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private void Restore() => _main.RequestReview(_restorePlan!);
}
