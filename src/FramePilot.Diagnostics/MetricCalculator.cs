using FramePilot.Core;
using FramePilot.Core.Models;

namespace FramePilot.Diagnostics;

/// <summary>
/// Frame metric definitions (also published in docs/MEASUREMENT_PROTOCOL.md):
/// <list type="bullet">
/// <item>Average FPS = 1000 · N / Σ frameTimeMs (time-weighted, not the mean of per-frame FPS).</item>
/// <item>Percentiles use the nearest-rank method on frame times sorted ascending: rank = ⌈p/100 · N⌉.</item>
/// <item>P99 frame time = nearest-rank 99th percentile of frame time.</item>
/// <item>1% low FPS = 1000 / mean(the slowest k frame times), k = max(1, ⌈0.01 · N⌉). It is NOT 1000 / P99.</item>
/// <item>Spike = frame time greater than 2 × median frame time.</item>
/// </list>
/// Frames with non-finite or non-positive frame time are discarded.
/// </summary>
public static class MetricCalculator
{
    public static readonly double[] DefaultThresholdsMs = { 8.33, 16.67, 33.33, 50.0 };
    public const string Source = "FramePilot.MetricCalculator v1";

    public static double PercentileNearestRank(IReadOnlyList<double> sortedAscending, double percentile)
    {
        if (sortedAscending.Count == 0) throw new ArgumentException("No values.", nameof(sortedAscending));
        if (percentile <= 0) return sortedAscending[0];
        if (percentile >= 100) return sortedAscending[^1];
        var rank = (int)Math.Ceiling(percentile / 100.0 * sortedAscending.Count);
        rank = Math.Clamp(rank, 1, sortedAscending.Count);
        return sortedAscending[rank - 1];
    }

    public static double Median(IReadOnlyList<double> sortedAscending)
    {
        if (sortedAscending.Count == 0) throw new ArgumentException("No values.", nameof(sortedAscending));
        var n = sortedAscending.Count;
        return n % 2 == 1 ? sortedAscending[n / 2] : (sortedAscending[n / 2 - 1] + sortedAscending[n / 2]) / 2.0;
    }

    public static double OnePercentLowFps(IReadOnlyList<double> sortedAscending)
    {
        if (sortedAscending.Count == 0) throw new ArgumentException("No values.", nameof(sortedAscending));
        var k = Math.Max(1, (int)Math.Ceiling(sortedAscending.Count * 0.01));
        double sum = 0;
        for (var i = sortedAscending.Count - k; i < sortedAscending.Count; i++) sum += sortedAscending[i];
        return 1000.0 / (sum / k);
    }

    public static FrameStats Compute(IReadOnlyList<FrameSample> frames, IReadOnlyList<double>? thresholdsMs = null)
    {
        thresholdsMs ??= DefaultThresholdsMs;
        var valid = frames.Where(f => double.IsFinite(f.FrameTimeMs) && f.FrameTimeMs > 0).ToList();
        if (valid.Count < 2)
            throw new InvalidOperationException("At least two valid frames are required to compute statistics.");

        var times = valid.Select(f => f.FrameTimeMs).ToList();
        var sorted = times.OrderBy(t => t).ToList();
        var total = times.Sum();
        var n = times.Count;
        var mean = total / n;
        var variance = times.Sum(t => (t - mean) * (t - mean)) / n;
        var median = Median(sorted);

        var thresholds = thresholdsMs
            .Select(th =>
            {
                var c = times.Count(t => t > th);
                return new ThresholdCount(th, c, 100.0 * c / n);
            })
            .ToList();

        var spikes = times.Count(t => t > 2 * median);

        var gpuRatios = valid.Where(f => f.GpuBusyMs is > 0).Select(f => f.GpuBusyMs!.Value / f.FrameTimeMs).OrderBy(x => x).ToList();
        var cpuRatios = valid.Where(f => f.CpuBusyMs is > 0).Select(f => f.CpuBusyMs!.Value / f.FrameTimeMs).OrderBy(x => x).ToList();
        var pcLat = valid.Where(f => f.PcLatencyMs is > 0).Select(f => f.PcLatencyMs!.Value).OrderBy(x => x).ToList();
        var c2p = valid.Where(f => f.ClickToPhotonMs is > 0).Select(f => f.ClickToPhotonMs!.Value).OrderBy(x => x).ToList();

        Measured<double> MedianOrMissing(List<double> list, string what, double minCoverage)
        {
            if (list.Count == 0) return Measured<double>.Missing($"{what} was not reported by the telemetry source.", Source);
            if (list.Count < n * minCoverage)
                return Measured<double>.Missing($"{what} was reported for only {list.Count} of {n} frames.", Source);
            return Measured<double>.Of(Median(list), Source, $"{list.Count} of {n} frames");
        }

        var modes = valid.Where(f => !string.IsNullOrWhiteSpace(f.PresentMode))
            .GroupBy(f => f.PresentMode!)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        var dominantMode = modes is null
            ? Measured<string>.Missing("Present mode was not reported.", Source)
            : Measured<string>.Of(modes.Key, Source, $"{100.0 * modes.Count() / n:0.#}% of frames");

        var dropped = frames.Count == 0 ? 0 : 100.0 * frames.Count(f => !f.Displayed) / frames.Count;

        return new FrameStats(
            n,
            total,
            1000.0 * n / total,
            median,
            PercentileNearestRank(sorted, 99),
            OnePercentLowFps(sorted),
            Math.Sqrt(variance),
            spikes,
            thresholds,
            MedianOrMissing(gpuRatios, "GPU busy time", 0.5),
            MedianOrMissing(cpuRatios, "CPU busy time", 0.5),
            MedianOrMissing(pcLat, "PC latency", 0.5),
            MedianOrMissing(c2p, "Click-to-photon latency", 0.01),
            dominantMode,
            dropped);
    }
}
