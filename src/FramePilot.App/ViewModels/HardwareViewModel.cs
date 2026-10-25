using System.Globalization;
using FramePilot.App.Localization;
using FramePilot.Core;
using FramePilot.Core.Models;

namespace FramePilot.App.ViewModels;

public sealed record InfoRow(string Label, string Value, bool IsAvailable, string Tooltip);

public sealed record InfoSection(string Title, IReadOnlyList<InfoRow> Rows);

public sealed class HardwareViewModel : ObservableObject, IPage
{
    private readonly Workspace _w;

    public HardwareViewModel(Workspace w)
    {
        _w = w;
        _w.Changed += (_, _) => Raise(nameof(Sections));
    }

    public bool HasSnapshot => _w.Snapshot is not null;

    public void OnNavigatedTo()
    {
        Raise(nameof(Sections));
        Raise(nameof(HasSnapshot));
    }

    private static string Gb(long b) => (b / 1073741824.0).ToString("0.#", CultureInfo.CurrentCulture) + " GB";

    private static InfoRow Row<T>(string labelKey, Measured<T> m, Func<T, string>? fmt = null)
    {
        if (m.IsAvailable && m.Value is not null)
        {
            var text = fmt is null ? Convert.ToString(m.Value, CultureInfo.CurrentCulture) ?? "" : fmt(m.Value);
            var tip = Loc.T("Hw_Source") + ": " + m.Source + (m.Note is null ? "" : Environment.NewLine + m.Note);
            return new InfoRow(Loc.T(labelKey), text, true, tip);
        }
        return new InfoRow(Loc.T(labelKey), Loc.T("Unavailable"), false,
            Loc.T("Hw_Reason") + ": " + m.UnavailableReason + Environment.NewLine + Loc.T("Hw_Source") + ": " + m.Source);
    }

    private static InfoRow Plain(string labelKey, string value) => new(Loc.T(labelKey), value, true, "");

    private static string OnOff(bool b) => Loc.T(b ? "On" : "Off");

