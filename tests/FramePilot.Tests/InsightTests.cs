using FramePilot.Core;
using FramePilot.Core.Models;
using FramePilot.Diagnostics;
using FramePilot.Sensors;
using FramePilot.Telemetry;

namespace FramePilot.Tests;

/// <summary>Spike attribution, bottleneck and latency (spec 2026-11-04 §3.2–3.3).</summary>
public class SpikeAttributorTests
{
    private const double GB = 1024.0 * 1024 * 1024;

    /// <summary>2000 frames of 4 ms (8 s) with one 20 ms spike (frame 1000) ending at <c>spikeEndMs</c>.</summary>
    private static List<FrameSample> Session(out double spikeEndMs, double? gpuBusy = null, double? cpuBusy = null)
    {
        var list = new List<FrameSample>();
        double t = 0;
        spikeEndMs = 0;
        for (var i = 0; i < 2000; i++)
        {
            var ft = i == 1000 ? 20.0 : 4.0;
            t += ft;
            list.Add(new FrameSample(t, ft, i == 1000 ? gpuBusy : 3.0, i == 1000 ? cpuBusy : 2.0, ft, null, null, null, true));
            if (i == 1000) spikeEndMs = t;
        }
        return list;
    }

    /// <summary>A calm 10 Hz sensor series over 0–8.1 s; <paramref name="at"/> modifies the 3 samples nearest the spike.</summary>
    private static List<SensorSample> Sensors(double spikeEndMs, Func<SensorSample, SensorSample>? at = null)
    {
        var list = new List<SensorSample>();
        for (var t = 0.0; t <= 8100; t += 100)
        {
            var s = new SensorSample(t, 100, 100, 20, 3, "explorer", 1, 80, 5 * GB, 6 * GB, 8 * GB, 2, 65, 180, 0);
            if (at is not null && Math.Abs(t - spikeEndMs) <= 150) s = at(s);
            list.Add(s);
        }
        return list;
    }

    private static SpikeCause CauseOf(Func<SensorSample, SensorSample>? at, double? gpuBusy = null, double? cpuBusy = null)
    {
        var frames = Session(out var end, gpuBusy, cpuBusy);
        var r = SpikeAttributor.Analyze(frames, Sensors(end, at), Array.Empty<double>());
        return Assert.Single(r.Spikes).Cause;
    }

    [Fact]
    public void VramFull_RequiresBudgetAndPaging() =>
        Assert.Equal(SpikeCause.VramFull, CauseOf(s => s with { GameVramBytes = 7.9 * GB, PageReadsPerSec = 900 }));

    [Fact]
    public void VramFull_WithoutPaging_IsNotClaimed() =>
        Assert.NotEqual(SpikeCause.VramFull, CauseOf(s => s with { GameVramBytes = 7.9 * GB }));

    [Fact]
    public void Paging_Alone() => Assert.Equal(SpikeCause.Paging, CauseOf(s => s with { PageReadsPerSec = 400 }));

    [Fact]
    public void Paging_BelowFloor_IsIgnored() => Assert.NotEqual(SpikeCause.Paging, CauseOf(s => s with { PageReadsPerSec = 40 }));

    [Fact]
    public void CpuThrottle_ByPerformanceLimit() => Assert.Equal(SpikeCause.CpuThrottle, CauseOf(s => s with { CpuPerformanceLimitPercent = 70 }));

    [Fact]
    public void CpuThrottle_ByFrequencyDrop() => Assert.Equal(SpikeCause.CpuThrottle, CauseOf(s => s with { CpuPerformancePercent = 71 }));

    [Fact]
    public void CpuFrequencyDrop_OfTenPercent_IsNotThrottle() => Assert.NotEqual(SpikeCause.CpuThrottle, CauseOf(s => s with { CpuPerformancePercent = 90 }));

    [Fact]
    public void GpuLimit_HardwareSlowdown() => Assert.Equal(SpikeCause.GpuLimit, CauseOf(s => s with { GpuClockReasons = 0x40 }));

