"use client";
/* Performance Bento: a FramePilot session dashboard in the spirit of a finance bento board.
   Frame-time stream, before/after verdict, KPI tiles, distribution, run ranges, run columns,
   recommendations, system and change journal. Pure React + SVG, no chart library. */
import * as React from "react";
import { cn, fmt } from "@/lib/utils";
import { useProgress, useWidth } from "@/lib/hooks";
import { useLang } from "@/lib/i18n";
import { DotNumber } from "@/components/ui/dot-number";
import { CheckIcon } from "@/components/ui/icons";
import type { DiagnosticReport, Measured, MetricComparison, SessionRecord } from "@/lib/report";
import { gameName, tr } from "@/lib/report";

/* ---------------- tokens ---------------- */
const VOLT = "#CBFF4D";
const PAPER = "#F4F6EF";
const MOSS = "#7D877D";
const FLARE = "#FF6B5E";
const BASE = "#A9B4AB";
const MONO = "JetBrains Mono, ui-monospace, monospace";

const WASH = {
  volt: { top: "#e6ffa0", base: "#5d7a0c", bottom: "#93bb22", core: "#0b1100" },
  graphite: { top: "#8e998f", base: "#262d28", bottom: "#38423b", core: "#040605" },
  moss: { top: "#b7c8a2", base: "#34502f", bottom: "#557549", core: "#050c04" },
  amber: { top: "#ffd79a", base: "#8f5414", bottom: "#c98634", core: "#170b01" },
  ink: { top: "#5b6660", base: "#161b18", bottom: "#1f2622", core: "#020302" },
} as const;
type Wash = keyof typeof WASH;

/* ---------------- tooltip ---------------- */
type Tip = { x: number; y: number; title: string; rows: [string, string, string?][] } | null;
const TipCtx = React.createContext<(t: Tip) => void>(() => {});

function Tooltip({ tip }: { tip: Tip }) {
  const ref = React.useRef<HTMLDivElement>(null);
  const [pos, setPos] = React.useState({ left: 0, top: 0 });
  React.useLayoutEffect(() => {
    if (!tip || !ref.current) return;
    const r = ref.current.getBoundingClientRect();
    let left = tip.x + 14;
    let top = tip.y - r.height - 12;
    if (left + r.width > window.innerWidth - 8) left = tip.x - r.width - 14;
    if (top < 8) top = tip.y + 16;
    setPos({ left, top });
  }, [tip]);
  return (
    <div ref={ref} role="status" aria-live="polite"
      className={cn("pointer-events-none fixed z-50 min-w-40 border border-white/12 bg-[#0d100e]/95 px-3 py-2.5 text-[12px] shadow-[0_14px_34px_-12px_rgba(0,0,0,.7)] backdrop-blur-md transition-opacity duration-150 chamfer-sm",
        tip ? "opacity-100" : "opacity-0")}
      style={pos}>
      {tip && (
        <>
          <div className="mb-1.5 font-medium text-mist">{tip.title}</div>
          {tip.rows.map(([k, v, c]) => (
            <div key={k} className="flex items-center justify-between gap-5 leading-5">
              <span className="inline-flex items-center gap-1.5 text-mist">
                {c && <i className="inline-block size-1.5" style={{ background: c }} />}
                {k}
              </span>
              <b className="font-mono font-medium tabular text-paper">{v}</b>
            </div>
          ))}
        </>
      )}
    </div>
  );
}

/* ---------------- tile ---------------- */
function Tile({ wash, solid, glass, className, children, label, coreTop }: {
  wash?: Wash; solid?: boolean; glass?: boolean; className?: string; children: React.ReactNode; label: string; coreTop?: string;
}) {
  const w = wash ? WASH[wash] : null;
  return (
    <section aria-label={label}
      className={cn("tile chamfer flex flex-col p-5 sm:p-6", solid && "bg-volt text-charcoal", glass && "bg-white/[0.045] backdrop-blur-xl", className)}>
      {w && (
        <>
          <div className="wash" style={{ background: `linear-gradient(180deg, ${w.top} 0%, ${w.base} 38%, ${w.base} 72%, ${w.bottom} 100%)` }} />
          <div className="core" style={{ background: w.core, top: coreTop }} />
        </>
      )}
      {children}
    </section>
  );
}

const Title = ({ children, sub, dark }: { children: React.ReactNode; sub?: React.ReactNode; dark?: boolean }) => (
  <div>
    <h3 className={cn("text-[17px] font-semibold tracking-[-0.02em]", dark && "text-charcoal")}>{children}</h3>
    {sub && <p className={cn("mt-0.5 text-[12.5px]", dark ? "text-charcoal/65" : "text-paper/55")}>{sub}</p>}
  </div>
);

