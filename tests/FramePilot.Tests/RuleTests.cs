using FramePilot.Core.Changes;
using FramePilot.Core.Models;
using FramePilot.Rules;
using static FramePilot.Tests.Fixtures;

namespace FramePilot.Tests;

public class RuleTests
{
    private static IReadOnlyList<Recommendation> Eval(SystemSnapshot s, GameId? game = GameId.Cs2, SessionRecord? session = null,
        PerformanceGoal goal = PerformanceGoal.StableFps) =>
        RuleEngine.Evaluate(RuleCatalog.All, new RuleContext(s, game, session, goal));

    private static bool Has(IEnumerable<Recommendation> r, string id) => r.Any(x => x.RuleId == id);

    [Fact]
    public void EveryRule_HasCompleteMetadata()
    {
        foreach (var rule in RuleCatalog.All)
        {
            var m = rule.Meta;
            Assert.False(string.IsNullOrWhiteSpace(m.Id));
            Assert.True(m.Version >= 1);
            Assert.False(string.IsNullOrWhiteSpace(m.RequiredEvidence));
            Assert.False(string.IsNullOrWhiteSpace(m.Limitations.Ru));
            Assert.False(string.IsNullOrWhiteSpace(m.Limitations.En));
            Assert.True(m.LastValidatedAt.Year >= 2026);
            if (m.Mode == RecommendationKind.AutoApplicable)
                Assert.Contains(m.ActionId, new[] { ActionIds.EnableGameMode, ActionIds.EnableWindowedOptimizations, ActionIds.SetHighPerformanceGpu });
            else
                Assert.Null(m.ActionId);
        }
        Assert.Equal(RuleCatalog.All.Count, RuleCatalog.All.Select(r => r.Meta.Id).Distinct().Count());
    }

    [Fact]
    public void HealthySystem_ProducesNoSystemProblems()
    {
        var r = Eval(Fixtures.Snapshot(), GameId.Cs2);
        Assert.False(Has(r, "display.refresh-below-max"));
        Assert.False(Has(r, "windows.game-mode-off"));
        Assert.False(Has(r, "memory.single-module"));
        Assert.False(Has(r, "storage.low-free-space"));
    }

    [Fact]
    public void RefreshBelowMax_IsUserAction_WithHighConfidence()
    {
        var r = Eval(Fixtures.Snapshot(currentHz: 60, maxHz: 165)).Single(x => x.RuleId == "display.refresh-below-max");
        Assert.Equal(RecommendationKind.UserAction, r.Kind);
        Assert.Equal(Confidence.High, r.Confidence);
        Assert.Null(r.ActionId); // display modes are never changed automatically
    }

    [Fact]
    public void GameModeOff_IsAutoApplicable()
    {
        var r = Eval(Fixtures.Snapshot(gameModeOn: false)).Single(x => x.RuleId == "windows.game-mode-off");
        Assert.Equal(RecommendationKind.AutoApplicable, r.Kind);
        Assert.Equal(ActionIds.EnableGameMode, r.ActionId);
    }

    [Fact]
    public void WindowedOptimization_RequiresSessionEvidence()
    {
        Assert.False(Has(Eval(Fixtures.Snapshot()), "windows.windowed-optimizations"));

        var frames = Enumerable.Range(0, 300).Select(i => new FrameSample(i * 5, 5, 3, 2, 5, null, null, "Composed: Copy with GPU GDI", true)).ToList();
        var session = Fixtures.Session(GameId.Cs2, RunGroup.Baseline, frames);
        Assert.True(Has(Eval(Fixtures.Snapshot(), GameId.Cs2, session), "windows.windowed-optimizations"));

        var alreadyOn = Fixtures.Snapshot(windowedOpt: M(true));
        Assert.False(Has(Eval(alreadyOn, GameId.Cs2, session), "windows.windowed-optimizations"));
    }

