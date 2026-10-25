using System.Globalization;
using FramePilot.Core.Models;

namespace FramePilot.Telemetry;

public sealed record ParsedCapture(
    IReadOnlyList<FrameSample> Frames,
    string FrameTimeColumn,
    IReadOnlyList<string> Columns,
    int RowsRead,
    int RowsSkipped,
    string? SwapChain,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Parses PresentMon CSV output. Column names differ between PresentMon versions and metric modes, so columns are
/// resolved by name and the column used for frame time is reported with the result. Truncated last lines are tolerated.
/// </summary>
public static class PresentMonCsv
{
    /// <summary>Preferred frame-interval columns, in order. MsBetweenPresents is the classic definition used by most tools.</summary>
    public static readonly string[] FrameTimeColumns = { "MsBetweenPresents", "MsBetweenAppStart", "FrameTime" };
    private static readonly string[] GpuBusyColumns = { "MsGPUBusy", "GPUBusy" };
    private static readonly string[] CpuBusyColumns = { "MsCPUBusy", "CPUBusy" };
    private static readonly string[] PcLatencyColumns = { "MsPCLatency", "PCLatency" };
    private static readonly string[] ClickColumns = { "MsClickToPhotonLatency", "ClickToPhotonLatency" };

    public static ParsedCapture Parse(TextReader reader, string? processFilter = null)
    {
        var warnings = new List<string>();
        var headerLine = reader.ReadLine();
        if (headerLine is null) throw new InvalidDataException("The CSV file is empty.");
        var header = SplitCsv(headerLine).Select(h => h.Trim()).ToList();
        var index = header.Select((h, i) => (h, i)).GroupBy(x => x.h, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().i, StringComparer.OrdinalIgnoreCase);

        int Col(params string[] names)
        {
            foreach (var n in names) if (index.TryGetValue(n, out var i)) return i;
            return -1;
        }

        var ftName = FrameTimeColumns.FirstOrDefault(index.ContainsKey)
            ?? throw new InvalidDataException("No frame time column found. Expected one of: " + string.Join(", ", FrameTimeColumns));
        var ft = index[ftName];
        var app = Col("Application");
        var swap = Col("SwapChainAddress");
        var gpu = Col(GpuBusyColumns);
        var cpu = Col(CpuBusyColumns);
        var pcl = Col(PcLatencyColumns);
        var c2p = Col(ClickColumns);
        var mode = Col("PresentMode");
        var displayed = Col("DisplayedTime");
        var dropped = Col("Dropped");
        var betweenDisplay = Col("MsBetweenDisplayChange");

        var rows = new List<(string Swap, FrameSample Sample)>();
        int read = 0, skipped = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            read++;
            var f = SplitCsv(line);
            if (f.Count < header.Count - 2 || f.Count <= ft) { skipped++; continue; }
            if (processFilter is not null && app >= 0 && app < f.Count &&
                !string.Equals(Path.GetFileNameWithoutExtension(f[app]), Path.GetFileNameWithoutExtension(processFilter), StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }
            var frameTime = Num(f, ft);
            if (frameTime is null) { skipped++; continue; }

            bool isDisplayed = true;
            if (displayed >= 0 && displayed < f.Count)
                isDisplayed = Num(f, displayed) is not null;
            else if (dropped >= 0 && dropped < f.Count)
                isDisplayed = f[dropped].Trim() != "1";
            else if (betweenDisplay >= 0 && betweenDisplay < f.Count)
                isDisplayed = Num(f, betweenDisplay) is not null;

            rows.Add((swap >= 0 && swap < f.Count ? f[swap] : "",
                new FrameSample(0, frameTime.Value, Num(f, gpu), Num(f, cpu), Num(f, displayed), Num(f, pcl), Num(f, c2p),
                    mode >= 0 && mode < f.Count ? f[mode].Trim() : null, isDisplayed)));
        }

        if (rows.Count == 0) throw new InvalidDataException("The capture contains no frames for the selected process.");

        // A game may own several swap chains (e.g. launcher overlays); analyse the busiest one.
        var groups = rows.GroupBy(r => r.Swap).OrderByDescending(g => g.Count()).ToList();
        var main = groups[0];
        if (groups.Count > 1)
            warnings.Add($"{groups.Count} swap chains found; analysed the one with the most frames ({main.Count()} of {rows.Count}).");

        double t = 0;
        var frames = new List<FrameSample>(main.Count());
        foreach (var (_, s) in main)
        {
            t += s.FrameTimeMs;
            frames.Add(s with { TimeMs = t });
        }
        if (skipped > 0) warnings.Add($"{skipped} rows were skipped (other processes, truncated or non-numeric rows).");

        return new ParsedCapture(frames, ftName, header, read, skipped, main.Key, warnings);
    }

    private static double? Num(IReadOnlyList<string> f, int i)
    {
        if (i < 0 || i >= f.Count) return null;
        var s = f[i].Trim();
        if (s.Length == 0 || s.Equals("NA", StringComparison.OrdinalIgnoreCase)) return null;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
    }

    internal static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var sb = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString());
        return result;
    }
}