const Na = ({ m, children }: { m: Measured<unknown> | undefined; children: React.ReactNode }) => {
  const { t } = useLang();
  if (m && m.isAvailable) return <>{children}</>;
  return <span className="text-paper/45 underline decoration-dotted underline-offset-4" title={`${m?.unavailableReason ?? ""} — ${m?.source ?? ""}`}>{t.unavailable}</span>;
};

/* ---------------- frame-time stream ---------------- */
function FrameTimeChart({ report }: { report: DiagnosticReport }) {
  const { t, lang } = useLang();
  const setTip = React.useContext(TipCtx);
  const [ref, W] = useWidth<HTMLDivElement>();
  const fs = report.latestFrames;
  const g = useProgress(1100, fs?.sessionId);
  const [hover, setHover] = React.useState<number | null>(null);
  const H = 230;
  if (!fs || fs.maxFrameTimeMs.length < 2) return <p className="mt-auto max-w-md text-sm text-paper/70">{t.no_frames}</p>;
  const n = fs.maxFrameTimeMs.length;
  const sortedMean = [...fs.meanFrameTimeMs].sort((a, b) => a - b);
  const typical = sortedMean[Math.floor(n / 2)];
  const session = report.sessions.find((s) => s.id === fs.sessionId);
  const p99 = session?.stats.p99FrameTimeMs ?? typical * 2;
  const top = Math.max(p99 * 1.9, typical * 3);
  const L = 36, R = 10, T = 16, B = 24;
  const X = (i: number) => L + (i / (n - 1)) * (W - L - R);
  const Y = (v: number) => T + (1 - Math.min(v, top) / top) * (H - T - B) * 1;
  const base = H - B;
  const grow = (v: number) => base - (base - Y(v)) * g;
  let maxD = "";
  let meanD = "";
  for (let i = 0; i < n; i++) {
    maxD += (i ? "L" : "M") + X(i).toFixed(1) + "," + grow(fs.maxFrameTimeMs[i]).toFixed(1);
    meanD += (i ? "L" : "M") + X(i).toFixed(1) + "," + grow(fs.meanFrameTimeMs[i]).toFixed(1);
  }
  const areaD = maxD + `L${X(n - 1)},${base}L${X(0)},${base}Z`;
  const seconds = fs.durationMs / 1000;
  const step = seconds > 240 ? 60 : seconds > 90 ? 30 : seconds > 30 ? 10 : 5;
  const ticks: number[] = [];
  for (let s = 0; s <= seconds + 0.01; s += step) ticks.push(s);
  const raw = top / 4;
  const mag = Math.pow(10, Math.floor(Math.log10(raw)));
  const stepV = [1, 2, 2.5, 5, 10].map((k) => k * mag).find((k) => k >= raw) ?? raw;
  const gridVals: number[] = [];
  for (let v = stepV; v < top; v += stepV) gridVals.push(Math.round(v * 100) / 100);

  const onMove = (e: React.PointerEvent<SVGRectElement>) => {
    const r = (e.currentTarget.ownerSVGElement as SVGSVGElement).getBoundingClientRect();
    const i = Math.max(0, Math.min(n - 1, Math.round(((e.clientX - r.left - L) / (W - L - R)) * (n - 1))));
    setHover(i);
    setTip({
      x: r.left + X(i), y: r.top + Y(fs.maxFrameTimeMs[i]),
      title: `${fmt((i / (n - 1)) * seconds, 1, lang)} s`,
      rows: [
        ["max", `${fmt(fs.maxFrameTimeMs[i], 2, lang)} ${t.ms}`, PAPER],
        ["mean", `${fmt(fs.meanFrameTimeMs[i], 2, lang)} ${t.ms}`, "#101412"],
        ["FPS", fmt(1000 / fs.meanFrameTimeMs[i], 0, lang)],
      ],
    });
  };
  return (
    <div ref={ref} className="mt-auto -mx-5 sm:-mx-6">
      {W > 0 && (
        <svg width={W} height={H} viewBox={`0 0 ${W} ${H}`} role="img"
          aria-label={`${t.t_frametime}: P99 ${fmt(p99, 2, lang)} ${t.ms}`}>
          {gridVals.map((v) => (
            <g key={v}>
              <line x1={L} x2={W - R} y1={Y(v)} y2={Y(v)} stroke="rgba(16,20,18,.28)" />
              <text x={L - 8} y={Y(v) + 3.5} textAnchor="end" fontSize="10" fill="rgba(16,20,18,.62)" fontFamily={MONO}>{v}</text>
            </g>
          ))}
          <path d={areaD} fill={PAPER} fillOpacity=".9" />
          <path d={meanD} fill="none" stroke="#101412" strokeWidth="1.6" strokeOpacity=".85" />
          <g style={{ opacity: g > 0.7 ? 1 : 0, transition: "opacity .4s" }}>
            <line x1={L} x2={W - R} y1={Y(p99)} y2={Y(p99)} stroke="#101412" strokeDasharray="5 5" strokeWidth="1.4" />
            <rect x={W - R - 96} y={Y(p99) - 21} width="92" height="18" fill="#101412" />
            <text x={W - R - 50} y={Y(p99) - 8} textAnchor="middle" fontSize="10.5" fontWeight="600" fill={VOLT} fontFamily={MONO}>P99 {fmt(p99, 2, lang)}</text>
          </g>
          {ticks.map((s) => (
            <text key={s} x={L + (s / seconds) * (W - L - R)} y={H - 7} textAnchor="middle" fontSize="10" fill="rgba(244,246,239,.75)" fontFamily={MONO}>{s}s</text>
          ))}
          {hover !== null && <line x1={X(hover)} x2={X(hover)} y1={T} y2={base} stroke="#101412" strokeWidth="1.2" />}
          <rect x={L} y={0} width={Math.max(0, W - L - R)} height={H} fill="transparent"
            onPointerMove={onMove} onPointerLeave={() => { setHover(null); setTip(null); }} />
        </svg>
      )}
    </div>
  );
}

