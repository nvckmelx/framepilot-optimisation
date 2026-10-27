# TuneFPS brand 1.0

Replaces the FRAMEPILOT brand in `docs/brand` (superseded). Source of truth: `TuneFPS-brand-handoff.zip`
(brand book, PNG/ICO, fonts, installer art), produced in the TuneFPS naming/brand chat.

- `logo/` — approved outlined SVGs (Archivo Black, tracking −40). Rebuilt with `logo-build/build.py` from the
  same font and settings (`python space.py ArchivoBlack-Regular.ttf && python build.py ArchivoBlack-Regular.ttf -40 out`).
  Never retype the wordmark with a font.
- `design-system/design-tokens.json` — colours, type, spacing, motion. `generate.py` emits `tunefps-tokens.css`,
  `wpf/TuneFPS.Theme.xaml` and `contrast-report.md` (25 WCAG checks, all pass).

Key rules: dark UI only; Tune Blue `#007BFF` for the mark and fills, Deep Blue `#0066D6` for buttons with text,
`#4C9DFF` for accent text and the "after" series, Steel `#A3B3C6` for "before"; spikes are marked with a diamond and
text; Archivo Black has no Cyrillic, so Cyrillic display text uses Inter Tight ExtraBold. All performance numbers in
brand materials are demo data.
