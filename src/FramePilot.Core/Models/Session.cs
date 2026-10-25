namespace FramePilot.Core.Models;

/// <summary>One presented frame as reported by the telemetry source. Optional fields are null when the source did not provide them.</summary>
public sealed record FrameSample(
    double TimeMs,
    double FrameTimeMs,
    double? GpuBusyMs,
    double? CpuBusyMs,
    double? DisplayedTimeMs,
    double? PcLatencyMs,
    double? ClickToPhotonMs,
    string? PresentMode,
    bool Displayed);

public enum RunGroup
{
    Unlabeled,
    Baseline,
    After
}

/// <summary>Conditions under which a session was recorded. Comparisons are only meaningful when these match.</summary>
public sealed record TestConditions(
    string? GameVersion,
    string? GpuDriverVersion,
    int? DisplayWidth,
    int? DisplayHeight,
    int? RefreshHz,
    string? ScenarioNote,
    int? AppliedChangeBatchCount);

public sealed record ThresholdCount(double ThresholdMs, int Count, double Percent);

/// <summary>Computed frame statistics. Definitions are documented in docs/MEASUREMENT_PROTOCOL.md and covered by tests.</summary>
public sealed record FrameStats(
    int FrameCount,
    double DurationMs,
    double AverageFps,
    double MedianFrameTimeMs,
    double P99FrameTimeMs,
    double OnePercentLowFps,
    double StdDevFrameTimeMs,
    int SpikeCount,
    IReadOnlyList<ThresholdCount> Thresholds,
    Measured<double> MedianGpuBusyRatio,
    Measured<double> MedianCpuBusyRatio,
    Measured<double> MedianPcLatencyMs,
    Measured<double> MedianClickToPhotonMs,
    Measured<string> DominantPresentMode,
    double DroppedPercent);

public sealed record SessionRecord(
    Guid Id,
    GameId Game,
    string ProcessName,
    DateTimeOffset StartedAt,
    double RequestedSeconds,
    RunGroup Group,
    TestConditions Conditions,
    string TelemetrySource,
    string FrameTimeColumn,
    IReadOnlyList<string> AvailableColumns,
    FrameStats Stats,
    string? CsvPath,
    bool IsDemo,
    IReadOnlyList<string> Warnings);