/* ---------------- verdict ---------------- */
function VerdictTile({ report }: { report: DiagnosticReport }) {
  const { t, lang } = useLang();
  const c = report.comparison;
  const g = useProgress(900, c?.overall);
  if (!c || c.metrics.length === 0)
    return (
      <Tile wash="graphite" label={t.t_verdict} className="min-h-[390px] lg:col-span-4 md:col-span-6">
        <Title>{t.t_verdict}</Title>
        <p className="mt-auto text-[15px] leading-6 text-paper/80">{t.t_verdict_none}</p>
      </Tile>
    );
  const good = c.metrics.filter((m) => m.verdict === "Improved").length;
  const n = 48;
  const share = good / c.metrics.length;
  const ticks = Array.from({ length: n + 1 }, (_, i) => {
    const f = i / n, a = Math.PI + f * Math.PI;
    const r1 = 112, r2 = i % 8 === 0 ? 90 : 97;
    return { x1: r1 * Math.cos(a), y1: r1 * Math.sin(a), x2: r2 * Math.cos(a), y2: r2 * Math.sin(a), on: f <= share * g && share > 0 };
  });
  const tone = c.overall === "Improved" ? VOLT : c.overall === "Regressed" ? FLARE : PAPER;
  return (
    <Tile wash="graphite" label={t.t_verdict} className="min-h-[390px] lg:col-span-4 md:col-span-6">
      <div className="flex items-start justify-between gap-3">
        <Title>{t.t_verdict}</Title>
        <span className="bg-black/55 px-2.5 py-1 font-mono text-[11px] text-paper/80 chamfer-sm">
          {c.metrics[0]?.baselineRuns ?? 0} + {c.metrics[0]?.afterRuns ?? 0}
        </span>
      </div>
      <svg viewBox="-125 -122 250 132" className="mx-auto mt-3 w-full max-w-[260px]" aria-hidden="true">
        {ticks.map((k, i) => (
          <line key={i} x1={k.x1} y1={k.y1} x2={k.x2} y2={k.y2} stroke={k.on ? VOLT : "rgba(244,246,239,.22)"} strokeWidth="3" strokeLinecap="square" />
        ))}
        <text y="-26" textAnchor="middle" fontSize="44" fontWeight="200" fill={PAPER} letterSpacing="-2">{good}<tspan fontSize="20" fill="rgba(244,246,239,.5)">/{c.metrics.length}</tspan></text>
        <text y="-4" textAnchor="middle" fontSize="11" fill="rgba(244,246,239,.55)">{t.verdict.Improved}</text>
      </svg>
      <p className="mt-1 text-[13.5px] font-semibold leading-5" style={{ color: tone }}>{t.verdict[c.overall]}</p>
      <p className="mt-1 text-[12.5px] leading-[18px] text-paper/60">{tr(c.summary, lang)}</p>
      <ul className="mt-auto pt-3">
        {c.metrics.map((m, i) => (
          <li key={m.metric} className={cn("flex h-8 items-center gap-2 text-[13px]", i && "border-t border-white/10")}>
            <span className={cn("grid size-4 place-items-center", m.verdict === "Improved" ? "bg-volt text-charcoal" : "bg-white/10 text-transparent")}>
              <CheckIcon width={12} height={12} strokeWidth={3} />
            </span>
            <span className="flex-1 font-medium">{t.metric[m.metric] ?? m.metric}</span>
            <span className="font-mono text-[12px] tabular" style={{ color: m.verdict === "Improved" ? VOLT : m.verdict === "Regressed" ? FLARE : "rgba(244,246,239,.55)" }}>
              {m.deltaPercent > 0 ? "+" : ""}{fmt(m.deltaPercent, 1, lang)}%
            </span>
          </li>
        ))}
      </ul>
      {c.warnings.length > 0 && <p className="mt-2 text-[12px] text-amber">⚠ {c.warnings.map((w) => tr(w, lang)).join(" · ")}</p>}
    </Tile>
  );
}

