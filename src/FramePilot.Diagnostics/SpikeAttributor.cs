using FramePilot.Core;
using FramePilot.Core.Models;

namespace FramePilot.Diagnostics;

/// <summary>
/// Matches every frame-time spike (frame &gt; 2 × median, as in <see cref="MetricCalculator"/>) with what the system sensors
/// showed around it. The result is a coincidence, never a proven cause, and "no match" is a valid answer:
/// shader compilation, network or game-side work are invisible to these sensors and are not guessed.
/// Thresholds are documented in docs/superpowers/specs/2026-11-04-tunefps-1.1-design.md §3.2.
/// </summary>
public static class SpikeAttributor
{
    public const double WindowBeforeMs = 500;
    public const double WindowAfterMs = 100;
    public const double MarkerMatchMs = 1000;

    public const double VramGameShare = 0.95;
    public const double VramAdapterShare = 0.97;
    public const double PagingMinPerSec = 50;
    /// <summary>Paging counts as "up" above this multiple of the session median (robust when paging is frequent).</summary>
    public const double PagingMedianFactor = 4;
    public const double CpuLimitBelowPercent = 90;
    public const double CpuFrequencyDropRatio = 0.85;
    public const double BackgroundRisePoints = 15;
    public const double GpuBoundRatio = 0.85;

    /// <summary>NVML clock event reasons that mean "slowed down by power or temperature" (idle and app clocks excluded).</summary>
    public const ulong GpuLimitReasons = 0x4 | 0x8 | 0x20 | 0x40 | 0x80;
    /// <summary>Hardware slowdowns are always notable; the software power cap is only notable when it is not active most of the time.</summary>
    public const ulong GpuHardwareReasons = 0x8 | 0x40 | 0x80;

    public static SessionInsights Analyze(
        IReadOnlyList<FrameSample> frames,
        IReadOnlyList<SensorSample> sensors,
        IReadOnlyList<double> markerTimesMs,
        double? vramBudgetBytes = null,
        bool sensorTimingApproximate = false)
    {
        var spikes = new List<SpikeAttribution>();
        if (frames.Count > 0)
        {
            var sorted = frames.Select(f => f.FrameTimeMs).OrderBy(x => x).ToList();
            var median = MetricCalculator.Median(sorted);
            var baseline = Baseline.From(sensors);
            foreach (var f in frames.Where(f => f.FrameTimeMs > 2 * median))
                spikes.Add(Attribute(f, sensors, baseline, vramBudgetBytes));
        }

        var markers = markerTimesMs.OrderBy(t => t).Select(t =>
        {
            var nearest = spikes.Where(s => Math.Abs(s.TimeMs - t) <= MarkerMatchMs)
                .OrderBy(s => Math.Abs(s.TimeMs - t)).FirstOrDefault();
            return new LagMarker(t, nearest?.TimeMs);
        }).ToList();

        return new SessionInsights(spikes, markers, BottleneckClassifier.Classify(frames), BottleneckClassifier.Latency(frames),
            UnavailableSensors(sensors), sensorTimingApproximate);
    }

    internal static SpikeAttribution Attribute(FrameSample f, IReadOnlyList<SensorSample> sensors, Baseline b, double? budgetParam)
    {
        var from = f.TimeMs - f.FrameTimeMs - WindowBeforeMs;
        var to = f.TimeMs + WindowAfterMs;
        var w = sensors.Where(s => s.TimeMs >= from && s.TimeMs <= to).ToList();
        SpikeAttribution Make(SpikeCause c, double? v = null, double? r = null, string? p = null) => new(f.TimeMs, f.FrameTimeMs, c, v, r, p);

        if (w.Count > 0)
        {
            // 1. Video memory full, together with rising paging.
            var budget = w.Select(s => s.VramBudgetBytes).FirstOrDefault(x => x is > 0) ?? budgetParam;
            var pagingUp = IsPagingUp(w, b);
            if (budget is > 0)
            {
                var game = Max(w, s => s.GameVramBytes);
                var adapter = Max(w, s => s.AdapterVramBytes);
                var full = (game is not null && game >= VramGameShare * budget) || (adapter is not null && adapter >= VramAdapterShare * budget);
                if (full && pagingUp) return Make(SpikeCause.VramFull, game ?? adapter, budget);
            }
            // 2. Paging from disk.
            if (pagingUp) return Make(SpikeCause.Paging, Max(w, s => s.PageReadsPerSec), b.PageReadsMedian);
            // 3. CPU throttling.
            var limit = Min(w, s => s.CpuPerformanceLimitPercent);
            var perf = Min(w, s => s.CpuPerformancePercent);
            if ((limit is not null && limit < CpuLimitBelowPercent) ||
                (perf is not null && b.CpuPerformanceMedian is > 0 && perf < CpuFrequencyDropRatio * b.CpuPerformanceMedian))
                return Make(SpikeCause.CpuThrottle, perf, b.CpuPerformanceMedian);
            // 4. GPU slowed by power or temperature.
            var reasons = w.Aggregate(0UL, (acc, s) => acc | (s.GpuClockReasons ?? 0));
            var notable = (reasons & GpuHardwareReasons) != 0 ||
                          ((reasons & GpuLimitReasons) != 0 && b.GpuLimitedShare < 0.5);
            if (notable) return Make(SpikeCause.GpuLimit, Max(w, s => s.GpuTemperatureC), Max(w, s => s.GpuPowerW));
            // 5. A background process woke up.
            var other = Max(w, s => s.OtherCpuPercent);
            if (other is not null && b.OtherCpuMedian is not null && other - b.OtherCpuMedian >= BackgroundRisePoints)
            {
                var top = w.Where(s => s.TopOtherCpuPercent is not null).OrderByDescending(s => s.TopOtherCpuPercent).FirstOrDefault();
                return Make(SpikeCause.BackgroundProcess, top?.TopOtherCpuPercent ?? other, b.OtherCpuMedian, top?.TopOtherProcess);
            }
        }

        // 6. Nothing in the sensors: say which side of the frame took the time, if PresentMon reported it.
        if (f.GpuBusyMs is double g && f.FrameTimeMs > 0)
        {
            if (g >= GpuBoundRatio * f.FrameTimeMs) return Make(SpikeCause.GpuSideFrame, g, f.FrameTimeMs);
            if (f.CpuBusyMs is double c && c > g) return Make(SpikeCause.CpuSideFrame, c, f.FrameTimeMs);
        }
        return Make(SpikeCause.NoMatch);
    }