    public IReadOnlyList<InfoSection> Sections
    {
        get
        {
            var s = _w.Snapshot;
            if (s is null) return Array.Empty<InfoSection>();
            var list = new List<InfoSection>
            {
                new(Loc.T("Hw_Windows"), new[]
                {
                    Row("Hw_Product", s.Os.ProductName),
                    Row("Hw_Version", s.Os.DisplayVersion),
                    Plain("Hw_Build", s.Os.BuildString),
                    Row("Hw_Edition", s.Os.Edition),
                    Plain("Hw_Supported", s.Os.IsWindows11 && s.Os.Is64BitOs ? Loc.T("Yes") : Loc.T("Hw_NotSupported"))
                }),
                new(Loc.T("Hw_Cpu"), new[]
                {
                    Row("Hw_Model", s.Cpu.Name),
                    Row("Hw_Cores", s.Cpu.PhysicalCores),
                    Row("Hw_Threads", s.Cpu.LogicalProcessors),
                    Row("Hw_MaxClock", s.Cpu.MaxClockMhz, v => $"{v} MHz"),
                    Row("Hw_Load", s.Cpu.LoadPercent, v => $"{v:0} %")
                })
            };

            foreach (var g in s.Gpus)
                list.Add(new InfoSection(Loc.T("Hw_Gpu"), new[]
                {
                    Row("Hw_Model", g.Name),
                    Plain("Hw_Vendor", g.Vendor.ToString()),
                    Row("Hw_Driver", g.DriverVersion),
                    Row("Hw_DriverDate", g.DriverDate, d => d.ToString("d", CultureInfo.CurrentCulture)),
                    Row("Hw_Vram", g.DedicatedMemoryBytes, Gb),
                    Row("Hw_Integrated", g.LikelyIntegrated, b => (b ? Loc.T("Yes") : Loc.T("No")) + " · " + Loc.T("Hw_Heuristic"))
                }));

            var mem = new List<InfoRow>
            {
                Row("Hw_Total", s.Memory.TotalBytes, Gb),
                Row("Hw_Free", s.Memory.AvailableBytes, Gb),
                Plain("Hw_Modules", s.Memory.Modules.Count.ToString(CultureInfo.CurrentCulture)),
                Row("Hw_Slots", s.Memory.SlotsTotal)
            };
            foreach (var m in s.Memory.Modules)
            {
                var slot = m.Slot.IsAvailable ? m.Slot.Value : "?";
                mem.Add(Row("Hw_Module", m.CapacityBytes, b =>
                    $"{slot}: {Gb(b)} · {(m.RatedSpeedMts.IsAvailable ? m.RatedSpeedMts.Value + " MT/s" : "—")} → {(m.ConfiguredSpeedMts.IsAvailable ? m.ConfiguredSpeedMts.Value + " MT/s" : "—")} · {(m.PartNumber.IsAvailable ? m.PartNumber.Value : "")}"));
            }
            mem.Add(Plain("Hw_XmpNote", Loc.T("Hw_XmpNoteText")));
            list.Add(new InfoSection(Loc.T("Hw_Memory"), mem));

            foreach (var d in s.Disks)
            {
                var rows = new List<InfoRow>
                {
                    Row("Hw_Model", d.FriendlyName),
                    Plain("Hw_Type", d.MediaType.ToString()),
                    Row("Hw_Bus", d.BusType),
                    Row("Hw_Health", d.HealthStatus),
                    Row("Hw_Size", d.SizeBytes, Gb)
                };
                foreach (var v in d.Volumes)
                    rows.Add(Row("Hw_Volume", v.FreeBytes, f => $"{v.Letter} — {Gb(f)} {Loc.T("Hw_FreeOf")} {(v.TotalBytes.IsAvailable ? Gb(v.TotalBytes.Value) : "?")}"));
                list.Add(new InfoSection(Loc.T("Hw_Disk"), rows));
            }

            foreach (var d in s.Displays)
                list.Add(new InfoSection(Loc.T("Hw_Display") + (d.IsPrimary ? " · " + Loc.T("Hw_Primary") : ""), new[]
                {
                    Plain("Hw_Device", d.DeviceName),
                    Row("Hw_Adapter", d.AdapterName),
                    Row("Hw_Resolution", d.Width, w => $"{w} × {(d.Height.IsAvailable ? d.Height.Value : 0)}"),
                    Row("Hw_Refresh", d.CurrentRefreshHz, h => $"{h} Hz"),
                    Row("Hw_MaxRefresh", d.MaxRefreshHzAtCurrentResolution, h => $"{h} Hz"),
                    Row("Hw_MaxRefreshAny", d.MaxRefreshHzAnyMode, h => $"{h} Hz")
                }));

            list.Add(new InfoSection(Loc.T("Hw_Settings"), new[]
            {
                Row("Hw_GameMode", s.Settings.GameMode, OnOff),
                Row("Hw_Hags", s.Settings.HardwareAcceleratedGpuScheduling, OnOff),
                Row("Hw_Windowed", s.Settings.WindowedGameOptimizations, OnOff),
                Row("Hw_Power", s.Settings.ActivePowerScheme, p => p.Name),
                Row("Hw_Hvci", s.Settings.MemoryIntegrity, OnOff),
                Row("Hw_Capture", s.Settings.BackgroundCapture, OnOff)
            }));

            var bg = new List<InfoRow> { Row("Hw_TotalCpu", s.Background.TotalCpuPercent, v => $"{v:0} %") };
            foreach (var p in s.Background.TopProcesses.Take(8))
                bg.Add(new InfoRow(p.Name, $"{p.CpuPercent:0.0} % CPU · {Gb(p.WorkingSetBytes)}", true, Loc.T("Hw_BgTip")));
            list.Add(new InfoSection(Loc.F("Hw_Background", s.Background.WindowSeconds.ToString("0", CultureInfo.CurrentCulture)), bg));

            if (s.CollectorErrors.Count > 0)
                list.Add(new InfoSection(Loc.T("Hw_Errors"), s.CollectorErrors.Select(e => new InfoRow("—", e, false, e)).ToList()));
            return list;
        }
    }
}
