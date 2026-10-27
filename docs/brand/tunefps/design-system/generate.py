"""Generate TuneFPS CSS, WPF XAML and a contrast report from design-tokens.json.

usage: python generate.py <design-tokens.json> <out-dir>
Writes:
  <out>/tunefps-tokens.css
  <out>/wpf/TuneFPS.Theme.xaml
  <out>/contrast-report.md
Fails (exit 1) if a pairing that must pass WCAG contrast does not.
"""
import json
import re
import sys
from pathlib import Path

SRC, OUT = Path(sys.argv[1]), Path(sys.argv[2])
T = json.loads(SRC.read_text(encoding="utf-8"))
(OUT / "wpf").mkdir(parents=True, exist_ok=True)


# ---------- token access ----------
def get(path):
    node = T
    for part in path.split("."):
        node = node[part]
    return node


def resolve(value):
    if isinstance(value, str):
        m = re.fullmatch(r"\{([^}]+)\}", value)
        if m:
            return resolve(get(m.group(1))["$value"])
    return value


def walk(node, prefix=()):
    """Yield (path tuple, token dict) for every token (dict with $value)."""
    for k, v in node.items():
        if k.startswith("$"):
            continue
        if isinstance(v, dict) and "$value" in v:
            yield prefix + (k,), v
        elif isinstance(v, dict):
            yield from walk(v, prefix + (k,))


def kebab(s):
    s = s.replace("_", "-")
    return re.sub(r"(?<=[a-z0-9])([A-Z])", r"-\1", s).lower()


def var(path):
    return "--tf-" + "-".join(kebab(p) for p in path)


# ---------- contrast ----------
def lum(hexstr):
    h = hexstr.lstrip("#")[:6]
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    c = [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c]
    return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]


def ratio(a, b):
    la, lb = sorted([lum(a), lum(b)], reverse=True)
    return (la + 0.05) / (lb + 0.05)


C = lambda p: resolve(get(p)["$value"])
checks = [
    # (fg, bg, minimum, what)
    ("color.text.primary", "color.bg.app", 4.5, "Основной текст"),
    ("color.text.secondary", "color.bg.app", 4.5, "Вторичный текст"),
    ("color.text.muted", "color.bg.app", 4.5, "Приглушённый текст"),
    ("color.text.muted", "color.border.subtle", 4.5, "Приглушённый текст на выбранном пункте"),
    ("color.text.subtle", "color.bg.app", 4.5, "Самый тихий текст"),
    ("color.text.subtle", "color.bg.sunken", 4.5, "Самый тихий текст на боковой панели"),
    ("color.text.label", "color.bg.app", 4.5, "Подписи капсом"),
    ("color.text.link", "color.bg.app", 4.5, "Ссылки и акцентный текст"),
    ("color.text.link", "color.bg.sunken", 4.5, "Ссылки на тёмной панели"),
    ("color.text.danger", "color.bg.app", 4.5, "Текст ошибки"),
    ("color.status.success", "color.bg.app", 4.5, "Текст прироста"),
    ("color.status.warning", "color.bg.app", 4.5, "Текст предупреждения"),
    ("color.text.onAccent", "state.buttonPrimary.rest", 4.5, "Текст основной кнопки"),
    ("color.text.onAccent", "state.buttonPrimary.hover", 4.5, "Текст кнопки при наведении"),
    ("color.text.onAccent", "state.buttonPrimary.pressed", 4.5, "Текст нажатой кнопки"),
    ("state.focus.ring", "color.bg.app", 3.0, "Кольцо фокуса"),
    ("state.focus.ring", "color.bg.sunken", 3.0, "Кольцо фокуса на панели"),
    ("color.border.control", "color.bg.app", 3.0, "Граница полей ввода"),
    ("color.chart.after", "color.bg.app", 3.0, "Линия «после»"),
    ("color.chart.before", "color.bg.app", 3.0, "Линия «до»"),
    ("color.chart.series3", "color.bg.app", 3.0, "Серия 3"),
    ("color.chart.series4", "color.bg.app", 3.0, "Серия 4"),
    ("color.chart.spike", "color.bg.app", 3.0, "Скачки времени кадра"),
    ("color.chart.threshold", "color.bg.app", 3.0, "Линия порога"),
    ("color.chart.axis", "color.bg.app", 4.5, "Подписи осей"),
]
info_only = [
    ("brand.tuneBlue", "color.bg.app", "Tune Blue как цвет текста на тёмном — не использовать для мелкого текста"),
    ("color.text.onAccent", "brand.tuneBlue", "Белый на Tune Blue — только иконка/логотип, не кнопки с текстом"),
    ("brand.tuneBlue", "brand.white", "Tune Blue на белом (светлая версия логотипа — логотипы освобождены от требования)"),
    ("brand.steel", "brand.white", "Steel на белом — нельзя для текста на светлом"),
]
rows, failed = [], []
for fg, bg, need, what in checks:
    r = ratio(C(fg), C(bg))
    ok = r >= need
    rows.append(f"| {what} | `{C(fg)}` | `{C(bg)}` | {r:.2f}:1 | ≥ {need}:1 | {'да' if ok else 'НЕТ'} |")
    if not ok:
        failed.append(what)
