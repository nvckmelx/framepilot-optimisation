using System.Windows.Input;
using FramePilot.App.Localization;
using FramePilot.App.Views;
using FramePilot.Core.Changes;

namespace FramePilot.App.ViewModels;

public sealed record DriftRow(string Target, string Applied, string Current);

public sealed class RecoveryViewModel : ObservableObject, IPage
{
    private readonly MainViewModel _main;
    private string? _lastResult;

    public RecoveryViewModel(MainViewModel main)
    {
        _main = main;
        RollbackAllCommand = new RelayCommand(RollbackAll, () => ActiveCount > 0);
        CheckDriftCommand = new RelayCommand(Refresh);
        _main.Workspace.Changed += (_, _) => Refresh();
    }

    private ChangeEngine Engine => _main.Workspace.App.Changes;

    public ICommand RollbackAllCommand { get; }
    public ICommand CheckDriftCommand { get; }

    public int ActiveCount { get; private set; }
    public IReadOnlyList<DriftRow> Drift { get; private set; } = Array.Empty<DriftRow>();
    public IReadOnlyList<ChangeItem> Problems { get; private set; } = Array.Empty<ChangeItem>();
    public bool HasDrift => Drift.Count > 0;
    public bool HasProblems => Problems.Count > 0;

    public string StartupReport
    {
        get
        {
            var r = _main.Workspace.App.StartupRecovery;
            if (r is null || r.Examined == 0) return Loc.T("Recovery_StartupClean");
            var text = Loc.F("Recovery_StartupSummary", r.Examined, r.MarkedApplied, r.MarkedFailed, r.CompletedRollbacks);
            return r.Notes.Count == 0 ? text : text + Environment.NewLine + string.Join(Environment.NewLine, r.Notes);
        }
    }

    public string? LastResult { get => _lastResult; private set => Set(ref _lastResult, value); }

    public void OnNavigatedTo() => Refresh();

    private void Refresh()
    {
        var all = _main.Workspace.App.Store.GetAll();
        ActiveCount = all.Count(r => r.IsActive);
        Drift = Engine.DetectDrift().Select(d => new DriftRow(d.Record.TargetDescription, d.Record.Planned.Display, d.Current.Display)).ToList();
        Problems = all.Where(r => r.State is ChangeState.RollbackFailed or ChangeState.RollbackPending)
            .Select(r => new ChangeItem
            {
                Record = r,
                RollbackCommand = new RelayCommand(() =>
                {
                    RollbackUi.RollbackWithConflictCheck(Engine, r.Id);
                    _main.Workspace.NotifyChanged();
                })
            }).ToList();
        foreach (var n in new[] { nameof(ActiveCount), nameof(Drift), nameof(Problems), nameof(HasDrift), nameof(HasProblems), nameof(StartupReport) })
            Raise(n);
    }

    private void RollbackAll()
    {
        if (!DialogWindow.Confirm(Loc.T("Recovery_AllTitle"), Loc.F("Recovery_AllBody", ActiveCount), Loc.T("Recovery_AllButton"), Loc.T("Cancel")))
            return;
        var results = Engine.RollbackAll(force: false);
        var conflicts = results.Where(r => r.Outcome == RollbackOutcome.Conflict).ToList();
        foreach (var c in conflicts) RollbackUi.RollbackWithConflictCheck(Engine, c.RecordId);
        var ok = results.Count(r => r.Outcome is RollbackOutcome.RolledBack or RollbackOutcome.AlreadyRolledBack);
        var failed = results.Count(r => r.Outcome is RollbackOutcome.Failed or RollbackOutcome.TargetNotResolvable);
        LastResult = Loc.F("Recovery_AllResult", ok, conflicts.Count, failed);
        _main.Workspace.App.Store.Audit("rollback-all", LastResult);
        _main.Workspace.NotifyChanged();
        _ = _main.Workspace.RunInventoryAsync();
    }
}
