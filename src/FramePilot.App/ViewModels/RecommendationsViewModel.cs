using System.Windows.Input;
using FramePilot.App.Localization;
using FramePilot.App.Services;
using FramePilot.App.Views;
using FramePilot.Core.Changes;
using FramePilot.Core.Models;
using FramePilot.Licensing;

namespace FramePilot.App.ViewModels;

public sealed record EvidenceRow(string Label, string Value, string Source);

public sealed class RecItem : ObservableObject
{
    private bool _isSelected;
    private bool _isExpanded;

    public RecItem(Recommendation rec, string lang)
    {
        Rec = rec;
        Title = rec.Title.Get(lang);
        Symptom = rec.Symptom.Get(lang);
        Causes = rec.PossibleCauses.Get(lang);
        Action = rec.Action.Get(lang);
        Limitations = rec.Limitations.Get(lang);
        Verification = rec.Verification?.Get(lang);
        Evidence = rec.Evidence.Select(e => new EvidenceRow(e.Label.Get(lang), e.Value, e.Source)).ToList();
        ConfidenceText = Loc.T("Confidence_" + rec.Confidence);
        Flags = string.Join(" · ", new[]
        {
            rec.RequiresRestart ? Loc.T("Rec_NeedsGameRestart") : null,
            rec.RequiresElevation ? Loc.T("Rec_NeedsAdmin") : null,
            $"{rec.RuleId} v{rec.RuleVersion}"
        }.Where(x => x is not null));
        _isSelected = rec.Kind == RecommendationKind.AutoApplicable;
        OpenSourceCommand = new RelayCommand(p => { if (p is SourceRef s) Shell.Open(s.Url); });
        ToggleCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    public Recommendation Rec { get; }
    public string Title { get; }
    public string Symptom { get; }
    public string Causes { get; }
    public string Action { get; }
    public string Limitations { get; }
    public string? Verification { get; }
    public bool HasVerification => Verification is not null;
    public IReadOnlyList<EvidenceRow> Evidence { get; }
    public string ConfidenceText { get; }
    public string Flags { get; }
    public IReadOnlyList<SourceRef> Sources => Rec.Sources;
    public bool IsAuto => Rec.Kind == RecommendationKind.AutoApplicable;
    public ICommand OpenSourceCommand { get; }
    public ICommand ToggleCommand { get; }

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
}

public sealed record PlanRow(string Title, string Target, string Current, string Desired, string Note, bool WillChange);

public sealed class RecommendationsViewModel : ObservableObject, IPage
{
    private readonly MainViewModel _main;
    private IReadOnlyList<PlanRow> _plan = Array.Empty<PlanRow>();
    private IReadOnlyList<PlanRow> _results = Array.Empty<PlanRow>();
    private bool _planVisible;
    private string? _resultSummary;
    private bool _createRestorePoint;

    private Workspace W => _main.Workspace;

    public RecommendationsViewModel(MainViewModel main)
    {
        _main = main;
        _createRestorePoint = main.Workspace.App.Settings.RestorePointBeforeApply;
        BuildPlanCommand = new RelayCommand(BuildPlan, () => AutoItems.Any(i => i.IsSelected));
        ApplyCommand = new AsyncCommand(ApplyAsync, () => _planVisible && _plan.Any(p => p.WillChange));
        ClosePlanCommand = new RelayCommand(() => PlanVisible = false);
        MeasureAfterCommand = new RelayCommand(() =>
        {
            _main.Sessions.PresetGroup(RunGroup.After);
            _main.Navigate(_main.Sessions);
        });
        W.Changed += (_, _) => Rebuild();
        Rebuild();
    }

    public IReadOnlyList<RecItem> AutoItems { get; private set; } = Array.Empty<RecItem>();
    public IReadOnlyList<RecItem> UserItems { get; private set; } = Array.Empty<RecItem>();
    public IReadOnlyList<RecItem> DiagItems { get; private set; } = Array.Empty<RecItem>();

    public bool HasAny => AutoItems.Count + UserItems.Count + DiagItems.Count > 0;
    public bool HasSnapshot => W.Snapshot is not null;
    public bool HasSession => W.LatestSessionForTarget is not null;
    public bool IsPro => W.App.License.IsPro;
    public bool IsDemo => W.IsDemo;

    public ICommand BuildPlanCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand ClosePlanCommand { get; }
    public ICommand MeasureAfterCommand { get; }

    public IReadOnlyList<PlanRow> Plan { get => _plan; private set => Set(ref _plan, value); }
    public IReadOnlyList<PlanRow> Results { get => _results; private set { Set(ref _results, value); Raise(nameof(HasResults)); } }
    public bool HasResults => _results.Count > 0;
    public string? ResultSummary { get => _resultSummary; private set => Set(ref _resultSummary, value); }