/* ---------------- KPI ---------------- */
function Spark({ sessions, pick, color }: { sessions: SessionRecord[]; pick: (s: SessionRecord) => number; color: string }) {
  const [ref, W] = useWidth<HTMLDivElement>();
  const H = 30;
  const vals = sessions.map(pick);
  if (vals.length < 2) return <div ref={ref} className="h-[30px]" />;
  const lo = Math.min(...vals), hi = Math.max(...vals), span = hi - lo || 1;
  const X = (i: number) => 3 + (i / (vals.length - 1)) * (W - 6);
  const Y = (v: number) => H - 4 - ((v - lo) / span) * (H - 8);
  return (
    <div ref={ref} className="h-[30px]">
      {W > 0 && (
        <svg width={W} height={H} aria-hidden="true">
          <path d={vals.map((v, i) => `${i ? "L" : "M"}${X(i)},${Y(v)}`).join("")} fill="none" stroke={color} strokeOpacity=".5" strokeWidth="1.4" />
          {vals.map((v, i) => (
            <rect key={i} x={X(i) - 3} y={Y(v) - 3} width="6" height="6"
              fill={sessions[i].group === "After" ? (color === "#101412" ? "#101412" : VOLT) : sessions[i].group === "Baseline" ? BASE : "rgba(244,246,239,.4)"} />
          ))}
        </svg>
      )}
    </div>
  );
}

function Kpi({ label, value, unit, sub, sessions, pick, solid, children }: {
  label: string; value: string; unit?: string; sub: string; sessions: SessionRecord[]; pick: (s: SessionRecord) => number; solid?: boolean; children?: React.ReactNode;
}) {
  return (
    <Tile solid={solid} glass={!solid} label={label} className="min-h-[172px] lg:col-span-3 md:col-span-6">
      <div className={cn("text-[12.5px] font-semibold", solid ? "text-charcoal/65" : "text-paper/60")}>{label}</div>
      <div className="mt-2 flex items-baseline gap-1.5">
        <span className="text-[40px] font-extralight leading-none tracking-[-0.04em] tabular">{value}</span>
        {unit && <span className={cn("text-[15px] font-medium", solid ? "text-charcoal/60" : "text-paper/50")}>{unit}</span>}
      </div>
      <div className={cn("mt-1 text-[12px]", solid ? "text-charcoal/60" : "text-paper/45")}>{sub}</div>
      <div className="mt-auto pt-2">{children ?? <Spark sessions={sessions} pick={pick} color={solid ? "#101412" : PAPER} />}</div>
    </Tile>
  );
}

/* ---------------- histogram ---------------- */
function Histogram({ report }: { report: DiagnosticReport }) {
  const { t, lang } = useLang();
  const setTip = React.useContext(TipCtx);
  const [ref, W] = useWidth<HTMLDivElement>();
  const fs = report.latestFrames;
  const session = report.sessions.find((s) => s.id === fs?.sessionId);
  const g = useProgress(900, fs?.sessionId);
  const H = 210;
  if (!fs || fs.histogram.length === 0) return <p className="mt-auto text-sm text-paper/70">{t.no_frames}</p>;
  const total = fs.histogram.reduce((a, b) => a + b.count, 0);
  // Show the body of the distribution: up to ~2.2× P99 (min 8 ms); frames beyond are summarised as a tail.
  const p99 = session?.stats.p99FrameTimeMs ?? 16;
  const limitMs = Math.max(6, Math.ceil(p99 * 1.6));
  const bins = fs.histogram.filter((b) => b.fromMs < limitMs);
  const tail = total - bins.reduce((a, b) => a + b.count, 0);
  const maxC = Math.max(...bins.map((b) => b.count), 1);
  const L = 10, R = 10, T = 10, B = 24;
  const bw = (W - L - R) / bins.length;
  const X = (ms: number) => L + (ms / limitMs) * (W - L - R);
  const spike = (session?.stats.medianFrameTimeMs ?? 0) * 2;
  return (
    <div ref={ref} className="mt-auto">
      {W > 0 && (
        <svg width={W} height={H} viewBox={`0 0 ${W} ${H}`} role="img" aria-label={t.t_hist}>
          {bins.map((b, i) => {
            const h = (b.count / maxC) * (H - T - B) * g;
            const hot = spike > 0 && b.fromMs >= spike;
            return (
              <rect key={i} x={L + i * bw + 0.5} y={H - B - h} width={Math.max(1, bw - 1)} height={h}
                fill={hot ? FLARE : PAPER} fillOpacity={hot ? 1 : 0.88}
                onPointerMove={(e) => setTip({ x: e.clientX, y: e.clientY, title: `${fmt(b.fromMs, 1, lang)}–${fmt(b.toMs, 1, lang)} ${t.ms}`,
                  rows: [[t.frames, fmt(b.count, 0, lang)], ["%", fmt((100 * b.count) / total, 2, lang)]] })}
                onPointerLeave={() => setTip(null)} />
            );
          })}
          {[8.33, 16.67, 33.33].filter((v) => v < limitMs).map((v) => (
            <g key={v}>
              <line x1={X(v)} x2={X(v)} y1={T} y2={H - B} stroke="rgba(16,20,18,.7)" strokeDasharray="3 4" />
              <text x={X(v) + 4} y={T + 10} fontSize="10" fill="rgba(244,246,239,.85)" fontFamily={MONO}>{Math.round(1000 / v)} FPS</text>
            </g>
          ))}
          {Array.from({ length: Math.floor(limitMs / 4) + 1 }, (_, i) => i * 4).map((ms) => (
            <text key={ms} x={X(ms)} y={H - 7} textAnchor="middle" fontSize="10" fill="rgba(244,246,239,.7)" fontFamily={MONO}>{ms}</text>
          ))}
        </svg>
      )}
      <div className="mt-1 flex justify-between text-[11.5px] text-paper/55">
        <span>{t.ms} →</span>
        {tail > 0 && <span>+{fmt(tail, 0, lang)} {t.frames} &gt; {fmt(limitMs, 0, lang)} {t.ms}</span>}
      </div>
    </div>
  );
}

