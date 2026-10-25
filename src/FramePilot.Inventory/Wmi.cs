using System.Globalization;
using System.Management;
using FramePilot.Core;

namespace FramePilot.Inventory;

internal static class Wmi
{
    public const string Cimv2 = @"root\cimv2";
    public const string Storage = @"root\Microsoft\Windows\Storage";
    public const string DeviceGuard = @"root\Microsoft\Windows\DeviceGuard";

    public static List<ManagementBaseObject> Query(string scope, string query)
    {
        var ms = new ManagementScope(scope);
        ms.Connect();
        using var searcher = new ManagementObjectSearcher(ms, new ObjectQuery(query));
        searcher.Options.Timeout = TimeSpan.FromSeconds(20);
        var list = new List<ManagementBaseObject>();
        foreach (var o in searcher.Get()) list.Add(o);
        return list;
    }

    public static object? Prop(ManagementBaseObject o, string name)
    {
        try { return o[name]; }
        catch { return null; }
    }

    public static Measured<string> Str(ManagementBaseObject o, string name, string source)
    {
        var v = Prop(o, name)?.ToString()?.Trim();
        return string.IsNullOrEmpty(v)
            ? Measured<string>.Missing($"{name} is empty or not reported", source)
            : Measured<string>.Of(v, source);
    }

    public static Measured<int> Int(ManagementBaseObject o, string name, string source, bool zeroIsMissing = true)
    {
        var raw = Prop(o, name);
        if (raw is null) return Measured<int>.Missing($"{name} is not reported", source);
        try
        {
            var v = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
            if (zeroIsMissing && v == 0) return Measured<int>.Missing($"{name} reported 0 (unknown)", source);
            return Measured<int>.Of((int)Math.Min(int.MaxValue, v), source);
        }
        catch (Exception ex)
        {
            return Measured<int>.Missing($"{name}: {ex.Message}", source);
        }
    }

    public static Measured<long> Long(ManagementBaseObject o, string name, string source, bool zeroIsMissing = true)
    {
        var raw = Prop(o, name);
        if (raw is null) return Measured<long>.Missing($"{name} is not reported", source);
        try
        {
            var v = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
            if (zeroIsMissing && v == 0) return Measured<long>.Missing($"{name} reported 0 (unknown)", source);
            return Measured<long>.Of(v, source);
        }
        catch (Exception ex)
        {
            return Measured<long>.Missing($"{name}: {ex.Message}", source);
        }
    }
}
