import * as React from "react";
import { prefersReducedMotion } from "@/lib/utils";

export interface DotNumberProps {
  value: number;
  digits?: number;
  /** Kept for call-site compatibility: scales the number (7 ≈ 64 px). */
  pitch?: number;
  color?: string;
  label?: string;
  className?: string;
}

/** Big metric in the TuneFPS display face (Archivo Black, tabular). Counts up over 900 ms
 *  (motion.duration.counter) unless the visitor asked for reduced motion. */
export function DotNumber({ value, digits = 0, pitch = 7, color = "#FFFFFF", label, className }: DotNumberProps) {
  const [shown, setShown] = React.useState(prefersReducedMotion() ? value : 0);
  const from = React.useRef(shown);
  React.useEffect(() => {
    if (prefersReducedMotion()) {
      setShown(value);
      return;
    }
    const start = from.current;
    const t0 = performance.now();
    let raf = 0;
    const f = (now: number) => {
      const t = Math.max(0, Math.min(1, (now - t0) / 900));
      const v = start + (value - start) * (1 - Math.pow(1 - t, 3));
      setShown(v);
      from.current = v;
      if (t < 1) raf = requestAnimationFrame(f);
    };
    raf = requestAnimationFrame(f);
    return () => cancelAnimationFrame(raf);
  }, [value]);

  const final = Number.isFinite(value) ? value.toFixed(digits) : "—";
  const text = Number.isFinite(shown) ? shown.toFixed(digits) : "—";
  return (
    <span role="img" aria-label={label ?? final} className={className}
      style={{ display: "inline-grid", color, fontFamily: "var(--font-display)", fontWeight: 800, fontSize: Math.round(pitch * 9.2),
        lineHeight: 1, letterSpacing: "-0.01em", fontVariantNumeric: "tabular-nums" }}>
      {/* the final value reserves the width so the layout does not jump while counting */}
      <span aria-hidden="true" style={{ gridArea: "1/1", visibility: "hidden" }}>{final}</span>
      <span aria-hidden="true" style={{ gridArea: "1/1", textAlign: "right" }}>{text}</span>
    </span>
  );
}
