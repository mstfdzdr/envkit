using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        : base("History", SymbolRegular.History24)
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
    public partial string PreviewNote { get; set; } = "Select a snapshot to see what restoring it would change.";

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

    partial void OnSelectedChanged(SnapshotInfo? value)
    {
        _restorePlan = null;
        Preview = [];
        CanRestore = false;
        if (value is null)
        {
            PreviewNote = Snapshots.Count == 0
                ? "No snapshots yet. One is taken automatically before every change."
                : "Select a snapshot to see what restoring it would change.";
            return;
        }

        try
        {
            var snapshot = _services.Snapshots.Load(value.Id);
            _restorePlan = _services.NewPlan().Restore(snapshot).Build($"Restore snapshot from {value.CreatedAt.ToLocalTime():g}");
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException or KeyNotFoundException)
        {
            PreviewNote = $"This snapshot cannot be read: {ex.Message}";
            return;
        }

        Preview = PlanSummary.Summarize(_restorePlan);
        CanRestore = !_restorePlan.IsEmpty;
        PreviewNote = _restorePlan.IsEmpty ? "The current state matches this snapshot." : "Restoring would make these changes:";
    }

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private void Restore() => _main.RequestReview(_restorePlan!);
}
