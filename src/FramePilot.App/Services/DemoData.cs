using FramePilot.Core;
using FramePilot.Core.Models;
using FramePilot.Diagnostics;

namespace FramePilot.App.Services;

/// <summary>
/// Clearly marked demonstration data for previewing the interface. Every object it produces has IsDemo = true,
/// is stored separately from real measurements and is labelled "DEMO DATA" in the UI and in reports.
/// </summary>
public static class DemoData
{
    private const string Src = "DEMO DATA";

    private static Measured<T> D<T>(T v) => Measured<T>.Of(v, Src);

    public static SystemSnapshot Snapshot()
    {
        var gpu = new GpuInfo(D("Demo GeForce GPU"), GpuVendor.Nvidia, D("32.0.15.0000"), D(DateTime.Now.AddDays(-40)),
            D(12L * 1024 * 1024 * 1024), D(false), "PCI\\VEN_10DE&DEV_0000");
        var igpu = new GpuInfo(D("Demo Integrated Graphics"), GpuVendor.Intel, D("31.0.101.0000"), D(DateTime.Now.AddDays(-200)),
            Measured<long>.Missing("Shared memory", Src), D(true), "PCI\\VEN_8086&DEV_0000");
        var module = new MemoryModule(D(16L * 1024 * 1024 * 1024), D(6000), D(4800), D("Demo"), D("DEMO-16G"), D("DIMM 1"));
        return new SystemSnapshot(SystemSnapshot.CurrentSchema, Guid.NewGuid(), DateTimeOffset.Now, true,
            new OsInfo(D("Windows 11 Pro (demo)"), D("25H2"), D(26200), D(1000), true, D("Professional")),
            new CpuInfo(D("Demo 8-Core Processor"), D("Demo"), D(8), D(16), D(4500), D(7.5)),
            new[] { gpu, igpu },
            new MemoryInfo(D(32L * 1024 * 1024 * 1024), D(20L * 1024 * 1024 * 1024), new[] { module, module }, D(4)),
            new[]
            {
                new DiskInfo(0, D("Demo NVMe SSD"), DiskMediaType.Ssd, D("NVMe"), D("Healthy"), D(1000L * 1000 * 1000 * 1000),
                    new[] { new VolumeInfo("C:", D(1000L * 1000 * 1000 * 1000), D(80L * 1000 * 1000 * 1000), D("NTFS")) }),
                new DiskInfo(1, D("Demo HDD"), DiskMediaType.Hdd, D("SATA"), D("Healthy"), D(2000L * 1000 * 1000 * 1000),
                    new[] { new VolumeInfo("D:", D(2000L * 1000 * 1000 * 1000), D(900L * 1000 * 1000 * 1000), D("NTFS")) })
            },
            new[] { new DisplayInfo(@"\\.\DISPLAY1", D("Demo display adapter"), true, D(2560), D(1440), D(60), D(165), D(165)) },
            new WindowsGamingSettings(D(false), D(true), Measured<bool>.Missing("Not set explicitly", Src),
                D(new PowerSchemeInfo(new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"), "Balanced")), D(true), D(true)),
            new BackgroundObservation(5, D(12.0), new[]
            {
                new BackgroundProcessSample("DemoUpdater", 1000, 9.5, 300L * 1024 * 1024),
                new BackgroundProcessSample("DemoBrowser", 1001, 3.1, 900L * 1024 * 1024)
            }),
            new[]
            {
                new DetectedGame(GameId.Cs2, true, @"D:\Demo\Counter-Strike Global Offensive", @"D:\Demo\Counter-Strike Global Offensive\game\bin\win64\cs2.exe",
                    "cs2", "DEMO DATA", D("Steam build 0000000"), D("setting.fullscreen=0 (demo)"), false),
                new DetectedGame(GameId.Valorant, false, null, null, "VALORANT-Win64-Shipping", "DEMO DATA",
                    Measured<string>.Missing("Not installed (demo)", Src), Measured<string>.Missing("Not installed (demo)", Src), false)
            },
            Array.Empty<string>());
    }

    /// <summary>Deterministic synthetic frame series for a demo session.</summary>
    public static List<FrameSample> Frames(int seed, double baseMs, double jitter, double spikeChance)
    {
        var rnd = new Random(seed);
        var list = new List<FrameSample>();
        double t = 0;
        for (var i = 0; i < 12000; i++)
        {
            var ft = baseMs + (rnd.NextDouble() - 0.5) * jitter;
            if (rnd.NextDouble() < spikeChance) ft *= 2.5 + rnd.NextDouble() * 2;
            t += ft;
            list.Add(new FrameSample(t, ft, ft * 0.72, ft * 0.85, ft, null, null, "Composed: Copy with GPU GDI", true));
        }
        return list;
    }

    public static IReadOnlyList<SessionRecord> Sessions()
    {
        var list = new List<SessionRecord>();
        var cond = new TestConditions("Steam build 0000000", "32.0.15.0000", 2560, 1440, 60, "Demo: Dust II, same route", null);
        for (var i = 0; i < 3; i++)
        {
            var frames = Frames(100 + i, 4.2, 0.9, 0.006);
            list.Add(Make(100 + i, frames, RunGroup.Baseline, cond, DateTimeOffset.Now.AddMinutes(-60 + i * 5)));
        }
        for (var i = 0; i < 3; i++)
        {
            var frames = Frames(200 + i, 4.0, 0.7, 0.002);
            list.Add(Make(200 + i, frames, RunGroup.After, cond, DateTimeOffset.Now.AddMinutes(-20 + i * 5)));
        }
        return list;
    }

    private static SessionRecord Make(int seed, List<FrameSample> frames, RunGroup group, TestConditions cond, DateTimeOffset at) =>
        new(DeterministicGuid(seed), GameId.Cs2, "cs2", at, 60, group, cond, "DEMO DATA (synthetic)", "MsBetweenPresents",
            new[] { "Application", "MsBetweenPresents", "MsGPUBusy", "MsCPUBusy", "PresentMode" },
            MetricCalculator.Compute(frames), null, true, new[] { "Synthetic demonstration data" });

    public static Guid DeterministicGuid(int seed)
    {
        var b = new byte[16];
        new Random(seed).NextBytes(b);
        return new Guid(b);
    }

    public static List<FrameSample> FramesForSession(Guid id)
    {
        var seed = SeedOf(id);
        return seed < 200 ? Frames(seed, 4.2, 0.9, 0.006) : Frames(seed, 4.0, 0.7, 0.002);
    }

    public static int SeedOf(Guid id)
    {
        for (var s = 100; s < 300; s++)
            if (DeterministicGuid(s) == id) return s;
        return 100;
    }
}
