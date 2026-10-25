using FramePilot.Core.Models;
using FramePilot.Diagnostics;
using static FramePilot.Tests.Fixtures;

namespace FramePilot.Tests;

public class MetricTests
{
    [Fact]
    public void AverageFps_IsTimeWeighted_NotMeanOfInstantFps()
    {
        // 1 frame of 10 ms and 1 frame of 30 ms: 2 frames in 40 ms = 50 FPS (mean of instant FPS would be 66.7).
        var s = MetricCalculator.Compute(Frames(10, 30));
        Assert.Equal(50.0, s.AverageFps, 6);
    }

    [Fact]
    public void P99_UsesNearestRank()
    {
        var times = Enumerable.Range(1, 100).Select(i => (double)i).ToArray();
        var s = MetricCalculator.Compute(Frames(times));
        Assert.Equal(99.0, s.P99FrameTimeMs, 6); // rank ceil(0.99*100)=99 → value 99
    }

    [Fact]
    public void OnePercentLow_IsMeanOfSlowestOnePercent_NotInverseP99()
    {
        var times = Enumerable.Repeat(10.0, 99).Append(50.0).ToArray();
        var s = MetricCalculator.Compute(Frames(times));
        Assert.Equal(10.0, s.P99FrameTimeMs, 6);
        Assert.Equal(20.0, s.OnePercentLowFps, 6);           // 1000 / 50
        Assert.NotEqual(1000.0 / s.P99FrameTimeMs, s.OnePercentLowFps);
    }

    [Fact]
    public void OnePercentLow_UsesAtLeastOneFrame_AndRoundsUp()
    {
        // 150 frames → k = ceil(1.5) = 2 slowest frames: 40 and 60 → mean 50 → 20 FPS
        var times = Enumerable.Repeat(5.0, 148).Concat(new[] { 40.0, 60.0 }).ToArray();
        var s = MetricCalculator.Compute(Frames(times));
        Assert.Equal(20.0, s.OnePercentLowFps, 6);
    }

    [Fact]
    public void Median_EvenCount_AveragesMiddlePair()
    {
        Assert.Equal(2.5, MetricCalculator.Median(new List<double> { 1, 2, 3, 4 }), 6);
        Assert.Equal(2.0, MetricCalculator.Median(new List<double> { 1, 2, 3 }), 6);
    }

    [Fact]
    public void Spikes_AreFramesLongerThanTwiceMedian()
    {
        var s = MetricCalculator.Compute(Frames(10, 10, 10, 10, 21, 20, 10));
        Assert.Equal(1, s.SpikeCount); // 21 > 20; 20 is not > 20
    }

    [Fact]
    public void Thresholds_CountFramesStrictlyAbove()
    {
        var s = MetricCalculator.Compute(Frames(new[] { 5.0, 10.0, 17.0, 40.0 }), new[] { 16.67, 33.33 });
        Assert.Equal(2, s.Thresholds[0].Count);
        Assert.Equal(1, s.Thresholds[1].Count);
        Assert.Equal(25.0, s.Thresholds[1].Percent, 6);
    }

    [Fact]
    public void InvalidFrames_AreDiscarded()
    {
        var frames = Frames(10, 10, 10).Append(new FrameSample(0, double.NaN, null, null, null, null, null, null, true))
            .Append(new FrameSample(0, -1, null, null, null, null, null, null, true)).ToList();
        var s = MetricCalculator.Compute(frames);
        Assert.Equal(3, s.FrameCount);
    }