    [Fact]
    public void GpuPowerCap_ActiveAllSession_IsNotNotable()
    {
        var frames = Session(out var end);
        var sensors = Sensors(end).Select(s => s with { GpuClockReasons = 0x4 }).ToList();
        var r = SpikeAttributor.Analyze(frames, sensors, Array.Empty<double>());
        Assert.NotEqual(SpikeCause.GpuLimit, Assert.Single(r.Spikes).Cause);
    }

    [Fact]
    public void BackgroundProcess_NamesTheBusiestProcess()
    {
        var frames = Session(out var end);
        var r = SpikeAttributor.Analyze(frames, Sensors(end, s => s with { OtherCpuPercent = 41, TopOtherProcess = "OneDrive", TopOtherCpuPercent = 38 }), Array.Empty<double>());
        var spike = Assert.Single(r.Spikes);
        Assert.Equal(SpikeCause.BackgroundProcess, spike.Cause);
        Assert.Equal("OneDrive", spike.ProcessName);
        Assert.Equal(38, spike.Value);
    }

    [Fact]
    public void NoSensorMatch_FallsBackToFrameSide()
    {
        Assert.Equal(SpikeCause.GpuSideFrame, CauseOf(null, gpuBusy: 19, cpuBusy: 4));
        Assert.Equal(SpikeCause.CpuSideFrame, CauseOf(null, gpuBusy: 4, cpuBusy: 18));
    }

    [Fact]
    public void NothingKnown_IsNoMatch_NotAGuess()
    {
        var frames = Fixtures.Frames(Enumerable.Repeat(4.0, 100).Append(20.0).Concat(Enumerable.Repeat(4.0, 100)).ToArray());
        var r = SpikeAttributor.Analyze(frames, Array.Empty<SensorSample>(), Array.Empty<double>());
        Assert.Equal(SpikeCause.NoMatch, Assert.Single(r.Spikes).Cause);
        Assert.Contains("vram", r.UnavailableSensors);
        Assert.Contains("gpu-temperature", r.UnavailableSensors);
    }

    [Fact]
    public void SpikeDefinition_MatchesMetricCalculator()
    {
        var frames = Session(out _);
        var stats = MetricCalculator.Compute(frames);
        Assert.Equal(stats.SpikeCount, SpikeAttributor.Analyze(frames, Array.Empty<SensorSample>(), Array.Empty<double>()).Spikes.Count);
    }

    [Fact]
    public void Marker_IsMatchedToNearestSpikeWithinOneSecond()
    {
        var frames = Session(out var end);
        var r = SpikeAttributor.Analyze(frames, Array.Empty<SensorSample>(), new[] { end + 300, 50.0 });
        Assert.Equal(2, r.Markers.Count);
        Assert.Null(r.Markers[0].NearestSpikeTimeMs);           // 50 ms: no spike within ±1 s (spike at ~4 s)
        Assert.Equal(end, r.Markers[1].NearestSpikeTimeMs);
    }

    [Fact]
    public void Bottleneck_ShareOfGpuBoundFrames()
    {
        var frames = new List<FrameSample>();
        double t = 0;
        for (var i = 0; i < 10; i++)
        {
            t += 10;
            frames.Add(new FrameSample(t, 10, i < 7 ? 9.5 : 5, 4, 10, null, null, null, true, 22));
        }
        var b = BottleneckClassifier.Classify(frames);
        Assert.True(b.GpuBoundShare.IsAvailable);
        Assert.Equal(0.7, b.GpuBoundShare.Value, 6);
        var l = BottleneckClassifier.Latency(frames);
        Assert.False(l.PcLatencyMs.IsAvailable);
        Assert.Equal(22, l.DisplayLatencyMs.Value);
    }

    [Fact]
    public void Bottleneck_WithoutPresentMon2Columns_IsUnavailable() =>
        Assert.False(BottleneckClassifier.Classify(Fixtures.Frames(4, 4, 4)).GpuBoundShare.IsAvailable);
}

