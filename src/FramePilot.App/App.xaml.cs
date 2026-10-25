using System.Windows;
using System.Windows.Threading;
using FramePilot.App.Localization;
using FramePilot.App.Services;
using FramePilot.App.ViewModels;
using FramePilot.App.Views;

namespace FramePilot.App;

public partial class App : Application
{
    private SingleInstance? _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _single = SingleInstance.TryAcquire();
        if (_single is null)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ErrorReporter.Log("unobserved", args.Exception.ToString());
            args.SetObserved();
        };

        AppServices services;
        try
        {
            services = new AppServices();
        }
        catch (Exception ex)
        {
            MessageBox.Show("FramePilot could not open its local database:\n" + ex.Message, "FramePilot", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        ErrorReporter.LogDirectory = services.LogsDirectory;
        ErrorReporter.Log("start", $"FramePilot {services.AppVersion} started");
        Loc.Instance.Language = services.Settings.Language;

        // Reconcile journal entries left in intermediate states by a crash, power loss or reboot (read-only check).
        try
        {
            services.StartupRecovery = services.Changes.Recover();
            if (services.StartupRecovery.Examined > 0)
                services.Store.Audit("recovery", $"Examined {services.StartupRecovery.Examined}, applied {services.StartupRecovery.MarkedApplied}, failed {services.StartupRecovery.MarkedFailed}");
        }
        catch (Exception ex)
        {
            ErrorReporter.Log("recovery", ex.ToString());
        }

        var vm = new MainViewModel(services);
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        vm.Initialize();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _single?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorReporter.Report(e.Exception);
        e.Handled = true;
    }
}

/// <summary>Prevents two instances from writing the same journal concurrently.</summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private SingleInstance(Mutex m) => _mutex = m;

    public static SingleInstance? TryAcquire()
    {
        var m = new Mutex(true, @"Local\FramePilot.SingleInstance", out var created);
        if (created) return new SingleInstance(m);
        m.Dispose();
        DialogWindowFallback();
        return null;
    }

    private static void DialogWindowFallback() =>
        MessageBox.Show("FramePilot is already running.", "FramePilot", MessageBoxButton.OK, MessageBoxImage.Information);

    public void Dispose()
    {
        try { _mutex.ReleaseMutex(); } catch { /* not owned */ }
        _mutex.Dispose();
    }
}
