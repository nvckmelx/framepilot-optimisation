using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using FramePilot.App.Views;

namespace FramePilot.App.Services;

public static class ErrorReporter
{
    public static string? LogDirectory { get; set; }

    public static void Log(string kind, string message)
    {
        try
        {
            if (LogDirectory is null) return;
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(Path.Combine(LogDirectory, "app.log"),
                $"{DateTimeOffset.Now:O} [{kind}] {message}{Environment.NewLine}");
        }
        catch
        {
            // logging must never crash the app
        }
    }

    public static void Report(Exception ex)
    {
        Log("error", ex.ToString());
        try
        {
            Application.Current?.Dispatcher.Invoke(() =>
                DialogWindow.Show(Localization.Loc.T("Error_Title"), Localization.Loc.T("Error_Body") + Environment.NewLine + Environment.NewLine + ex.Message,
                    DialogKind.Error));
        }
        catch
        {
            // ignore secondary failures
        }
    }
}

public enum RestorePointOutcome
{
    Created,
    NotCreated,
    Declined,
    HelperMissing,
    Failed
}

/// <summary>Starts the elevated helper for exactly one allowlisted verb. The main app stays non-elevated.</summary>
public static class RestorePointService
{
    public static async Task<RestorePointOutcome> CreateAsync()
    {
        var helper = Path.Combine(AppContext.BaseDirectory, "FramePilot.PrivilegedHelper.exe");
        if (!File.Exists(helper)) return RestorePointOutcome.HelperMissing;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(helper)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = "create-restore-point",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (p is null) return RestorePointOutcome.Failed;
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await p.WaitForExitAsync(cts.Token);
            return p.ExitCode switch
            {
                0 => RestorePointOutcome.Created,
                10 => RestorePointOutcome.NotCreated,
                _ => RestorePointOutcome.Failed
            };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return RestorePointOutcome.Declined;
        }
        catch (Exception ex)
        {
            ErrorReporter.Log("restore-point", ex.Message);
            return RestorePointOutcome.Failed;
        }
    }
}

public static class Shell
{
    public static void Open(string pathOrUrl)
    {
        try
        {
            Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            ErrorReporter.Log("shell", ex.Message);
        }
    }
}