/* ---------------- run ranges (dumbbell) ---------------- */
function Ranges({ metrics }: { metrics: MetricComparison[] }) {
  const { t, lang } = useLang();
  const g = useProgress(900, metrics.length);
  return (
    <ul className="mt-auto space-y-4">
      {metrics.map((m) => {
        const lo = Math.min(m.baselineMin, m.afterMin), hi = Math.max(m.baselineMax, m.afterMax);
        const pad = (hi - lo) * 0.12 || 1;
        const P = (v: number) => ((v - lo + pad) / (hi - lo + 2 * pad)) * 100;
        const bar = (a: number, b: number, color: string, y: number) => (
          <div className="absolute h-[7px]" style={{ left: `${P(a)}%`, width: `${Math.max(1.5, (P(b) - P(a)) * g)}%`, top: y, background: color }} />
        );
        return (
          <li key={m.metric}>
            <div className="flex items-baseline justify-between text-[12.5px]">
              <span className="font-semibold">{t.metric[m.metric] ?? m.metric}</span>
              <span className="font-mono text-[11.5px] text-paper/60 tabular">{fmt(m.baselineMedian, 1, lang)} → <span style={{ color: m.verdict === "Improved" ? VOLT : PAPER }}>{fmt(m.afterMedian, 1, lang)}</span></span>
            </div>
            <div className="relative mt-1.5 h-[22px]">
              <div className="absolute inset-x-0 top-[10px] h-px bg-white/12" />
              {bar(m.baselineMin, m.baselineMax, BASE, 2)}
              {bar(m.afterMin, m.afterMax, VOLT, 13)}
            </div>
          </li>
        );
      })}
      <li className="flex gap-4 pt-1 text-[11.5px] text-paper/60">
        <span className="inline-flex items-center gap-1.5"><i className="inline-block h-[7px] w-3" style={{ background: BASE }} />{t.before}</span>
        <span className="inline-flex items-center gap-1.5"><i className="inline-block h-[7px] w-3" style={{ background: VOLT }} />{t.after}</span>
      </li>
    </ul>
  );
}

/* ---------------- run columns ---------------- */
function Runs({ sessions }: { sessions: SessionRecord[] }) {
  const { t, lang } = useLang();
  const setTip = React.useContext(TipCtx);
  const [ref, W] = useWidth<HTMLDivElement>();
  const g = useProgress(900, sessions.length);
  const H = 190;
  const shown = sessions.slice(-8);
  if (shown.length === 0) return <p className="mt-auto text-sm text-paper/70">{t.no_sessions}</p>;
  const maxV = Math.max(...shown.map((s) => s.stats.averageFps)) * 1.08;
  const B = 20;
  const bw = Math.min(26, (W - 8) / shown.length - 8);
  const gap = (W - bw * shown.length) / Math.max(1, shown.length);
  return (
    <div ref={ref} className="mt-auto">
      {W > 0 && (
        <svg width={W} height={H} viewBox={`0 0 ${W} ${H}`} role="img" aria-label={t.t_runs}>
          {shown.map((s, i) => {
            const x = gap / 2 + i * (bw + gap);
            const full = H - B - 4;
            const h = (s.stats.averageFps / maxV) * full * g;
            const color = s.group === "After" ? VOLT : s.group === "Baseline" ? PAPER : "rgba(244,246,239,.45)";
            return (
              <g key={s.id} onPointerMove={(e) => setTip({ x: e.clientX, y: e.clientY,
                title: new Date(s.startedAt).toLocaleString(lang === "ru" ? "ru-RU" : "en-US", { dateStyle: "short", timeStyle: "short" }),
                rows: [["FPS", fmt(s.stats.averageFps, 0, lang), color], ["P99", `${fmt(s.stats.p99FrameTimeMs, 2, lang)} ${t.ms}`], ["1% low", fmt(s.stats.onePercentLowFps, 0, lang)]] })}
                onPointerLeave={() => setTip(null)}>
                <rect x={x} y={4} width={bw} height={full} rx={bw / 2} fill="rgba(0,0,0,.26)" />
                <rect x={x} y={4 + full - h} width={bw} height={h} rx={bw / 2} fill={color} />
                <text x={x + bw / 2} y={H - 5} textAnchor="middle" fontSize="10" fill="rgba(244,246,239,.7)" fontFamily={MONO}>{i + 1}</text>
              </g>
            );
          })}
        </svg>
      )}
    </div>
  );
}