    [Fact]
    public void HybridGpu_PassesOnlyTheGameExecutable()
    {
        var gpus = new[]
        {
            new GpuInfo(M("Intel(R) UHD Graphics 770"), GpuVendor.Intel, M("31"), M(DateTime.Now), Missing<long>(), M(true), "PCI\\VEN_8086"),
            new GpuInfo(M("NVIDIA GeForce RTX 4070"), GpuVendor.Nvidia, M("32"), M(DateTime.Now), M(12L << 30), M(false), "PCI\\VEN_10DE")
        };
        var r = Eval(Fixtures.Snapshot(gpus: gpus)).Single(x => x.RuleId == "gpu.app-preference-high-performance");
        Assert.EndsWith("cs2.exe", r.ActionParameters![ActionIds.ParamExecutablePath]);
    }

    [Fact]
    public void PowerSaver_IsDetected()
    {
        Assert.True(Has(Eval(Fixtures.Snapshot(powerScheme: PowerSaverRule.PowerSaver)), "power.power-saver-plan"));
    }

    [Fact]
    public void SingleModule_IsDiagnosticOnly()
    {
        var mods = new[] { new MemoryModule(M(16L << 30), M(6000), M(6000), M("V"), M("P"), M("A1")) };
        var r = Eval(Fixtures.Snapshot(modules: mods)).Single(x => x.RuleId == "memory.single-module");
        Assert.Equal(RecommendationKind.DiagnosticOnly, r.Kind);
    }

    [Fact]
    public void MemorySpeed_IsLowConfidenceManualCheck()
    {
        var mods = new[]
        {
            new MemoryModule(M(16L << 30), M(6000), M(4800), M("V"), M("P"), M("A1")),
            new MemoryModule(M(16L << 30), M(6000), M(4800), M("V"), M("P"), M("B1"))
        };
        var r = Eval(Fixtures.Snapshot(modules: mods)).Single(x => x.RuleId == "memory.configured-below-rated");
        Assert.Equal(Confidence.Low, r.Confidence);
        Assert.Equal(RecommendationKind.UserAction, r.Kind);
    }

    [Fact]
    public void UnavailableMemorySpeed_ProducesNoGuess()
    {
        var mods = new[]
        {
            new MemoryModule(M(16L << 30), Missing<int>(), Missing<int>(), M("V"), M("P"), M("A1")),
            new MemoryModule(M(16L << 30), Missing<int>(), Missing<int>(), M("V"), M("P"), M("B1"))
        };
        Assert.False(Has(Eval(Fixtures.Snapshot(modules: mods)), "memory.configured-below-rated"));
    }

    [Fact]
    public void GameOnHdd_IsDetectedViaVolumeMapping()
    {
        var disks = new[]
        {
            new DiskInfo(0, M("SSD"), DiskMediaType.Ssd, M("NVMe"), M("Healthy"), M(1L << 40), new[] { new VolumeInfo("D:", M(1L << 40), M(1L << 39), M("NTFS")) }),
            new DiskInfo(1, M("HDD"), DiskMediaType.Hdd, M("SATA"), M("Healthy"), M(2L << 40), new[] { new VolumeInfo("C:", M(2L << 40), M(1L << 40), M("NTFS")) })
        };
        Assert.True(Has(Eval(Fixtures.Snapshot(disks: disks)), "storage.game-on-hdd"));
    }

    [Fact]
    public void BackgroundProcesses_AreReported_ButGamesIgnored()
    {
        var bg = new[]
        {
            new BackgroundProcessSample("cs2", 1, 60, 1),
            new BackgroundProcessSample("SomeUpdater", 2, 12, 1)
        };
        var r = Eval(Fixtures.Snapshot(background: bg)).Single(x => x.RuleId == "background.cpu-heavy-processes");
        Assert.DoesNotContain(r.Evidence, e => e.Value.StartsWith("cs2"));
        Assert.Equal(RecommendationKind.UserAction, r.Kind);
    }

    [Fact]
    public void Reflex_OnlyForNvidia_AndNeverAutoApplied()
    {
        var r = Eval(Fixtures.Snapshot()).Single(x => x.RuleId == "game.nvidia-reflex");
        Assert.Equal(RecommendationKind.UserAction, r.Kind);
        Assert.Contains("Boost", r.Action.En);

        var amd = new[] { new GpuInfo(M("AMD Radeon RX 7800 XT"), GpuVendor.Amd, M("1"), M(DateTime.Now), M(16L << 30), M(false), "PCI\\VEN_1002") };
        Assert.False(Has(Eval(Fixtures.Snapshot(gpus: amd)), "game.nvidia-reflex"));
    }

