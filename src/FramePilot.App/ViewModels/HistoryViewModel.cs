using System.Globalization;
using System.Windows.Input;
using FramePilot.App.Localization;
using FramePilot.App.Views;
using FramePilot.Core.Changes;

namespace FramePilot.App.ViewModels;

public sealed class ChangeItem
{
    public required ChangeRecord Record { get; init; }
    public string Target => Record.TargetDescription;
    public string Values => $"{Record.Original.Display} → {Record.Planned.Display}";
    public string StateText => Loc.T("State_" + Record.State);
    public string When => Record.UpdatedAt.ToString("g", CultureInfo.CurrentCulture);
    public string Action => $"{Record.ActionId} v{Record.ActionVersion}" + (Record.RuleId is null ? "" : $" · {Record.RuleId}");
    public string? Error => Record.Error ?? Record.Note;
    public bool CanRollback => Record.IsActive;
    public bool IsProblem => Record.State is ChangeState.Failed or ChangeState.RollbackFailed or ChangeState.RollbackPending;
    public bool IsActive => Record.State is ChangeState.Applied or ChangeState.Verified;
    public required ICommand RollbackCommand { get; init; }
}

public static class RollbackUi
{
    /// <summary>Runs a rollback, asking before overwriting a value the user changed after FramePilot applied it.</summary>
    public static void RollbackWithConflictCheck(ChangeEngine engine, Guid id)
    {
        var r = engine.Rollback(id);
        if (r.Outcome == RollbackOutcome.Conflict)
        {
            if (DialogWindow.Confirm(Loc.T("Rollback_ConflictTitle"),
                    Loc.F("Rollback_ConflictBody", r.CurrentValue?.Display ?? "?") + Environment.NewLine + Environment.NewLine + r.Message,
                    Loc.T("Rollback_Overwrite"), Loc.T("Rollback_Keep")))
                r = engine.Rollback(id, force: true);
            else
                return;
        }
        var kind = r.Outcome is RollbackOutcome.RolledBack or RollbackOutcome.AlreadyRolledBack ? DialogKind.Info : DialogKind.Warning;
        DialogWindow.Show(Loc.T("Rollback_Title"), Loc.T("RollbackOutcome_" + r.Outcome) + (r.Message is null ? "" : Environment.NewLine + r.Message), kind);
    }
}

public sealed class HistoryViewModel : ObservableObject, IPage
{
    private readonly MainViewModel _main;

    public HistoryViewModel(MainViewModel main)
    {
        _main = main;
        RefreshCommand = new RelayCommand(Refresh);
        _main.Workspace.Changed += (_, _) => Refresh();
    }

    public ICommand RefreshCommand { get; }
    public IReadOnlyList<ChangeItem> Items { get; private set; } = Array.Empty<ChangeItem>();
    public bool IsEmpty => Items.Count == 0;

    public void OnNavigatedTo() => Refresh();

    public void Refresh()
    {
        var engine = _main.Workspace.App.Changes;
        Items = _main.Workspace.App.Store.GetAll()
            .OrderByDescending(r => r.UpdatedAt)
            .Select(r => new ChangeItem
            {
                Record = r,
                RollbackCommand = new RelayCommand(() =>
                {
                    RollbackUi.RollbackWithConflictCheck(engine, r.Id);
                    _main.Workspace.NotifyChanged();
                    _ = _main.Workspace.RunInventoryAsync();
                }, () => r.IsActive)
            })
            .ToList();
        Raise(nameof(Items));
        Raise(nameof(IsEmpty));
    }
}