public class SensorTests
{
    [Fact]
    public void Csv_RoundTrips_IncludingUnavailableAndQuotedNames()
    {
        var samples = new[]
        {
            new SensorSample(100, 98.5, 100, 21.25, 3.5, "Discord, Inc \"beta\"", 2.5, 77, 5e9, 6e9, 8e9, 3, 64, 181.2, 4),
            new SensorSample(200, null, null, null, null, null, null, null, null, null, null, null, null, null, null)
        };
        var sw = new StringWriter();
        SensorCsv.Write(sw, samples);
        var back = SensorCsv.Read(new StringReader(sw.ToString()));
        Assert.Equal(samples, back);
    }

    [Fact]
    public void Alignment_WithQpc_IsExact()
    {
        var frames = Fixtures.Frames(10, 10, 10, 10);                 // ends at 10, 20, 30, 40 ms
        const long f = 10_000_000;                                    // 100 ns ticks
        var q0 = 5_000_000_000L;
        var raw = new[] { new RawSensorSample(q0 + 200_000, Empty()) }; // +20 ms after frame 0
        var (s, m, approx) = SensorAlignment.Align(raw, new[] { q0 + 100_000 }, frames, q0, f);
        Assert.False(approx);
        Assert.Equal(30, Assert.Single(s).TimeMs, 6);
        Assert.Equal(20, Assert.Single(m), 6);
    }

    [Fact]
    public void Alignment_WithoutQpc_AlignsEnds_AndIsFlaggedApproximate()
    {
        var frames = Fixtures.Frames(10, 10, 10, 10);
        const long f = 1000;                                          // 1 ms ticks
        var raw = new[] { new RawSensorSample(500, Empty()), new RawSensorSample(520, Empty()) };
        var (s, _, approx) = SensorAlignment.Align(raw, Array.Empty<long>(), frames, null, f);
        Assert.True(approx);
        Assert.Equal(new[] { 20.0, 40.0 }, s.Select(x => x.TimeMs));
    }

    [Fact]
    public void ProcessCpu_SplitsGameOthersAndTop_V2()
    {
        var rows = new List<(string, double)> { ("_Total", 800), ("Idle:0", 500), ("cs2:4242", 400), ("OneDrive:77", 160), ("Discord:9", 40), ("Discord:10", 40) };
        var (game, other, top, topValue) = SensorSampler.ProcessCpu(rows, v2: true, "cs2", 4242, cores: 8);
        Assert.Equal(50, game);
        Assert.Equal(30, other);
        Assert.Equal("OneDrive", top);
        Assert.Equal(20, topValue);
    }

    [Fact]
    public void ProcessCpu_ClassicInstances_MatchByName()
    {
        var rows = new List<(string, double)> { ("cs2", 200), ("chrome", 100), ("chrome#1", 100) };
        var (game, other, top, _) = SensorSampler.ProcessCpu(rows, v2: false, "cs2", null, cores: 4);
        Assert.Equal(50, game);
        Assert.Equal(50, other);
        Assert.Equal("chrome", top);
    }

    private static SensorSample Empty() => new(0, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
}

public class PresentMon2ColumnTests
{
    [Fact]
    public void Parser_ReadsDisplayLatencyAndQpc()
    {
        const string csv = "Application,SwapChainAddress,CPUStartQPC,MsBetweenPresents,MsGPUBusy,MsCPUBusy,DisplayLatency\n" +
                           "cs2.exe,0x1,1000000,4.0,3.5,2.0,21.5\n" +
                           "cs2.exe,0x1,1040000,4.0,3.6,2.1,22.5\n";
        var p = PresentMonCsv.Parse(new StringReader(csv), "cs2");
        Assert.Equal(1000000, p.FirstFrameQpc);
        Assert.Equal(21.5, p.Frames[0].DisplayLatencyMs);
        Assert.Equal(3.6, p.Frames[1].GpuBusyMs);
    }

