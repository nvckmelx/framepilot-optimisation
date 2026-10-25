using System.IO;
using FramePilot.Core.Models;
using FramePilot.Diagnostics;
using FramePilot.GameAdapters;
using FramePilot.Inventory;
using FramePilot.Telemetry;

namespace FramePilot.App.Services;

public sealed record CaptureResult(SessionRecord? Session, CaptureStatus Status, string? Message);

/// <summary>Measure step: run PresentMon for the selected game, parse its CSV and compute documented metrics.</summary>
public sealed class CaptureService
{
    private readonly AppServices _app;
    private PresentMonRunner? _runner;
    private CancellationTokenSource? _cts;

    public CaptureService(AppServices app) => _app = app;

    public bool IsCapturing => _cts is not null;

    public string? PresentMonPath => PresentMonLocator.Locate(_app.Settings.PresentMonPath);

    public void RequestStop() => _cts?.Cancel();

    public async Task<CaptureResult> CaptureAsync(GameId game, int seconds, RunGroup group, string? note, SystemSnapshot? snapshot)
    {
        var exe = PresentMonPath;
        if (exe is null) return new(null, CaptureStatus.PresentMonNotFound, null);
        if (!GameCatalog.IsRunning(game))
            return new(null, CaptureStatus.NoData, Localization.Loc.T("Capture_GameNotRunning"));

        var csv = Path.Combine(_app.SessionsDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss}_{game}_{Guid.NewGuid():N}.csv");
        var startedAt = DateTimeOffset.Now;
        var cpu = new SystemCpuSampler();
        cpu.Prime();

        _runner = new PresentMonRunner(exe);
        _cts = new CancellationTokenSource();
        CaptureOutcome outcome;
        try
        {
            outcome = await _runner.CaptureAsync(
                new CaptureRequest(GameCatalog.ProcessName(game), seconds, csv, _app.Settings.ElevatedCapture), _cts.Token);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
        }
        var cpuLoad = cpu.Sample();

        if (outcome.Status != CaptureStatus.Completed || outcome.CsvPath is null)
        {
            _app.Store.Audit("capture", $"{game}: {outcome.Status} {outcome.Message}");
            return new(null, outcome.Status, outcome.Message);
        }

        var session = BuildSession(outcome.CsvPath, game, group, note, snapshot, startedAt, seconds,
            "PresentMon " + (PresentMonLocator.Version(exe) ?? "?"), cpuLoad);
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
            "PresentMon CSV (imported)", null);
        _app.Store.SaveSession(session);
        return session;
    }

    private static SessionRecord BuildSession(string csv, GameId game, RunGroup group, string? note, SystemSnapshot? snapshot,
        DateTimeOffset startedAt, double requestedSeconds, string source, double? cpuLoad)
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

        return new SessionRecord(Guid.NewGuid(), game, GameCatalog.ProcessName(game), startedAt, requestedSeconds > 0 ? requestedSeconds : stats.DurationMs / 1000,
            group, conditions, source, parsed.FrameTimeColumn, parsed.Columns, stats, csv, false, warnings);
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
