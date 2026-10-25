using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FramePilot.Licensing;

public enum LicenseTier
{
    Free,
    Pro,
    Founders
}

public sealed record LicensePayload(string LicenseId, string Email, LicenseTier Tier, string MachineCode, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

public enum LicenseStatus
{
    Free,
    Valid,
    Expired,
    WrongMachine,
    InvalidSignature,
    Malformed
}

public sealed record LicenseState(LicenseStatus Status, LicensePayload? Payload)
{
    public bool IsPro => Status == LicenseStatus.Valid && Payload is { Tier: LicenseTier.Pro or LicenseTier.Founders };
    public static readonly LicenseState FreeTier = new(LicenseStatus.Free, null);
}

/// <summary>
/// Offline license keys: "FP1." + base64url(payload JSON) + "." + base64url(ECDSA P-256 / SHA-256 signature).
/// The app only holds the public key. Rollback and diagnostics never depend on the license.
/// </summary>
public static class LicenseVerifier
{
    public const string Prefix = "FP1.";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static LicenseState Verify(string? key, string publicKeySpkiBase64, string machineCode, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(key)) return LicenseState.FreeTier;
        var parts = key.Trim().Split('.');
        if (parts.Length != 3 || parts[0] + "." != Prefix) return new(LicenseStatus.Malformed, null);
        byte[] payloadBytes, signature;
        try
        {
            payloadBytes = B64UrlDecode(parts[1]);
            signature = B64UrlDecode(parts[2]);
        }
        catch
        {
            return new(LicenseStatus.Malformed, null);
        }

        using var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeySpkiBase64), out _);
        }
        catch
        {
            return new(LicenseStatus.InvalidSignature, null);
        }
        if (!ecdsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256))
            return new(LicenseStatus.InvalidSignature, null);

        LicensePayload? payload;
        try { payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes, JsonOpts); }
        catch { return new(LicenseStatus.Malformed, null); }
        if (payload is null) return new(LicenseStatus.Malformed, null);
        if (!string.Equals(payload.MachineCode, machineCode, StringComparison.OrdinalIgnoreCase)) return new(LicenseStatus.WrongMachine, payload);
        if (payload.ExpiresAt < now) return new(LicenseStatus.Expired, payload);
        return new(LicenseStatus.Valid, payload);
    }

    public static string Sign(LicensePayload payload, ECDsa privateKey)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
        var sig = privateKey.SignData(bytes, HashAlgorithmName.SHA256);
        return Prefix + B64UrlEncode(bytes) + "." + B64UrlEncode(sig);
    }

    /// <summary>Short, non-reversible machine code derived from a stable machine identifier.</summary>
    public static string MachineCodeFrom(string machineIdentifier)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("FramePilot|" + machineIdentifier.Trim().ToLowerInvariant()));
        var hex = Convert.ToHexString(hash, 0, 10);
        return string.Join("-", Enumerable.Range(0, 5).Select(i => hex.Substring(i * 4, 4)));
    }

    public static string B64UrlEncode(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] B64UrlDecode(string s)
    {
        var b = s.Replace('-', '+').Replace('_', '/');
        b += (b.Length % 4) switch { 2 => "==", 3 => "=", 0 => "", _ => throw new FormatException("Bad base64url length") };
        return Convert.FromBase64String(b);
    }
}

/// <summary>What each tier unlocks. Diagnostics, history viewing and rollback are always free.</summary>
public static class Entitlements
{
    public const int FreeSessionHistory = 20;
    public static bool CanAutoApply(LicenseState s) => s.IsPro;
    public static bool CanExportHtml(LicenseState s) => true;
    public static bool CanRollback(LicenseState s) => true;
    public static int SessionHistoryLimit(LicenseState s) => s.IsPro ? int.MaxValue : FreeSessionHistory;
}