for fg, bg, what in info_only:
    r = ratio(C(fg), C(bg))
    rows.append(f"| {what} | `{C(fg)}` | `{C(bg)}` | {r:.2f}:1 | справочно | — |")

report = ["# Контраст TuneFPS (WCAG 2.2)", "",
          "Посчитано скриптом `tools/generate.py` из `design-tokens.json` (формула относительной яркости WCAG). "
          "Текст: ≥ 4.5:1; элементы интерфейса и графики: ≥ 3:1.", "",
          "| Пара | Цвет | Фон | Контраст | Нужно | Проходит |", "|---|---|---|---|---|---|", *rows, ""]
(OUT / "contrast-report.md").write_text("\n".join(report), encoding="utf-8")


# ---------- CSS ----------
def css_value(tok):
    t, v = tok.get("$type"), resolve(tok["$value"])
    if t == "fontFamily":
        return ", ".join(f"'{f}'" if " " in f else f for f in v)
    if t == "cubicBezier":
        return f"cubic-bezier({', '.join(str(x) for x in v)})"
    if t == "shadow":
        return f"{v['offsetX']} {v['offsetY']} {v['blur']} {v['spread']} {v['color']}"
    if t == "typography":
        return None
    return str(v)


lines = []
for path, tok in walk(T):
    val = css_value(tok)
    if val is not None:
        lines.append(f"  {var(path)}: {val};")

typo_classes = []
for path, tok in walk(T["typography"], ("typography",)):
    v = tok["$value"]
    fam = css_value({"$type": "fontFamily", "$value": resolve(v["fontFamily"])})
    decl = [f"font-family: {fam}", f"font-weight: {v['fontWeight']}", f"font-size: {v['fontSize']}",
            f"line-height: {v['lineHeight']}", f"letter-spacing: {v['letterSpacing']}"]
    if v["fontWeight"] == 800:
        decl.append("font-synthesis: none")
    if "textTransform" in v:
        decl.append(f"text-transform: {v['textTransform']}")
    if "fontVariantNumeric" in v:
        decl.append(f"font-variant-numeric: {v['fontVariantNumeric']}")
    cls = ".tf-" + "-".join(kebab(p) for p in path[1:])
    typo_classes.append(f"{cls} {{ {'; '.join(decl)}; }}")

