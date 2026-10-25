using System.Diagnostics;
using System.Runtime.InteropServices;
using FramePilot.Core;
using FramePilot.Core.Models;
using Microsoft.Win32;

namespace FramePilot.Inventory;

internal static class SettingsCollector
{
    public static WindowsGamingSettings Collect(List<string> errors)
    {
        return new WindowsGamingSettings(
            GameMode(),
            Hags(),
            WindowedOptimizations(),
            PowerScheme(errors),
            MemoryIntegrity(),
            BackgroundCapture());
    }

    private static Measured<bool> GameMode()
    {
        const string src = @"HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled";
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar");
            var v = k?.GetValue("AutoGameModeEnabled");
            if (v is null) return Measured<bool>.Of(true, src, "Not set explicitly; Windows 11 default (on) applies");
            return Measured<bool>.Of(Convert.ToInt32(v) != 0, src);
        }
        catch (Exception ex)
        {
            return Measured<bool>.Missing(ex.Message, src);
        }
    }

    private static Measured<bool> Hags()
    {
        const string src = @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode";
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
            var v = k?.GetValue("HwSchMode");
            if (v is null) return Measured<bool>.Missing("Not set; the driver/Windows default applies and is not reported", src);
            return Convert.ToInt32(v) switch
            {
                2 => Measured<bool>.Of(true, src),
                1 => Measured<bool>.Of(false, src),
                var other => Measured<bool>.Missing($"Unexpected value {other}", src)
            };
        }
        catch (Exception ex)
        {
            return Measured<bool>.Missing(ex.Message, src);
        }
    }

    private static Measured<bool> WindowedOptimizations()
    {
        const string src = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences\DirectXUserGlobalSettings";
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
            var s = k?.GetValue("DirectXUserGlobalSettings")?.ToString();
            if (s is null) return Measured<bool>.Missing("Not set explicitly; Windows default applies", src);
            foreach (var part in s.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2 && kv[0].Equals("SwapEffectUpgradeEnable", StringComparison.OrdinalIgnoreCase))
                    return Measured<bool>.Of(kv[1].Trim() == "1", src);
            }
            return Measured<bool>.Missing("Token not set explicitly; Windows default applies", src);
        }
        catch (Exception ex)
        {
            return Measured<bool>.Missing(ex.Message, src);
        }
    }

    private static Measured<PowerSchemeInfo> PowerScheme(List<string> errors)
    {
        const string src = "powrprof PowerGetActiveScheme";
        try
        {
            if (Native.PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0 || ptr == IntPtr.Zero)
                return Measured<PowerSchemeInfo>.Missing("PowerGetActiveScheme failed", src);
            Guid guid;
            try { guid = Marshal.PtrToStructure<Guid>(ptr); }
            finally { Native.LocalFree(ptr); }

            var name = guid.ToString();
            uint size = 0;
            if (Native.PowerReadFriendlyName(IntPtr.Zero, ref guid, IntPtr.Zero, IntPtr.Zero, null, ref size) == 0 && size > 0)
            {
                var buf = new byte[size];
                if (Native.PowerReadFriendlyName(IntPtr.Zero, ref guid, IntPtr.Zero, IntPtr.Zero, buf, ref size) == 0)
                    name = System.Text.Encoding.Unicode.GetString(buf).TrimEnd('\0');
            }
            return Measured<PowerSchemeInfo>.Of(new PowerSchemeInfo(guid, name), src);
        }
        catch (Exception ex)
        {
            errors.Add("Power scheme: " + ex.Message);
            return Measured<PowerSchemeInfo>.Missing(ex.Message, src);
        }
    }

    private static Measured<bool> MemoryIntegrity()
    {
        // Win32_DeviceGuard.SecurityServicesRunning contains 2 when HVCI is running.
        const string wmiSrc = "WMI Win32_DeviceGuard.SecurityServicesRunning";
        try
        {
            var dg = Wmi.Query(Wmi.DeviceGuard, "SELECT SecurityServicesRunning FROM Win32_DeviceGuard").FirstOrDefault();
            if (dg is not null && Wmi.Prop(dg, "SecurityServicesRunning") is Array arr)
            {
                var running = arr.Cast<object>().Select(o => Convert.ToInt32(o)).ToList();
                return Measured<bool>.Of(running.Contains(2), wmiSrc);
            }
        }
        catch
        {
            // fall back to the registry configuration value
        }

        const string regSrc = @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity\Enabled";
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
            var v = k?.GetValue("Enabled");
            if (v is null) return Measured<bool>.Missing("Not configured", regSrc);
            return Measured<bool>.Of(Convert.ToInt32(v) == 1, regSrc, "Configuration value; running state not confirmed");
        }
        catch (Exception ex)
        {
            return Measured<bool>.Missing(ex.Message, regSrc);
        }
    }

    private static Measured<bool> BackgroundCapture()
    {
        const string src = @"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\AppCaptureEnabled";
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR");
            var v = k?.GetValue("AppCaptureEnabled");
            if (v is null) return Measured<bool>.Missing("Not set explicitly", src);
            return Measured<bool>.Of(Convert.ToInt32(v) != 0, src);
        }
        catch (Exception ex)
        {
            return Measured<bool>.Missing(ex.Message, src);
        }
    }
}

/// <summary>Observes CPU use of running processes over a short window. Read-only: never terminates anything.</summary>
public static class BackgroundObserver
{
    public static async Task<(BackgroundObservation Observation, Measured<double> SystemCpu)> ObserveAsync(TimeSpan window, CancellationToken ct)
    {
        var sampler = new SystemCpuSampler();
        var primed = sampler.Prime();
        var before = Snapshot();
        var sw = Stopwatch.StartNew();
        await Task.Delay(window, ct).ConfigureAwait(false);
        var elapsed = sw.Elapsed.TotalMilliseconds;
        var after = Snapshot();
        var sys = primed ? sampler.Sample() : null;

        var cores = Environment.ProcessorCount;
        var samples = new List<BackgroundProcessSample>();
        foreach (var (pid, a) in after)
        {
            if (!before.TryGetValue(pid, out var b) || b.Name != a.Name) continue;
            var cpuMs = (a.Cpu - b.Cpu).TotalMilliseconds;
            if (cpuMs < 0) continue;
            samples.Add(new BackgroundProcessSample(a.Name, pid, 100.0 * cpuMs / (elapsed * cores), a.WorkingSet));
        }

        var grouped = samples
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new BackgroundProcessSample(g.Key, g.First().ProcessId, g.Sum(x => x.CpuPercent), g.Sum(x => x.WorkingSetBytes)))
            .Where(s => !string.Equals(s.Name, "Idle", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.CpuPercent)
            .Take(12)
            .ToList();

        var sysMeasured = sys is double v
            ? Measured<double>.Of(v, "kernel32 GetSystemTimes")
            : Measured<double>.Missing("GetSystemTimes unavailable", "kernel32 GetSystemTimes");
        return (new BackgroundObservation(window.TotalSeconds, sysMeasured, grouped), sysMeasured);
    }

    private sealed record ProcInfo(string Name, TimeSpan Cpu, long WorkingSet);

    private static Dictionary<int, ProcInfo> Snapshot()
    {
        var map = new Dictionary<int, ProcInfo>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                map[p.Id] = new ProcInfo(p.ProcessName, p.TotalProcessorTime, p.WorkingSet64);
            }
            catch
            {
                // protected or exited processes cannot be read without elevation; they are skipped
            }
            finally
            {
                p.Dispose();
            }
        }
        return map;
    }
}