    [Fact]
    public void OldSessionJson_WithoutNewFields_StillLoads()
    {
        var s = new SessionRecord(Guid.NewGuid(), GameId.Cs2, "cs2", DateTimeOffset.Now, 60, RunGroup.Baseline,
            new TestConditions(null, null, null, null, null, null, null), "PresentMon", "MsBetweenPresents", new[] { "MsBetweenPresents" },
            MetricCalculator.Compute(Fixtures.Frames(4, 4, 9)), null, false, Array.Empty<string>());
        var json = Json.Serialize(s);
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        node.Remove("sensorsCsvPath");
        node.Remove("insights");
        var back = Json.Deserialize<SessionRecord>(node.ToJsonString())!;
        Assert.Null(back.Insights);
        Assert.Null(back.SensorsCsvPath);
        Assert.Equal(s.Stats.SpikeCount, back.Stats.SpikeCount);
    }
}

public class AutoCaptureWatcherTests
{
    [Fact]
    public void FiresOncePerLaunch_AfterDelay()
    {
        int? pid = 100;
        var w = new FramePilot.GameAdapters.AutoCaptureWatcher(g => g == GameId.Cs2 ? pid : null);
        var t0 = DateTimeOffset.Parse("2026-11-04T10:00:00Z");
        var delay = TimeSpan.FromSeconds(120);
        Assert.Null(w.Poll(t0, delay, enabled: true, busy: false));
        Assert.Null(w.Poll(t0.AddSeconds(119), delay, true, false));
        Assert.Equal(GameId.Cs2, w.Poll(t0.AddSeconds(121), delay, true, false));
        Assert.Null(w.Poll(t0.AddSeconds(500), delay, true, false));          // same launch: never again
        pid = 200;                                                          // the game was restarted
        Assert.Null(w.Poll(t0.AddSeconds(501), delay, true, false));
        Assert.Equal(GameId.Cs2, w.Poll(t0.AddSeconds(622), delay, true, false));
    }

    [Fact]
    public void Disabled_OrBusy_DoesNotFire_ButKeepsTheLaunchTime()
    {
        var w = new FramePilot.GameAdapters.AutoCaptureWatcher(g => g == GameId.Valorant ? 7 : null);
        var t0 = DateTimeOffset.Parse("2026-11-04T10:00:00Z");
        var delay = TimeSpan.FromSeconds(60);
        Assert.Null(w.Poll(t0, delay, enabled: false, busy: false));
        Assert.Null(w.Poll(t0.AddSeconds(90), delay, enabled: true, busy: true));
        Assert.Equal(GameId.Valorant, w.Poll(t0.AddSeconds(95), delay, enabled: true, busy: false));
    }
}

public class ReportPrivacyTests
{
    [Fact]
    public void ProcessNames_AreRemovedFromExport_ByDefault()
    {
        var frames = Fixtures.Frames(4, 4, 4, 20, 4, 4);
        var insights = new SessionInsights(new[] { new SpikeAttribution(36, 20, SpikeCause.BackgroundProcess, 38, 3, "MyPrivateApp") },
            Array.Empty<LagMarker>(), BottleneckClassifier.Classify(frames), BottleneckClassifier.Latency(frames), Array.Empty<string>(), false);
        var s = new SessionRecord(Guid.NewGuid(), GameId.Cs2, "cs2", DateTimeOffset.Now, 60, RunGroup.Baseline,
            new TestConditions(null, null, null, null, null, null, null), "PresentMon", "MsBetweenPresents", new[] { "MsBetweenPresents" },
            MetricCalculator.Compute(frames), null, false, Array.Empty<string>(), null, insights);
        var report = FramePilot.Reports.ReportBuilder.Build("1.1.0", "ru", null, new[] { s }, Array.Empty<Recommendation>(), null,
            Array.Empty<FramePilot.Core.Changes.ChangeRecord>());
        var json = FramePilot.Reports.ReportBuilder.ToJson(report);
        Assert.DoesNotContain("MyPrivateApp", json);
        Assert.Contains("backgroundProcess", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("framepilot.report/1", report.Schema);
    }
}