css = f"""/* TuneFPS design tokens v1.0 — GENERATED from design-tokens.json by tools/generate.py. Do not edit by hand. */
/* Fonts: SIL OFL 1.1, files in ../01-final/fonts (licenses next to them). */
@font-face {{ font-family: 'Archivo Black'; src: url('../01-final/fonts/ArchivoBlack-Regular.ttf') format('truetype'); font-weight: 400 900; font-display: swap; }}
@font-face {{ font-family: 'Inter'; src: url('../01-final/fonts/variable/Inter[opsz,wght].ttf') format('truetype'); font-weight: 100 900; font-display: swap; }}
@font-face {{ font-family: 'Inter Tight'; src: url('../01-final/fonts/variable/InterTight[wght].ttf') format('truetype'); font-weight: 100 900; font-display: swap; }}

:root {{
  color-scheme: dark;
{chr(10).join(lines)}
}}

/* ---------- Typography ---------- */
{chr(10).join(typo_classes)}
.tf-num {{ font-variant-numeric: tabular-nums; }}

/* ---------- Base ---------- */
.tf-surface {{ background: var(--tf-color-bg-app); color: var(--tf-color-text-primary); font-family: var(--tf-font-family-ui); }}
.tf-card {{ background: var(--tf-color-bg-sunken); border: var(--tf-size-border) solid var(--tf-color-border-subtle); border-radius: var(--tf-radius-md); }}
:where(.tf-surface) :focus-visible {{ outline: var(--tf-size-focus-ring) solid var(--tf-state-focus-ring); outline-offset: var(--tf-size-focus-offset); }}

/* ---------- Buttons ---------- */
.tf-btn {{
  display: inline-flex; align-items: center; justify-content: center; gap: var(--tf-space-2);
  height: var(--tf-size-control-md); padding: 0 var(--tf-space-4); border-radius: var(--tf-radius-md);
  font: 600 14px/1 var(--tf-font-family-ui); cursor: pointer; border: var(--tf-size-border) solid transparent;
  transition: background-color var(--tf-motion-duration-fast) var(--tf-motion-easing-standard),
              border-color var(--tf-motion-duration-fast) var(--tf-motion-easing-standard),
              transform var(--tf-motion-duration-xfast) var(--tf-motion-easing-standard);
}}
.tf-btn--lg {{ height: var(--tf-size-control-lg); padding: 0 var(--tf-space-5); }}
.tf-btn:active:not(:disabled) {{ transform: scale(0.98); }}
.tf-btn--primary {{ background: var(--tf-state-button-primary-rest); color: var(--tf-color-text-on-accent); }}
.tf-btn--primary:hover:not(:disabled) {{ background: var(--tf-state-button-primary-hover); }}
.tf-btn--primary:active:not(:disabled) {{ background: var(--tf-state-button-primary-pressed); }}
.tf-btn--secondary {{ background: var(--tf-state-button-secondary-rest); color: var(--tf-color-text-primary); border-color: var(--tf-color-border-strong); }}
.tf-btn--secondary:hover:not(:disabled) {{ background: var(--tf-state-button-secondary-hover); }}
.tf-btn--secondary:active:not(:disabled) {{ background: var(--tf-state-button-secondary-pressed); }}
.tf-btn--ghost {{ background: transparent; color: var(--tf-color-text-link); }}
.tf-btn--ghost:hover:not(:disabled) {{ background: var(--tf-state-row-hover); }}
.tf-btn:disabled {{ background: var(--tf-state-button-primary-disabled-bg); color: var(--tf-state-button-primary-disabled-text); border-color: transparent; cursor: not-allowed; }}
.tf-btn[aria-busy="true"] {{ cursor: progress; }}

/* ---------- Navigation ---------- */
.tf-nav-item {{
  position: relative; display: flex; align-items: center; gap: var(--tf-space-3); height: var(--tf-size-control-lg);
  padding: 0 var(--tf-space-3); border-radius: var(--tf-radius-sm); color: var(--tf-color-text-secondary);
  background: var(--tf-state-nav-item-rest); border: 0; width: 100%; text-align: left; font: 500 14px/1 var(--tf-font-family-ui); cursor: pointer;
  transition: background-color var(--tf-motion-duration-fast) var(--tf-motion-easing-standard), color var(--tf-motion-duration-fast) var(--tf-motion-easing-standard);
}}
.tf-nav-item:hover {{ background: var(--tf-state-nav-item-hover); color: var(--tf-color-text-primary); }}
.tf-nav-item[aria-current="page"] {{ background: var(--tf-state-nav-item-selected); color: var(--tf-color-text-primary); }}
.tf-nav-item[aria-current="page"]::before {{ content: ""; position: absolute; left: 0; top: 10px; bottom: 10px; width: 3px; border-radius: 2px; background: var(--tf-brand-tune-blue); }}

/* ---------- Inputs & toggles ---------- */
.tf-input {{ height: var(--tf-size-control-md); padding: 0 var(--tf-space-3); border-radius: var(--tf-radius-sm); background: var(--tf-color-bg-sunken); color: var(--tf-color-text-primary); border: var(--tf-size-border) solid var(--tf-color-border-control); font: 400 14px/1 var(--tf-font-family-ui); }}
.tf-input[aria-invalid="true"] {{ border-color: var(--tf-color-status-danger); }}

/* ---------- Status ---------- */
.tf-badge {{ display: inline-flex; align-items: center; gap: var(--tf-space-1); height: 22px; padding: 0 var(--tf-space-2); border-radius: var(--tf-radius-xs); font: 600 12px/1 var(--tf-font-family-ui); }}
.tf-badge--success {{ color: var(--tf-color-status-success); background: var(--tf-color-status-success-soft); }}
.tf-badge--warning {{ color: var(--tf-color-status-warning); background: var(--tf-color-status-warning-soft); }}
.tf-badge--danger {{ color: var(--tf-color-text-danger); background: var(--tf-color-status-danger-soft); }}
.tf-badge--neutral {{ color: var(--tf-color-text-muted); background: var(--tf-state-row-hover); }}
.tf-demo-flag {{ display: inline-flex; align-items: center; height: 20px; padding: 0 6px; border-radius: var(--tf-radius-xs); border: 1px dashed var(--tf-color-status-warning); color: var(--tf-color-status-warning); font: 600 11px/1 var(--tf-font-family-ui); letter-spacing: .08em; text-transform: uppercase; }}

/* ---------- Charts ---------- */
.tf-chart-grid {{ stroke: var(--tf-color-chart-grid); stroke-width: 1; }}
.tf-chart-axis {{ fill: var(--tf-color-chart-axis); font: 500 11px var(--tf-font-family-ui); font-variant-numeric: tabular-nums; }}
.tf-line-after {{ fill: none; stroke: var(--tf-color-chart-after); stroke-width: var(--tf-size-chart-stroke); stroke-linejoin: round; stroke-linecap: round; }}
.tf-line-before {{ fill: none; stroke: var(--tf-color-chart-before); stroke-width: var(--tf-size-chart-stroke); stroke-dasharray: 6 4; stroke-linejoin: round; }}
.tf-line-threshold {{ fill: none; stroke: var(--tf-color-chart-threshold); stroke-width: 1.5; stroke-dasharray: 3 3; }}
.tf-area-after {{ fill: var(--tf-color-chart-area-after); stroke: none; }}
.tf-noise-band {{ fill: var(--tf-color-chart-noise-band); }}

/* ---------- Motion ---------- */
@keyframes tf-fade-up {{ from {{ opacity: 0; transform: translateY(var(--tf-motion-distance-enter)); }} to {{ opacity: 1; transform: none; }} }}
@keyframes tf-fade-in {{ from {{ opacity: 0; }} to {{ opacity: 1; }} }}
@keyframes tf-draw {{ from {{ stroke-dashoffset: var(--tf-path-length, 1000); }} to {{ stroke-dashoffset: 0; }} }}
@keyframes tf-grow-y {{ from {{ transform: scaleY(0); }} to {{ transform: scaleY(1); }} }}
@keyframes tf-grow-x {{ from {{ transform: scaleX(0); }} to {{ transform: scaleX(1); }} }}
@keyframes tf-skeleton {{ from {{ background-position: 200% 0; }} to {{ background-position: -200% 0; }} }}
@keyframes tf-live-dot {{ 0%, 100% {{ opacity: 1; }} 50% {{ opacity: .35; }} }}

.tf-enter {{ animation: tf-fade-up var(--tf-motion-duration-moderate) var(--tf-motion-easing-decelerate) both; animation-delay: calc(var(--i, 0) * var(--tf-motion-duration-stagger)); }}
.tf-draw {{ stroke-dasharray: var(--tf-path-length, 1000); animation: tf-draw var(--tf-motion-duration-chart-enter) var(--tf-motion-easing-decelerate) both; }}
/* .tf-draw on a dashed line: animate a mask path instead, or the dash pattern is lost. */
.tf-bar {{ transform-box: fill-box; transform-origin: 50% 100%; animation: tf-grow-y var(--tf-motion-duration-chart-enter) var(--tf-motion-easing-decelerate) both; animation-delay: calc(var(--i, 0) * var(--tf-motion-duration-stagger)); }}
.tf-bar-h {{ transform-box: fill-box; transform-origin: 0 50%; animation: tf-grow-x var(--tf-motion-duration-chart-enter) var(--tf-motion-easing-decelerate) both; animation-delay: calc(var(--i, 0) * var(--tf-motion-duration-stagger)); }}
.tf-skeleton {{ background: linear-gradient(90deg, var(--tf-color-bg-sunken) 25%, var(--tf-color-bg-raised) 50%, var(--tf-color-bg-sunken) 75%); background-size: 200% 100%; animation: tf-skeleton var(--tf-motion-duration-skeleton-loop) linear infinite; border-radius: var(--tf-radius-sm); }}
.tf-live-dot {{ width: 8px; height: 8px; border-radius: 50%; background: var(--tf-color-status-danger); animation: tf-live-dot 1.6s var(--tf-motion-easing-standard) infinite; }}

@media (prefers-reduced-motion: reduce) {{
  *, *::before, *::after {{ animation-duration: 1ms !important; animation-iteration-count: 1 !important; animation-delay: 0ms !important; transition-duration: 1ms !important; scroll-behavior: auto !important; }}
  .tf-draw {{ stroke-dashoffset: 0 !important; }}
  .tf-live-dot {{ animation: none; }}
}}
"""
(OUT / "tunefps-tokens.css").write_text(css, encoding="utf-8")


