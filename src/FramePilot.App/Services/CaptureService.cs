using System.Diagnostics;
using System.IO;
using FramePilot.Core.Models;
using FramePilot.Diagnostics;
using FramePilot.GameAdapters;
using FramePilot.Inventory;
using FramePilot.Sensors;
using FramePilot.Telemetry;

namespace FramePilot.App.Services;

public sealed record CaptureResult(SessionRecord? Session, CaptureStatus Status, string? Message);

/// <summary>Measure step: run PresentMon for the selected game, parse its CSV and compute documented metrics.</summary>
public sealed class CaptureService
{
    private readonly AppServices _app;
    private PresentMonRunner? _runner;
    private CancellationTokenSource? _cts;
    private readonly List<long> _markers = new();

    public CaptureService(AppServices app) => _app = app;

    /// <summary>Raised on the capturing thread when the player sets a lag marker; the UI uses it for feedback.</summary>
    public event Action? MarkerAdded;

    /// <summary>"It stuttered here": stores the current QPC time. Only works while a capture is running.</summary>
    public bool AddMarker()
    {
        if (_cts is null) return false;
        lock (_markers) _markers.Add(Stopwatch.GetTimestamp());
        MarkerAdded?.Invoke();
        return true;
    }

    public int MarkerCount { get { lock (_markers) return _markers.Count; } }

    public bool IsCapturing => _cts is not null;

    public string? PresentMonPath => PresentMonLocator.Locate(_app.Settings.PresentMonPath);

    public void RequestStop() => _cts?.Cancel();

    public async Task<CaptureResult> CaptureAsync(GameId game, int seconds, RunGroup group, string? note, SystemSnapshot? snapshot)
    {
        if (_app.IsDesignPreview || _app.Settings.DemoMode)
            return new(null, CaptureStatus.NoData, Localization.Loc.T("Capture_DemoBlocked"));
        var exe = PresentMonPath;
        if (exe is null) return new(null, CaptureStatus.PresentMonNotFound, null);
        if (!GameCatalog.IsRunning(game))
            return new(null, CaptureStatus.NoData, Localization.Loc.T("Capture_GameNotRunning"));

        var csv = Path.Combine(_app.SessionsDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss}_{game}_{Guid.NewGuid():N}.csv");
        var startedAt = DateTimeOffset.Now;
        var cpu = new SystemCpuSampler();
        cpu.Prime();

        _runner = new PresentMonRunner(exe);
        lock (_markers) _markers.Clear();
        var sampler = new SensorSampler(GameCatalog.ProcessName(game), VramBudget(snapshot));
        sampler.Start();
        _cts = new CancellationTokenSource();
        CaptureOutcome outcome;
        IReadOnlyList<RawSensorSample> raw;
        try
        {
            outcome = await _runner.CaptureAsync(
                new CaptureRequest(GameCatalog.ProcessName(game), seconds, csv, _app.Settings.ElevatedCapture), _cts.Token);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            raw = await sampler.StopAsync();
        }
        var cpuLoad = cpu.Sample();
        long[] markers;
        lock (_markers) markers = _markers.ToArray();
        var capture = new SensorCapture(raw, markers, sampler.Notes);

        if (outcome.Status != CaptureStatus.Completed || outcome.CsvPath is null)
        {
            _app.Store.Audit("capture", $"{game}: {outcome.Status} {outcome.Message}");
            return new(null, outcome.Status, outcome.Message);
        }

        var session = BuildSession(outcome.CsvPath, game, group, note, snapshot, startedAt, seconds,
            "PresentMon " + (PresentMonLocator.Version(exe) ?? "?"), cpuLoad, capture);
        _app.Store.SaveSession(session);
        _app.Store.Audit("capture", $"{game}: {session.Stats.FrameCount} frames, {session.Stats.AverageFps:0.0} FPS");
        return new(session, CaptureStatus.Completed, outcome.Message);
    }

    /// <summary>Analyse an existing PresentMon CSV (for example one recorded with PresentMon itself).</summary>
    public SessionRecord Import(string csvPath, GameId game, RunGroup group, string? note, SystemSnapshot? snapshot)
    {
        var copy = Path.Combine(_app.SessionsDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss}_{game}_import_{Guid.NewGuid():N}.csv");
        File.Copy(csvPath, copy);
        var session = BuildSession(copy, game, group, note, snapshot, new DateTimeOffset(File.GetLastWriteTime(csvPath)), 0,
            "PresentMon CSV (imported)", null, null);
        _app.Store.SaveSession(session);
        return session;
    }

    private sealed record SensorCapture(IReadOnlyList<RawSensorSample> Raw, IReadOnlyList<long> Markers, IReadOnlyList<string> Notes);

    private static double? VramBudget(SystemSnapshot? snapshot)
    {
        var gpu = snapshot?.Gpus.FirstOrDefault(g => g.LikelyIntegrated.IsAvailable && !g.LikelyIntegrated.Value) ?? snapshot?.Gpus.FirstOrDefault();
        return gpu?.DedicatedMemoryBytes.IsAvailable == true && gpu.DedicatedMemoryBytes.Value > 0 ? gpu.DedicatedMemoryBytes.Value : null;
    }

