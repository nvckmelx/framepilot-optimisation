using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FramePilot.App.Localization;
using FramePilot.Core.Models;

namespace FramePilot.App.Controls;

/// <summary>
/// Frame-time chart drawn with DrawingContext (no chart dependency). Min/max downsampling keeps every spike, the y-axis
/// grows to the real maximum, spikes are diamonds, lag markers are flags, and the attributed cause of the focused spike is
/// shown as a callout. Motion (spec 2026-11-04 §4.1): one scan reveal per new session with hit markers on spikes;
/// skipped when Windows animations are off or high contrast is on. Keyboard: ←/→ move the cursor,
/// PageUp/PageDown (or Tab/Shift+Tab inside the chart) jump between spikes, Home/End to the ends, Esc clears.
/// </summary>
public sealed class FrameTimeChart : FrameworkElement
{
    public static readonly DependencyProperty FramesProperty = DependencyProperty.Register(
        nameof(Frames), typeof(IReadOnlyList<FrameSample>), typeof(FrameTimeChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((FrameTimeChart)d).OnDataChanged()));

    public static readonly DependencyProperty InsightsProperty = DependencyProperty.Register(
        nameof(Insights), typeof(SessionInsights), typeof(FrameTimeChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((FrameTimeChart)d).OnInsightsChanged()));

