namespace FramePilot.Core.Models;

public enum GameId
{
    Cs2,
    Valorant
}

public enum GpuVendor
{
    Unknown,
    Nvidia,
    Amd,
    Intel,
    Microsoft,
    Other
}

public enum DiskMediaType
{
    Unknown,
    Hdd,
    Ssd,
    Scm
}

/// <summary>Complete static picture of the PC at one moment. Static analysis is not game behaviour analysis.</summary>
public sealed record SystemSnapshot(
    string SchemaVersion,
    Guid Id,
    DateTimeOffset CollectedAt,
    bool IsDemo,
    OsInfo Os,
    CpuInfo Cpu,
    IReadOnlyList<GpuInfo> Gpus,
    MemoryInfo Memory,
    IReadOnlyList<DiskInfo> Disks,
    IReadOnlyList<DisplayInfo> Displays,
    WindowsGamingSettings Settings,
    BackgroundObservation Background,
    IReadOnlyList<DetectedGame> Games,
    IReadOnlyList<string> CollectorErrors)
{
    public const string CurrentSchema = "framepilot.snapshot/1";
}

public sealed record OsInfo(
    Measured<string> ProductName,
    Measured<string> DisplayVersion,
    Measured<int> Build,
    Measured<int> Ubr,
    bool Is64BitOs,
    Measured<string> Edition)
{
    public bool IsWindows11 => Build.IsAvailable && Build.Value >= 22000;
    public string BuildString => Build.IsAvailable ? (Ubr.IsAvailable ? $"{Build.Value}.{Ubr.Value}" : Build.Value.ToString()) : "—";
}

public sealed record CpuInfo(
    Measured<string> Name,
    Measured<string> Manufacturer,
    Measured<int> PhysicalCores,
    Measured<int> LogicalProcessors,
    Measured<int> MaxClockMhz,
    Measured<double> LoadPercent);

public sealed record GpuInfo(
    Measured<string> Name,
    GpuVendor Vendor,
    Measured<string> DriverVersion,
    Measured<DateTime> DriverDate,
    Measured<long> DedicatedMemoryBytes,
    Measured<bool> LikelyIntegrated,
    string? PnpDeviceId);

public sealed record MemoryModule(
    Measured<long> CapacityBytes,
    Measured<int> RatedSpeedMts,
    Measured<int> ConfiguredSpeedMts,
    Measured<string> Manufacturer,
    Measured<string> PartNumber,
    Measured<string> Slot);

public sealed record MemoryInfo(
    Measured<long> TotalBytes,
    Measured<long> AvailableBytes,
    IReadOnlyList<MemoryModule> Modules,
    Measured<int> SlotsTotal);

public sealed record VolumeInfo(string Letter, Measured<long> TotalBytes, Measured<long> FreeBytes, Measured<string> FileSystem);

public sealed record DiskInfo(
    int DiskNumber,
    Measured<string> FriendlyName,
    DiskMediaType MediaType,
    Measured<string> BusType,
    Measured<string> HealthStatus,
    Measured<long> SizeBytes,
    IReadOnlyList<VolumeInfo> Volumes);

public sealed record DisplayInfo(
    string DeviceName,
    Measured<string> AdapterName,
    bool IsPrimary,
    Measured<int> Width,
    Measured<int> Height,
    Measured<int> CurrentRefreshHz,
    Measured<int> MaxRefreshHzAtCurrentResolution,
    Measured<int> MaxRefreshHzAnyMode);

public sealed record PowerSchemeInfo(Guid Guid, string Name);

/// <summary>Read-only view of gaming-relevant Windows settings. Several of these are never modified by FramePilot.</summary>
public sealed record WindowsGamingSettings(
    Measured<bool> GameMode,
    Measured<bool> HardwareAcceleratedGpuScheduling,
    Measured<bool> WindowedGameOptimizations,
    Measured<PowerSchemeInfo> ActivePowerScheme,
    Measured<bool> MemoryIntegrity,
    Measured<bool> BackgroundCapture);

public sealed record BackgroundProcessSample(string Name, int ProcessId, double CpuPercent, long WorkingSetBytes);

public sealed record BackgroundObservation(
    double WindowSeconds,
    Measured<double> TotalCpuPercent,
    IReadOnlyList<BackgroundProcessSample> TopProcesses);

public sealed record DetectedGame(
    GameId Game,
    bool Installed,
    string? InstallPath,
    string? ExecutablePath,
    string ProcessName,
    string DetectionSource,
    Measured<string> Version,
    Measured<string> ConfigSummary,
    bool IsRunning);
