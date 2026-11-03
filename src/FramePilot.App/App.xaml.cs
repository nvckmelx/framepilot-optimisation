using System.Windows;
using System.Windows.Media;
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

        var designPreview = e.Args.Any(a => a.Equals("--design-preview", StringComparison.OrdinalIgnoreCase));
        ConfigureFonts();

        _single = SingleInstance.TryAcquire(designPreview);
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
            services = new AppServices(designPreview);
        }
        catch (Exception ex)
        {
            MessageBox.Show("TuneFPS could not open its local database:\n" + ex.Message, "TuneFPS", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        ErrorReporter.LogDirectory = services.LogsDirectory;
        ErrorReporter.Log("start", $"TuneFPS {services.AppVersion} started{(designPreview ? " (design preview)" : "")}");
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

    /// <summary>
    /// Embedded brand fonts (OFL 1.1, Assets/Fonts). Archivo Black has no Cyrillic, so Cyrillic display text falls back
    /// per glyph to "TuneFPS Display Cyrillic" (Inter Tight ExtraBold renamed to weight 400, so WPF never synthesises bold).
    /// </summary>
    private void ConfigureFonts()
    {
        try
        {
            var baseUri = new Uri("pack://application:,,,/");
            Resources["UiFont"] = new FontFamily(baseUri, "./Assets/Fonts/#Inter, Segoe UI");
            Resources["DisplayFont"] = new FontFamily(baseUri, "./Assets/Fonts/#Archivo Black, ./Assets/Fonts/#TuneFPS Display Cyrillic, Segoe UI Black");
        }
        catch (Exception ex)
        {
            ErrorReporter.Log("fonts", ex.Message);
        }
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

    public static SingleInstance? TryAcquire(bool designPreview = false)
    {
        // The mutex name is kept from 1.0.x so an old and a new build never run against the same journal at once.
        var m = new Mutex(true, designPreview ? @"Local\FramePilot.SingleInstance.DesignPreview" : @"Local\FramePilot.SingleInstance", out var created);
        if (created) return new SingleInstance(m);
        m.Dispose();
        DialogWindowFallback();
        return null;
    }

    private static void DialogWindowFallback() =>
        MessageBox.Show("TuneFPS is already running.", "TuneFPS", MessageBoxButton.OK, MessageBoxImage.Information);

    public void Dispose()
    {
        try { _mutex.ReleaseMutex(); } catch { /* not owned */ }
        _mutex.Dispose();
    }
}