    public static readonly DependencyProperty BudgetMsProperty = DependencyProperty.Register(
        nameof(BudgetMs), typeof(double), typeof(FrameTimeChart),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Animation clock in seconds (0 → <see cref="IntroSeconds"/>); a value ≥ IntroSeconds renders the final state.</summary>
    public static readonly DependencyProperty ClockProperty = DependencyProperty.Register(
        nameof(Clock), typeof(double), typeof(FrameTimeChart),
        new FrameworkPropertyMetadata(99.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Identity of the shown session: a new value plays the scan intro once.</summary>
    public static readonly DependencyProperty SessionIdProperty = DependencyProperty.Register(
        nameof(SessionId), typeof(Guid), typeof(FrameTimeChart),
        new FrameworkPropertyMetadata(Guid.Empty, (d, e) => ((FrameTimeChart)d).PlayIntro((Guid)e.NewValue)));

    public Guid SessionId { get => (Guid)GetValue(SessionIdProperty); set => SetValue(SessionIdProperty, value); }

    public IReadOnlyList<FrameSample>? Frames { get => (IReadOnlyList<FrameSample>?)GetValue(FramesProperty); set => SetValue(FramesProperty, value); }
    public SessionInsights? Insights { get => (SessionInsights?)GetValue(InsightsProperty); set => SetValue(InsightsProperty, value); }
    /// <summary>Frame budget of the display (1000 / refresh Hz); 0 hides the line.</summary>
    public double BudgetMs { get => (double)GetValue(BudgetMsProperty); set => SetValue(BudgetMsProperty, value); }
    public double Clock { get => (double)GetValue(ClockProperty); set => SetValue(ClockProperty, value); }

    private const double ScanStart = 0.15, ScanDuration = 1.1, IntroSeconds = 2.0;
    private const double Left = 46, Right = 12, Top = 16, Bottom = 24;

    private static readonly Brush LineBrush = Frozen(Color.FromRgb(0x4C, 0x9D, 0xFF));
    private static readonly Brush AreaBrush = Frozen(Color.FromArgb(0x24, 0x4C, 0x9D, 0xFF));
    private static readonly Brush GridBrush = Frozen(Color.FromRgb(0x2C, 0x30, 0x38));
    private static readonly Brush LabelBrush = Frozen(Color.FromRgb(0x9B, 0xA3, 0xAE));
    private static readonly Brush TextBrush = Frozen(Colors.White);
    private static readonly Brush Text2Brush = Frozen(Color.FromRgb(0xC9, 0xCF, 0xD6));
    private static readonly Brush SpikeBrush = Frozen(Color.FromRgb(0xF8, 0x71, 0x71));
    private static readonly Brush BudgetBrush = Frozen(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static readonly Brush PanelBrush = Frozen(Color.FromRgb(0x22, 0x25, 0x2B));
    private static readonly Brush PanelLine = Frozen(Color.FromRgb(0x3A, 0x40, 0x49));
    private static readonly Brush DotBrush = Frozen(Color.FromRgb(0x11, 0x12, 0x14));
    private static readonly Brush FlagBrush = Frozen(Color.FromRgb(0xF5, 0x9E, 0x0B));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    // Motion curves shared with the mockup (motion.js): entrance, move and the single overshoot for the hit marker.
    private static readonly Func<double, double> Out = Bezier(0.16, 1, 0.3, 1);
    private static readonly Func<double, double> Move = Bezier(0.65, 0, 0.35, 1);
    private static readonly Func<double, double> Pop = Bezier(0.34, 1.56, 0.64, 1);

    private (double Lo, double Hi)[]? _buckets;
    private double _maxX = 1, _maxY = 10, _p99;
    private int _bucketWidth = -1;
    private int _cursor = -1;           // bucket index under keyboard/mouse cursor, -1 = none
    private int _spikeFocus = -1;       // index into Insights.Spikes
    private Guid _lastAnimatedFor;

    public FrameTimeChart()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Cross;
        SnapsToDevicePixels = true;
    }

    /// <summary>Plays the scan reveal once (call when a new session is shown). Honours the Windows animation setting.</summary>
    public void PlayIntro(Guid sessionId)
    {
        if (sessionId == _lastAnimatedFor || sessionId == Guid.Empty) return;
        _lastAnimatedFor = sessionId;
        if (!MotionAllowed)
        {
            Clock = 99;
            return;
        }
        BeginAnimation(ClockProperty, new DoubleAnimation(0, IntroSeconds, TimeSpan.FromSeconds(IntroSeconds)) { FillBehavior = FillBehavior.HoldEnd });
    }

    public static bool MotionAllowed => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;

    private void OnDataChanged()
    {
        _bucketWidth = -1;
        _cursor = -1;
        _spikeFocus = -1;
    }

    private void OnInsightsChanged()
    {
        _spikeFocus = -1;
        UpdateAutomationName();
    }

    // ---------------- layout helpers ----------------

    private Rect Plot => new(Left, Top, Math.Max(0, ActualWidth - Left - Right), Math.Max(0, ActualHeight - Top - Bottom));

    private void EnsureBuckets()
    {
        var frames = Frames;
        var plot = Plot;
        var n = Math.Max(1, (int)plot.Width);
        if (frames is null || frames.Count < 2) { _buckets = null; return; }
        if (_buckets is not null && _bucketWidth == n) return;
        _bucketWidth = n;
        _maxX = Math.Max(1, frames[^1].TimeMs);
        var buckets = new (double Lo, double Hi)[n];
        for (var i = 0; i < n; i++) buckets[i] = (double.NaN, double.NaN);
        double max = 0;
        foreach (var f in frames)
        {
            var b = Math.Clamp((int)(f.TimeMs / _maxX * n), 0, n - 1);
            var (lo, hi) = buckets[b];
            buckets[b] = (double.IsNaN(lo) ? f.FrameTimeMs : Math.Min(lo, f.FrameTimeMs), double.IsNaN(hi) ? f.FrameTimeMs : Math.Max(hi, f.FrameTimeMs));
            if (f.FrameTimeMs > max) max = f.FrameTimeMs;
        }
        _buckets = buckets;
        var sorted = frames.Select(f => f.FrameTimeMs).OrderBy(x => x).ToList();
        _p99 = sorted[Math.Clamp((int)Math.Ceiling(sorted.Count * 0.99) - 1, 0, sorted.Count - 1)];
        // The axis always reaches the real maximum, so the worst spike is never clipped.
        _maxY = NiceCeiling(Math.Max(max * 1.08, Math.Max(_p99 * 1.6, sorted[sorted.Count / 2] * 2.5)));
    }

    private double X(double timeMs) => Plot.Left + Plot.Width * timeMs / _maxX;
    private double Y(double ms) => Plot.Bottom - Plot.Height * Math.Min(ms, _maxY) / _maxY;

    // ---------------- rendering ----------------

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var plot = Plot;
        if (plot.Width < 40 || plot.Height < 40) return;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var face = new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var bold = new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        FormattedText Label(string s, Brush b, double size = 11, Typeface? tf = null) =>
            new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf ?? face, size, b, dpi);

        EnsureBuckets();
        var buckets = _buckets;
        if (buckets is null)
        {
            dc.DrawText(Label(Loc.T("Chart_NoData"), LabelBrush, 13), new Point(plot.Left, plot.Top + plot.Height / 2 - 8));
            return;
        }

        // Grid at the classic frame budgets that fit the axis.
        var gridPen = new Pen(GridBrush, 1);
        gridPen.Freeze();
        foreach (var v in new[] { 4.17, 6.94, 8.33, 16.67, 33.33, 50, 100, 200 }.Where(v => v < _maxY).TakeLast(4))
        {
            var y = Math.Round(Y(v)) + 0.5;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var t = Label(v.ToString("0.##", CultureInfo.CurrentCulture), LabelBrush);
            dc.DrawText(t, new Point(plot.Left - t.Width - 8, y - t.Height / 2));
        }
        var secs = _maxX / 1000;
        var step = secs > 240 ? 60 : secs > 90 ? 30 : secs > 30 ? 10 : 5;
        for (var s = 0.0; s <= secs + 0.001; s += step)
        {
            var t = Label(string.Format(CultureInfo.CurrentCulture, Loc.T("Chart_Seconds"), s), LabelBrush);
            dc.DrawText(t, new Point(X(s * 1000) - t.Width / 2, plot.Bottom + 6));
        }

        var clock = Clock;
        var reveal = Move(Math.Clamp((clock - ScanStart) / ScanDuration, 0, 1));
        var revealX = plot.Left + plot.Width * reveal;

        // Line + area, revealed by the scanner.
        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var lc = line.Open())
        using (var ac = area.Open())
        {
            var started = false;
            var lastX = plot.Left;
            for (var b = 0; b < buckets.Length; b++)
            {
                var (lo, hi) = buckets[b];
                if (double.IsNaN(lo)) continue;
                var x = plot.Left + b + 0.5;
                if (x > revealX) break;
                var yLo = Y(lo);
                var yHi = Y(hi);
                if (!started)
                {
                    lc.BeginFigure(new Point(x, yLo), false, false);
                    ac.BeginFigure(new Point(x, plot.Bottom), true, true);
                    ac.LineTo(new Point(x, yLo), false, false);
                    started = true;
                }
                lc.LineTo(new Point(x, yLo), true, true);
                ac.LineTo(new Point(x, yLo), false, false);
                if (Math.Abs(yHi - yLo) > 0.5)
                {
                    lc.LineTo(new Point(x, yHi), true, true);
                    lc.LineTo(new Point(x, yLo), true, true);
                }
                lastX = x;
            }
            if (started) ac.LineTo(new Point(lastX, plot.Bottom), false, false);
        }
        line.Freeze();
        area.Freeze();
        dc.DrawGeometry(AreaBrush, null, area);
        var linePen = new Pen(LineBrush, 1.4) { LineJoin = PenLineJoin.Round };
        linePen.Freeze();
        dc.DrawGeometry(null, linePen, line);

        // Frame budget of the display and P99.
        if (BudgetMs > 0 && BudgetMs < _maxY)
        {
            var by = Math.Round(Y(BudgetMs)) + 0.5;
            var bp = new Pen(BudgetBrush, 1) { DashStyle = new DashStyle(new[] { 4.0, 4.0 }, 0) };
            bp.Freeze();
            dc.DrawLine(bp, new Point(plot.Left, by), new Point(plot.Right, by));
            var bt = Label(string.Format(CultureInfo.CurrentCulture, Loc.T("Chart_Budget"), BudgetMs), BudgetBrush);
            dc.DrawText(bt, new Point(plot.Left + 6, by - bt.Height - 2));
        }
        var py = Math.Round(Y(_p99)) + 0.5;
        var p99Pen = new Pen(new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)), 1) { DashStyle = new DashStyle(new[] { 5.0, 5.0 }, 0) };
        p99Pen.Freeze();
        dc.DrawLine(p99Pen, new Point(plot.Left, py), new Point(plot.Right, py));
        var pl = Label(string.Format(CultureInfo.CurrentCulture, "P99 {0:0.00} {1}", _p99, Loc.T("Unit_Ms")), TextBrush, 11.5, bold);
        dc.DrawText(pl, new Point(plot.Right - pl.Width - 4, py - pl.Height - 3));

        // Scanner head and trail.
        if (reveal > 0 && reveal < 1 || (clock < ScanStart + ScanDuration + 0.2 && reveal > 0))
        {
            var fade = reveal < 1 ? 1 : 1 - Math.Clamp((clock - ScanStart - ScanDuration) / 0.2, 0, 1);
            var trail = new LinearGradientBrush(Color.FromArgb(0, 0x4C, 0x9D, 0xFF), Color.FromArgb((byte)(56 * fade), 0x4C, 0x9D, 0xFF), 0);
            dc.DrawRectangle(trail, null, new Rect(Math.Max(plot.Left, revealX - 90), plot.Top, Math.Min(90, revealX - plot.Left), plot.Height));
            var head = new Pen(new SolidColorBrush(Color.FromArgb((byte)(255 * fade), 255, 255, 255)), 1.5);
            dc.DrawLine(head, new Point(revealX, plot.Top - 4), new Point(revealX, plot.Bottom));
        }

        // Spikes: diamonds; each fires a hit marker when the scanner passes it.
        var spikes = Insights?.Spikes ?? Array.Empty<SpikeAttribution>();
        var ringPen = new Pen(SpikeBrush, 2);
        ringPen.Freeze();
        for (var i = 0; i < spikes.Count; i++)
        {
            var s = spikes[i];
            var sx = X(s.TimeMs);
            if (sx > revealX + 0.5) continue;
            var sy = Y(s.FrameTimeMs);
            var passed = PassTime(sx, plot);
            var age = clock - passed;
            var scale = clock >= IntroSeconds ? 1 : Pop(Math.Clamp(age / 0.28, 0, 1));
            if (age < 0.45 && clock < IntroSeconds)
            {
                var rr = Out(Math.Clamp(age / 0.45, 0, 1));
                var ring = new Pen(new SolidColorBrush(Color.FromArgb((byte)(230 * (1 - rr)), 0xF8, 0x71, 0x71)), 2);
                dc.DrawEllipse(null, ring, new Point(sx, sy), 6 + 18 * rr, 6 + 18 * rr);
            }
            var d = 5.0 * scale * (i == _spikeFocus ? 1.3 : 1);
            var dia = new StreamGeometry();
            using (var g = dia.Open())
            {
                g.BeginFigure(new Point(sx, sy - d), true, true);
                g.LineTo(new Point(sx + d, sy), true, true);
                g.LineTo(new Point(sx, sy + d), true, true);
                g.LineTo(new Point(sx - d, sy), true, true);
            }
            dia.Freeze();
            dc.DrawGeometry(DotBrush, ringPen, dia);
        }

        // Lag markers set by the player (Ctrl+Shift+F9): small flags at the top.
        foreach (var m in Insights?.Markers ?? Array.Empty<LagMarker>())
        {
            var mx = X(m.TimeMs);
            if (mx > revealX) continue;
            var flagPen = new Pen(FlagBrush, 1.5);
            dc.DrawLine(flagPen, new Point(mx, plot.Top - 6), new Point(mx, plot.Top + 16));
            var flag = new StreamGeometry();
            using (var g = flag.Open())
            {
                g.BeginFigure(new Point(mx, plot.Top - 6), true, true);
                g.LineTo(new Point(mx + 10, plot.Top - 2), true, true);
                g.LineTo(new Point(mx, plot.Top + 2), true, true);
            }
            flag.Freeze();
            dc.DrawGeometry(FlagBrush, null, flag);
        }

        // Cursor (mouse/keyboard) with a readout, or the callout for the focused / worst spike.
        if (_cursor >= 0 && _cursor < buckets.Length && !double.IsNaN(buckets[_cursor].Hi))
        {
            var cx = plot.Left + _cursor + 0.5;
            var cursorPen = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 255, 255, 255)), 1);
            dc.DrawLine(cursorPen, new Point(cx, plot.Top), new Point(cx, plot.Bottom));
            var t = _cursor * _maxX / buckets.Length;
            var (lo, hi) = buckets[_cursor];
            var title = string.Format(CultureInfo.CurrentCulture, Loc.T("Chart_CursorTitle"), t / 1000, hi, 1000 / Math.Max(0.01, lo));
            DrawCallout(dc, Label(title, TextBrush, 12.5, bold), null, cx, Y(hi), plot);
        }
        else if (clock >= ScanStart + ScanDuration && spikes.Count > 0)
        {
            var idx = _spikeFocus >= 0 ? _spikeFocus : WorstSpike(spikes);
            var s = spikes[idx];
            var a = clock >= IntroSeconds ? 1 : Out(Math.Clamp((clock - PassTime(X(s.TimeMs), plot) - 0.12) / 0.25, 0, 1));
            if (a > 0)
            {
                dc.PushOpacity(a);
                var title = string.Format(CultureInfo.CurrentCulture, Loc.T("Chart_SpikeTitle"), s.TimeMs / 1000, s.FrameTimeMs, InsightText.CauseTitle(s.Cause));
                var detail = InsightText.CauseDetail(s);
                DrawCallout(dc, Label(title, TextBrush, 12.5, bold), detail is null ? null : Label(detail, Text2Brush, 12), X(s.TimeMs), Y(s.FrameTimeMs), plot, -10 * (1 - a));
                dc.Pop();
            }
        }

        if (IsKeyboardFocused)
        {
            var focusPen = new Pen(LineBrush, 2);
            dc.DrawRoundedRectangle(null, focusPen, new Rect(1, 1, w - 2, h - 2), 8, 8);
        }
    }

    private static int WorstSpike(IReadOnlyList<SpikeAttribution> spikes)
    {
        var worst = 0;
        for (var i = 1; i < spikes.Count; i++) if (spikes[i].FrameTimeMs > spikes[worst].FrameTimeMs) worst = i;
        return worst;
    }

    /// <summary>When the scanner reaches x (seconds on the animation clock).</summary>
    private static double PassTime(double x, Rect plot)
    {
        var target = (x - plot.Left) / Math.Max(1, plot.Width);
        double lo = 0, hi = 1;
        for (var i = 0; i < 24; i++)
        {
            var m = (lo + hi) / 2;
            if (Move(m) < target) lo = m; else hi = m;
        }
        return ScanStart + hi * ScanDuration;
    }

    private void DrawCallout(DrawingContext dc, FormattedText title, FormattedText? detail, double x, double y, Rect plot, double dx = 0)
    {
        var width = Math.Max(title.Width, detail?.Width ?? 0) + 24;
        var height = title.Height + (detail?.Height ?? 0) + 18;
        var bx = x + 14 + dx;
        if (bx + width > plot.Right) bx = x - 14 - width;
        var by = Math.Clamp(y - 10, plot.Top, plot.Bottom - height);
        var box = new Rect(bx, by, width, height);
        dc.DrawRoundedRectangle(PanelBrush, new Pen(PanelLine, 1), box, 8, 8);
        dc.DrawText(title, new Point(bx + 12, by + 8));
        if (detail is not null) dc.DrawText(detail, new Point(bx + 12, by + 10 + title.Height));
    }

    // ---------------- interaction ----------------

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_buckets is null) return;
        var p = e.GetPosition(this);
        _cursor = Math.Clamp((int)(p.X - Plot.Left), 0, _buckets.Length - 1);
        _spikeFocus = -1;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _cursor = -1;
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (_buckets is null) return;
        var spikes = Insights?.Spikes ?? Array.Empty<SpikeAttribution>();
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        switch (e.Key)
        {
            case Key.Left:
            case Key.Right:
                _spikeFocus = -1;
                if (_cursor < 0) _cursor = e.Key == Key.Left ? _buckets.Length - 1 : 0;
                else _cursor = Math.Clamp(_cursor + (e.Key == Key.Left ? -1 : 1) * (shift ? 10 : 1), 0, _buckets.Length - 1);
                e.Handled = true;
                break;
            case Key.Home:
            case Key.End:
                _spikeFocus = -1;
                _cursor = e.Key == Key.Home ? 0 : _buckets.Length - 1;
                e.Handled = true;
                break;
            case Key.PageDown:
            case Key.PageUp:
            case Key.Tab:
                if (spikes.Count == 0) return;
                var back = e.Key == Key.PageUp || (e.Key == Key.Tab && shift);
                var next = _spikeFocus + (back ? -1 : 1);
                if (e.Key == Key.Tab && (next < 0 || next >= spikes.Count))
                {
                    _spikeFocus = -1;          // let Tab leave the chart after the last spike
                    InvalidateVisual();
                    return;
                }
                _spikeFocus = Math.Clamp(next, 0, spikes.Count - 1);
                _cursor = -1;
                e.Handled = true;
                break;
            case Key.Escape:
                _cursor = -1;
                _spikeFocus = -1;
                e.Handled = true;
                break;
            default:
                return;
        }
        UpdateAutomationName();
        InvalidateVisual();
    }

    // ---------------- accessibility ----------------

    /// <summary>What a screen reader announces: the focused spike with its cause, the cursor readout, or a summary.</summary>
    public string AccessibleText
    {
        get
        {
            var spikes = Insights?.Spikes ?? Array.Empty<SpikeAttribution>();
            if (_spikeFocus >= 0 && _spikeFocus < spikes.Count)
            {
                var s = spikes[_spikeFocus];
                return string.Format(CultureInfo.CurrentCulture, Loc.T("Chart_SpikeA11y"), _spikeFocus + 1, spikes.Count, s.TimeMs / 1000,
                    s.FrameTimeMs, InsightText.CauseTitle(s.Cause), InsightText.CauseDetail(s) ?? "");
            }
            if (_cursor >= 0 && _buckets is not null && _cursor < _buckets.Length && !double.IsNaN(_buckets[_cursor].Hi))
                return string.Format(CultureInfo.CurrentCulture, Loc.T("Chart_CursorTitle"), _cursor * _maxX / _buckets.Length / 1000,
                    _buckets[_cursor].Hi, 1000 / Math.Max(0.01, _buckets[_cursor].Lo));
            return string.Format(CultureInfo.CurrentCulture, Loc.T("Chart_SummaryA11y"), _p99, spikes.Count);
        }
    }

    private void UpdateAutomationName()
    {
        var text = AccessibleText;
        var old = AutomationProperties.GetName(this);
        AutomationProperties.SetName(this, text);
        if (UIElementAutomationPeer.FromElement(this) is { } peer)
            peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, old, text);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ChartPeer(this);

    private sealed class ChartPeer : FrameworkElementAutomationPeer
    {
        public ChartPeer(FrameTimeChart owner) : base(owner) { }
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
        protected override string GetClassNameCore() => nameof(FrameTimeChart);
        protected override bool IsKeyboardFocusableCore() => true;
        protected override string GetNameCore() => ((FrameTimeChart)Owner).AccessibleText;
    }

    // ---------------- maths ----------------

    private static double NiceCeiling(double v)
    {
        double[] steps = { 8, 10, 12, 16, 20, 25, 30, 40, 50, 60, 80, 100, 150, 200, 300, 500 };
        foreach (var s in steps) if (v <= s) return s;
        return Math.Ceiling(v / 100) * 100;
    }

    /// <summary>CSS-style cubic-bezier(x1, y1, x2, y2) easing, solved with Newton iterations.</summary>
    internal static Func<double, double> Bezier(double x1, double y1, double x2, double y2)
    {
        double cx = 3 * x1, bx = 3 * (x2 - x1) - cx, ax = 1 - cx - bx;
        double cy = 3 * y1, by = 3 * (y2 - y1) - cy, ay = 1 - cy - by;
        return x =>
        {
            if (x <= 0) return 0;
            if (x >= 1) return 1;
            var t = x;
            for (var i = 0; i < 12; i++)
            {
                var xt = ((ax * t + bx) * t + cx) * t - x;
                var d = (3 * ax * t + 2 * bx) * t + cx;
                if (Math.Abs(xt) < 1e-6 || d == 0) break;
                t = Math.Clamp(t - xt / d, 0, 1);
            }
            return ((ay * t + by) * t + cy) * t;
        };
    }
}
