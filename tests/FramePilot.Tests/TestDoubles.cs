using FramePilot.Core;
using FramePilot.Core.Changes;
using FramePilot.Core.Models;
using FramePilot.Optimization;

namespace FramePilot.Tests;

/// <summary>In-memory "registry" used to test the change engine without touching the real system.</summary>
public sealed class MemoryStore
{
    public Dictionary<string, SettingValue> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> FailWritesFor { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SilentlyIgnoreWritesFor { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int Writes { get; set; }
}

public sealed class MemoryTarget : ISettingTarget
{
    private readonly MemoryStore _store;

    public MemoryTarget(MemoryStore store, string key, string description)
    {
        _store = store;
        Key = key;
        Description = description;
    }

    public string Key { get; }
    public string Description { get; }

    public SettingValue Read() => _store.Values.TryGetValue(Key, out var v) ? v : SettingValue.Absent;

    public void Write(SettingValue value)
    {
        if (_store.FailWritesFor.Contains(Key)) throw new UnauthorizedAccessException("simulated failure");
        _store.Writes++;
        if (_store.SilentlyIgnoreWritesFor.Contains(Key)) return;
        if (!value.Exists) _store.Values.Remove(Key);
        else _store.Values[Key] = value;
    }
}

public sealed class MemoryFactory : ITargetFactory, ISettingTargetResolver
{
    private readonly MemoryStore _store;
    public MemoryFactory(MemoryStore store) => _store = store;

    public ISettingTarget Create(string subKey, string valueName, SettingValueKind kind, string description) =>
        new MemoryTarget(_store, $"HKCU\\{subKey}|{valueName}|{kind}", description);

    public ISettingTarget? Resolve(string key) =>
        key.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase) ? new MemoryTarget(_store, key, key) : null;
}

public sealed class MemoryJournal : IChangeJournal
{
    private readonly List<ChangeRecord> _rows = new();
    public int Writes { get; private set; }

    public void Insert(ChangeRecord record)
    {
        Writes++;
        _rows.Add(Clone(record));
    }

    public void Update(ChangeRecord record)
    {
        Writes++;
        var i = _rows.FindIndex(r => r.Id == record.Id);
        if (i < 0) throw new InvalidOperationException("update of unknown record");
        _rows[i] = Clone(record);
    }

    public IReadOnlyList<ChangeRecord> GetAll() => _rows.Select(Clone).ToList();
    public ChangeRecord? Get(Guid id) => _rows.Where(r => r.Id == id).Select(Clone).FirstOrDefault();

    /// <summary>Simulates a crash by forcing a row into a given state.</summary>
    public void ForceState(Guid id, ChangeState state)
    {
        var i = _rows.FindIndex(r => r.Id == id);
        _rows[i].State = state;
    }

    private static ChangeRecord Clone(ChangeRecord r) => new()
    {
        Id = r.Id, BatchId = r.BatchId, ActionId = r.ActionId, ActionVersion = r.ActionVersion, RuleId = r.RuleId,
        TargetKey = r.TargetKey, TargetDescription = r.TargetDescription, Original = r.Original, Planned = r.Planned,
        State = r.State, CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt, Error = r.Error, Note = r.Note
    };
}

public static class Fixtures
{
    public static Measured<T> M<T>(T v) => Measured<T>.Of(v, "test");
    public static Measured<T> Missing<T>() => Measured<T>.Missing("not available in test", "test");

    public static List<FrameSample> Frames(params double[] frameTimes)
    {
        var list = new List<FrameSample>();
        double t = 0;
        foreach (var ft in frameTimes)
        {
            t += ft;
            list.Add(new FrameSample(t, ft, null, null, ft, null, null, null, true));
        }
        return list;
    }

    public static SystemSnapshot Snapshot(
        bool gameModeOn = true,
        int currentHz = 144, int maxHz = 144,
        GpuInfo[]? gpus = null,
        MemoryModule[]? modules = null,
        DiskInfo[]? disks = null,
        DetectedGame[]? games = null,
        BackgroundProcessSample[]? background = null,
        Guid? powerScheme = null,
        Measured<bool>? windowedOpt = null)
    {
        gpus ??= new[]
        {
            new GpuInfo(M("NVIDIA GeForce RTX 4070"), GpuVendor.Nvidia, M("32.0.15.6094"), M(DateTime.Now.AddDays(-30)), M(12L << 30), M(false), "PCI\\VEN_10DE&DEV_2786")
        };
        modules ??= new[]
        {
            new MemoryModule(M(16L << 30), M(6000), M(6000), M("Vendor"), M("PN"), M("DIMM A1")),
            new MemoryModule(M(16L << 30), M(6000), M(6000), M("Vendor"), M("PN"), M("DIMM B1"))
        };
        disks ??= new[]
        {
            new DiskInfo(0, M("NVMe SSD"), DiskMediaType.Ssd, M("NVMe"), M("Healthy"), M(1_000_000_000_000L),
                new[] { new VolumeInfo("C:", M(1_000_000_000_000L), M(500_000_000_000L), M("NTFS")) })
        };
        games ??= new[]
        {
            new DetectedGame(GameId.Cs2, true, @"C:\Steam\steamapps\common\Counter-Strike Global Offensive",
                @"C:\Steam\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe", "cs2", "test", M("Steam build 1"), Missing<string>(), false),
            new DetectedGame(GameId.Valorant, false, null, null, "VALORANT-Win64-Shipping", "test", Missing<string>(), Missing<string>(), false)
        };
        return new SystemSnapshot(SystemSnapshot.CurrentSchema, Guid.NewGuid(), DateTimeOffset.Now, false,
            new OsInfo(M("Windows 11 Pro"), M("25H2"), M(26200), M(1), true, M("Professional")),
            new CpuInfo(M("Test CPU"), M("Test"), M(8), M(16), M(5000), M(5.0)),
            gpus,
            new MemoryInfo(M(32L << 30), M(20L << 30), modules, M(4)),
            disks,
            new[] { new DisplayInfo(@"\\.\DISPLAY1", M("Adapter"), true, M(2560), M(1440), M(currentHz), M(maxHz), M(maxHz)) },
            new WindowsGamingSettings(M(gameModeOn), M(true), windowedOpt ?? Missing<bool>(),
                M(new PowerSchemeInfo(powerScheme ?? new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"), "Balanced")), M(true), M(false)),
            new BackgroundObservation(5, M(4.0), background ?? Array.Empty<BackgroundProcessSample>()),
            games,
            Array.Empty<string>());
    }

    public static SessionRecord Session(GameId game, RunGroup group, IReadOnlyList<FrameSample> frames, TestConditions? cond = null, bool demo = false) =>
        new(Guid.NewGuid(), game, "cs2", DateTimeOffset.Now, 60, group,
            cond ?? new TestConditions("1", "drv", 2560, 1440, 144, null, null), "test", "MsBetweenPresents",
            Array.Empty<string>(), FramePilot.Diagnostics.MetricCalculator.Compute(frames), null, demo, Array.Empty<string>());
}
