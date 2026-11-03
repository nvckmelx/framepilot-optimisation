using System.Globalization;
using FramePilot.Core.Models;

namespace FramePilot.App.Localization;

/// <summary>Human text for spike attributions, bottleneck and latency. Always "coincided with", never "caused by".</summary>
public static class InsightText
{
    private const double GB = 1024.0 * 1024 * 1024;

    public static string CauseTitle(SpikeCause cause) => Loc.T("Cause_" + cause);

    public static string? CauseDetail(SpikeAttribution s)
    {
        string F(string key, params object?[] a) => string.Format(CultureInfo.CurrentCulture, Loc.T(key), a);
        return s.Cause switch
        {
            SpikeCause.VramFull when s.Value is double v && s.Reference is double r => F("Detail_VramFull", v / GB, r / GB),
            SpikeCause.Paging when s.Value is double v => F("Detail_Paging", v, s.Reference ?? 0),
            SpikeCause.CpuThrottle when s.Value is double v => F("Detail_CpuThrottle", v, s.Reference ?? 100),
            SpikeCause.CpuThrottle => Loc.T("Detail_CpuLimit"),
            SpikeCause.GpuLimit => F("Detail_GpuLimit", s.Value ?? 0, s.Reference ?? 0),
            SpikeCause.BackgroundProcess => F("Detail_Background", s.ProcessName ?? "?", s.Value ?? 0),
            SpikeCause.CpuSideFrame or SpikeCause.GpuSideFrame when s.Value is double v => F("Detail_FrameSide", v, s.Reference ?? s.FrameTimeMs),
            SpikeCause.NoMatch => Loc.T("Detail_NoMatch"),
            _ => null
        };
    }

    /// <summary>"3 of 3 spikes coincided with something" style summary plus counts per cause, most frequent first.</summary>
    public static IReadOnlyList<(SpikeCause Cause, int Count)> Groups(SessionInsights? i) =>
        i is null ? Array.Empty<(SpikeCause, int)>() :
        i.Spikes.GroupBy(s => s.Cause).Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2).ToList();

    public static string Bottleneck(SessionInsights? i)
    {
        if (i is null || !i.Bottleneck.GpuBoundShare.TryGet(out var share)) return Loc.T("Bottleneck_Unavailable");
        var gpu = (int)Math.Round(share * 100);
        return string.Format(CultureInfo.CurrentCulture, Loc.T(gpu >= 60 ? "Bottleneck_Gpu" : gpu <= 40 ? "Bottleneck_Cpu" : "Bottleneck_Mixed"), gpu, 100 - gpu);
    }

    public static string Latency(SessionInsights? i)
    {
        if (i is null) return Loc.T("Latency_Unavailable");
        if (i.Latency.PcLatencyMs.TryGet(out var pc)) return string.Format(CultureInfo.CurrentCulture, Loc.T("Latency_Pc"), pc);
        if (i.Latency.DisplayLatencyMs.TryGet(out var d)) return string.Format(CultureInfo.CurrentCulture, Loc.T("Latency_Display"), d);
        return Loc.T("Latency_Unavailable");
    }
}
