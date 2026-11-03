using System.Globalization;
using System.Windows.Input;
using FramePilot.App.Localization;
using FramePilot.Core.Changes;
using FramePilot.App.Services;
using FramePilot.Core.Models;
using FramePilot.Diagnostics;
using FramePilot.GameAdapters;

namespace FramePilot.App.ViewModels;

public sealed record StatusLine(string Text, bool Ok);

/// <summary>One cause chip under the frame-time chart: "video memory full · 3".</summary>
public sealed record CauseChip(string Text, bool IsTop);

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

    // ---------------- latest session (spec 2026-11-04 §4: Overview) ----------------

    private Guid _framesFor;
    private IReadOnlyList<FrameSample> _frames = Array.Empty<FrameSample>();
    private SessionInsights? _insights;

    public SessionRecord? Latest => W.LatestSessionForTarget;
    public bool HasSession => Latest is not null;
    public bool NoSession => Latest is null;
    public Guid LatestId => Latest?.Id ?? Guid.Empty;

    public IReadOnlyList<FrameSample> LatestFrames { get { LoadLatest(); return _frames; } }

    /// <summary>Stored insights, or computed from frames (and sensors if any) for sessions recorded before 1.1.</summary>
    public SessionInsights? LatestInsights { get { LoadLatest(); return _insights; } }

    private void LoadLatest()
    {
        var s = Latest;
        if (s is null) { _frames = Array.Empty<FrameSample>(); _insights = null; _framesFor = Guid.Empty; return; }
        if (s.Id == _framesFor) return;
        _framesFor = s.Id;
        try
        {
            _frames = CaptureService.LoadFrames(s);
            _insights = s.Insights ?? SpikeAttributor.Analyze(_frames, CaptureService.LoadSensors(s), Array.Empty<double>());
        }
        catch (Exception ex)
        {
            Services.ErrorReporter.Log("overview", ex.Message);
            _frames = Array.Empty<FrameSample>();
            _insights = null;
        }
    }

    public string SessionTitle => Latest is null ? Headline : GameCatalog.DisplayName(Latest.Game);

    public string SessionLine
    {
        get
        {
            var s = Latest;
            if (s is null) return Loc.T("Overview_Sub");
            var c = s.Conditions;
            var res = c.DisplayWidth is int w && c.DisplayHeight is int h ? $", {w}×{h}" : "";
            var hz = c.RefreshHz is int r ? string.Format(CultureInfo.CurrentCulture, Loc.T("Overview_AtHz"), r) : "";
            return string.Format(CultureInfo.CurrentCulture, Loc.T("Overview_SessionLine"), s.StartedAt.LocalDateTime, s.Stats.DurationMs / 1000) + res + hz;
        }
    }

    public double AvgFps => Latest?.Stats.AverageFps ?? double.NaN;
    public double P99Ms => Latest?.Stats.P99FrameTimeMs ?? double.NaN;
    public double OneLowFps => Latest?.Stats.OnePercentLowFps ?? double.NaN;
    public double SpikeCount => LatestInsights?.Spikes.Count ?? Latest?.Stats.SpikeCount ?? double.NaN;

    public double BudgetMs => Latest?.Conditions.RefreshHz is int hz && hz > 0 ? 1000.0 / hz : 0;

    private ComparisonResult? Comparison
    {
        get
        {
            var sessions = W.Sessions.Where(x => x.Game == W.TargetGame && x.IsDemo == W.IsDemo).ToList();
            var b = sessions.Where(x => x.Group == RunGroup.Baseline).ToList();
            var a = sessions.Where(x => x.Group == RunGroup.After).ToList();
            return b.Count == 0 || a.Count == 0 ? null : SessionComparer.Compare(b, a);
        }
    }

    private string Delta(string metric, string fallback)
    {
        var m = Comparison?.Metrics.FirstOrDefault(x => x.Metric == metric);
        if (m is null || m.Verdict is not (ComparisonVerdict.Improved or ComparisonVerdict.Regressed)) return fallback;
        return string.Format(CultureInfo.CurrentCulture, Loc.T(m.Verdict == ComparisonVerdict.Improved ? "Kpi_Better" : "Kpi_Worse"), m.DeltaPercent);
    }

    public string AvgFpsSub => Delta("AverageFps", Loc.T("Kpi_AvgSub"));
    public string P99Sub => Delta("P99FrameTimeMs", P99Ms > 0 ? string.Format(CultureInfo.CurrentCulture, Loc.T("Kpi_P99Sub"), 1000 / P99Ms) : "");
    public string OneLowSub => Delta("OnePercentLowFps", Loc.T("Kpi_LowSub"));
    public bool AvgFpsUp => Comparison?.Metrics.FirstOrDefault(x => x.Metric == "AverageFps")?.Verdict == ComparisonVerdict.Improved;
    public bool P99Up => Comparison?.Metrics.FirstOrDefault(x => x.Metric == "P99FrameTimeMs")?.Verdict == ComparisonVerdict.Improved;
    public bool OneLowUp => Comparison?.Metrics.FirstOrDefault(x => x.Metric == "OnePercentLowFps")?.Verdict == ComparisonVerdict.Improved;

    public string SpikesSub
    {
        get
        {
            var i = LatestInsights;
            if (i is null || i.Spikes.Count == 0) return Loc.T("Kpi_NoSpikes");
            var matched = i.Spikes.Count(s => s.Cause != SpikeCause.NoMatch);
            return string.Format(CultureInfo.CurrentCulture, Loc.T("Kpi_SpikesMatched"), matched, i.Spikes.Count);
        }
    }

    public IReadOnlyList<CauseChip> CauseChips =>
        InsightText.Groups(LatestInsights).Select((g, i) => new CauseChip($"{InsightText.CauseTitle(g.Cause)} · {g.Count}", i == 0 && g.Cause != SpikeCause.NoMatch)).ToList();

    public string MarkersLine
    {
        get
        {
            var m = LatestInsights?.Markers ?? Array.Empty<LagMarker>();
            if (m.Count == 0) return Loc.T("Overview_MarkersNone");
            return string.Format(CultureInfo.CurrentCulture, Loc.T("Overview_Markers"), m.Count, m.Count(x => x.NearestSpikeTimeMs is not null));
        }
    }

    public string BottleneckText => InsightText.Bottleneck(LatestInsights);
    public double GpuBoundShare => LatestInsights?.Bottleneck.GpuBoundShare.OrDefault(0) ?? 0;
    public bool HasBottleneck => LatestInsights?.Bottleneck.GpuBoundShare.IsAvailable == true;
    public string LatencyText => InsightText.Latency(LatestInsights);
    public string SensorNote => LatestInsights is { } i && i.UnavailableSensors.Count > 0
        ? string.Format(CultureInfo.CurrentCulture, Loc.T("Overview_SensorsMissing"), string.Join(", ", i.UnavailableSensors.Select(x => Loc.T("Sensor_" + x))))
        : (LatestInsights?.SensorTimingApproximate == true ? Loc.T("Overview_SensorsApprox") : "");
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
                     nameof(IsGoalLatency), nameof(Latest), nameof(HasSession), nameof(NoSession), nameof(LatestId), nameof(LatestFrames),
                     nameof(LatestInsights), nameof(SessionTitle), nameof(SessionLine), nameof(AvgFps), nameof(P99Ms), nameof(OneLowFps),
                     nameof(SpikeCount), nameof(BudgetMs), nameof(AvgFpsSub), nameof(P99Sub), nameof(OneLowSub), nameof(AvgFpsUp), nameof(P99Up),
                     nameof(OneLowUp), nameof(SpikesSub), nameof(CauseChips), nameof(MarkersLine), nameof(BottleneckText), nameof(GpuBoundShare),
                     nameof(HasBottleneck), nameof(LatencyText), nameof(SensorNote)
                 })
            Raise(p);
    }
}
