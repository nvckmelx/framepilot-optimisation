using System.Globalization;
using System.Net;
using System.Text;
using FramePilot.Core;
using FramePilot.Core.Changes;
using FramePilot.Core.Models;
using FramePilot.Diagnostics;

namespace FramePilot.Reports;

public sealed record ChangeSummary(Guid Id, Guid BatchId, string ActionId, int ActionVersion, string Target, string Original, string Planned, string State, DateTimeOffset UpdatedAt, string? Error);

public sealed record DiagnosticReport(
    string Schema,
    DateTimeOffset GeneratedAt,
    string AppVersion,
    bool IsDemo,
    string Language,
    SystemSnapshot? Snapshot,
    IReadOnlyList<SessionRecord> Sessions,
    IReadOnlyList<Recommendation> Recommendations,
    ComparisonResult? Comparison,
    IReadOnlyList<ChangeSummary> Changes,
    IReadOnlyDictionary<string, string> MetricDefinitions,
    IReadOnlyList<string> Limitations,
    FrameSeries? LatestFrames = null)
{
    public const string CurrentSchema = "framepilot.report/1";
}

/// <summary>Histogram bin of frame times: [FromMs, ToMs). The last bin is open-ended.</summary>
public sealed record HistogramBin(double FromMs, double ToMs, int Count);

/// <summary>
/// Compact frame-time series for charts. Frames are grouped into buckets of <see cref="BucketSize"/> consecutive frames;
/// each bucket stores its maximum (so spikes stay visible) and mean frame time. The histogram is computed from all frames.
/// </summary>
public sealed record FrameSeries(
    Guid SessionId,
    int SourceFrameCount,
    int BucketSize,
    double DurationMs,
    IReadOnlyList<double> MaxFrameTimeMs,
    IReadOnlyList<double> MeanFrameTimeMs,
    IReadOnlyList<HistogramBin> Histogram)
{
    public const int MaxPoints = 1500;

    public static FrameSeries? From(Guid sessionId, IReadOnlyList<FrameSample>? frames)
    {
        if (frames is null) return null;
        var times = frames.Select(f => f.FrameTimeMs).Where(t => double.IsFinite(t) && t > 0).ToList();
        if (times.Count < 2) return null;
        var bucket = Math.Max(1, (int)Math.Ceiling(times.Count / (double)MaxPoints));
        var max = new List<double>();
        var mean = new List<double>();
        for (var i = 0; i < times.Count; i += bucket)
        {
            var n = Math.Min(bucket, times.Count - i);
            double m = 0, sum = 0;
            for (var j = i; j < i + n; j++)
            {
                if (times[j] > m) m = times[j];
                sum += times[j];
            }
            max.Add(Math.Round(m, 3));
            mean.Add(Math.Round(sum / n, 3));
        }
        // 0.5 ms bins up to 50 ms, then one open bin.
        const double width = 0.5, limit = 50;
        var counts = new int[(int)(limit / width) + 1];
        foreach (var t in times) counts[Math.Min(counts.Length - 1, (int)(t / width))]++;
        var bins = counts.Select((c, i) => new HistogramBin(i * width, i == counts.Length - 1 ? double.PositiveInfinity : (i + 1) * width, c))
            .ToList();
        // Trim empty tail bins (keep the open bin only if it has frames).
        while (bins.Count > 1 && bins[^1].Count == 0) bins.RemoveAt(bins.Count - 1);
        return new FrameSeries(sessionId, times.Count, bucket, times.Sum(), max, mean, bins);
    }
}

public static class ReportBuilder
{
    public static IReadOnlyDictionary<string, string> MetricDefinitions { get; } = new Dictionary<string, string>
    {
        ["averageFps"] = "1000 * N / sum(frameTimeMs)",
        ["medianFrameTimeMs"] = "median of frame times",
        ["p99FrameTimeMs"] = "nearest-rank 99th percentile of frame time: sorted[ceil(0.99*N)-1]",
        ["onePercentLowFps"] = "1000 / mean(slowest k frame times), k = max(1, ceil(0.01*N)); not 1000/P99",
        ["spike"] = "frame time > 2 * median frame time",
        ["gpuBusyRatio"] = "median of MsGPUBusy / frame time per frame (PresentMon)",
        ["pcLatency"] = "PresentMon MsPCLatency when reported; never shown as click-to-photon latency",
        ["frameTimeSource"] = "PresentMon column MsBetweenPresents, else MsBetweenAppStart, else FrameTime; the column used is stored per session"
    };