/* ---------------- recommendations ---------------- */
function Recs({ report }: { report: DiagnosticReport }) {
  const { t, lang } = useLang();
  const recs = report.recommendations;
  const count = (k: string) => recs.filter((r) => r.kind === k).length;
  const groups = [
    { k: "AutoApplicable", label: t.k_auto, color: VOLT, n: count("AutoApplicable") },
    { k: "UserAction", label: t.k_user, color: PAPER, n: count("UserAction") },
    { k: "DiagnosticOnly", label: t.k_diag, color: MOSS, n: count("DiagnosticOnly") },
  ];
  const total = Math.max(1, recs.length);
  return (
    <>
      <div className="mt-4 flex h-2 gap-[3px]">
        {groups.filter((x) => x.n > 0).map((x) => <div key={x.k} style={{ flex: x.n / total, background: x.color }} />)}
      </div>
      <div className="mt-2 flex flex-wrap gap-x-5 gap-y-1 text-[12px] text-paper/65">
        {groups.map((x) => (
          <span key={x.k} className="inline-flex items-center gap-1.5"><i className="inline-block size-2" style={{ background: x.color }} /><b className="font-mono font-medium text-paper">{x.n}</b> {x.label}</span>
        ))}
      </div>
      <ul className="mt-4">
        {recs.slice(0, 6).map((r, i) => {
          const color = groups.find((x) => x.k === r.kind)?.color ?? PAPER;
          return (
            <li key={r.ruleId} className={cn("flex items-start gap-3 py-2.5", i && "border-t border-white/8")}>
              <i className="mt-1.5 inline-block h-2.5 w-1 shrink-0" style={{ background: color }} />
              <div className="min-w-0 flex-1">
                <div className="text-[13.5px] font-semibold leading-5">{tr(r.title, lang)}</div>
                <div className="text-[12.5px] leading-[18px] text-paper/55">{tr(r.symptom, lang)}</div>
              </div>
              <span className="shrink-0 font-mono text-[11px] text-paper/45">{t.confidence[r.confidence]}</span>
            </li>
          );
        })}
      </ul>
    </>
  );
}

/* ---------------- system ---------------- */
function System({ report }: { report: DiagnosticReport }) {
  const { t, lang } = useLang();
  const s = report.snapshot;
  if (!s) return <p className="mt-4 text-sm text-paper/60">{t.unavailable}</p>;
  const gpu = s.gpus.find((g) => g.likelyIntegrated?.isAvailable && g.likelyIntegrated.value === false) ?? s.gpus[0];
  const d = s.displays.find((x) => x.isPrimary) ?? s.displays[0];
  const onoff = (m: Measured<boolean> | undefined) => <Na m={m}>{m?.value ? t.on : t.off}</Na>;
  const rows: [string, React.ReactNode][] = [
    [t.sys_os, <><Na m={s.os.productName}>{s.os.productName.value}</Na> <span className="text-paper/45"><Na m={s.os.build}>{s.os.build.value}</Na></span></>],
    [t.sys_cpu, <><Na m={s.cpu.name}>{s.cpu.name.value}</Na> <span className="text-paper/45"><Na m={s.cpu.physicalCores}>{s.cpu.physicalCores.value}</Na>/<Na m={s.cpu.logicalProcessors}>{s.cpu.logicalProcessors.value}</Na></span></>],
    [t.sys_gpu, gpu ? <><Na m={gpu.name}>{gpu.name.value}</Na> <span className="text-paper/45"><Na m={gpu.dedicatedMemoryBytes}>{fmt((gpu.dedicatedMemoryBytes.value ?? 0) / 1024 ** 3, 0, lang)} GB</Na></span></> : t.unavailable],
    [t.sys_ram, <Na m={s.memory.totalBytes}>{fmt((s.memory.totalBytes.value ?? 0) / 1024 ** 3, 0, lang)} GB · {s.memory.modules.length}×</Na>],
    [t.sys_display, d ? <><Na m={d.width}>{d.width.value}×{d.height.value}</Na> @ <Na m={d.currentRefreshHz}>{d.currentRefreshHz.value} Hz</Na>
      {d.maxRefreshHzAtCurrentResolution?.isAvailable && d.currentRefreshHz?.isAvailable && (d.maxRefreshHzAtCurrentResolution.value ?? 0) > (d.currentRefreshHz.value ?? 0) &&
        <span className="text-amber"> / max {d.maxRefreshHzAtCurrentResolution.value}</span>}</> : t.unavailable],
    [t.sys_gamemode, onoff(s.settings.gameMode)],
    [t.sys_hags, onoff(s.settings.hardwareAcceleratedGpuScheduling)],
    [t.sys_power, <Na m={s.settings.activePowerScheme}>{s.settings.activePowerScheme.value?.name}</Na>],
  ];
  return (
    <dl className="mt-4">
      {rows.map(([k, v], i) => (
        <div key={k} className={cn("flex gap-3 py-2 text-[13px]", i && "border-t border-white/8")}>
          <dt className="w-28 shrink-0 text-paper/55">{k}</dt>
          <dd className="min-w-0 flex-1 truncate font-medium">{v}</dd>
        </div>
      ))}
    </dl>
  );
}