    private static bool IsPagingUp(List<SensorSample> w, Baseline b)
    {
        var reads = Max(w, s => s.PageReadsPerSec);
        return reads is not null && reads > PagingMinPerSec && reads > PagingMedianFactor * (b.PageReadsMedian ?? 0);
    }

    private static double? Max(IEnumerable<SensorSample> w, Func<SensorSample, double?> f)
    {
        double? m = null;
        foreach (var s in w) if (f(s) is double v && (m is null || v > m)) m = v;
        return m;
    }

    private static double? Min(IEnumerable<SensorSample> w, Func<SensorSample, double?> f)
    {
        double? m = null;
        foreach (var s in w) if (f(s) is double v && (m is null || v < m)) m = v;
        return m;
    }

    internal static IReadOnlyList<string> UnavailableSensors(IReadOnlyList<SensorSample> s)
    {
        var missing = new List<string>();
        void Check(string name, Func<SensorSample, object?> f) { if (!s.Any(x => f(x) is not null)) missing.Add(name); }
        Check("cpu-frequency", x => x.CpuPerformancePercent);
        Check("cpu-limit", x => x.CpuPerformanceLimitPercent);
        Check("process-cpu", x => x.OtherCpuPercent);
        Check("gpu-load", x => x.GameGpuPercent);
        Check("vram", x => x.GameVramBytes ?? x.AdapterVramBytes);
        Check("paging", x => x.PageReadsPerSec);
        Check("gpu-temperature", x => x.GpuTemperatureC);
        return missing;
    }

    /// <summary>Session-wide reference values the spike windows are compared against.</summary>
    internal sealed record Baseline(double? PageReadsMedian, double? CpuPerformanceMedian, double? OtherCpuMedian, double GpuLimitedShare)
    {
        public static Baseline From(IReadOnlyList<SensorSample> s)
        {
            static double? Med(IEnumerable<double?> xs)
            {
                var l = xs.Where(x => x is not null).Select(x => x!.Value).OrderBy(x => x).ToList();
                return l.Count == 0 ? null : MetricCalculator.Median(l);
            }
            var withReasons = s.Where(x => x.GpuClockReasons is not null).ToList();
            var limitedShare = withReasons.Count == 0 ? 0 :
                (double)withReasons.Count(x => (x.GpuClockReasons!.Value & GpuLimitReasons) != 0) / withReasons.Count;
            return new Baseline(
                Med(s.Select(x => x.PageReadsPerSec)),
                Med(s.Select(x => x.CpuPerformancePercent)),
                Med(s.Select(x => x.OtherCpuPercent)),
                limitedShare);
        }
    }
}

/// <summary>Where the frame time goes (CPU/engine vs GPU) and how long input/output takes, from PresentMon 2 columns.</summary>
public static class BottleneckClassifier
{
    private const string Source = "PresentMon";

    public static BottleneckSummary Classify(IReadOnlyList<FrameSample> frames)
    {
        var withGpu = frames.Where(f => f.GpuBusyMs is not null && f.FrameTimeMs > 0).ToList();
        var share = withGpu.Count == 0
            ? Measured<double>.Missing("The capture has no GPUBusy column (PresentMon 2.x is required)", Source)
            : Measured<double>.Of((double)withGpu.Count(f => f.GpuBusyMs!.Value >= SpikeAttributor.GpuBoundRatio * f.FrameTimeMs) / withGpu.Count,
                Source, $"{withGpu.Count} of {frames.Count} frames");
        return new BottleneckSummary(share, MedianOf(frames, f => f.CpuBusyMs, "CPUBusy"), MedianOf(frames, f => f.GpuBusyMs, "GPUBusy"));
    }

    public static LatencySummary Latency(IReadOnlyList<FrameSample> frames) => new(
        MedianOf(frames, f => f.PcLatencyMs, "PCLatency (needs NVIDIA Reflex in the game)"),
        MedianOf(frames, f => f.ClickToPhotonMs, "ClickToPhotonLatency (needs Reflex latency markers)"),
        MedianOf(frames, f => f.DisplayLatencyMs, "DisplayLatency"));

    private static Measured<double> MedianOf(IReadOnlyList<FrameSample> frames, Func<FrameSample, double?> pick, string column)
    {
        var l = frames.Select(pick).Where(v => v is not null).Select(v => v!.Value).OrderBy(v => v).ToList();
        return l.Count == 0
            ? Measured<double>.Missing($"The capture has no {column} column", Source)
            : Measured<double>.Of(MetricCalculator.Median(l), Source, $"{l.Count} of {frames.Count} frames");
    }
}