# ---------- XAML ----------
def xaml_color(hexstr):
    h = hexstr.lstrip("#").upper()
    return "#" + (h[6:8] + h[:6] if len(h) == 8 else h)


def pascal(path):
    return "".join(p[0].upper() + p[1:] for p in path)


def ms(v):
    return int(str(v).replace("ms", ""))


def px(v):
    return str(v).replace("px", "")


x = ['<?xml version="1.0" encoding="utf-8"?>',
     '<!-- TuneFPS theme v1.0 for WPF (.NET 10). GENERATED from design-tokens.json by tools/generate.py. -->',
     '<!-- NOT COMPILED in the design environment (no .NET SDK there): build and review on Windows before merging. -->',
     '<!-- Fonts: put the static TTFs from 01-final/fonts/static into src/FramePilot.App/Assets/Fonts/ (Build Action: Resource). -->',
     '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
     '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"',
     '                    xmlns:sys="clr-namespace:System;assembly=mscorlib"',
     '                    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"',
     '                    xmlns:po="http://schemas.microsoft.com/winfx/2006/xaml/presentation/options"',
     '                    mc:Ignorable="po">', ""]

x.append("    <!-- Colors and brushes -->")
for path, tok in walk(T):
    if tok.get("$type") != "color":
        continue
    key = pascal(path)
    col = xaml_color(resolve(tok["$value"]))
    x.append(f'    <Color x:Key="{key}Color">{col}</Color>')
    x.append(f'    <SolidColorBrush x:Key="{key}Brush" Color="{{StaticResource {key}Color}}" po:Freeze="True" />')