    public bool PlanVisible
    {
        get => _planVisible;
        private set => Set(ref _planVisible, value);
    }

    public bool CreateRestorePoint
    {
        get => _createRestorePoint;
        set
        {
            if (Set(ref _createRestorePoint, value)) W.App.Settings.RestorePointBeforeApply = value;
        }
    }

    public void OnNavigatedTo() => Rebuild();

    private void Rebuild()
    {
        var lang = Loc.Instance.Language;
        var items = W.Recommendations.Select(r => new RecItem(r, lang)).ToList();
        AutoItems = items.Where(i => i.Rec.Kind == RecommendationKind.AutoApplicable).ToList();
        UserItems = items.Where(i => i.Rec.Kind == RecommendationKind.UserAction).ToList();
        DiagItems = items.Where(i => i.Rec.Kind == RecommendationKind.DiagnosticOnly).ToList();
        foreach (var n in new[]
                 {
                     nameof(AutoItems), nameof(UserItems), nameof(DiagItems), nameof(HasAny), nameof(HasSnapshot), nameof(HasSession),
                     nameof(IsPro), nameof(IsDemo)
                 })
            Raise(n);
    }

    private List<ActionRequest> SelectedRequests()
    {
        var list = new List<ActionRequest>();
        foreach (var item in AutoItems.Where(i => i.IsSelected))
        {
            var id = item.Rec.ActionId;
            if (id is null || !W.App.Actions.TryGetValue(id, out var action)) continue;
            var parameters = item.Rec.ActionParameters ?? new Dictionary<string, string>();
            list.Add(new ActionRequest(action, parameters, item.Rec.RuleId));
        }
        return list;
    }

    private void BuildPlan()
    {
        Results = Array.Empty<PlanRow>();
        ResultSummary = null;
        if (W.IsDemo)
        {
            DialogWindow.Show(Loc.T("Plan_Title"), Loc.T("Plan_DemoBlocked"), DialogKind.Info);
            return;
        }
        try
        {
            var lang = Loc.Instance.Language;
            var preview = W.App.Changes.Preview(SelectedRequests());
            Plan = preview.Select(p => new PlanRow(
                p.Description.Get(lang), p.TargetDescription, p.Current.Display, p.Desired.Display,
                p.AlreadyManaged ? Loc.T("Plan_AlreadyManaged") : p.AlreadySatisfied ? Loc.T("Plan_AlreadySet") : Loc.T("Plan_WillChange"),
                !p.AlreadyManaged && !p.AlreadySatisfied)).ToList();
            PlanVisible = true;
        }
        catch (Exception ex)
        {
            DialogWindow.Show(Loc.T("Plan_Title"), ex.Message, DialogKind.Error);
        }
    }

    private async Task ApplyAsync()
    {
        if (!Entitlements.CanAutoApply(W.App.License))
        {
            DialogWindow.Show(Loc.T("Plan_ProTitle"), Loc.T("Plan_ProBody"), DialogKind.Info);
            return;
        }
        if (!DialogWindow.Confirm(Loc.T("Plan_ConfirmTitle"), Loc.T("Plan_ConfirmBody"), Loc.T("Plan_Apply"), Loc.T("Cancel"))) return;

        var notes = new List<string>();
        if (CreateRestorePoint)
        {
            var rp = await RestorePointService.CreateAsync();
            notes.Add(Loc.T("RP_" + rp));
            W.App.Store.Audit("restore-point", rp.ToString());
            if (rp is RestorePointOutcome.Declined or RestorePointOutcome.Failed or RestorePointOutcome.HelperMissing &&
                !DialogWindow.Confirm(Loc.T("Plan_Title"), Loc.T("RP_" + rp) + Environment.NewLine + Loc.T("RP_Continue"), Loc.T("Plan_Apply"), Loc.T("Cancel")))
                return;
        }

        var result = await Task.Run(() => W.App.Changes.Apply(SelectedRequests()));
        W.App.Store.Audit("apply", $"Batch {result.BatchId}: " + string.Join(", ", result.Items.Select(i => $"{i.TargetKey}={i.Outcome}")));
        Results = result.Items.Select(i => new PlanRow(Loc.T("Outcome_" + i.Outcome), i.TargetDescription, "", "", i.Message ?? "",
            i.Outcome == ItemOutcome.Verified)).ToList();
        notes.Insert(0, result.AllSucceeded ? Loc.T("Apply_Success") : Loc.T("Apply_Partial"));
        ResultSummary = string.Join(Environment.NewLine, notes);
        PlanVisible = false;

        // Applied settings change the snapshot: re-scan so rules see the new state.
        await W.RunInventoryAsync();
    }
}
