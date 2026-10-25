using System.Globalization;
using System.Windows.Input;
using FramePilot.App.Localization;
using FramePilot.Core.Changes;
using FramePilot.Core.Models;
using FramePilot.GameAdapters;

namespace FramePilot.App.ViewModels;

public sealed record StatusLine(string Text, bool Ok);

public sealed class OverviewViewModel : ObservableObject, IPage
{
    private readonly MainViewModel _main;
    private Workspace W => _main.Workspace;

    public OverviewViewModel(MainViewModel main)
    {
        _main = main;
        CheckCommand = new AsyncCommand(CheckAsync, () => !W.Busy && !W.Capture.IsCapturing);
        ViewPlanCommand = new RelayCommand(() => _main.Navigate(_main.Recommendations));
        MeasureCommand = new RelayCommand(() => _main.Navigate(_main.Sessions));
        HardwareCommand = new RelayCommand(() => _main.Navigate(_main.Hardware));
        main.Workspace.Changed += (_, _) => RefreshAll();
        main.Workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Workspace.Busy) or nameof(Workspace.BusyText)) { Raise(nameof(IsBusy)); Raise(nameof(BusyText)); }
        };
    }

    public ICommand CheckCommand { get; }
    public ICommand ViewPlanCommand { get; }
    public ICommand MeasureCommand { get; }
    public ICommand HardwareCommand { get; }

    public bool IsBusy => W.Busy;
    public string? BusyText => W.BusyText;

    public bool IsCs2 { get => W.TargetGame == GameId.Cs2; set { if (value) W.TargetGame = GameId.Cs2; } }
    public bool IsValorant { get => W.TargetGame == GameId.Valorant; set { if (value) W.TargetGame = GameId.Valorant; } }
    public bool IsGoalStable { get => W.Goal == PerformanceGoal.StableFps; set { if (value) W.Goal = PerformanceGoal.StableFps; } }
    public bool IsGoalLatency { get => W.Goal == PerformanceGoal.MinimalLatency; set { if (value) W.Goal = PerformanceGoal.MinimalLatency; } }

    public string Headline => W.Snapshot is null
        ? Loc.T("Overview_ReadyHeadline")
        : (W.Recommendations.Count == 0 ? Loc.T("Overview_NothingFound") : Loc.F("Overview_FoundN", W.Recommendations.Count));

    public string GameLine => Loc.T("Overview_Game") + ": " + GameCatalog.DisplayName(W.TargetGame);
    public string GoalLine => Loc.T("Overview_Goal") + ": " + Loc.T(W.Goal == PerformanceGoal.StableFps ? "Goal_Stable" : "Goal_Latency");

    public string LastTestLine
    {
        get
        {
            var s = W.LatestSessionForTarget;
            return Loc.T("Overview_LastTest") + ": " + (s is null
                ? Loc.T("Overview_NotYet")
                : s.StartedAt.ToString("g", CultureInfo.CurrentCulture) + $" · {s.Stats.AverageFps:0} FPS · P99 {s.Stats.P99FrameTimeMs:0.0} ms");
        }
    }

    public bool HasSnapshot => W.Snapshot is not null;
    public int AutoCount => W.Recommendations.Count(r => r.Kind == RecommendationKind.AutoApplicable);
    public int UserCount => W.Recommendations.Count(r => r.Kind == RecommendationKind.UserAction);
    public int DiagCount => W.Recommendations.Count(r => r.Kind == RecommendationKind.DiagnosticOnly);

    public IReadOnlyList<StatusLine> Status
    {
        get
        {
            var list = new List<StatusLine>();
            var snap = W.Snapshot;
            list.Add(snap is null
                ? new StatusLine(Loc.T("Status_NotScanned"), false)
                : new StatusLine(Loc.F("Status_Scanned", snap.CollectedAt.ToString("g", CultureInfo.CurrentCulture)), true));
            if (snap is not null && snap.CollectorErrors.Count > 0)
                list.Add(new StatusLine(Loc.F("Status_CollectorErrors", snap.CollectorErrors.Count), false));
            var game = snap?.Games.FirstOrDefault(g => g.Game == W.TargetGame);
            if (game is not null)
                list.Add(new StatusLine(game.Installed ? Loc.T("Status_GameFound") : Loc.T("Status_GameMissing"), game.Installed));
            list.Add(W.LatestSessionForTarget is null
                ? new StatusLine(Loc.T("Status_NeedSession"), false)
                : new StatusLine(Loc.T("Status_HasSession"), true));
            list.Add(W.Capture.PresentMonPath is null
                ? new StatusLine(Loc.T("Status_NoPresentMon"), false)
                : new StatusLine(Loc.T("Status_PresentMonOk"), true));
            var active = W.App.Store.GetAll().Count(c => c.IsActive);
            list.Add(active == 0
                ? new StatusLine(Loc.T("Status_NoChanges"), true)
                : new StatusLine(Loc.F("Status_ActiveChanges", active), true));
            return list;
        }
    }

    private async Task CheckAsync()
    {
        await W.RunInventoryAsync();
        RefreshAll();
    }

    public void OnNavigatedTo() => RefreshAll();

    private void RefreshAll()
    {
        foreach (var p in new[]
                 {
                     nameof(Headline), nameof(GameLine), nameof(GoalLine), nameof(LastTestLine), nameof(HasSnapshot), nameof(AutoCount),
                     nameof(UserCount), nameof(DiagCount), nameof(Status), nameof(IsCs2), nameof(IsValorant), nameof(IsGoalStable),
                     nameof(IsGoalLatency)
                 })
            Raise(p);
    }
}
