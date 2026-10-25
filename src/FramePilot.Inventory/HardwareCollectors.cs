using System.Globalization;
using System.Management;
using FramePilot.Core;
using FramePilot.Core.Models;
using Microsoft.Win32;

namespace FramePilot.Inventory;

internal static class HardwareCollectors
{
    public static OsInfo CollectOs(List<string> errors)
    {
        const string reg = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        Measured<string> caption;
        try
        {
            var os = Wmi.Query(Wmi.Cimv2, "SELECT Caption FROM Win32_OperatingSystem").FirstOrDefault();
            caption = os is null ? Measured<string>.Missing("No Win32_OperatingSystem instance", "WMI Win32_OperatingSystem")
                                 : Wmi.Str(os, "Caption", "WMI Win32_OperatingSystem.Caption");
        }
        catch (Exception ex)
        {
            errors.Add("OS caption: " + ex.Message);
            caption = Measured<string>.Missing(ex.Message, "WMI Win32_OperatingSystem");
        }

        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        Measured<string> RegStr(string name) => Measured.Try<string>(reg + "\\" + name, () => key?.GetValue(name)?.ToString());
        Measured<int> RegInt(string name) => Measured.Try<int>(reg + "\\" + name, () =>
        {
            var v = key?.GetValue(name) ?? throw new InvalidOperationException("Value not present");
            return Convert.ToInt32(v, CultureInfo.InvariantCulture);
        });

        return new OsInfo(caption, RegStr("DisplayVersion"), RegInt("CurrentBuildNumber"), RegInt("UBR"),
            Environment.Is64BitOperatingSystem, RegStr("EditionID"));
    }

