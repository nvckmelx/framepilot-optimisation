using System.Runtime.InteropServices;

namespace FramePilot.Sensors;

/// <summary>
/// Minimal read-only wrapper over the Windows Performance Data Helper (pdh.dll). Counters are added by their English
/// path (<c>PdhAddEnglishCounter</c>) so they work on any Windows display language. No elevation is required.
/// </summary>
internal sealed class PdhQuery : IDisposable
{
    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_FMT_NOCAP100 = 0x00008000;
    private const int PDH_MORE_DATA = unchecked((int)0x800007D2);
    private const uint PDH_CSTATUS_VALID_DATA = 0x0;
    private const uint PDH_CSTATUS_NEW_DATA = 0x1;

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhAddEnglishCounterW(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern int PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern int PdhCloseQuery(IntPtr query);

    private IntPtr _query;
    private readonly Dictionary<string, IntPtr> _counters = new();

    public PdhQuery()
    {
        var rc = PdhOpenQueryW(null, IntPtr.Zero, out _query);
        if (rc != 0) throw new InvalidOperationException($"PdhOpenQuery failed: 0x{rc:X8}");
    }

    /// <summary>Adds a counter (wildcard instances allowed). Returns false when the counter does not exist on this PC.</summary>
    public bool TryAdd(string path)
    {
        if (_counters.ContainsKey(path)) return true;
        if (PdhAddEnglishCounterW(_query, path, IntPtr.Zero, out var c) != 0) return false;
        _counters[path] = c;
        return true;
    }

    public bool Has(string path) => _counters.ContainsKey(path);

    public bool Collect() => PdhCollectQueryData(_query) == 0;

    /// <summary>Formatted values by instance name for one counter; empty when no valid data yet.</summary>
    public IReadOnlyList<(string Instance, double Value)> Read(string path)
    {
        var list = new List<(string, double)>();
        if (!_counters.TryGetValue(path, out var counter)) return list;
        uint size = 0;
        var rc = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out var count, IntPtr.Zero);
        if (rc != PDH_MORE_DATA || size == 0) return list;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            rc = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, buffer);
            if (rc != 0) return list;
            // PDH_FMT_COUNTERVALUE_ITEM_W on x64: LPWSTR name (8) + { DWORD CStatus; [pad 4]; double value } (16) = 24 bytes.
            var itemSize = IntPtr.Size + 16;
            for (var i = 0; i < count; i++)
            {
                var item = buffer + i * itemSize;
                var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? "";
                var status = (uint)Marshal.ReadInt32(item + IntPtr.Size);
                if (status != PDH_CSTATUS_VALID_DATA && status != PDH_CSTATUS_NEW_DATA) continue;
                var value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item + IntPtr.Size + 8));
                if (double.IsFinite(value)) list.Add((name, value));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return list;
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero) PdhCloseQuery(_query);
        _query = IntPtr.Zero;
    }
}

/// <summary>
/// Read-only NVIDIA Management Library access (nvml.dll ships with the NVIDIA driver). Loaded dynamically:
/// on AMD/Intel or without the driver, <see cref="TryOpen"/> returns null and GPU temperature stays unavailable.
/// </summary>
internal sealed class Nvml : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int HandleFn(uint index, out IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int TempFn(IntPtr device, int sensor, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int UIntFn(IntPtr device, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ULongFn(IntPtr device, out ulong value);

    private readonly IntPtr _lib;
    private readonly IntPtr _device;
    private readonly InitFn _shutdown;
    private readonly TempFn _temp;
    private readonly UIntFn _power;
    private readonly ULongFn? _reasons;

    private Nvml(IntPtr lib, IntPtr device, InitFn shutdown, TempFn temp, UIntFn power, ULongFn? reasons)
    {
        _lib = lib; _device = device; _shutdown = shutdown; _temp = temp; _power = power; _reasons = reasons;
    }

    public static Nvml? TryOpen(out string? reason)
    {
        reason = null;
        IntPtr lib = IntPtr.Zero;
        var candidates = new[]
        {
            "nvml.dll",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll")
        };
        foreach (var c in candidates)
            if (NativeLibrary.TryLoad(c, out lib)) break;
        if (lib == IntPtr.Zero) { reason = "nvml.dll not found (no NVIDIA driver)"; return null; }
        try
        {
            T Fn<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, name));
            T? Opt<T>(string name) where T : Delegate =>
                NativeLibrary.TryGetExport(lib, name, out var p) ? Marshal.GetDelegateForFunctionPointer<T>(p) : null;

            if (Fn<InitFn>("nvmlInit_v2")() != 0) { reason = "nvmlInit failed"; NativeLibrary.Free(lib); return null; }
            var shutdown = Fn<InitFn>("nvmlShutdown");
            if (Fn<HandleFn>("nvmlDeviceGetHandleByIndex_v2")(0, out var dev) != 0)
            {
                shutdown(); NativeLibrary.Free(lib); reason = "no NVIDIA GPU"; return null;
            }
            var reasons = Opt<ULongFn>("nvmlDeviceGetCurrentClocksEventReasons") ?? Opt<ULongFn>("nvmlDeviceGetCurrentClocksThrottleReasons");
            return new Nvml(lib, dev, shutdown, Fn<TempFn>("nvmlDeviceGetTemperature"), Fn<UIntFn>("nvmlDeviceGetPowerUsage"), reasons);
        }
        catch (Exception ex)
        {
            reason = "NVML: " + ex.Message;
            NativeLibrary.Free(lib);
            return null;
        }
    }

    public double? TemperatureC() => _temp(_device, 0 /* NVML_TEMPERATURE_GPU */, out var t) == 0 ? t : null;
    public double? PowerW() => _power(_device, out var mw) == 0 ? mw / 1000.0 : null;
    public ulong? ClockReasons() => _reasons is not null && _reasons(_device, out var r) == 0 ? r : null;

    public void Dispose()
    {
        try { _shutdown(); } catch { /* ignore */ }
        NativeLibrary.Free(_lib);
    }
}
