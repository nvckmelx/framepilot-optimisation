using System.Diagnostics;
using System.Globalization;
using System.Text;
using FramePilot.Core.Models;

namespace FramePilot.Sensors;

/// <summary>A sensor reading stamped with the raw QPC time (<see cref="Stopwatch.GetTimestamp"/>), before alignment.</summary>
public sealed record RawSensorSample(long Qpc, SensorSample Values);

/// <summary>
/// Samples system sensors at 10 Hz while a session is recorded. Read-only: performance counters (PDH) and NVML.
/// It never opens the game process, never injects and never changes anything.
/// </summary>
public sealed class SensorSampler : IAsyncDisposable
{
    public const int IntervalMs = 100;

    private const string CpuPerf = @"\Processor Information(_Total)\% Processor Performance";
    private const string CpuLimit = @"\Processor Information(_Total)\% Performance Limit";
    private const string ProcV2 = @"\Process V2(*)\% Processor Time";
    private const string ProcV1 = @"\Process(*)\% Processor Time";
    private const string GpuEngine = @"\GPU Engine(*)\Utilization Percentage";
    private const string GpuProcMem = @"\GPU Process Memory(*)\Dedicated Usage";
    private const string GpuAdapterMem = @"\GPU Adapter Memory(*)\Dedicated Usage";
    private const string PageReads = @"\Memory\Page Reads/sec";

    private readonly string _game;
    private readonly double? _vramBudget;
    private readonly List<RawSensorSample> _samples = new();
    private readonly List<string> _notes = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public SensorSampler(string gameProcessName, double? vramBudgetBytes)
    {
        _game = Path.GetFileNameWithoutExtension(gameProcessName);
        _vramBudget = vramBudgetBytes;
    }

    /// <summary>Why some sensors were not available on this PC (empty when everything worked).</summary>
    public IReadOnlyList<string> Notes => _notes;

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Factory.StartNew(() => Run(token), token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public async Task<IReadOnlyList<RawSensorSample>> StopAsync()
    {
        _cts?.Cancel();
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        lock (_samples) return _samples.ToList();
    }

    private void Run(CancellationToken ct)
    {
        PdhQuery? pdh = null;
        Nvml? nvml = null;
        try
        {
            try { pdh = new PdhQuery(); } catch (Exception ex) { _notes.Add("PDH: " + ex.Message); }
            var procCounter = ProcV1;
            if (pdh is not null)
            {
                foreach (var c in new[] { CpuPerf, CpuLimit, GpuEngine, GpuProcMem, GpuAdapterMem, PageReads })
                    if (!pdh.TryAdd(c)) _notes.Add("counter not available: " + c);
                procCounter = pdh.TryAdd(ProcV2) ? ProcV2 : ProcV1;
                if (procCounter == ProcV1 && !pdh.TryAdd(ProcV1)) _notes.Add("counter not available: " + ProcV1);
                pdh.Collect(); // rate counters need two collections
            }
            nvml = Nvml.TryOpen(out var nvmlReason);
            if (nvml is null && nvmlReason is not null) _notes.Add(nvmlReason);

            var cores = Math.Max(1, Environment.ProcessorCount);
            int? pid = null;
            var lastPidLookup = 0L;
            var next = Stopwatch.GetTimestamp();
            var interval = Stopwatch.Frequency * IntervalMs / 1000;

            while (!ct.IsCancellationRequested)
            {
                next += interval;
                var wait = (next - Stopwatch.GetTimestamp()) * 1000 / Stopwatch.Frequency;
                if (wait > 0 && ct.WaitHandle.WaitOne((int)wait)) break;

                var now = Stopwatch.GetTimestamp();
                if (pid is null || now - lastPidLookup > Stopwatch.Frequency)
                {
                    pid = FindPid(_game);
                    lastPidLookup = now;
                }

                double? cpuPerf = null, cpuLimit = null, gameCpu = null, otherCpu = null, topCpu = null, gpu = null,
                    gameVram = null, adapterVram = null, pages = null;
                string? top = null;
                if (pdh is not null && pdh.Collect())
                {
                    cpuPerf = Single(pdh, CpuPerf);
                    cpuLimit = Single(pdh, CpuLimit);
                    pages = Single(pdh, PageReads);
                    (gameCpu, otherCpu, top, topCpu) = ProcessCpu(pdh.Read(procCounter), procCounter == ProcV2, _game, pid, cores);
                    if (pid is int p)
                    {
                        var tag = $"pid_{p}_";
                        var eng = pdh.Read(GpuEngine).Where(x => x.Instance.Contains(tag, StringComparison.OrdinalIgnoreCase) &&
                                                                x.Instance.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase)).ToList();
                        gpu = eng.Count == 0 ? null : Math.Min(100, eng.Sum(x => x.Value));
                        var mem = pdh.Read(GpuProcMem).Where(x => x.Instance.Contains(tag, StringComparison.OrdinalIgnoreCase)).ToList();
                        gameVram = mem.Count == 0 ? null : mem.Sum(x => x.Value);
                    }
                    var adapters = pdh.Read(GpuAdapterMem);
                    adapterVram = adapters.Count == 0 ? null : adapters.Max(x => x.Value);
                }

                var sample = new SensorSample(0, cpuPerf, cpuLimit, gameCpu, otherCpu, top, topCpu, gpu, gameVram, adapterVram,
                    _vramBudget, pages, nvml?.TemperatureC(), nvml?.PowerW(), nvml?.ClockReasons());
                lock (_samples) _samples.Add(new RawSensorSample(now, sample));
            }
        }
        catch (Exception ex)
        {
            _notes.Add("sampler stopped: " + ex.Message);
        }
        finally
        {
            pdh?.Dispose();
            nvml?.Dispose();
        }
    }