    public static CpuInfo CollectCpu(List<string> errors)
    {
        const string src = "WMI Win32_Processor";
        try
        {
            var procs = Wmi.Query(Wmi.Cimv2, "SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
            if (procs.Count == 0) throw new InvalidOperationException("No Win32_Processor instances");
            var p = procs[0];
            var cores = procs.Select(x => Wmi.Int(x, "NumberOfCores", src)).ToList();
            var coreSum = cores.All(c => c.IsAvailable) ? Measured<int>.Of(cores.Sum(c => c.Value), src + ".NumberOfCores") : cores[0];
            return new CpuInfo(
                Wmi.Str(p, "Name", src + ".Name"),
                Wmi.Str(p, "Manufacturer", src + ".Manufacturer"),
                coreSum,
                Measured<int>.Of(Environment.ProcessorCount, "Environment.ProcessorCount"),
                Wmi.Int(p, "MaxClockSpeed", src + ".MaxClockSpeed"),
                Measured<double>.Missing("Not sampled yet", "GetSystemTimes"));
        }
        catch (Exception ex)
        {
            errors.Add("CPU: " + ex.Message);
            var m = Measured<string>.Missing(ex.Message, src);
            var mi = Measured<int>.Missing(ex.Message, src);
            return new CpuInfo(m, m, mi, Measured<int>.Of(Environment.ProcessorCount, "Environment.ProcessorCount"), mi,
                Measured<double>.Missing("Not sampled", "GetSystemTimes"));
        }
    }

    public static List<GpuInfo> CollectGpus(List<string> errors)
    {
        const string src = "WMI Win32_VideoController";
        var list = new List<GpuInfo>();
        try
        {
            var vram = ReadVramFromRegistry();
            foreach (var g in Wmi.Query(Wmi.Cimv2, "SELECT Name, DriverVersion, DriverDate, PNPDeviceID, AdapterRAM FROM Win32_VideoController"))
            {
                var name = Wmi.Str(g, "Name", src + ".Name");
                var pnp = Wmi.Prop(g, "PNPDeviceID")?.ToString();
                var vendor = VendorOf(pnp, name.Value);

                Measured<DateTime> driverDate;
                var rawDate = Wmi.Prop(g, "DriverDate")?.ToString();
                try
                {
                    driverDate = string.IsNullOrEmpty(rawDate)
                        ? Measured<DateTime>.Missing("DriverDate not reported", src + ".DriverDate")
                        : Measured<DateTime>.Of(ManagementDateTimeConverter.ToDateTime(rawDate), src + ".DriverDate");
                }
                catch (Exception ex)
                {
                    driverDate = Measured<DateTime>.Missing(ex.Message, src + ".DriverDate");
                }

                Measured<long> mem;
                var match = pnp is null ? null : vram.FirstOrDefault(v => pnp.StartsWith(v.MatchingDeviceId, StringComparison.OrdinalIgnoreCase));
                if (match is not null && match.Bytes > 0)
                    mem = Measured<long>.Of(match.Bytes, "Registry HardwareInformation.qwMemorySize");
                else
                {
                    var ar = Wmi.Long(g, "AdapterRAM", src + ".AdapterRAM");
                    mem = ar.IsAvailable && ar.Value >= 0xFFF00000L
                        ? Measured<long>.Missing("AdapterRAM is capped at 4 GB and the registry value was not found", src + ".AdapterRAM")
                        : ar;
                }

                list.Add(new GpuInfo(name, vendor, Wmi.Str(g, "DriverVersion", src + ".DriverVersion"), driverDate, mem,
                    LikelyIntegrated(vendor, name.Value), pnp));
            }
        }
        catch (Exception ex)
        {
            errors.Add("GPU: " + ex.Message);
        }
        return list;
    }

    internal static GpuVendor VendorOf(string? pnp, string? name)
    {
        var p = pnp?.ToUpperInvariant() ?? "";
        if (p.Contains("VEN_10DE")) return GpuVendor.Nvidia;
        if (p.Contains("VEN_1002")) return GpuVendor.Amd;
        if (p.Contains("VEN_8086")) return GpuVendor.Intel;
        var n = name?.ToUpperInvariant() ?? "";
        if (n.Contains("NVIDIA")) return GpuVendor.Nvidia;
        if (n.Contains("AMD") || n.Contains("RADEON")) return GpuVendor.Amd;
        if (n.Contains("INTEL")) return GpuVendor.Intel;
        if (n.Contains("MICROSOFT")) return GpuVendor.Microsoft;
        return string.IsNullOrEmpty(n) ? GpuVendor.Unknown : GpuVendor.Other;
    }

    /// <summary>Heuristic, clearly labelled: Windows does not expose an "integrated" flag through WMI.</summary>
    internal static Measured<bool> LikelyIntegrated(GpuVendor vendor, string? name)
    {
        const string src = "Heuristic by vendor and model name";
        var n = name ?? "";
        switch (vendor)
        {
            case GpuVendor.Nvidia:
                return Measured<bool>.Of(false, src);
            case GpuVendor.Intel:
                return Measured<bool>.Of(!n.Contains("Arc", StringComparison.OrdinalIgnoreCase) || n.Contains("Arc(TM) Graphics", StringComparison.OrdinalIgnoreCase), src);
            case GpuVendor.Amd:
                var integrated = n.EndsWith("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase)
                                 || n.EndsWith("Radeon Graphics", StringComparison.OrdinalIgnoreCase)
                                 || n.Contains("Vega", StringComparison.OrdinalIgnoreCase) && !n.Contains("RX Vega", StringComparison.OrdinalIgnoreCase);
                return Measured<bool>.Of(integrated, src);
            default:
                return Measured<bool>.Missing("Unknown vendor", src);
        }
    }

    private sealed record VramEntry(string MatchingDeviceId, long Bytes);

    private static List<VramEntry> ReadVramFromRegistry()
    {
        var list = new List<VramEntry>();
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls is null) return list;
            foreach (var sub in cls.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
            {
                try
                {
                    using var k = cls.OpenSubKey(sub);
                    var id = k?.GetValue("MatchingDeviceId")?.ToString();
                    var raw = k?.GetValue("HardwareInformation.qwMemorySize") ?? k?.GetValue("HardwareInformation.MemorySize");
                    if (id is null || raw is null) continue;
                    long bytes = raw switch
                    {
                        long l => l,
                        int i => unchecked((uint)i),
                        byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
                        byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
                        _ => 0
                    };
                    list.Add(new VramEntry(id, bytes));
                }
                catch
                {
                    // protected subkeys (e.g. "Properties") are skipped
                }
            }
        }
        catch
        {
            // registry unavailable: caller falls back to WMI
        }
        return list;
    }

    public static MemoryInfo CollectMemory(List<string> errors)
    {
        const string src = "WMI Win32_PhysicalMemory";
        Measured<long> total = Measured<long>.Missing("Not read", "WMI Win32_ComputerSystem");
        Measured<long> avail = Measured<long>.Missing("Not read", "WMI Win32_OperatingSystem");
        var modules = new List<MemoryModule>();
        Measured<int> slots = Measured<int>.Missing("Not read", "WMI Win32_PhysicalMemoryArray");
        try
        {
            var cs = Wmi.Query(Wmi.Cimv2, "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem").FirstOrDefault();
            if (cs is not null) total = Wmi.Long(cs, "TotalPhysicalMemory", "WMI Win32_ComputerSystem.TotalPhysicalMemory");
            var os = Wmi.Query(Wmi.Cimv2, "SELECT FreePhysicalMemory FROM Win32_OperatingSystem").FirstOrDefault();
            if (os is not null)
            {
                var kb = Wmi.Long(os, "FreePhysicalMemory", "WMI Win32_OperatingSystem.FreePhysicalMemory");
                avail = kb.IsAvailable ? Measured<long>.Of(kb.Value * 1024, kb.Source) : kb;
            }
            foreach (var m in Wmi.Query(Wmi.Cimv2, "SELECT Capacity, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber, DeviceLocator FROM Win32_PhysicalMemory"))
            {
                modules.Add(new MemoryModule(
                    Wmi.Long(m, "Capacity", src + ".Capacity"),
                    Wmi.Int(m, "Speed", src + ".Speed"),
                    Wmi.Int(m, "ConfiguredClockSpeed", src + ".ConfiguredClockSpeed"),
                    Wmi.Str(m, "Manufacturer", src + ".Manufacturer"),
                    Wmi.Str(m, "PartNumber", src + ".PartNumber"),
                    Wmi.Str(m, "DeviceLocator", src + ".DeviceLocator")));
            }
            var arrays = Wmi.Query(Wmi.Cimv2, "SELECT MemoryDevices FROM Win32_PhysicalMemoryArray");
            var counts = arrays.Select(a => Wmi.Int(a, "MemoryDevices", "WMI Win32_PhysicalMemoryArray.MemoryDevices")).Where(x => x.IsAvailable).ToList();
            if (counts.Count > 0) slots = Measured<int>.Of(counts.Sum(c => c.Value), "WMI Win32_PhysicalMemoryArray.MemoryDevices");
        }
        catch (Exception ex)
        {
            errors.Add("Memory: " + ex.Message);
        }
        return new MemoryInfo(total, avail, modules, slots);
    }

    public static List<DiskInfo> CollectDisks(List<string> errors)
    {
        const string src = "WMI MSFT_PhysicalDisk";
        var disks = new List<DiskInfo>();
        var letterToDisk = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var p in Wmi.Query(Wmi.Storage, "SELECT DiskNumber, DriveLetter FROM MSFT_Partition"))
            {
                var dl = Wmi.Prop(p, "DriveLetter");
                var letter = dl switch
                {
                    char c => c,
                    ushort u => (char)u,
                    string s when s.Length > 0 => s[0],
                    _ => '\0'
                };
                if (letter == '\0' || !char.IsLetter(letter)) continue;
                var dn = Wmi.Int(p, "DiskNumber", "WMI MSFT_Partition", zeroIsMissing: false);
                if (dn.IsAvailable) letterToDisk[$"{char.ToUpperInvariant(letter)}:"] = dn.Value;
            }
        }
        catch (Exception ex)
        {
            errors.Add("Partitions: " + ex.Message);
        }

