using System.Globalization;
using System.IO;
using FramePilot.Core.Changes;
using FramePilot.Core.Models;
using FramePilot.GameAdapters;
using FramePilot.Inventory;
using FramePilot.Licensing;
using FramePilot.Optimization;
using FramePilot.Storage;
using Microsoft.Win32;

namespace FramePilot.App.Services;

public static class LicenseConfig
{
    /// <summary>
    /// Vendor public key (SPKI, base64). Replace with the output of "fp-license keygen" before a commercial release.
    /// Until then no key validates and the app runs as Free; diagnostics and rollback are fully available.
    /// </summary>
    public const string PublicKey = "";
}

public sealed class AppSettings
{
    private readonly FramePilotStore _store;
    public AppSettings(FramePilotStore store) => _store = store;

    private string? Get(string k) => _store.GetSetting(k);
    private void Put(string k, string? v) => _store.SetSetting(k, v);

    public string Language
    {
        get => Get("language") ?? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en");
        set => Put("language", value);
    }

    public string? PresentMonPath { get => Get("presentmon.path"); set => Put("presentmon.path", value); }

    public int CaptureSeconds
    {
        get => int.TryParse(Get("capture.seconds"), out var s) && s is >= 10 and <= 1800 ? s : 120;
        set => Put("capture.seconds", value.ToString(CultureInfo.InvariantCulture));
    }

    public bool ElevatedCapture { get => Get("capture.elevated") != "0"; set => Put("capture.elevated", value ? "1" : "0"); }
    public bool DemoMode { get => Get("demo") == "1"; set => Put("demo", value ? "1" : "0"); }
    public bool RestorePointBeforeApply { get => Get("apply.restorepoint") == "1"; set => Put("apply.restorepoint", value ? "1" : "0"); }

    public GameId TargetGame
    {
        get => Enum.TryParse<GameId>(Get("target.game"), out var g) ? g : GameId.Cs2;
        set => Put("target.game", value.ToString());
    }

    public PerformanceGoal Goal
    {
        get => Enum.TryParse<PerformanceGoal>(Get("goal"), out var g) ? g : PerformanceGoal.StableFps;
        set => Put("goal", value.ToString());
    }

    public string? LicenseKey { get => Get("license.key"); set => Put("license.key", value); }
}

/// <summary>Composition root. Created once at startup.</summary>
public sealed class AppServices
{
    public string DataDirectory { get; }
    public string SessionsDirectory { get; }
    public string LogsDirectory { get; }
    public FramePilotStore Store { get; }
    public AppSettings Settings { get; }
    public IInventoryService Inventory { get; } = new InventoryService();
    public GameDetector Games { get; } = new();
    public ChangeEngine Changes { get; }
    public IReadOnlyDictionary<string, IChangeAction> Actions { get; }
    public string MachineCode { get; }
    public LicenseState License { get; private set; }
    public RecoveryReport? StartupRecovery { get; set; }
    public string AppVersion { get; } = typeof(AppServices).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public AppServices()
    {
        DataDirectory = FramePilotStore.DefaultDataDirectory;
        SessionsDirectory = Path.Combine(DataDirectory, "sessions");
        LogsDirectory = Path.Combine(DataDirectory, "logs");
        Directory.CreateDirectory(SessionsDirectory);
        Directory.CreateDirectory(LogsDirectory);

        Store = new FramePilotStore(Path.Combine(DataDirectory, "framepilot.db"));
        Settings = new AppSettings(Store);
        Changes = new ChangeEngine(Store, new RegistryTargetResolver());
        Actions = ActionRegistry.Create(new RegistryTargetFactory());
        MachineCode = LicenseVerifier.MachineCodeFrom(ReadMachineGuid());
        License = EvaluateLicense(Settings.LicenseKey);
    }

    public LicenseState EvaluateLicense(string? key)
    {
        if (string.IsNullOrWhiteSpace(LicenseConfig.PublicKey))
            return string.IsNullOrWhiteSpace(key) ? LicenseState.FreeTier : new LicenseState(LicenseStatus.InvalidSignature, null);
        return LicenseVerifier.Verify(key, LicenseConfig.PublicKey, MachineCode, DateTimeOffset.UtcNow);
    }

    public LicenseState SetLicenseKey(string? key)
    {
        Settings.LicenseKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        License = EvaluateLicense(Settings.LicenseKey);
        return License;
    }

    private static string ReadMachineGuid()
    {
        try
        {
            using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return k?.GetValue("MachineGuid")?.ToString() ?? Environment.MachineName;
        }
        catch
        {
            return Environment.MachineName;
        }
    }
}
