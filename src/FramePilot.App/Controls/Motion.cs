using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FramePilot.App.Controls;

/// <summary>
/// TuneFPS motion language for WPF (spec 2026-11-04 §4.1): entrance curve cubic-bezier(.16,1,.3,1), base unit 250 ms,
/// stagger 40 ms, only transform and opacity animate. Everything is skipped when Windows animations are off
/// (SystemParameters.ClientAreaAnimation) or high contrast is on.
/// </summary>
public static class Motion
{
    public static bool Allowed => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;

    /// <summary>Entrance easing equivalent to cubic-bezier(.16,1,.3,1).</summary>
    public static IEasingFunction Out { get; } = new BezierEase(0.16, 1, 0.3, 1);

    // ---- Reveal: fade + 8 px rise, staggered by index ----

    public static readonly DependencyProperty RevealIndexProperty = DependencyProperty.RegisterAttached(
        "RevealIndex", typeof(int), typeof(Motion), new PropertyMetadata(-1, OnRevealIndexChanged));

    public static int GetRevealIndex(DependencyObject d) => (int)d.GetValue(RevealIndexProperty);
    public static void SetRevealIndex(DependencyObject d, int value) => d.SetValue(RevealIndexProperty, value);

    private static void OnRevealIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe || (int)e.NewValue < 0) return;
        fe.Loaded -= Play;
        fe.Loaded += Play;
        fe.IsVisibleChanged -= OnVisible;
        fe.IsVisibleChanged += OnVisible;
    }

    private static void OnVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is FrameworkElement fe) Play(fe, new RoutedEventArgs());
    }

    private static void Play(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || !fe.IsVisible) return;
        if (!Allowed)
        {
            fe.BeginAnimation(UIElement.OpacityProperty, null);
            return;
        }
        var delay = TimeSpan.FromMilliseconds(40 * Math.Min(8, GetRevealIndex(fe)));
        var dur = new Duration(TimeSpan.FromMilliseconds(250));
        var t = fe.RenderTransform as TranslateTransform;
        if (t is null || t.IsFrozen)
        {
            t = new TranslateTransform();
            fe.RenderTransform = t;
        }
        fe.Opacity = 0;
        fe.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, dur) { BeginTime = delay, EasingFunction = Out });
        t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, dur) { BeginTime = delay, EasingFunction = Out });
    }
}

/// <summary>A TextBlock that counts up to <see cref="Value"/> over 900 ms (tabular figures, no layout jump).</summary>
public sealed class CountUpText : TextBlock
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(CountUpText), new PropertyMetadata(double.NaN, OnValueChanged));

    public static readonly DependencyProperty DecimalsProperty = DependencyProperty.Register(
        nameof(Decimals), typeof(int), typeof(CountUpText), new PropertyMetadata(0, (d, _) => ((CountUpText)d).Render()));

    private static readonly DependencyProperty ShownProperty = DependencyProperty.Register(
        "Shown", typeof(double), typeof(CountUpText), new PropertyMetadata(0.0, (d, _) => ((CountUpText)d).Render()));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int Decimals { get => (int)GetValue(DecimalsProperty); set => SetValue(DecimalsProperty, value); }

    public CountUpText() => Typography.SetNumeralAlignment(this, FontNumeralAlignment.Tabular);

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (CountUpText)d;
        var to = (double)e.NewValue;
        if (double.IsNaN(to) || double.IsInfinity(to))
        {
            c.BeginAnimation(ShownProperty, null);
            c.Text = "—";
            return;
        }
        if (!Motion.Allowed)
        {
            c.BeginAnimation(ShownProperty, null);
            c.SetValue(ShownProperty, to);
            c.Render();
            return;
        }
        var from = double.IsNaN((double)e.OldValue) ? 0 : (double)c.GetValue(ShownProperty);
        c.BeginAnimation(ShownProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(900))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        });
    }

    private void Render()
    {
        if (double.IsNaN(Value)) { Text = "—"; return; }
        Text = ((double)GetValue(ShownProperty)).ToString("F" + Decimals, CultureInfo.CurrentCulture);
    }
}

/// <summary>cubic-bezier easing for WPF animations (same curves as the web and the brand motion demo).</summary>
public sealed class BezierEase : EasingFunctionBase
{
    private readonly Func<double, double> _f;
    private readonly double _x1, _y1, _x2, _y2;

    public BezierEase(double x1, double y1, double x2, double y2)
    {
        (_x1, _y1, _x2, _y2) = (x1, y1, x2, y2);
        _f = FrameTimeChart.Bezier(x1, y1, x2, y2);
        EasingMode = EasingMode.EaseIn; // EaseInCore is the curve itself
    }

    protected override double EaseInCore(double normalizedTime) => _f(normalizedTime);

    protected override Freezable CreateInstanceCore() => new BezierEase(_x1, _y1, _x2, _y2);
}