    public static readonly string[] StandardLimitations =
    {
        "Frame time is not input lag. Full click-to-photon latency requires dedicated hardware measurement and is not reported.",
        "Static PC inventory is not the same as analysing game behaviour; game sessions are required for performance conclusions.",
        "Matches with different players, maps and situations are not a laboratory A/B test.",
        "Values marked unavailable were not obtained; FramePilot never substitutes estimates for them."
    };

    public static DiagnosticReport Build(string appVersion, string language, SystemSnapshot? snapshot, IReadOnlyList<SessionRecord> sessions,
        IReadOnlyList<Recommendation> recommendations, ComparisonResult? comparison, IReadOnlyList<ChangeRecord> changes,
        SessionRecord? latestSession = null, IReadOnlyList<FrameSample>? latestFrames = null)
    {
        var demo = (snapshot?.IsDemo ?? false) || sessions.Any(s => s.IsDemo);
        return new DiagnosticReport(DiagnosticReport.CurrentSchema, DateTimeOffset.Now, appVersion, demo, language, snapshot, sessions,
            recommendations, comparison,
            changes.Select(c => new ChangeSummary(c.Id, c.BatchId, c.ActionId, c.ActionVersion, c.TargetDescription,
                c.Original.Display, c.Planned.Display, c.State.ToString(), c.UpdatedAt, c.Error)).ToList(),
            MetricDefinitions, StandardLimitations,
            latestSession is null ? null : FrameSeries.From(latestSession.Id, latestFrames));
    }

    public static string ToJson(DiagnosticReport report) => Json.Serialize(report, indented: true);