    [Fact]
    public void TooFewFrames_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => MetricCalculator.Compute(Frames(10)));
    }

    [Fact]
    public void MissingGpuBusy_IsReportedUnavailable_NotZero()
    {
        var s = MetricCalculator.Compute(Frames(10, 10, 10));
        Assert.False(s.MedianGpuBusyRatio.IsAvailable);
        Assert.NotNull(s.MedianGpuBusyRatio.UnavailableReason);
        Assert.False(s.MedianPcLatencyMs.IsAvailable);
    }

    [Fact]
    public void GpuBusyRatio_IsMedianOfPerFrameRatio()
    {
        var frames = new List<FrameSample>
        {
            new(10, 10, 9, 5, 10, null, null, "Hardware: Independent Flip", true),
            new(20, 10, 9.5, 5, 10, null, null, "Hardware: Independent Flip", true),
            new(30, 10, 5, 5, 10, null, null, "Composed: Flip", true)
        };
        var s = MetricCalculator.Compute(frames);
        Assert.Equal(0.9, s.MedianGpuBusyRatio.Value, 6);
        Assert.Equal("Hardware: Independent Flip", s.DominantPresentMode.Value);
    }
}

public class ComparisonTests
{
    private static SessionRecord Run(RunGroup g, double ft) => Session(GameId.Cs2, g, Frames(Enumerable.Repeat(ft, 200).ToArray()));

    [Fact]
    public void FewerThanThreeRuns_IsInsufficient()
    {
        var r = SessionComparer.Compare(new[] { Run(RunGroup.Baseline, 10) }, new[] { Run(RunGroup.After, 5) });
        Assert.Equal(ComparisonVerdict.InsufficientRuns, r.Overall);
    }

    [Fact]
    public void NonOverlappingRanges_AreImprovement()
    {
        var b = new[] { 10.0, 10.2, 10.4 }.Select(x => Run(RunGroup.Baseline, x)).ToList();
        var a = new[] { 8.0, 8.2, 8.4 }.Select(x => Run(RunGroup.After, x)).ToList();
        var r = SessionComparer.Compare(b, a);
        Assert.Equal(ComparisonVerdict.Improved, r.Overall);
        Assert.Equal(ComparisonVerdict.Improved, r.Metrics.Single(m => m.Metric == "AverageFps").Verdict);
    }

    [Fact]
    public void OverlappingRanges_AreNoise()
    {
        var b = new[] { 10.0, 9.0, 11.0 }.Select(x => Run(RunGroup.Baseline, x)).ToList();
        var a = new[] { 9.5, 10.5, 10.0 }.Select(x => Run(RunGroup.After, x)).ToList();
        var r = SessionComparer.Compare(b, a);
        Assert.Equal(ComparisonVerdict.NoSignificantChange, r.Overall);
        Assert.Contains("разброса", r.Summary.Ru);
    }

    [Fact]
    public void Regression_IsReported()
    {
        var b = new[] { 8.0, 8.1, 8.2 }.Select(x => Run(RunGroup.Baseline, x)).ToList();
        var a = new[] { 10.0, 10.1, 10.2 }.Select(x => Run(RunGroup.After, x)).ToList();
        Assert.Equal(ComparisonVerdict.Regressed, SessionComparer.Compare(b, a).Overall);
    }

    [Fact]
    public void DifferentDriverVersions_MakeComparisonIncomparable()
    {
        var b = new[] { 10.0, 10.1, 10.2 }.Select(x => Run(RunGroup.Baseline, x)).ToList();
        var a = new[] { 8.0, 8.1, 8.2 }.Select(x => Session(GameId.Cs2, RunGroup.After, Frames(Enumerable.Repeat(x, 200).ToArray()),
            new TestConditions("1", "other-driver", 2560, 1440, 144, null, null))).ToList();
        var r = SessionComparer.Compare(b, a);
        Assert.Equal(ComparisonVerdict.IncomparableConditions, r.Overall);
        Assert.NotEmpty(r.Warnings);
    }

    [Fact]
    public void MixingDemoAndReal_ProducesWarning()
    {
        var b = new[] { 10.0, 10.1, 10.2 }.Select(x => Run(RunGroup.Baseline, x)).ToList();
        var a = new[] { 8.0, 8.1, 8.2 }.Select(x => Session(GameId.Cs2, RunGroup.After, Frames(Enumerable.Repeat(x, 200).ToArray()), demo: true)).ToList();
        var r = SessionComparer.Compare(b, a);
        Assert.Equal(ComparisonVerdict.IncomparableConditions, r.Overall);
    }
}
