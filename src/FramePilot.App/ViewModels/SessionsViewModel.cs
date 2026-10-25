using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FramePilot.App.Localization;
using FramePilot.App.Services;
using FramePilot.App.Views;
using FramePilot.Core;
using FramePilot.Core.Models;
using FramePilot.Diagnostics;
using FramePilot.GameAdapters;
using FramePilot.Licensing;
using FramePilot.Telemetry;
using Microsoft.Win32;

namespace FramePilot.App.ViewModels;

public sealed class SessionItem : ObservableObject
{
    public SessionItem(SessionRecord s) => Session = s;
    public SessionRecord Session { get; }
    public string Title => $"{GameCatalog.DisplayName(Session.Game)} · {Session.StartedAt.ToString("g", CultureInfo.CurrentCulture)}";
    public string GroupText => Loc.T("Group_" + Session.Group);
    public string Fps => Session.Stats.AverageFps.ToString("0", CultureInfo.CurrentCulture);
    public string P99 => Session.Stats.P99FrameTimeMs.ToString("0.00", CultureInfo.CurrentCulture);
    public string Low1 => Session.Stats.OnePercentLowFps.ToString("0", CultureInfo.CurrentCulture);
    public string Spikes => $"{Session.Stats.SpikeCount} ({100.0 * Session.Stats.SpikeCount / Math.Max(1, Session.Stats.FrameCount):0.#}%)";
    public bool IsDemo => Session.IsDemo;
}

public sealed record MetricRow(string Name, string Value, string Hint);

public sealed class SessionsViewModel : ObservableObject, IPage
{
    private readonly MainViewModel _main;
    private SessionItem? _selected;
    private IReadOnlyList<FrameSample> _frames = Array.Empty<FrameSample>();
    private int _seconds;
    private RunGroup _group = RunGroup.Baseline;
    private string _note = "";
    private string? _captureStatus;
    private double _captureProgress;
    private bool _capturing;
    private ComparisonResult? _comparison;
    private readonly DispatcherTimer _timer;
    private DateTime _captureStart;

    private Workspace W => _main.Workspace;

