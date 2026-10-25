using FramePilot.Core.Changes;
using FramePilot.Core.Models;

namespace FramePilot.Optimization;

/// <summary>Parses and edits DirectX preference strings such as "SwapEffectUpgradeEnable=1;GpuPreference=2;" while preserving other tokens.</summary>
public static class DxPreferenceString
{
    public static List<KeyValuePair<string, string>> Parse(string? value)
    {
        var list = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrWhiteSpace(value)) return list;
        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            list.Add(new(part[..eq].Trim(), part[(eq + 1)..].Trim()));
        }
        return list;
    }

    public static string? Get(string? value, string token) =>
        Parse(value).FirstOrDefault(kv => string.Equals(kv.Key, token, StringComparison.OrdinalIgnoreCase)).Value;

    public static string Set(string? value, string token, string tokenValue)
    {
        var list = Parse(value);
        var idx = list.FindIndex(kv => string.Equals(kv.Key, token, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) list[idx] = new(list[idx].Key, tokenValue);
        else list.Add(new(token, tokenValue));
        return string.Concat(list.Select(kv => $"{kv.Key}={kv.Value};"));
    }
}

/// <summary>Creates allowlisted targets. Tests substitute an in-memory factory.</summary>
public interface ITargetFactory
{
    ISettingTarget Create(string subKey, string valueName, SettingValueKind kind, string description);
}

public sealed class RegistryTargetFactory : ITargetFactory
{
    public ISettingTarget Create(string subKey, string valueName, SettingValueKind kind, string description) =>
        new RegistryValueTarget(subKey, valueName, kind, description);
}

public sealed class EnableGameModeAction : IChangeAction
{
    private readonly ITargetFactory _factory;
    public EnableGameModeAction(ITargetFactory factory) => _factory = factory;

    public string Id => ActionIds.EnableGameMode;
    public int Version => 1;
    public LocalizedText Title => new("Включить режим игры Windows", "Turn on Windows Game Mode");
    public bool RequiresElevation => false;
    public bool RequiresRestart => false;

    public IReadOnlyList<PlannedChange> BuildPlan(IReadOnlyDictionary<string, string> parameters) => new[]
    {
        new PlannedChange(
            _factory.Create(RegistryAllowlist.GameBar, "AutoGameModeEnabled", SettingValueKind.DWord, @"HKCU\Software\Microsoft\GameBar → AutoGameModeEnabled"),
            _ => SettingValue.DWord(1),
            new LocalizedText("AutoGameModeEnabled = 1 (режим игры включён)", "AutoGameModeEnabled = 1 (Game Mode on)"))
    };
}

public sealed class EnableWindowedOptimizationsAction : IChangeAction
{
    public const string Token = "SwapEffectUpgradeEnable";
    private readonly ITargetFactory _factory;
    public EnableWindowedOptimizationsAction(ITargetFactory factory) => _factory = factory;

    public string Id => ActionIds.EnableWindowedOptimizations;
    public int Version => 1;
    public LocalizedText Title => new("Включить оптимизацию для игр в оконном режиме", "Enable optimizations for windowed games");
    public bool RequiresElevation => false;
    public bool RequiresRestart => true;

    public IReadOnlyList<PlannedChange> BuildPlan(IReadOnlyDictionary<string, string> parameters) => new[]
    {
        new PlannedChange(
            _factory.Create(RegistryAllowlist.DirectXUserGpuPreferences, "DirectXUserGlobalSettings", SettingValueKind.String,
                @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences → DirectXUserGlobalSettings"),
            current => SettingValue.Str(DxPreferenceString.Set(current.Kind == SettingValueKind.String ? current.Data : null, Token, "1")),
            new LocalizedText("SwapEffectUpgradeEnable=1 (остальные параметры строки сохраняются)", "SwapEffectUpgradeEnable=1 (other tokens in the string are preserved)"))
    };
}

public sealed class SetHighPerformanceGpuAction : IChangeAction
{
    public const string Token = "GpuPreference";
    private readonly ITargetFactory _factory;
    public SetHighPerformanceGpuAction(ITargetFactory factory) => _factory = factory;

    public string Id => ActionIds.SetHighPerformanceGpu;
    public int Version => 1;
    public LocalizedText Title => new("Высокопроизводительный GPU для игры", "High-performance GPU for the game");
    public bool RequiresElevation => false;
    public bool RequiresRestart => true;

    public IReadOnlyList<PlannedChange> BuildPlan(IReadOnlyDictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue(ActionIds.ParamExecutablePath, out var exe))
            throw new ArgumentException("Executable path is required.");
        ValidateExecutablePath(exe);
        return new[]
        {
            new PlannedChange(
                _factory.Create(RegistryAllowlist.DirectXUserGpuPreferences, exe, SettingValueKind.String,
                    $@"HKCU\Software\Microsoft\DirectX\UserGpuPreferences → {exe}"),
                current => SettingValue.Str(DxPreferenceString.Set(current.Kind == SettingValueKind.String ? current.Data : null, Token, "2")),
                new LocalizedText($"GpuPreference=2 только для {Path.GetFileName(exe)}", $"GpuPreference=2 only for {Path.GetFileName(exe)}"))
        };
    }

    /// <summary>Only absolute paths to the supported games' executables are accepted.</summary>
    public static void ValidateExecutablePath(string exe)
    {
        if (string.IsNullOrWhiteSpace(exe) || exe.Length > 1024 || !Path.IsPathFullyQualified(exe) || exe.Contains('|'))
            throw new ArgumentException("Invalid executable path.");
        var name = Path.GetFileName(exe);
        if (!string.Equals(name, "cs2.exe", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "VALORANT-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only supported game executables are allowed.");
    }
}

public static class ActionRegistry
{
    public static IReadOnlyDictionary<string, IChangeAction> Create(ITargetFactory factory)
    {
        var list = new IChangeAction[]
        {
            new EnableGameModeAction(factory),
            new EnableWindowedOptimizationsAction(factory),
            new SetHighPerformanceGpuAction(factory)
        };
        return list.ToDictionary(a => a.Id, StringComparer.Ordinal);
    }
}