x += ["", "    <!-- Fonts (pack URIs to embedded static fonts) -->",
      '    <FontFamily x:Key="DisplayFont">pack://application:,,,/Assets/Fonts/#Archivo Black, pack://application:,,,/Assets/Fonts/#Inter Tight ExtraBold, Segoe UI</FontFamily>',
      '    <FontFamily x:Key="UiFont">pack://application:,,,/Assets/Fonts/#Inter, Segoe UI Variable Text, Segoe UI</FontFamily>',
      '    <FontFamily x:Key="MonoFont">Cascadia Mono, Consolas</FontFamily>', ""]

x.append("    <!-- Type sizes (app scale) -->")
for name, tok in T["typography"]["app"].items():
    if name.startswith("$"):
        continue
    x.append(f'    <sys:Double x:Key="FontSize{name[0].upper() + name[1:]}">{px(tok["$value"]["fontSize"])}</sys:Double>')

x += ["", "    <!-- Spacing, radii, sizes -->"]
for k, tok in T["space"].items():
    if k.startswith("$"):
        continue
    x.append(f'    <sys:Double x:Key="Space{k.replace("_", "")}">{px(tok["$value"])}</sys:Double>')
for k, tok in T["radius"].items():
    if k.startswith("$") or tok["$type"] != "dimension" or k == "full":
        continue
    x.append(f'    <CornerRadius x:Key="Radius{k.upper() if len(k) == 2 else k.capitalize()}">{px(tok["$value"])}</CornerRadius>')
