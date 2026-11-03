namespace FramePilot.Core.Models;

/// <summary>
/// One system sensor reading taken while a session was recorded (10 Hz). Time is in milliseconds on the
/// same scale as <see cref="FrameSample.TimeMs"/>. Every field is null when its source was unavailable.
/// </summary>
public sealed record SensorSample(
    double TimeMs,
    double? CpuPerformancePercent,      // % Processor Performance (can exceed 100 with boost)
    double? CpuPerformanceLimitPercent, // % Performance Limit (< 100 = limited by power/thermal)
    double? GameCpuPercent,             // share of all logical cores used by the game
    double? OtherCpuPercent,            // share of all logical cores used by every other process
    string? TopOtherProcess,
    double? TopOtherCpuPercent,
    double? GameGpuPercent,             // 3D engine utilisation attributed to the game
    double? GameVramBytes,              // dedicated video memory used by the game
    double? AdapterVramBytes,           // dedicated video memory used on the adapter (all processes)
    double? VramBudgetBytes,            // dedicated video memory of the adapter
    double? PageReadsPerSec,            // hard page reads from disk
    double? GpuTemperatureC,            // NVML, NVIDIA only
    double? GpuPowerW,                  // NVML, NVIDIA only
    ulong? GpuClockReasons);            // NVML clock event / throttle reason bit mask

/// <summary>What a frame-time spike coincided with. Coincidence, not proven cause.</summary>
public enum SpikeCause
{
    VramFull,
    Paging,
    CpuThrottle,
    GpuLimit,
    BackgroundProcess,
    CpuSideFrame,
    GpuSideFrame,
    NoMatch
}

/// <param name="TimeMs">End of the spike frame on the session time scale.</param>
/// <param name="Value">The sensor value that matched (bytes, pages/s, %, °C), see <paramref name="Cause"/>.</param>
/// <param name="Reference">What the value was compared with (budget, session median, power W).</param>
public sealed record SpikeAttribution(
    double TimeMs,
    double FrameTimeMs,
    SpikeCause Cause,
    double? Value,
    double? Reference,
    string? ProcessName);

/// <summary>A "it stuttered here" mark the player set with the hotkey, and the spike nearest to it (±1 s).</summary>
public sealed record LagMarker(double TimeMs, double? NearestSpikeTimeMs);

public sealed record BottleneckSummary(
    Measured<double> GpuBoundShare,
    Measured<double> MedianCpuBusyMs,
    Measured<double> MedianGpuBusyMs);

public sealed record LatencySummary(
    Measured<double> PcLatencyMs,
    Measured<double> ClickToPhotonMs,
    Measured<double> DisplayLatencyMs);

public sealed record SessionInsights(
    IReadOnlyList<SpikeAttribution> Spikes,
    IReadOnlyList<LagMarker> Markers,
    BottleneckSummary Bottleneck,
    LatencySummary Latency,
    IReadOnlyList<string> UnavailableSensors,
    bool SensorTimingApproximate);