    [Fact]
    public void ValorantRules_OnlyForValorant()
    {
        Assert.False(Has(Eval(Fixtures.Snapshot(), GameId.Cs2), "valorant.multithreaded-rendering"));
        var r = Eval(Fixtures.Snapshot(), GameId.Valorant);
        Assert.True(Has(r, "valorant.multithreaded-rendering"));
        var sec = r.Single(x => x.RuleId == "valorant.security-features-info");
        Assert.Equal(RecommendationKind.DiagnosticOnly, sec.Kind);
        Assert.Null(sec.ActionId);
    }

    [Fact]
    public void GpuLimited_And_CpuLimited_AreMutuallyExclusive()
    {
        var gpuBound = Enumerable.Range(0, 400).Select(i => new FrameSample(0, 8 + (i % 7) * 0.6, 7.9 + (i % 7) * 0.6, 3, 8, null, null, null, true)).ToList();
        var r1 = Eval(Fixtures.Snapshot(), GameId.Cs2, Fixtures.Session(GameId.Cs2, RunGroup.Baseline, gpuBound));
        Assert.True(Has(r1, "session.gpu-limited"));
        Assert.False(Has(r1, "session.cpu-or-engine-limited"));

        var cpuBound = Enumerable.Range(0, 400).Select(i => new FrameSample(0, 8 + (i % 7) * 0.6, 3, 7, 8, null, null, null, true)).ToList();
        var r2 = Eval(Fixtures.Snapshot(), GameId.Cs2, Fixtures.Session(GameId.Cs2, RunGroup.Baseline, cpuBound));
        Assert.False(Has(r2, "session.gpu-limited"));
        Assert.True(Has(r2, "session.cpu-or-engine-limited"));
    }

    [Fact]
    public void FrameCap_IsOnlyAFinding_ForLatencyGoal()
    {
        // Flat 6.94 ms ≈ 144 FPS at a 144 Hz monitor.
        var capped = Fixtures.Session(GameId.Cs2, RunGroup.Baseline, Frames(Enumerable.Repeat(6.944, 500).ToArray()));
        Assert.False(Has(Eval(Fixtures.Snapshot(), GameId.Cs2, capped, PerformanceGoal.StableFps), "session.frame-rate-capped"));
        Assert.True(Has(Eval(Fixtures.Snapshot(), GameId.Cs2, capped, PerformanceGoal.MinimalLatency), "session.frame-rate-capped"));
    }

    [Fact]
    public void Spikes_AreDetected()
    {
        var times = Enumerable.Range(0, 1000).Select(i => i % 40 == 0 ? 30.0 : 5.0 + (i % 3) * 0.2).ToArray();
        Assert.True(Has(Eval(Fixtures.Snapshot(), GameId.Cs2, Fixtures.Session(GameId.Cs2, RunGroup.Baseline, Frames(times))), "session.frame-time-spikes"));
    }

    [Fact]
    public void DemoSession_IsIgnored_ForRealSnapshot()
    {
        var times = Enumerable.Range(0, 1000).Select(i => i % 40 == 0 ? 30.0 : 5.0).ToArray();
        var demo = Fixtures.Session(GameId.Cs2, RunGroup.Baseline, Frames(times), demo: true);
        Assert.False(Has(Eval(Fixtures.Snapshot(), GameId.Cs2, demo), "session.frame-time-spikes"));
    }

    [Fact]
    public void NoAutoApplicableRule_TouchesSecurityFeatures()
    {
        var auto = RuleCatalog.All.Where(r => r.Meta.Mode == RecommendationKind.AutoApplicable).Select(r => r.Meta.ActionId).ToList();
        Assert.All(auto, id => Assert.DoesNotContain("security", id!, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3, auto.Distinct().Count());
    }
}