for k in ("sm", "md", "lg"):
    x.append(f'    <sys:Double x:Key="ControlHeight{k.upper()}">{px(T["size"]["control"][k]["$value"])}</sys:Double>')
x.append(f'    <sys:Double x:Key="TitleBarHeight">{px(T["size"]["titleBar"]["$value"])}</sys:Double>')
x.append(f'    <GridLength x:Key="SidebarWidth">{px(T["size"]["sidebar"]["$value"])}</GridLength>')
x.append(f'    <Thickness x:Key="ContentPadding">{px(T["size"]["contentPadding"]["$value"])}</Thickness>')

x += ["", "    <!-- Motion: durations and KeySplines (cubic-bezier equivalents for SplineDoubleKeyFrame) -->"]
for k, tok in T["motion"]["duration"].items():
    if k.startswith("$"):
        continue
    v = ms(tok["$value"])
    x.append(f'    <Duration x:Key="Duration{k[0].upper() + k[1:]}">0:0:{v / 1000:.3f}</Duration>')
for k, tok in T["motion"]["easing"].items():
    if k.startswith("$"):
        continue
    a, b, c, d = tok["$value"]
    x.append(f'    <KeySpline x:Key="Ease{k[0].upper() + k[1:]}">{a},{b} {c},{d}</KeySpline>')

d_fast = ms(T["motion"]["duration"]["fast"]["$value"]) / 1000
d_mod = ms(T["motion"]["duration"]["moderate"]["$value"]) / 1000
d_chart = ms(T["motion"]["duration"]["chartEnter"]["$value"]) / 1000
x += ["",
      "    <!-- Reusable storyboards. Skip them when SystemParameters.ClientAreaAnimation is false (Windows 'Animation effects' off). -->",
      '    <Storyboard x:Key="EnterFadeUp">',
      f'        <DoubleAnimationUsingKeyFrames Storyboard.TargetProperty="Opacity"><SplineDoubleKeyFrame KeyTime="0:0:{d_mod:.3f}" Value="1" KeySpline="0.05,0.7 0.1,1" /></DoubleAnimationUsingKeyFrames>',
      f'        <DoubleAnimationUsingKeyFrames Storyboard.TargetProperty="(UIElement.RenderTransform).(TranslateTransform.Y)"><SplineDoubleKeyFrame KeyTime="0:0:{d_mod:.3f}" Value="0" KeySpline="0.05,0.7 0.1,1" /></DoubleAnimationUsingKeyFrames>',
      '    </Storyboard>',
      "    <!-- Chart line draw: set StrokeDashArray to the path length (in stroke-thickness units) and animate StrokeDashOffset from that length to 0. -->",
      '    <Storyboard x:Key="ChartBarGrow">',
      f'        <DoubleAnimationUsingKeyFrames Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleY)"><SplineDoubleKeyFrame KeyTime="0:0:{d_chart:.3f}" Value="1" KeySpline="0.05,0.7 0.1,1" /></DoubleAnimationUsingKeyFrames>',
      '    </Storyboard>', ""]

