using System.Globalization;
using FramePilot.Core.Changes;
using Microsoft.Win32;

namespace FramePilot.Optimization;

/// <summary>
/// A single HKCU registry value. Key format: <c>HKCU\{subkey}|{valueName}|{kind}</c>.
/// Only current-user locations on the allowlist can be created, so the main app never needs elevation for them.
/// </summary>
public sealed class RegistryValueTarget : ISettingTarget
{
    public string SubKey { get; }
    public string ValueName { get; }
    public SettingValueKind Kind { get; }

    public RegistryValueTarget(string subKey, string valueName, SettingValueKind kind, string description)
    {
        if (!RegistryAllowlist.IsAllowed(subKey))
            throw new UnauthorizedAccessException($"Registry location is not on the FramePilot allowlist: {subKey}");
        if (kind is not (SettingValueKind.String or SettingValueKind.DWord))
            throw new ArgumentOutOfRangeException(nameof(kind));
        SubKey = subKey;
        ValueName = valueName;
        Kind = kind;
        Description = description;
    }

    public string Key => $"HKCU\\{SubKey}|{ValueName}|{Kind}";
    public string Description { get; }

    public SettingValue Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: false);
        if (key is null) return SettingValue.Absent;
        var raw = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (raw is null) return SettingValue.Absent;
        var kind = key.GetValueKind(ValueName);
        return kind switch
        {
            RegistryValueKind.DWord => new SettingValue(true, SettingValueKind.DWord, Convert.ToUInt32(unchecked((uint)(int)raw)).ToString(CultureInfo.InvariantCulture)),
            RegistryValueKind.QWord => new SettingValue(true, SettingValueKind.QWord, Convert.ToUInt64(unchecked((ulong)(long)raw)).ToString(CultureInfo.InvariantCulture)),
            RegistryValueKind.String or RegistryValueKind.ExpandString => new SettingValue(true, SettingValueKind.String, raw.ToString()),
            _ => new SettingValue(true, SettingValueKind.String, raw.ToString())
        };
    }

    public void Write(SettingValue value)
    {
        if (!value.Exists)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(SubKey, writable: true);
            existing?.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(SubKey, writable: true)
                        ?? throw new IOException($"Cannot open HKCU\\{SubKey} for writing.");
        switch (value.Kind)
        {
            case SettingValueKind.DWord:
                var u = uint.Parse(value.Data ?? "0", CultureInfo.InvariantCulture);
                key.SetValue(ValueName, unchecked((int)u), RegistryValueKind.DWord);
                break;
            case SettingValueKind.QWord:
                var q = ulong.Parse(value.Data ?? "0", CultureInfo.InvariantCulture);
                key.SetValue(ValueName, unchecked((long)q), RegistryValueKind.QWord);
                break;
            case SettingValueKind.String:
                key.SetValue(ValueName, value.Data ?? "", RegistryValueKind.String);
                break;
            default:
                throw new InvalidOperationException("Unsupported value kind.");
        }
    }
}

public static class RegistryAllowlist
{
    public const string GameBar = @"Software\Microsoft\GameBar";
    public const string DirectXUserGpuPreferences = @"Software\Microsoft\DirectX\UserGpuPreferences";

    private static readonly string[] Allowed = { GameBar, DirectXUserGpuPreferences };

    public static bool IsAllowed(string subKey) =>
        Allowed.Any(a => string.Equals(a, subKey.Trim('\\'), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Resolves journal keys back to registry targets. Refuses anything outside the allowlist.</summary>
public sealed class RegistryTargetResolver : ISettingTargetResolver
{
    public ISettingTarget? Resolve(string key)
    {
        if (!key.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = key.Substring(5).Split('|');
        if (parts.Length != 3) return null;
        if (!Enum.TryParse<SettingValueKind>(parts[2], out var kind)) return null;
        if (!RegistryAllowlist.IsAllowed(parts[0])) return null;
        if (parts[1].Length == 0 || parts[1].Length > 1024) return null;
        try
        {
            return new RegistryValueTarget(parts[0], parts[1], kind, $"HKCU\\{parts[0]} → {parts[1]}");
        }
        catch
        {
            return null;
        }
    }
}
