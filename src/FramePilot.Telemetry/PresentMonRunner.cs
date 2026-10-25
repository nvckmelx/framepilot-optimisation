using System.ComponentModel;
using System.Diagnostics;

namespace FramePilot.Telemetry;

public sealed record CaptureRequest(string ProcessName, int Seconds, string OutputCsvPath, bool Elevated);

public enum CaptureStatus
{
    Completed,
    PresentMonNotFound,
    ElevationDeclined,
    Failed,
    NoData,
    Cancelled
}

public sealed record CaptureOutcome(CaptureStatus Status, string? CsvPath, int? ExitCode, string? Message);

/// <summary>Finds a PresentMon console executable without downloading anything.</summary>
public static class PresentMonLocator
{
    public static string? Locate(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath)) return configuredPath;
        var baseDir = AppContext.BaseDirectory;
        foreach (var dir in new[] { Path.Combine(baseDir, "tools"), baseDir })
        {
            if (!Directory.Exists(dir)) continue;
            var exact = Path.Combine(dir, "PresentMon.exe");
            if (File.Exists(exact)) return exact;
            var any = Directory.EnumerateFiles(dir, "PresentMon*.exe")
                .Where(f => !Path.GetFileName(f).Contains("Service", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (any is not null) return any;
        }
        return null;
    }

    public static string? Version(string exePath)
    {
        try { return FileVersionInfo.GetVersionInfo(exePath).ProductVersion; }
        catch { return null; }
    }
}

/// <summary>
/// Runs the PresentMon console application with a fixed, validated argument set. PresentMon uses ETW and needs
/// administrator rights (or Performance Log Users membership); in elevated mode only this child process is elevated,
/// never FramePilot itself.
/// </summary>
public sealed class PresentMonRunner
{
    public const string SessionName = "FramePilotCapture";
    private readonly string _exe;

    public PresentMonRunner(string presentMonExe)
    {
        _exe = presentMonExe;
    }

    public static IReadOnlyList<string> BuildArguments(CaptureRequest request)
    {
        if (!IsValidProcessName(request.ProcessName)) throw new ArgumentException("Invalid process name.");
        if (request.Seconds is < 10 or > 1800) throw new ArgumentOutOfRangeException(nameof(request), "Capture length must be 10–1800 s.");
        if (!Path.IsPathFullyQualified(request.OutputCsvPath) || !request.OutputCsvPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Output path must be an absolute .csv path.");
        var exe = request.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? request.ProcessName : request.ProcessName + ".exe";
        return new[]
        {
            "--process_name", exe,
            "--output_file", request.OutputCsvPath,
            "--timed", request.Seconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--terminate_after_timed",
            "--terminate_on_proc_exit",
            "--stop_existing_session",
            "--session_name", SessionName,
            "--no_console_stats"
        };
    }

    public static bool IsValidProcessName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 128 &&
        name.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');

    public async Task<CaptureOutcome> CaptureAsync(CaptureRequest request, CancellationToken ct)
    {
        if (!File.Exists(_exe)) return new(CaptureStatus.PresentMonNotFound, null, null, "PresentMon executable not found.");
        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputCsvPath)!);
        if (File.Exists(request.OutputCsvPath)) File.Delete(request.OutputCsvPath);

        var args = BuildArguments(request);
        Process? process;
        try
        {
            process = Start(args, request.Elevated, hidden: true);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new(CaptureStatus.ElevationDeclined, null, null, "Administrator permission for PresentMon was declined.");
        }
        catch (Exception ex)
        {
            return new(CaptureStatus.Failed, null, null, ex.Message);
        }
        if (process is null) return new(CaptureStatus.Failed, null, null, "PresentMon did not start.");

        using (process)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(request.Seconds + 60));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Graceful stop: ask PresentMon to end the named ETW session so it can flush the CSV.
                await StopSessionAsync(request.Elevated).ConfigureAwait(false);
                try { await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { /* reported below */ }
                if (!process.HasExited)
                {
                    try { process.Kill(); } catch { /* elevated processes cannot be killed by a standard user */ }
                }
            }

            int? exitCode = null;
            try { if (process.HasExited) exitCode = process.ExitCode; } catch { /* exit code not accessible */ }

            var hasData = File.Exists(request.OutputCsvPath) && new FileInfo(request.OutputCsvPath).Length > 200;
            if (ct.IsCancellationRequested)
                return new(hasData ? CaptureStatus.Completed : CaptureStatus.Cancelled, hasData ? request.OutputCsvPath : null, exitCode,
                    hasData ? "Stopped early by the user." : "Cancelled before any frames were recorded.");
            if (!hasData)
                return new(exitCode is null or 0 ? CaptureStatus.NoData : CaptureStatus.Failed, null, exitCode,
                    exitCode is null or 0
                        ? "No frames were recorded. Make sure the game is running and rendering during the capture."
                        : $"PresentMon exited with code {exitCode}. It may need administrator rights.");
            return new(CaptureStatus.Completed, request.OutputCsvPath, exitCode, null);
        }
    }

    public async Task StopSessionAsync(bool elevated)
    {
        try
        {
            using var p = Start(new[] { "--terminate_existing_session", "--session_name", SessionName }, elevated, hidden: true);
            if (p is not null) await p.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token).ConfigureAwait(false);
        }
        catch
        {
            // best effort
        }
    }

    private Process? Start(IReadOnlyList<string> args, bool elevated, bool hidden)
    {
        if (elevated)
        {
            // runas requires ShellExecute, which takes a single argument string.
            var psi = new ProcessStartInfo(_exe)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(" ", args.Select(Quote)),
                WindowStyle = hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
            };
            return Process.Start(psi);
        }
        var std = new ProcessStartInfo(_exe)
        {
            UseShellExecute = false,
            CreateNoWindow = hidden,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };
        foreach (var a in args) std.ArgumentList.Add(a);
        return Process.Start(std);
    }

    internal static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.All(c => !char.IsWhiteSpace(c) && c != '"')) return arg;
        return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }
}
