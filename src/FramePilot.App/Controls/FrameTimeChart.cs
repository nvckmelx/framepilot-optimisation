using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FramePilot.Core.Models;

namespace FramePilot.App.Controls;

/// <summary>Frame-time line chart drawn directly with DrawingContext (no third-party chart dependency).</summary>
public sealed class FrameTimeChart : FrameworkElement
{
    public static readonly DependencyProperty FramesProperty = DependencyProperty.Register(
        nameof(Frames), typeof(IReadOnlyList<FrameSample>), typeof(FrameTimeChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<FrameSample>? Frames
    {
        get => (IReadOnlyList<FrameSample>?)GetValue(FramesProperty);
        set => SetValue(FramesProperty, value);
    }

    private static readonly Brush LineBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xCB, 0xFF, 0x4D)));
    private static readonly Brush GridBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x32, 0x2D)));
    private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0x9A)));
    private static readonly Brush WarnBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xB5, 0x47)));
    private static readonly Typeface Face = new("Segoe UI");

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var frames = Frames;
        const double left = 44, right = 8, top = 10, bottom = 22;
        var pw = w - left - right;
        var ph = h - top - bottom;
        if (pw < 20 || ph < 20) return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText Label(string s) => new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 11, LabelBrush, dpi);

        if (frames is null || frames.Count < 2)
        {
            dc.DrawText(Label(Localization.Loc.T("Chart_NoData")), new Point(left, top + ph / 2 - 8));
            return;
        }

        var times = frames.Select(f => f.FrameTimeMs).OrderBy(x => x).ToList();
        var p99 = times[Math.Clamp((int)Math.Ceiling(times.Count * 0.99) - 1, 0, times.Count - 1)];
        var maxY = Math.Max(p99 * 1.6, times[times.Count / 2] * 2.5);
        maxY = NiceCeiling(maxY);
        var maxX = Math.Max(1, frames[^1].TimeMs);

        var gridPen = new Pen(GridBrush, 1);
        for (var i = 0; i <= 4; i++)
        {
            var v = maxY * i / 4;
            var y = top + ph - ph * v / maxY;
            dc.DrawLine(gridPen, new Point(left, y), new Point(left + pw, y));
            var t = Label(v.ToString(v < 10 ? "0.#" : "0", CultureInfo.CurrentCulture));
            dc.DrawText(t, new Point(left - t.Width - 6, y - t.Height / 2));
        }
        var secs = maxX / 1000;
        var step = secs > 240 ? 60 : secs > 90 ? 30 : secs > 30 ? 10 : 5;
        for (var s = 0.0; s <= secs; s += step)
        {
            var x = left + pw * s * 1000 / maxX;
            var t = Label($"{s:0}s");
            dc.DrawText(t, new Point(x - t.Width / 2, top + ph + 4));
        }

        // Downsample to one min/max pair per horizontal pixel so spikes stay visible.
        var buckets = Math.Max(1, (int)pw);
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            var started = false;
            var idx = 0;
            for (var b = 0; b < buckets && idx < frames.Count; b++)
            {
                var limit = maxX * (b + 1) / buckets;
                double lo = double.MaxValue, hi = double.MinValue;
                while (idx < frames.Count && frames[idx].TimeMs <= limit)
                {
                    var v = frames[idx].FrameTimeMs;
                    if (v < lo) lo = v;
                    if (v > hi) hi = v;
                    idx++;
                }
                if (hi < lo) continue;
                var x = left + b + 0.5;
                var yLo = top + ph - ph * Math.Min(lo, maxY) / maxY;
                var yHi = top + ph - ph * Math.Min(hi, maxY) / maxY;
                if (!started)
                {
                    ctx.BeginFigure(new Point(x, yLo), false, false);
                    started = true;
                }
                else
                {
                    ctx.LineTo(new Point(x, yLo), true, false);
                }
                if (Math.Abs(yHi - yLo) > 0.5) ctx.LineTo(new Point(x, yHi), true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, new Pen(LineBrush, 1.2), geo);

        var p99Y = top + ph - ph * Math.Min(p99, maxY) / maxY;
        var dash = new Pen(WarnBrush, 1) { DashStyle = DashStyles.Dash };
        dc.DrawLine(dash, new Point(left, p99Y), new Point(left + pw, p99Y));
        var pl = Label($"P99 {p99:0.0} ms");
        dc.DrawText(pl, new Point(left + pw - pl.Width - 4, p99Y - pl.Height - 2));
    }

    private static double NiceCeiling(double v)
    {
        double[] steps = { 2, 4, 5, 8, 10, 12, 16, 20, 25, 33.4, 40, 50, 67, 100, 150, 200, 300, 500 };
        foreach (var s in steps) if (v <= s) return s;
        return Math.Ceiling(v / 100) * 100;
    }
}