x += ["    <!-- Focus visual: 2px ring, 2px offset -->",
      '    <Style x:Key="TuneFocusVisual">',
      '        <Setter Property="Control.Template">',
      '            <Setter.Value>',
      '                <ControlTemplate>',
      '                    <Border Margin="-4" BorderThickness="2" CornerRadius="10" BorderBrush="{StaticResource StateFocusRingBrush}" SnapsToDevicePixels="True" />',
      '                </ControlTemplate>',
      '            </Setter.Value>',
      '        </Setter>',
      '    </Style>', "",
      "    <!-- Primary button with animated states -->",
      '    <Style x:Key="TunePrimaryButton" TargetType="Button">',
      '        <Setter Property="Foreground" Value="{StaticResource ColorTextOnAccentBrush}" />',
      '        <Setter Property="FontFamily" Value="{StaticResource UiFont}" />',
      '        <Setter Property="FontWeight" Value="SemiBold" />',
      '        <Setter Property="FontSize" Value="14" />',
      '        <Setter Property="Height" Value="{StaticResource ControlHeightMD}" />',
      '        <Setter Property="Padding" Value="16,0" />',
      '        <Setter Property="Cursor" Value="Hand" />',
      '        <Setter Property="FocusVisualStyle" Value="{StaticResource TuneFocusVisual}" />',
      '        <Setter Property="Template">',
      '            <Setter.Value>',
      '                <ControlTemplate TargetType="Button">',
      '                    <Border x:Name="Bd" CornerRadius="{StaticResource RadiusMD}" Padding="{TemplateBinding Padding}" RenderTransformOrigin="0.5,0.5">',
      '                        <Border.Background><SolidColorBrush x:Name="BdBrush" Color="{StaticResource StateButtonPrimaryRestColor}" /></Border.Background>',
      '                        <Border.RenderTransform><ScaleTransform x:Name="Scale" ScaleX="1" ScaleY="1" /></Border.RenderTransform>',
      '                        <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />',
      '                    </Border>',
      '                    <ControlTemplate.Triggers>',
      '                        <Trigger Property="IsMouseOver" Value="True">',
      f'                            <Trigger.EnterActions><BeginStoryboard><Storyboard><ColorAnimation Storyboard.TargetName="BdBrush" Storyboard.TargetProperty="Color" To="{{StaticResource StateButtonPrimaryHoverColor}}" Duration="0:0:{d_fast:.3f}" /></Storyboard></BeginStoryboard></Trigger.EnterActions>',
      f'                            <Trigger.ExitActions><BeginStoryboard><Storyboard><ColorAnimation Storyboard.TargetName="BdBrush" Storyboard.TargetProperty="Color" To="{{StaticResource StateButtonPrimaryRestColor}}" Duration="0:0:{d_fast:.3f}" /></Storyboard></BeginStoryboard></Trigger.ExitActions>',
      '                        </Trigger>',
      '                        <Trigger Property="IsPressed" Value="True">',
      '                            <Setter TargetName="BdBrush" Property="Color" Value="{StaticResource StateButtonPrimaryPressedColor}" />',
      '                            <Setter TargetName="Scale" Property="ScaleX" Value="0.98" />',
      '                            <Setter TargetName="Scale" Property="ScaleY" Value="0.98" />',
      '                        </Trigger>',
      '                        <Trigger Property="IsEnabled" Value="False">',
      '                            <Setter TargetName="BdBrush" Property="Color" Value="{StaticResource StateButtonPrimaryDisabledBgColor}" />',
      '                            <Setter Property="Foreground" Value="{StaticResource StateButtonPrimaryDisabledTextBrush}" />',
      '                        </Trigger>',
      '                    </ControlTemplate.Triggers>',
      '                </ControlTemplate>',
      '            </Setter.Value>',
      '        </Setter>',
      '    </Style>', "",
      "</ResourceDictionary>", ""]
(OUT / "wpf" / "TuneFPS.Theme.xaml").write_text("\n".join(x), encoding="utf-8")

print(f"css vars: {len(lines)}  typography classes: {len(typo_classes)}  contrast checks: {len(checks)}  failed: {failed}")
sys.exit(1 if failed else 0)
