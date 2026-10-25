using System.Diagnostics;
using System.Text.RegularExpressions;
using FramePilot.Core;
using FramePilot.Core.Models;
using Microsoft.Win32;

namespace FramePilot.GameAdapters;

public static class GameCatalog
{
    public const string Cs2Process = "cs2";
    public const string ValorantProcess = "VALORANT-Win64-Shipping";

    public static string ProcessName(GameId game) => game == GameId.Cs2 ? Cs2Process : ValorantProcess;
    public static string ExeName(GameId game) => ProcessName(game) + ".exe";
    public static string DisplayName(GameId game) => game == GameId.Cs2 ? "Counter-Strike 2" : "VALORANT";

    public static bool IsRunning(GameId game)
    {
        var procs = Process.GetProcessesByName(ProcessName(game));
        var running = procs.Length > 0;
        foreach (var p in procs) p.Dispose();
        return running;
    }
}

/// <summary>Detects CS2 (Steam) and VALORANT (Riot Client) installations by reading their own metadata files. Read-only.</summary>
public sealed class GameDetector
{
    public IReadOnlyList<DetectedGame> DetectAll() => new[] { DetectCs2(), DetectValorant() };

    public DetectedGame DetectCs2()
    {
        const string src = "Steam libraryfolders.vdf + appmanifest_730.acf";
        var running = GameCatalog.IsRunning(GameId.Cs2);
        try
        {
            var steam = SteamRoot();
            if (steam is null)
                return NotFound(GameId.Cs2, "Steam installation not found in HKCU\\Software\\Valve\\Steam", running);

            foreach (var lib in SteamLibraries(steam))
            {
                var manifest = Path.Combine(lib, "steamapps", "appmanifest_730.acf");
                if (!File.Exists(manifest)) continue;
                var state = Vdf.Parse(File.ReadAllText(manifest)).Child("AppState");
                var installDir = state?.Get("installdir");
                if (string.IsNullOrEmpty(installDir)) continue;
                var root = Path.Combine(lib, "steamapps", "common", installDir);
                var exe = Path.Combine(root, "game", "bin", "win64", "cs2.exe");
                var buildId = state?.Get("buildid");
                var version = string.IsNullOrEmpty(buildId)
                    ? Measured<string>.Missing("buildid not present in manifest", "appmanifest_730.acf")
                    : Measured<string>.Of("Steam build " + buildId, "appmanifest_730.acf buildid");
                return new DetectedGame(GameId.Cs2, File.Exists(exe), root, File.Exists(exe) ? exe : null, GameCatalog.Cs2Process,
                    src, version, ReadCs2VideoConfig(steam), running);
            }
            return NotFound(GameId.Cs2, "appmanifest_730.acf not found in any Steam library", running);
        }
        catch (Exception ex)
        {
            return NotFound(GameId.Cs2, ex.Message, running);
        }
    }

    public DetectedGame DetectValorant()
    {
        var running = GameCatalog.IsRunning(GameId.Valorant);
        try
        {
            string? root = null;
            var source = "";
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var settings = Path.Combine(programData, "Riot Games", "Metadata", "valorant.live", "valorant.live.product_settings.yaml");
            if (File.Exists(settings))
            {
                var m = Regex.Match(File.ReadAllText(settings), @"product_install_full_path:\s*""?([^""\r\n]+)""?");
                if (m.Success)
                {
                    root = m.Groups[1].Value.Trim().Replace('/', '\\');
                    source = "Riot Client valorant.live.product_settings.yaml";
                }
            }
            if (root is null)
            {
                var fallback = @"C:\Riot Games\VALORANT\live";
                if (Directory.Exists(fallback))
                {
                    root = fallback;
                    source = "Default Riot Games folder";
                }
            }
            if (root is null)
                return NotFound(GameId.Valorant, "Riot Client metadata for VALORANT not found", running);

            var exe = Path.Combine(root, "ShooterGame", "Binaries", "Win64", "VALORANT-Win64-Shipping.exe");
            var exists = File.Exists(exe);
            var version = exists
                ? Measured.Try<string>("FileVersionInfo.ProductVersion", () => FileVersionInfo.GetVersionInfo(exe).ProductVersion)
                : Measured<string>.Missing("Executable not found", "FileVersionInfo");
            return new DetectedGame(GameId.Valorant, exists, root, exists ? exe : null, GameCatalog.ValorantProcess, source, version,
                Measured<string>.Missing("VALORANT settings are stored by the game/account; FramePilot does not read or modify them", "—"),
                running);
        }
        catch (Exception ex)
        {
            return NotFound(GameId.Valorant, ex.Message, running);
        }
    }

    private static DetectedGame NotFound(GameId game, string reason, bool running) =>
        new(game, false, null, null, GameCatalog.ProcessName(game), reason,
            Measured<string>.Missing(reason, "Game detection"), Measured<string>.Missing(reason, "Game detection"), running);

    public static string? SteamRoot()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var p = k?.GetValue("SteamPath")?.ToString();
        if (string.IsNullOrEmpty(p)) return null;
        p = p.Replace('/', '\\');
        return Directory.Exists(p) ? p : null;
    }

    public static IReadOnlyList<string> SteamLibraries(string steamRoot)
    {
        var list = new List<string> { steamRoot };
        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) return list;
        list.AddRange(ParseLibraryFolders(File.ReadAllText(vdf)));
        return list.Where(d => Directory.Exists(d)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IEnumerable<string> ParseLibraryFolders(string text)
    {
        var root = Vdf.Parse(text);
        var folders = root.Child("libraryfolders") ?? root.Child("LibraryFolders");
        if (folders is null) yield break;
        foreach (var child in folders.Children.Values)
        {
            var path = child.Get("path");
            if (!string.IsNullOrEmpty(path)) yield return path;
        }
        // Very old format: "1" "D:\\SteamLibrary"
        foreach (var kv in folders.Values)
            if (int.TryParse(kv.Key, out _) && kv.Value.Contains(':')) yield return kv.Value;
    }

    /// <summary>Reports a few raw entries from the newest cs2_video.txt without interpreting or modifying them.</summary>
    private static Measured<string> ReadCs2VideoConfig(string steamRoot)
    {
        const string src = "Steam userdata/*/730/local/cfg/cs2_video.txt";
        try
        {
            var userdata = Path.Combine(steamRoot, "userdata");
            if (!Directory.Exists(userdata)) return Measured<string>.Missing("Steam userdata folder not found", src);
            var file = Directory.EnumerateDirectories(userdata)
                .Select(d => Path.Combine(d, "730", "local", "cfg", "cs2_video.txt"))
                .Where(f => File.Exists(f))
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .FirstOrDefault();
            if (file is null) return Measured<string>.Missing("cs2_video.txt not found", src);
            var root = Vdf.Parse(File.ReadAllText(file));
            var node = root.Children.Values.FirstOrDefault() ?? root;
            string[] keys = { "setting.defaultres", "setting.defaultresheight", "setting.fullscreen", "setting.mat_vsync", "setting.r_low_latency" };
            var parts = keys.Select(k => (k, v: node.Get(k))).Where(x => x.v is not null).Select(x => $"{x.k}={x.v}").ToList();
            return parts.Count == 0
                ? Measured<string>.Missing("Known keys not present in cs2_video.txt", src)
                : Measured<string>.Of(string.Join("; ", parts), src, "Raw values, not interpreted");
        }
        catch (Exception ex)
        {
            return Measured<string>.Missing(ex.Message, src);
        }
    }
}
