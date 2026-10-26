import * as React from "react";
import { useWidth } from "@/lib/hooks";
import { prefersReducedMotion } from "@/lib/utils";

/* A live-looking frame-time trace for the hero: the thing FramePilot actually looks at.
   It plays a synthetic recording (labelled as such) — a steady ~240 FPS pace with occasional hitches. */

function makeSource(n: number) {
  let s = 7;
  const r = () => ((s = (s * 1103515245 + 12345) >>> 0) / 4294967296);
  const out: number[] = [];
  for (let i = 0; i < n; i++) {
    let v = 4.1 + (r() - 0.5) * 0.9 + Math.sin(i / 37) * 0.35;
    if (i % 173 === 91) v *= 3.4;
    if (i % 311 === 207) v *= 2.2;
    out.push(v);
  }
  return out;
}

export function FrameTrace({ caption, p99Label, spikeLabel }: { caption: string; p99Label: string; spikeLabel: string }) {
  const [box, width] = useWidth<HTMLDivElement>();
  const H = 260;
  const pad = { l: 44, r: 12, t: 18, b: 26 };
  const path = React.useRef<SVGPathElement>(null);
  const area = React.useRef<SVGPathElement>(null);
  const mark = React.useRef<SVGGElement>(null);
  const src = React.useMemo(() => makeSource(4096), []);
  const maxY = 18;

  React.useEffect(() => {
    if (!width) return;
    const visible = Math.max(80, Math.floor((width - pad.l - pad.r) / 2.2));
    const X = (i: number) => pad.l + (i / (visible - 1)) * (width - pad.l - pad.r);
    const Y = (v: number) => pad.t + (1 - Math.min(v, maxY) / maxY) * (H - pad.t - pad.b);
    let offset = 0;
    let raf = 0;
    let last = performance.now();
    const draw = () => {
      let d = "";
      let spikeAt = -1;
      for (let i = 0; i < visible; i++) {
        const v = src[(offset + i) % src.length];
        d += (i ? "L" : "M") + X(i).toFixed(1) + "," + Y(v).toFixed(1);
        if (v > 10 && i > visible * 0.15 && i < visible * 0.85) spikeAt = i;
      }
      path.current?.setAttribute("d", d);
      area.current?.setAttribute("d", d + `L${X(visible - 1)},${H - pad.b}L${X(0)},${H - pad.b}Z`);
      if (mark.current) {
        if (spikeAt >= 0) {
          const v = src[(offset + spikeAt) % src.length];
          mark.current.setAttribute("transform", `translate(${X(spikeAt)},${Y(v)})`);
          mark.current.style.opacity = "1";
          const t = mark.current.querySelector("text");
          if (t) t.textContent = `${spikeLabel} ${v.toFixed(1)}`;
        } else mark.current.style.opacity = "0";
      }
    };
    draw();
    if (prefersReducedMotion()) return;
    const tick = (now: number) => {
      if (now - last > 33) {
        offset = (offset + 1) % src.length;
        last = now;
        draw();
      }
      raf = requestAnimationFrame(tick);
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, [width, src, spikeLabel]);

  const Y = (v: number) => pad.t + (1 - v / maxY) * (H - pad.t - pad.b);
  const sorted = [...src].sort((a, b) => a - b);
  const p99 = sorted[Math.ceil(0.99 * sorted.length) - 1];

  return (
    <figure className="relative">
      <div ref={box} className="relative w-full" style={{ height: H }}>
        {width > 0 && (
          <svg width={width} height={H} viewBox={`0 0 ${width} ${H}`} className="absolute inset-0" aria-hidden="true">
            <defs>
              <linearGradient id="fp-trace-fill" x1="0" y1="0" x2="0" y2="1">
                <stop offset="0" stopColor="#CBFF4D" stopOpacity=".26" />
                <stop offset="1" stopColor="#CBFF4D" stopOpacity="0" />
              </linearGradient>
              <linearGradient id="fp-trace-fade" x1="0" x2="1">
                <stop offset="0" stopColor="#fff" stopOpacity="0" />
                <stop offset=".12" stopColor="#fff" stopOpacity="1" />
                <stop offset=".94" stopColor="#fff" stopOpacity="1" />
                <stop offset="1" stopColor="#fff" stopOpacity="0" />
              </linearGradient>
              <mask id="fp-trace-mask">
                <rect width={width} height={H} fill="url(#fp-trace-fade)" />
              </mask>
            </defs>
            {[4.17, 8.33, 16.67].map((v) => (
              <g key={v}>
                <line x1={pad.l} x2={width - pad.r} y1={Y(v)} y2={Y(v)} stroke="rgba(244,246,239,.08)" />
                <text x={pad.l - 8} y={Y(v) + 3.5} textAnchor="end" fontSize="10" fill="#7D877D" fontFamily="JetBrains Mono, monospace">
                  {v}
                </text>
              </g>
            ))}
            <line x1={pad.l} x2={width - pad.r} y1={Y(p99)} y2={Y(p99)} stroke="#FFB547" strokeDasharray="4 5" strokeOpacity=".8" />
            <text x={pad.l + 4} y={Y(p99) - 6} textAnchor="start" fontSize="10.5" fill="#FFB547" fontFamily="JetBrains Mono, monospace">
              {p99Label} {p99.toFixed(1)}
            </text>
            <g mask="url(#fp-trace-mask)">
              <path ref={area} fill="url(#fp-trace-fill)" />
              <path ref={path} fill="none" stroke="#CBFF4D" strokeWidth="1.6" strokeLinejoin="round" className="trace-glow" />
            </g>
            <g ref={mark} style={{ opacity: 0, transition: "opacity .2s" }}>
              <circle r="4.5" fill="#101412" stroke="#FF6B5E" strokeWidth="2" />
              <rect x="8" y="-22" width="92" height="20" fill="#FF6B5E" />
              <text x="14" y="-8" fontSize="11" fontWeight="700" fill="#101412" fontFamily="JetBrains Mono, monospace" />
            </g>
          </svg>
        )}
      </div>
      <figcaption className="mt-2 text-[13px] text-moss">{caption}</figcaption>
    </figure>
  );
}