        var drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed).ToList();
        VolumeInfo Vol(DriveInfo d)
        {
            var l = d.Name.TrimEnd('\\');
            return new VolumeInfo(l,
                Measured.Try<long>("DriveInfo.TotalSize", () => d.TotalSize),
                Measured.Try<long>("DriveInfo.AvailableFreeSpace", () => d.AvailableFreeSpace),
                Measured.Try<string>("DriveInfo.DriveFormat", () => d.DriveFormat));
        }

        try
        {
            foreach (var pd in Wmi.Query(Wmi.Storage, "SELECT DeviceId, FriendlyName, MediaType, BusType, HealthStatus, Size FROM MSFT_PhysicalDisk"))
            {
                if (!int.TryParse(Wmi.Prop(pd, "DeviceId")?.ToString(), out var number)) continue;
                var media = Convert.ToInt32(Wmi.Prop(pd, "MediaType") ?? 0, CultureInfo.InvariantCulture) switch
                {
                    3 => DiskMediaType.Hdd,
                    4 => DiskMediaType.Ssd,
                    5 => DiskMediaType.Scm,
                    _ => DiskMediaType.Unknown
                };
                var busRaw = Wmi.Prop(pd, "BusType");
                var bus = busRaw is null
                    ? Measured<string>.Missing("BusType not reported", src + ".BusType")
                    : Measured<string>.Of(BusName(Convert.ToInt32(busRaw, CultureInfo.InvariantCulture)), src + ".BusType");
                var healthRaw = Wmi.Prop(pd, "HealthStatus");
                var health = healthRaw is null
                    ? Measured<string>.Missing("HealthStatus not reported", src + ".HealthStatus")
                    : Measured<string>.Of(Convert.ToInt32(healthRaw, CultureInfo.InvariantCulture) switch
                    {
                        0 => "Healthy",
                        1 => "Warning",
                        2 => "Unhealthy",
                        _ => "Unknown"
                    }, src + ".HealthStatus");
                var vols = drives.Where(d => letterToDisk.TryGetValue(d.Name.TrimEnd('\\'), out var n) && n == number).Select(Vol).ToList();
                disks.Add(new DiskInfo(number, Wmi.Str(pd, "FriendlyName", src + ".FriendlyName"), media, bus, health,
                    Wmi.Long(pd, "Size", src + ".Size"), vols));
            }
        }
        catch (Exception ex)
        {
            errors.Add("Physical disks: " + ex.Message);
        }

        // Volumes we could not map to a physical disk are still reported (media type unknown).
        var mapped = disks.SelectMany(d => d.Volumes).Select(v => v.Letter).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphan = drives.Where(d => !mapped.Contains(d.Name.TrimEnd('\\'))).Select(Vol).ToList();
        if (orphan.Count > 0)
            disks.Add(new DiskInfo(-1, Measured<string>.Missing("Volume could not be mapped to a physical disk", "MSFT_Partition"),
                DiskMediaType.Unknown, Measured<string>.Missing("Unknown", src), Measured<string>.Missing("Unknown", src),
                Measured<long>.Missing("Unknown", src), orphan));
        return disks;
    }

    internal static string BusName(int bus) => bus switch
    {
        1 => "SCSI",
        2 => "ATAPI",
        3 => "ATA",
        6 => "Fibre Channel",
        7 => "USB",
        8 => "RAID",
        9 => "iSCSI",
        10 => "SAS",
        11 => "SATA",
        12 => "SD",
        13 => "MMC",
        15 => "File-backed virtual",
        16 => "Storage Spaces",
        17 => "NVMe",
        _ => $"Other ({bus})"
    };

    public static List<DisplayInfo> CollectDisplays(List<string> errors)
    {
        const string src = "user32 EnumDisplaySettingsEx";
        var list = new List<DisplayInfo>();
        try
        {
            for (uint i = 0; i < 16; i++)
            {
                var dd = Native.DISPLAY_DEVICE.Create();
                if (!Native.EnumDisplayDevices(null, i, ref dd, 0)) break;
                if ((dd.StateFlags & Native.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;

                var cur = Native.DEVMODE.Create();
                if (!Native.EnumDisplaySettingsEx(dd.DeviceName, Native.ENUM_CURRENT_SETTINGS, ref cur, 0))
                {
                    var missing = Measured<int>.Missing("EnumDisplaySettingsEx failed", src);
                    list.Add(new DisplayInfo(dd.DeviceName, Measured<string>.Of(dd.DeviceString, "EnumDisplayDevices"),
                        (dd.StateFlags & Native.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0, missing, missing, missing, missing, missing));
                    continue;
                }

                int maxSame = (int)cur.dmDisplayFrequency, maxAny = (int)cur.dmDisplayFrequency;
                for (var mode = 0; mode < 4096; mode++)
                {
                    var dm = Native.DEVMODE.Create();
                    if (!Native.EnumDisplaySettingsEx(dd.DeviceName, mode, ref dm, 0)) break;
                    var hz = (int)dm.dmDisplayFrequency;
                    if (hz <= 1) continue; // 0/1 mean "hardware default"
                    maxAny = Math.Max(maxAny, hz);
                    if (dm.dmPelsWidth == cur.dmPelsWidth && dm.dmPelsHeight == cur.dmPelsHeight) maxSame = Math.Max(maxSame, hz);
                }

                var curHz = (int)cur.dmDisplayFrequency;
                list.Add(new DisplayInfo(
                    dd.DeviceName,
                    Measured<string>.Of(dd.DeviceString, "EnumDisplayDevices"),
                    (dd.StateFlags & Native.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0,
                    Measured<int>.Of((int)cur.dmPelsWidth, src),
                    Measured<int>.Of((int)cur.dmPelsHeight, src),
                    curHz > 1 ? Measured<int>.Of(curHz, src) : Measured<int>.Missing("Driver reports hardware default frequency", src),
                    Measured<int>.Of(maxSame, src + " (mode list)"),
                    Measured<int>.Of(maxAny, src + " (mode list)")));
            }
        }
        catch (Exception ex)
        {
            errors.Add("Displays: " + ex.Message);
        }
        return list;
    }
}
