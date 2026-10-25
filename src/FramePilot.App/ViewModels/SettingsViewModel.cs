using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using FramePilot.App.Localization;
using FramePilot.App.Services;
using FramePilot.App.Views;
using FramePilot.Core.Models;
using FramePilot.Diagnostics;
using FramePilot.Licensing;
using FramePilot.Reports;
using FramePilot.Telemetry;
using Microsoft.Win32;

namespace FramePilot.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject, IPage
{
    private readonly MainViewModel _main;
    private string _licenseInput = "";

    private Workspace W => _main.Workspace;
    private AppServices App => _main.Workspace.App;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        _licenseInput = App.Settings.LicenseKey ?? "";
        BrowsePresentMonCommand = new RelayCommand(BrowsePresentMon);
        ClearPresentMonCommand = new RelayCommand(() => { App.Settings.PresentMonPath = null; RaiseAll(); });
        OpenPresentMonSiteCommand = new RelayCommand(() => Shell.Open("https://github.com/GameTechDev/PresentMon/releases"));
        ActivateCommand = new RelayCommand(Activate);
        CopyMachineCodeCommand = new RelayCommand(() => Clipboard.SetText(App.MachineCode));
        ExportJsonCommand = new RelayCommand(() => Export(json: true));
        ExportHtmlCommand = new RelayCommand(() => Export(json: false));
        OpenDataFolderCommand = new RelayCommand(() => Shell.Open(App.DataDirectory));
        DeleteDataCommand = new RelayCommand(DeleteData);
    }

    public ICommand BrowsePresentMonCommand { get; }
    public ICommand ClearPresentMonCommand { get; }
    public ICommand OpenPresentMonSiteCommand { get; }
    public ICommand ActivateCommand { get; }
    public ICommand CopyMachineCodeCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand ExportHtmlCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand DeleteDataCommand { get; }

    public bool IsRussian
    {
        get => Loc.Instance.Language == "ru";
        set { if (value) SetLanguage("ru"); }
    }

    public bool IsEnglish
    {
        get => Loc.Instance.Language == "en";
        set { if (value) SetLanguage("en"); }
    }

    private void SetLanguage(string lang)
    {
        App.Settings.Language = lang;
        Loc.Instance.Language = lang;
        RaiseAll();
    }

    public string PresentMonStatus
    {
        get
        {
            var p = W.Capture.PresentMonPath;
            return p is null ? Loc.T("Set_PresentMonMissing") : $"{p} · v{PresentMonLocator.Version(p) ?? "?"}";
        }
    }

    public bool ElevatedCapture
    {
        get => App.Settings.ElevatedCapture;
        set { App.Settings.ElevatedCapture = value; Raise(); }
    }

    public bool DemoMode
    {
        get => App.Settings.DemoMode;
        set
        {
            if (App.Settings.DemoMode == value) return;
            App.Settings.DemoMode = value;
            Raise();
            _main.RefreshDemoState();
        }
    }

    public string MachineCode => App.MachineCode;

    public string LicenseInput { get => _licenseInput; set => Set(ref _licenseInput, value); }

    public string LicenseStatusText
    {
        get
        {
            var l = App.License;
            var text = Loc.T("License_" + l.Status);
            if (l.Payload is { } p) text += $" · {p.Tier} · {Loc.T("License_Until")} {p.ExpiresAt:yyyy-MM-dd}";
            return text;
        }
    }

    public bool IsPro => App.License.IsPro;
    public string Version => App.AppVersion;
    public string DataFolder => App.DataDirectory;

    public void OnNavigatedTo() => RaiseAll();

    private void RaiseAll()
    {
        foreach (var n in new[]
                 {
                     nameof(IsRussian), nameof(IsEnglish), nameof(PresentMonStatus), nameof(ElevatedCapture), nameof(DemoMode),
                     nameof(LicenseStatusText), nameof(IsPro), nameof(LicenseInput)
                 })
            Raise(n);
    }

    private void BrowsePresentMon()
    {
        var dlg = new OpenFileDialog { Filter = "PresentMon (*.exe)|*.exe", Title = Loc.T("Set_PresentMonPick") };
        if (dlg.ShowDialog() != true) return;
        if (!Path.GetFileName(dlg.FileName).StartsWith("PresentMon", StringComparison.OrdinalIgnoreCase))
        {
            DialogWindow.Show(Loc.T("Set_PresentMon"), Loc.T("Set_PresentMonWrongFile"), DialogKind.Warning);
            return;
        }
        App.Settings.PresentMonPath = dlg.FileName;
        RaiseAll();
        W.NotifyChanged();
    }

    private void Activate()
    {
        var state = App.SetLicenseKey(LicenseInput);
        App.Store.Audit("license", state.Status.ToString());
        RaiseAll();
        W.NotifyChanged();
        DialogWindow.Show(Loc.T("License_Title"), LicenseStatusText, state.Status is LicenseStatus.Valid or LicenseStatus.Free ? DialogKind.Info : DialogKind.Warning);
    }

    private void Export(bool json)
    {
        var sessions = W.Sessions.Where(s => s.IsDemo == W.IsDemo).ToList();
        var target = sessions.Where(s => s.Game == W.TargetGame).ToList();
        var baseline = target.Where(s => s.Group == RunGroup.Baseline).ToList();
        var after = target.Where(s => s.Group == RunGroup.After).ToList();
        var comparison = baseline.Count + after.Count > 0 ? SessionComparer.Compare(baseline, after) : null;
        var report = ReportBuilder.Build(App.AppVersion, Loc.Instance.Language, W.Snapshot, sessions, W.Recommendations, comparison, App.Store.GetAll());

        var dlg = new SaveFileDialog
        {
            FileName = $"FramePilot-report-{DateTime.Now:yyyyMMdd-HHmm}{(W.IsDemo ? "-DEMO" : "")}.{(json ? "json" : "html")}",
            Filter = json ? "JSON (*.json)|*.json" : "HTML (*.html)|*.html"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            if (json)
            {
                File.WriteAllText(dlg.FileName, ReportBuilder.ToJson(report), new UTF8Encoding(false));
            }
            else
            {
                var latest = target.OrderByDescending(s => s.StartedAt).FirstOrDefault();
                var frames = latest is null ? null : CaptureService.LoadFrames(latest);
                File.WriteAllText(dlg.FileName, ReportBuilder.ToHtml(report, frames), new UTF8Encoding(false));
                Shell.Open(dlg.FileName);
            }
            App.Store.Audit("export", dlg.FileName);
        }
        catch (Exception ex)
        {
            DialogWindow.Show(Loc.T("Export_Title"), ex.Message, DialogKind.Error);
        }
    }

    private void DeleteData()
    {
        var active = App.Store.GetAll().Count(r => r.IsActive);
        if (!DialogWindow.Confirm(Loc.T("Set_DeleteTitle"), Loc.F("Set_DeleteBody", active), Loc.T("Delete"), Loc.T("Cancel"))) return;
        App.Store.DeleteLocalData(includeChangeJournal: false);
        try
        {
            foreach (var f in Directory.EnumerateFiles(App.SessionsDirectory, "*.csv")) File.Delete(f);
        }
        catch (Exception ex)
        {
            ErrorReporter.Log("delete", ex.Message);
        }
        _main.RefreshDemoState();
        DialogWindow.Show(Loc.T("Set_DeleteTitle"), Loc.T("Set_DeleteDone"), DialogKind.Info);
    }
}