    private static double? Single(PdhQuery pdh, string path)
    {
        var v = pdh.Read(path);
        return v.Count == 0 ? null : v[0].Value;
    }

    private static int? FindPid(string name)
    {
        try
        {
            var ps = Process.GetProcessesByName(name);
            try { return ps.Length == 0 ? null : ps[0].Id; }
            finally { foreach (var p in ps) p.Dispose(); }
        }
        catch { return null; }
    }

    /// <summary>
    /// Splits per-process CPU time into the game, everyone else and the busiest other process.
    /// "% Processor Time" is per core, so values are divided by the logical core count.
    /// Process V2 instances are "name:pid"; classic Process instances are "name" or "name#n".
    /// </summary>
    internal static (double? Game, double? Other, string? Top, double? TopValue) ProcessCpu(
        IReadOnlyList<(string Instance, double Value)> rows, bool v2, string game, int? pid, int cores)
    {
        if (rows.Count == 0) return (null, null, null, null);
        double gameSum = 0, other = 0;
        var perName = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (inst, value) in rows)
        {
            if (inst.Equals("_Total", StringComparison.OrdinalIgnoreCase) || inst.StartsWith("Idle", StringComparison.OrdinalIgnoreCase) ||
                inst.StartsWith("_Total", StringComparison.OrdinalIgnoreCase)) continue;
            string name;
            bool isGame;
            if (v2)
            {
                var colon = inst.LastIndexOf(':');
                name = colon > 0 ? inst[..colon] : inst;
                var instPid = colon > 0 && int.TryParse(inst[(colon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ip) ? ip : (int?)null;
                isGame = pid is not null ? instPid == pid : name.Equals(game, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                var hash = inst.IndexOf('#');
                name = hash > 0 ? inst[..hash] : inst;
                isGame = name.Equals(game, StringComparison.OrdinalIgnoreCase);
            }
            var share = value / cores;
            if (isGame) { gameSum += share; continue; }
            other += share;
            perName[name] = perName.GetValueOrDefault(name) + share;
        }
        var topPair = perName.OrderByDescending(kv => kv.Value).FirstOrDefault();
        return (gameSum, other, topPair.Key, topPair.Key is null ? null : topPair.Value);
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}

/// <summary>Puts raw QPC-stamped sensor samples and hotkey markers on the session's frame time scale.</summary>
public static class SensorAlignment
{
    /// <summary>
    /// With <paramref name="firstFrameQpc"/> (PresentMon --qpc_time) alignment is exact. Without it the end of sampling is
    /// aligned with the end of the last frame and the result is flagged approximate.
    /// </summary>
    public static (IReadOnlyList<SensorSample> Samples, IReadOnlyList<double> Markers, bool Approximate) Align(
        IReadOnlyList<RawSensorSample> raw, IReadOnlyList<long> markerQpc, IReadOnlyList<FrameSample> frames,
        long? firstFrameQpc, long qpcFrequency)
    {
        if (frames.Count == 0) return (Array.Empty<SensorSample>(), Array.Empty<double>(), true);
        Func<long, double> toMs;
        bool approximate;
        if (firstFrameQpc is long q0)
        {
            var t0 = frames[0].TimeMs;
            toMs = q => t0 + (q - q0) * 1000.0 / qpcFrequency;
            approximate = false;
        }
        else
        {
            var lastQ = raw.Count > 0 ? raw[^1].Qpc : markerQpc.DefaultIfEmpty(0).Max();
            var tEnd = frames[^1].TimeMs;
            toMs = q => tEnd - (lastQ - q) * 1000.0 / qpcFrequency;
            approximate = true;
        }
        var end = frames[^1].TimeMs;
        var samples = raw.Select(r => r.Values with { TimeMs = toMs(r.Qpc) })
            .Where(s => s.TimeMs >= -SensorSampler.IntervalMs && s.TimeMs <= end + SensorSampler.IntervalMs).ToList();
        var markers = markerQpc.Select(toMs).Where(t => t >= 0 && t <= end + 1000).ToList();
        return (samples, markers, approximate);
    }
}

/// <summary>sessions\&lt;id&gt;.sensors.csv — one row per sample, invariant culture, empty cell = unavailable.</summary>
public static class SensorCsv
{
    private static readonly string[] Header =
    {
        "TimeMs", "CpuPerformancePercent", "CpuPerformanceLimitPercent", "GameCpuPercent", "OtherCpuPercent", "TopOtherProcess",
        "TopOtherCpuPercent", "GameGpuPercent", "GameVramBytes", "AdapterVramBytes", "VramBudgetBytes", "PageReadsPerSec",
        "GpuTemperatureC", "GpuPowerW", "GpuClockReasons"
    };

    public static void Write(string path, IEnumerable<SensorSample> samples)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(false));
        Write(w, samples);
    }

    public static void Write(TextWriter w, IEnumerable<SensorSample> samples)
    {
        w.WriteLine(string.Join(",", Header));
        foreach (var s in samples)
        {
            string D(double? v) => v?.ToString("R", CultureInfo.InvariantCulture) ?? "";
            var name = s.TopOtherProcess is null ? "" : "\"" + s.TopOtherProcess.Replace("\"", "\"\"") + "\"";
            w.WriteLine(string.Join(",", D(s.TimeMs), D(s.CpuPerformancePercent), D(s.CpuPerformanceLimitPercent), D(s.GameCpuPercent),
                D(s.OtherCpuPercent), name, D(s.TopOtherCpuPercent), D(s.GameGpuPercent), D(s.GameVramBytes), D(s.AdapterVramBytes),
                D(s.VramBudgetBytes), D(s.PageReadsPerSec), D(s.GpuTemperatureC), D(s.GpuPowerW),
                s.GpuClockReasons?.ToString(CultureInfo.InvariantCulture) ?? ""));
        }
    }

    public static IReadOnlyList<SensorSample> Read(string path)
    {
        using var r = new StreamReader(path);
        return Read(r);
    }

    public static IReadOnlyList<SensorSample> Read(TextReader r)
    {
        var list = new List<SensorSample>();
        if (r.ReadLine() is null) return list;
        string? line;
        while ((line = r.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            var f = SplitCsv(line);
            if (f.Count < Header.Length) continue;
            double? D(int i) => double.TryParse(f[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
            ulong? U(int i) => ulong.TryParse(f[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
            if (D(0) is not double t) continue;
            list.Add(new SensorSample(t, D(1), D(2), D(3), D(4), f[5].Length == 0 ? null : f[5], D(6), D(7), D(8), D(9), D(10), D(11),
                D(12), D(13), U(14)));
        }
        return list;
    }

    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString());
        return result;
    }
}