/* ---------------- main ---------------- */
export interface PerformanceBentoProps {
  report: DiagnosticReport;
  className?: string;
}

export function PerformanceBento({ report, className }: PerformanceBentoProps) {
  const { t, lang } = useLang();
  const [tip, setTip] = React.useState<Tip>(null);
  const [mounted, setMounted] = React.useState(false);
  React.useEffect(() => {
    const id = requestAnimationFrame(() => setMounted(true));
    return () => cancelAnimationFrame(id);
  }, []);

  const latest = report.sessions.find((s) => s.id === report.latestFrames?.sessionId) ?? report.sessions[report.sessions.length - 1];
  const game = latest?.game;
  const sameGame = report.sessions.filter((s) => s.game === game);
  const st = latest?.stats;
  const avgFps = report.comparison?.metrics.find((m) => m.metric === "AverageFps");
  const spikePct = st ? (100 * st.spikeCount) / Math.max(1, st.frameCount) : NaN;
  const pitch = typeof window !== "undefined" && window.innerWidth < 520 ? 5.2 : 7;

  return (
    <TipCtx.Provider value={setTip}>
      <div className={cn("reveal grid grid-cols-1 gap-3.5 md:grid-cols-12", mounted && "in", className)}>
        {/* frame time */}
        <Tile wash="volt" coreTop="52%" label={t.t_frametime} className="min-h-[390px] md:col-span-12 lg:col-span-8">
          <div className="flex flex-wrap items-start justify-between gap-3">
            <Title dark sub={t.t_frametime_sub}>{t.t_frametime} · {gameName(game)}</Title>
            <div className="flex gap-4 text-[12px] font-medium text-charcoal/75">
              <span className="inline-flex items-center gap-1.5"><i className="inline-block size-2 bg-paper ring-1 ring-charcoal/40" />max</span>
              <span className="inline-flex items-center gap-1.5"><i className="inline-block h-0.5 w-3 bg-charcoal" />mean</span>
            </div>
          </div>
          {st && (
            <div className="mt-4 flex flex-wrap items-end gap-x-5 gap-y-2">
              <DotNumber value={st.averageFps} pitch={pitch} color="#101412" dim="rgba(16,20,18,.1)" label={`${fmt(st.averageFps, 0, lang)} ${t.t_avg_fps}`} />
              <div className="pb-1">
                <div className="font-mono text-[12.5px] font-semibold text-charcoal">
                  {avgFps ? `${avgFps.deltaPercent >= 0 ? "↑" : "↓"} ${fmt(Math.abs(avgFps.deltaPercent), 1, lang)}% ${t.after.toLowerCase()}` : t.t_avg_fps}
                </div>
                <div className="mt-0.5 text-[12.5px] text-charcoal/70">
                  {fmt(st.frameCount, 0, lang)} {t.frames} · {fmt(st.durationMs / 1000, 0, lang)} s · {latest?.conditions.displayWidth}×{latest?.conditions.displayHeight} @ {latest?.conditions.refreshHz} Hz
                </div>
              </div>
            </div>
          )}
          <FrameTimeChart report={report} />
        </Tile>

        <VerdictTile report={report} />

        {/* KPI row */}
        {st && (
          <>
            <Kpi solid label={t.t_p99} value={fmt(st.p99FrameTimeMs, 2, lang)} unit={t.ms} sub={`≈ ${fmt(1000 / st.p99FrameTimeMs, 0, lang)} FPS`}
              sessions={sameGame} pick={(s) => s.stats.p99FrameTimeMs} />
            <Kpi label={t.t_low} value={fmt(st.onePercentLowFps, 0, lang)} unit={t.fps} sub={`${t.t_avg_fps}: ${fmt(st.averageFps, 0, lang)}`}
              sessions={sameGame} pick={(s) => s.stats.onePercentLowFps} />
            <Kpi label={t.t_spikes} value={fmt(spikePct, 2, lang)} unit="%" sub={t.t_spikes_sub}
              sessions={sameGame} pick={(s) => (100 * s.stats.spikeCount) / Math.max(1, s.stats.frameCount)} />
            <Kpi label={t.t_gpu} value={st.medianGpuBusyRatio?.isAvailable ? fmt((st.medianGpuBusyRatio.value ?? 0) * 100, 0, lang) : "—"} unit={st.medianGpuBusyRatio?.isAvailable ? "%" : undefined}
              sub={st.medianGpuBusyRatio?.isAvailable ? t.t_gpu_sub : t.unavailable} sessions={sameGame} pick={(s) => s.stats.medianGpuBusyRatio?.value ?? 0}>
              <GpuBar ratio={st.medianGpuBusyRatio?.isAvailable ? st.medianGpuBusyRatio.value ?? 0 : null} />
            </Kpi>
          </>
        )}

        {/* distribution */}
        <Tile wash="moss" label={t.t_hist} className="min-h-[330px] md:col-span-12 lg:col-span-5">
          <Title sub={t.t_hist_sub}>{t.t_hist}</Title>
          <Histogram report={report} />
        </Tile>

        {/* ranges */}
        <Tile wash="ink" label={t.t_ranges} className="min-h-[330px] md:col-span-6 lg:col-span-4">
          <Title sub={t.t_ranges_sub}>{t.t_ranges}</Title>
          {report.comparison && report.comparison.metrics.length > 0
            ? <Ranges metrics={report.comparison.metrics} />
            : <p className="mt-auto text-sm text-paper/60">{t.t_verdict_none}</p>}
        </Tile>

        {/* runs */}
        <Tile wash="amber" label={t.t_runs} className="min-h-[330px] md:col-span-6 lg:col-span-3">
          <Title sub={t.t_runs_sub}>{t.t_runs}</Title>
          <div className="mt-3 flex gap-3 text-[11.5px] text-paper/75">
            <span className="inline-flex items-center gap-1.5"><i className="inline-block size-2 bg-paper" />{t.before}</span>
            <span className="inline-flex items-center gap-1.5"><i className="inline-block size-2 bg-volt" />{t.after}</span>
          </div>
          <Runs sessions={sameGame} />
        </Tile>

        {/* recommendations */}
        <Tile glass label={t.t_recs} className="md:col-span-12 lg:col-span-7">
          <Title>{t.t_recs} <span className="ml-1 font-mono text-[13px] font-medium text-paper/45">{report.recommendations.length}</span></Title>
          <Recs report={report} />
        </Tile>

        {/* system + changes */}
        <Tile glass label={t.t_system} className="md:col-span-12 lg:col-span-5">
          <Title>{t.t_system}</Title>
          <System report={report} />
          <div className="mt-5 border-t border-white/10 pt-4">
            <div className="text-[13px] font-semibold">{t.t_changes}</div>
            {report.changes.length === 0
              ? <p className="mt-1 text-[12.5px] text-paper/55">{t.t_changes_none}</p>
              : (
                <ul className="mt-2 space-y-2">
                  {report.changes.slice(0, 4).map((c) => (
                    <li key={c.id} className="text-[12px] leading-[17px]">
                      <div className="truncate font-mono text-paper/80" title={c.target}>{c.target}</div>
                      <div className="text-paper/50">{c.original} → <span className="text-paper/80">{c.planned}</span> · <span className={c.state === "Verified" ? "text-volt" : "text-amber"}>{t.state[c.state] ?? c.state}</span></div>
                    </li>
                  ))}
                </ul>
              )}
          </div>
        </Tile>
      </div>
      <Tooltip tip={tip} />
    </TipCtx.Provider>
  );
}

function GpuBar({ ratio }: { ratio: number | null }) {
  const n = 36;
  const g = useProgress(800, ratio);
  return (
    <div className="flex h-[22px] items-end gap-[3px]" aria-hidden="true">
      {Array.from({ length: n }, (_, i) => {
        const on = ratio !== null && i / (n - 1) <= ratio * g;
        return <i key={i} className="block w-[2px] flex-1" style={{ height: i % 6 === 0 ? 22 : 14, background: on ? VOLT : "rgba(244,246,239,.22)" }} />;
      })}
    </div>
  );
}

export default PerformanceBento;