    public static string ToHtml(DiagnosticReport r, IReadOnlyList<FrameSample>? latestFrames)
    {
        var ru = r.Language.StartsWith("ru", StringComparison.OrdinalIgnoreCase);
        string T(string ruText, string enText) => ru ? ruText : enText;
        string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        string M<TV>(Measured<TV> m, Func<TV, string>? fmt = null) =>
            m.IsAvailable && m.Value is not null
                ? E(fmt is null ? Convert.ToString(m.Value, CultureInfo.InvariantCulture) : fmt(m.Value))
                : $"<span class=na title=\"{E(m.UnavailableReason)} — {E(m.Source)}\">{T("Недоступно", "Unavailable")}</span>";

        var sb = new StringBuilder();
        sb.Append($$"""
            <!doctype html><html lang="{{(ru ? "ru" : "en")}}"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>FramePilot — {{T("Диагностический отчёт", "Diagnostic report")}}</title>
            <style>
            :root{--bg:#101412;--panel:#1B211D;--line:#39413B;--text:#F4F6EF;--muted:#AEB8AE;--accent:#CBFF4D;--warn:#ffb547;--bad:#ff6b5e}
            *{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:16px/1.6 Arial,Helvetica,sans-serif}
            main{max-width:1040px;margin:0 auto;padding:32px 20px 64px}h1{font-size:28px;margin:0 0 4px}h2{font-size:18px;margin:36px 0 12px;color:var(--accent);letter-spacing:.04em;text-transform:uppercase}
            .muted{color:var(--muted)}.na{color:var(--muted);border-bottom:1px dotted var(--muted);cursor:help}
            .demo{background:var(--accent);color:#101412;font-weight:700;padding:10px 14px;border-radius:4px;margin:16px 0}
            table{width:100%;border-collapse:collapse;background:var(--panel);border:1px solid var(--line);border-radius:8px;overflow:hidden}
            th,td{text-align:left;padding:9px 12px;border-bottom:1px solid var(--line);vertical-align:top}th{color:var(--muted);font-weight:600;width:34%}
            .card{background:var(--panel);border:1px solid var(--line);border-radius:8px;padding:24px;margin:12px 0}
            .tag{display:inline-block;font-size:12px;padding:2px 8px;border-radius:4px;border:1px solid var(--line);color:var(--muted);margin-right:6px}
            .tag.auto{border-color:var(--accent);color:var(--accent)}svg{width:100%;height:220px;background:var(--panel);border:1px solid var(--line);border-radius:10px}
            code{color:var(--accent)}
            </style></head><body><main>
            <h1>FRAMEPILOT</h1><div class="muted">{{T("Диагностический отчёт", "Diagnostic report")}} · {{E(r.GeneratedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))}} · v{{E(r.AppVersion)}}</div>
            """);
        if (r.IsDemo)
            sb.Append($"<div class=demo>{T("ДЕМО-ДАННЫЕ: этот отчёт содержит демонстрационные значения, а не измерения вашего ПК.", "DEMO DATA: this report contains demonstration values, not measurements of your PC.")}</div>");

        if (r.Snapshot is { } s)
        {
            sb.Append($"<h2>{T("Компьютер", "Computer")}</h2><table>");
            void Row(string k, string v) => sb.Append($"<tr><th>{E(k)}</th><td>{v}</td></tr>");
            Row("Windows", $"{M(s.Os.ProductName)} · {M(s.Os.DisplayVersion)} · build {E(s.Os.BuildString)}");
            Row(T("Процессор", "CPU"), $"{M(s.Cpu.Name)} · {M(s.Cpu.PhysicalCores)}C/{M(s.Cpu.LogicalProcessors)}T");
            foreach (var g in s.Gpus)
                Row(T("Видеокарта", "GPU"), $"{M(g.Name)} · {T("драйвер", "driver")} {M(g.DriverVersion)} · VRAM {M(g.DedicatedMemoryBytes, b => $"{b / 1073741824.0:0.#} GB")}");
            Row(T("Память", "Memory"), $"{M(s.Memory.TotalBytes, b => $"{b / 1073741824.0:0.#} GB")} · {T("модулей", "modules")}: {s.Memory.Modules.Count}");
            foreach (var d in s.Displays)
                Row(T("Монитор", "Display"), $"{E(d.DeviceName)} · {M(d.Width)}×{M(d.Height)} @ {M(d.CurrentRefreshHz)} Hz ({T("макс.", "max")} {M(d.MaxRefreshHzAtCurrentResolution)} Hz)");
            foreach (var d in s.Disks)
                Row(T("Накопитель", "Drive"), $"{M(d.FriendlyName)} · {E(d.MediaType.ToString())} · {M(d.BusType)} · {E(string.Join(", ", d.Volumes.Select(v => v.Letter)))}");
            Row(T("Режим игры", "Game Mode"), M(s.Settings.GameMode, b => b ? "on" : "off"));
            Row("HAGS", M(s.Settings.HardwareAcceleratedGpuScheduling, b => b ? "on" : "off"));
            Row(T("Оптимизация оконных игр", "Windowed game optimizations"), M(s.Settings.WindowedGameOptimizations, b => b ? "on" : "off"));
            Row(T("Схема питания", "Power plan"), M(s.Settings.ActivePowerScheme, p => p.Name));
            Row(T("Целостность памяти", "Memory Integrity"), M(s.Settings.MemoryIntegrity, b => b ? "on" : "off"));
            sb.Append("</table>");
        }

        if (r.Sessions.Count > 0)
        {
            sb.Append($"<h2>{T("Игровые сессии", "Game sessions")}</h2><table><tr><th>{T("Сессия", "Session")}</th><td><b>{T("Средний FPS · P99 · 1% low · скачки", "Avg FPS · P99 · 1% low · spikes")}</b></td></tr>");
            foreach (var x in r.Sessions)
            {
                var st = x.Stats;
                sb.Append($"<tr><th>{E(x.Game.ToString())} · {E(x.StartedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))} · {E(x.Group.ToString())}{(x.IsDemo ? " · DEMO" : "")}</th>" +
                          $"<td>{st.AverageFps:0} · {st.P99FrameTimeMs:0.00} ms · {st.OnePercentLowFps:0} · {st.SpikeCount} ({100.0 * st.SpikeCount / Math.Max(1, st.FrameCount):0.#}%)" +
                          $"<br><span class=muted>{st.FrameCount} {T("кадров", "frames")}, {T("колонка", "column")} <code>{E(x.FrameTimeColumn)}</code>, PC latency: {M(st.MedianPcLatencyMs, v => $"{v:0.0} ms")}</span></td></tr>");
            }
            sb.Append("</table>");
            if (latestFrames is { Count: > 1 }) sb.Append(FrameTimeSvg(latestFrames));
        }

        if (r.Comparison is { } c)
        {
            sb.Append($"<h2>{T("Сравнение", "Comparison")}</h2><div class=card><b>{E(c.Summary.Get(r.Language))}</b>");
            foreach (var w in c.Warnings) sb.Append($"<div class=muted>⚠ {E(w.Get(r.Language))}</div>");
            sb.Append("</div><table>");
            foreach (var m in c.Metrics)
                sb.Append($"<tr><th>{E(m.Metric)}</th><td>{m.BaselineMedian:0.##} → {m.AfterMedian:0.##} ({m.DeltaPercent:+0.#;-0.#;0}%) · {E(m.Verdict.ToString())}" +
                          $"<br><span class=muted>{T("до", "before")} [{m.BaselineMin:0.##}…{m.BaselineMax:0.##}], {T("после", "after")} [{m.AfterMin:0.##}…{m.AfterMax:0.##}]</span></td></tr>");
            sb.Append("</table>");
        }

        sb.Append($"<h2>{T("Рекомендации", "Recommendations")}</h2>");
        if (r.Recommendations.Count == 0) sb.Append($"<p class=muted>{T("Применимых рекомендаций не найдено.", "No applicable recommendations were found.")}</p>");
        foreach (var rec in r.Recommendations)
        {
            var kind = rec.Kind switch
            {
                RecommendationKind.AutoApplicable => T("Можно применить автоматически", "Can be applied automatically"),
                RecommendationKind.UserAction => T("Требует вашего решения", "Needs your decision"),
                _ => T("Только диагностика", "Diagnostic only")
            };
            sb.Append($"<div class=card><span class=\"tag{(rec.Kind == RecommendationKind.AutoApplicable ? " auto" : "")}\">{E(kind)}</span>" +
                      $"<span class=tag>{T("уверенность", "confidence")}: {E(rec.Confidence.ToString())}</span><span class=tag>{E(rec.RuleId)} v{rec.RuleVersion}</span>" +
                      $"<h3>{E(rec.Title.Get(r.Language))}</h3><p>{E(rec.Symptom.Get(r.Language))}</p><table>");
            foreach (var ev in rec.Evidence) sb.Append($"<tr><th>{E(ev.Label.Get(r.Language))}</th><td>{E(ev.Value)} <span class=muted>· {E(ev.Source)}</span></td></tr>");
            sb.Append($"</table><p><b>{T("Возможные причины", "Possible causes")}:</b> {E(rec.PossibleCauses.Get(r.Language))}</p>" +
                      $"<p><b>{T("Действие", "Action")}:</b> {E(rec.Action.Get(r.Language))}</p>");
            if (rec.Verification is { } v) sb.Append($"<p><b>{T("Как проверить", "How to verify")}:</b> {E(v.Get(r.Language))}</p>");
            sb.Append($"<p class=muted><b>{T("Ограничения", "Limitations")}:</b> {E(rec.Limitations.Get(r.Language))}</p></div>");
        }

        if (r.Changes.Count > 0)
        {
            sb.Append($"<h2>{T("Журнал изменений", "Change journal")}</h2><table>");
            foreach (var ch in r.Changes)
                sb.Append($"<tr><th>{E(ch.Target)}</th><td>{E(ch.Original)} → {E(ch.Planned)} · <b>{E(ch.State)}</b> · {E(ch.UpdatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))}{(ch.Error is null ? "" : "<br><span class=muted>" + E(ch.Error) + "</span>")}</td></tr>");
            sb.Append("</table>");
        }

        sb.Append($"<h2>{T("Определения метрик", "Metric definitions")}</h2><table>");
        foreach (var kv in r.MetricDefinitions) sb.Append($"<tr><th>{E(kv.Key)}</th><td><code>{E(kv.Value)}</code></td></tr>");
        sb.Append($"</table><h2>{T("Ограничения", "Limitations")}</h2><ul>");
        foreach (var l in r.Limitations) sb.Append($"<li class=muted>{E(l)}</li>");
        sb.Append("</ul></main></body></html>");
        return sb.ToString();
    }

    private static string FrameTimeSvg(IReadOnlyList<FrameSample> frames)
    {
        const int w = 1000, h = 220, pad = 24;
        var step = Math.Max(1, frames.Count / 1500);
        var pts = frames.Where((_, i) => i % step == 0).ToList();
        var maxY = Math.Max(1, pts.Max(f => f.FrameTimeMs));
        maxY = Math.Min(maxY, pts.Average(f => f.FrameTimeMs) * 6);
        var maxX = Math.Max(1, frames[^1].TimeMs);
        var sb = new StringBuilder();
        sb.Append($"<svg viewBox=\"0 0 {w} {h}\" preserveAspectRatio=\"none\" role=img aria-label=\"frame time\"><polyline fill=none stroke=\"#CBFF4D\" stroke-width=1.2 points=\"");
        foreach (var f in pts)
        {
            var x = pad + (w - 2 * pad) * f.TimeMs / maxX;
            var y = h - pad - (h - 2 * pad) * Math.Min(f.FrameTimeMs, maxY) / maxY;
            sb.Append(x.ToString("0.#", CultureInfo.InvariantCulture)).Append(',').Append(y.ToString("0.#", CultureInfo.InvariantCulture)).Append(' ');
        }
        sb.Append($"\"/><text x=\"{pad}\" y=\"16\" fill=\"#9aa39a\" font-size=\"12\">frame time, ms (max {maxY.ToString("0.#", CultureInfo.InvariantCulture)})</text></svg>");
        return sb.ToString();
    }
}