    public SessionsViewModel(MainViewModel main)
    {
        _main = main;
        _seconds = main.Workspace.App.Settings.CaptureSeconds;
        StartCommand = new AsyncCommand(StartAsync, () => !_capturing && !W.Busy);
        StopCommand = new RelayCommand(() => W.Capture.RequestStop(), () => _capturing);
        ImportCommand = new RelayCommand(Import, () => !_capturing);
        CompareCommand = new RelayCommand(Compare);
        DeleteCommand = new RelayCommand(Delete, () => _selected is not null && !_capturing);
        OpenCsvCommand = new RelayCommand(() =>
        {
            if (_selected?.Session.CsvPath is { } p && File.Exists(p)) Shell.Open(Path.GetDirectoryName(p)!);
        }, () => _selected?.Session.CsvPath is not null);
        SetGroupCommand = new RelayCommand(p => { if (p is SessionItem i) ChangeGroup(i); });
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) =>
        {
            var el = (DateTime.Now - _captureStart).TotalSeconds;
            CaptureProgress = Math.Min(100, 100 * el / Math.Max(1, _seconds));
            CaptureStatus = Loc.F("Capture_Recording", (int)el, _seconds);
        };
        W.Sessions.CollectionChanged += (_, _) => RebuildItems();
        W.Changed += (_, _) => { Raise(nameof(GameName)); Raise(nameof(PresentMonMissing)); };
        Loc.Instance.LanguageChanged += (_, _) => { RebuildItems(); Raise(nameof(Metrics)); Compare(); };
        RebuildItems();
    }

    public ObservableCollection<SessionItem> Items { get; } = new();

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand CompareCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand OpenCsvCommand { get; }
    public ICommand SetGroupCommand { get; }

    public string GameName => GameCatalog.DisplayName(W.TargetGame);
    public bool PresentMonMissing => !W.IsDemo && W.Capture.PresentMonPath is null;
    public bool IsDemo => W.IsDemo;

    public int[] DurationOptions { get; } = { 60, 120, 180, 300 };

    public int Seconds
    {
        get => _seconds;
        set
        {
            if (Set(ref _seconds, value)) W.App.Settings.CaptureSeconds = value;
        }
    }

    public bool IsBaseline { get => _group == RunGroup.Baseline; set { if (value) { _group = RunGroup.Baseline; Raise(); Raise(nameof(IsAfter)); Raise(nameof(IsUnlabeled)); } } }
    public bool IsAfter { get => _group == RunGroup.After; set { if (value) { _group = RunGroup.After; Raise(); Raise(nameof(IsBaseline)); Raise(nameof(IsUnlabeled)); } } }
    public bool IsUnlabeled { get => _group == RunGroup.Unlabeled; set { if (value) { _group = RunGroup.Unlabeled; Raise(); Raise(nameof(IsBaseline)); Raise(nameof(IsAfter)); } } }

    public void PresetGroup(RunGroup g)
    {
        _group = g;
        Raise(nameof(IsBaseline));
        Raise(nameof(IsAfter));
        Raise(nameof(IsUnlabeled));
    }

    public string Note { get => _note; set => Set(ref _note, value); }

    public bool Capturing
    {
        get => _capturing;
        private set { Set(ref _capturing, value); CommandManager.InvalidateRequerySuggested(); }
    }

    public string? CaptureStatus { get => _captureStatus; private set => Set(ref _captureStatus, value); }
    public double CaptureProgress { get => _captureProgress; private set => Set(ref _captureProgress, value); }

    public SessionItem? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            LoadFrames();
            Raise(nameof(Metrics));
            Raise(nameof(SelectedWarnings));
            Raise(nameof(HasSelection));
        }
    }

    public bool HasSelection => _selected is not null;

    public IReadOnlyList<FrameSample> Frames
    {
        get => _frames;
        private set => Set(ref _frames, value);
    }

    public IReadOnlyList<MetricRow> Metrics
    {
        get
        {
            if (_selected is null) return Array.Empty<MetricRow>();
            var s = _selected.Session;
            var st = s.Stats;
            string Na(Measured<double> m, string fmt, string unit) =>
                m.IsAvailable ? m.Value.ToString(fmt, CultureInfo.CurrentCulture) + unit : Loc.T("NoData");
            var list = new List<MetricRow>
            {
                new(Loc.T("M_AvgFps"), st.AverageFps.ToString("0.0", CultureInfo.CurrentCulture), Loc.T("M_AvgFpsHint")),
                new(Loc.T("M_Median"), st.MedianFrameTimeMs.ToString("0.00", CultureInfo.CurrentCulture) + " ms", Loc.T("M_MedianHint")),
                new(Loc.T("M_P99"), st.P99FrameTimeMs.ToString("0.00", CultureInfo.CurrentCulture) + " ms", Loc.T("M_P99Hint")),
                new(Loc.T("M_Low1"), st.OnePercentLowFps.ToString("0.0", CultureInfo.CurrentCulture), Loc.T("M_Low1Hint")),
                new(Loc.T("M_Spikes"), _selected.Spikes, Loc.T("M_SpikesHint")),
                new(Loc.T("M_GpuBusy"), Na(st.MedianGpuBusyRatio, "0.00", ""), Loc.T("M_GpuBusyHint")),
                new(Loc.T("M_PcLatency"), Na(st.MedianPcLatencyMs, "0.0", " ms"), Loc.T("M_PcLatencyHint")),
                new(Loc.T("M_Click"), Na(st.MedianClickToPhotonMs, "0.0", " ms"), Loc.T("M_ClickHint")),
                new(Loc.T("M_PresentMode"), st.DominantPresentMode.IsAvailable ? $"{st.DominantPresentMode.Value} ({st.DominantPresentMode.Note})" : Loc.T("NoData"), Loc.T("M_PresentModeHint")),
                new(Loc.T("M_Frames"), $"{st.FrameCount} · {st.DurationMs / 1000:0} s", s.TelemetrySource + " · " + s.FrameTimeColumn)
            };
            foreach (var th in st.Thresholds)
                list.Add(new(Loc.F("M_Over", th.ThresholdMs.ToString("0.##", CultureInfo.CurrentCulture)), $"{th.Count} ({th.Percent:0.##}%)", Loc.T("M_OverHint")));
            var c = s.Conditions;
            list.Add(new(Loc.T("M_Conditions"),
                $"{c.DisplayWidth}×{c.DisplayHeight} @ {c.RefreshHz} Hz · {Loc.T("M_Driver")} {c.GpuDriverVersion ?? "—"} · {c.GameVersion ?? "—"}",
                c.ScenarioNote ?? ""));
            return list;
        }
    }

    public IReadOnlyList<string> SelectedWarnings => _selected?.Session.Warnings ?? (IReadOnlyList<string>)Array.Empty<string>();

    public ComparisonResult? Comparison
    {
        get => _comparison;
        private set
        {
            Set(ref _comparison, value);
            Raise(nameof(ComparisonSummary));
            Raise(nameof(ComparisonRows));
            Raise(nameof(ComparisonWarnings));
            Raise(nameof(HasComparison));
            Raise(nameof(ComparisonPositive));
        }
    }

    public bool HasComparison => _comparison is not null;
    public bool ComparisonPositive => _comparison?.Overall == ComparisonVerdict.Improved;
    public string ComparisonSummary => _comparison?.Summary.Get(Loc.Instance.Language) ?? "";
    public IReadOnlyList<string> ComparisonWarnings => _comparison?.Warnings.Select(w => w.Get(Loc.Instance.Language)).ToList() ?? new List<string>();

    public IReadOnlyList<MetricRow> ComparisonRows => _comparison?.Metrics.Select(m => new MetricRow(
        Loc.T("CM_" + m.Metric),
        $"{m.BaselineMedian:0.##} → {m.AfterMedian:0.##} ({m.DeltaPercent:+0.#;-0.#;0}%)",
        $"{Loc.T("Verdict_" + m.Verdict)} · {Loc.T("CM_Before")} [{m.BaselineMin:0.##}…{m.BaselineMax:0.##}] · {Loc.T("CM_After")} [{m.AfterMin:0.##}…{m.AfterMax:0.##}]"))
        .ToList() ?? new List<MetricRow>();

    public void OnNavigatedTo()
    {
        Raise(nameof(PresentMonMissing));
        Raise(nameof(GameName));
        Raise(nameof(IsDemo));
        if (_selected is null && Items.Count > 0) Selected = Items[0];
        Compare();
    }

    private void RebuildItems()
    {
        var prev = _selected?.Session.Id;
        Items.Clear();
        foreach (var s in W.Sessions.OrderByDescending(s => s.StartedAt)) Items.Add(new SessionItem(s));
        _selected = null;
        Selected = Items.FirstOrDefault(i => i.Session.Id == prev) ?? Items.FirstOrDefault();
        Raise(nameof(Selected));
    }

    private void LoadFrames()
    {
        if (_selected is null) { Frames = Array.Empty<FrameSample>(); return; }
        try { Frames = CaptureService.LoadFrames(_selected.Session); }
        catch (Exception ex)
        {
            ErrorReporter.Log("frames", ex.Message);
            Frames = Array.Empty<FrameSample>();
        }
    }

    private async Task StartAsync()
    {
        if (W.IsDemo)
        {
            DialogWindow.Show(Loc.T("Capture_Title"), Loc.T("Capture_DemoBlocked"), DialogKind.Info);
            return;
        }
        if (W.Capture.PresentMonPath is null)
        {
            DialogWindow.Show(Loc.T("Capture_Title"), Loc.T("Capture_NoPresentMon"), DialogKind.Warning);
            return;
        }
        if (!WithinHistoryLimit()) return;
        if (!GameCatalog.IsRunning(W.TargetGame))
        {
            DialogWindow.Show(Loc.T("Capture_Title"), Loc.F("Capture_StartGameFirst", GameName), DialogKind.Info);
            return;
        }
        if (W.Snapshot is null) await W.RunInventoryAsync();

        Capturing = true;
        _captureStart = DateTime.Now;
        CaptureProgress = 0;
        CaptureStatus = Loc.T("Capture_Starting");
        _timer.Start();
        CaptureResult result;
        try
        {
            result = await W.Capture.CaptureAsync(W.TargetGame, Seconds, _group, Note, W.Snapshot);
        }
        finally
        {
            _timer.Stop();
            Capturing = false;
            CaptureProgress = 0;
        }

        if (result.Session is not null)
        {
            CaptureStatus = Loc.F("Capture_Done", result.Session.Stats.FrameCount);
            W.AddSession(result.Session);
            Selected = Items.FirstOrDefault(i => i.Session.Id == result.Session.Id);
            Compare();
        }
        else
        {
            CaptureStatus = Loc.T("CaptureStatus_" + result.Status) + (result.Message is null ? "" : " — " + result.Message);
            DialogWindow.Show(Loc.T("Capture_Title"), CaptureStatus, DialogKind.Warning);
        }
    }

    private bool WithinHistoryLimit()
    {
        var limit = Entitlements.SessionHistoryLimit(W.App.License);
        if (W.Sessions.Count(s => !s.IsDemo) < limit) return true;
        DialogWindow.Show(Loc.T("Sessions_LimitTitle"), Loc.F("Sessions_LimitBody", limit), DialogKind.Info);
        return false;
    }

    private void Import()
    {
        if (!WithinHistoryLimit()) return;
        var dlg = new OpenFileDialog { Filter = "PresentMon CSV (*.csv)|*.csv", Title = Loc.T("Capture_ImportTitle") };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var session = W.Capture.Import(dlg.FileName, W.TargetGame, _group, Note, W.Snapshot);
            W.AddSession(session);
            Selected = Items.FirstOrDefault(i => i.Session.Id == session.Id);
            Compare();
        }
        catch (Exception ex)
        {
            DialogWindow.Show(Loc.T("Capture_ImportTitle"), Loc.T("Capture_ImportFailed") + Environment.NewLine + ex.Message, DialogKind.Warning);
        }
    }

    private void Delete()
    {
        if (_selected is null) return;
        if (!DialogWindow.Confirm(Loc.T("Sessions_DeleteTitle"), Loc.T("Sessions_DeleteBody"), Loc.T("Delete"), Loc.T("Cancel"))) return;
        W.RemoveSession(_selected.Session);
        Compare();
    }

    private void ChangeGroup(SessionItem item)
    {
        var next = item.Session.Group switch
        {
            RunGroup.Baseline => RunGroup.After,
            RunGroup.After => RunGroup.Unlabeled,
            _ => RunGroup.Baseline
        };
        var updated = item.Session with { Group = next };
        var idx = W.Sessions.IndexOf(item.Session);
        if (idx >= 0) W.Sessions[idx] = updated;
        if (!updated.IsDemo) W.App.Store.SaveSession(updated);
        Compare();
    }

    private void Compare()
    {
        var sessions = W.Sessions.Where(s => s.Game == W.TargetGame && s.IsDemo == W.IsDemo).ToList();
        var baseline = sessions.Where(s => s.Group == RunGroup.Baseline).ToList();
        var after = sessions.Where(s => s.Group == RunGroup.After).ToList();
        Comparison = baseline.Count == 0 && after.Count == 0 ? null : SessionComparer.Compare(baseline, after);
    }
}