    private static SessionRecord BuildSession(string csv, GameId game, RunGroup group, string? note, SystemSnapshot? snapshot,
        DateTimeOffset startedAt, double requestedSeconds, string source, double? cpuLoad, SensorCapture? capture)
    {
        ParsedCapture parsed;
        try
        {
            using var reader = new StreamReader(csv);
            parsed = PresentMonCsv.Parse(reader, GameCatalog.ExeName(game));
        }
        catch (InvalidDataException) when (requestedSeconds == 0)
        {
            // Imported files may come from a capture of a renamed or launcher process: analyse all rows.
            using var reader = new StreamReader(csv);
            parsed = PresentMonCsv.Parse(reader, null);
        }
        var stats = MetricCalculator.Compute(parsed.Frames);

        var gpu = snapshot?.Gpus.FirstOrDefault(g => g.LikelyIntegrated.IsAvailable && !g.LikelyIntegrated.Value) ?? snapshot?.Gpus.FirstOrDefault();
        var display = snapshot?.Displays.FirstOrDefault(d => d.IsPrimary) ?? snapshot?.Displays.FirstOrDefault();
        var detected = snapshot?.Games.FirstOrDefault(g => g.Game == game);
        var conditions = new TestConditions(
            detected?.Version.IsAvailable == true ? detected.Version.Value : null,
            gpu?.DriverVersion.IsAvailable == true ? gpu.DriverVersion.Value : null,
            display?.Width.IsAvailable == true ? display.Width.Value : null,
            display?.Height.IsAvailable == true ? display.Height.Value : null,
            display?.CurrentRefreshHz.IsAvailable == true ? display.CurrentRefreshHz.Value : null,
            string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            null);

        var warnings = parsed.Warnings.ToList();
        if (snapshot is null) warnings.Add("No PC snapshot was available; test conditions are incomplete.");
        if (cpuLoad is double c) warnings.Add($"Whole-system CPU load during capture: {c:0}%.");
        if (requestedSeconds > 0 && stats.DurationMs < requestedSeconds * 1000 * 0.8)
            warnings.Add("The recording is shorter than requested (stopped early or the game was not rendering the whole time).");

        // Sensors and lag markers on the frame time scale (exact with PresentMon --qpc_time, otherwise end-aligned).
        string? sensorsCsv = null;
        IReadOnlyList<SensorSample> sensors = Array.Empty<SensorSample>();
        IReadOnlyList<double> markerMs = Array.Empty<double>();
        var approximate = false;
        if (capture is not null)
        {
            (sensors, markerMs, approximate) = SensorAlignment.Align(capture.Raw, capture.Markers, parsed.Frames, parsed.FirstFrameQpc, Stopwatch.Frequency);
            if (sensors.Count > 0)
            {
                sensorsCsv = Path.ChangeExtension(csv, ".sensors.csv");
                try { SensorCsv.Write(sensorsCsv, sensors); }
                catch (IOException ex) { warnings.Add("Sensor data could not be saved: " + ex.Message); sensorsCsv = null; }
            }
            foreach (var n in capture.Notes) warnings.Add("Sensors: " + n);
            if (approximate && sensors.Count > 0) warnings.Add("Sensor timing is approximate (PresentMon did not report QPC times).");
        }
        var insights = SpikeAttributor.Analyze(parsed.Frames, sensors, markerMs, VramBudget(snapshot), approximate);

        return new SessionRecord(Guid.NewGuid(), game, GameCatalog.ProcessName(game), startedAt, requestedSeconds > 0 ? requestedSeconds : stats.DurationMs / 1000,
            group, conditions, source, parsed.FrameTimeColumn, parsed.Columns, stats, csv, false, warnings, sensorsCsv, insights);
    }

    public static IReadOnlyList<SensorSample> LoadSensors(SessionRecord session)
    {
        if (session.IsDemo) return DemoData.SensorsForSession(session.Id);
        if (session.SensorsCsvPath is null || !File.Exists(session.SensorsCsvPath)) return Array.Empty<SensorSample>();
        try { return SensorCsv.Read(session.SensorsCsvPath); }
        catch (IOException) { return Array.Empty<SensorSample>(); }
    }

    public static IReadOnlyList<FrameSample> LoadFrames(SessionRecord session)
    {
        if (session.IsDemo) return DemoData.FramesForSession(session.Id);
        if (session.CsvPath is null || !File.Exists(session.CsvPath)) return Array.Empty<FrameSample>();
        try
        {
            using var reader = new StreamReader(session.CsvPath);
            return PresentMonCsv.Parse(reader, GameCatalog.ExeName(session.Game)).Frames;
        }
        catch (InvalidDataException)
        {
            using var reader = new StreamReader(session.CsvPath);
            return PresentMonCsv.Parse(reader, null).Frames;
        }
    }
}
