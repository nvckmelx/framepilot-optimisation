import * as React from "react";
import { prefersReducedMotion } from "@/lib/utils";

/* 5×7 module-matrix glyphs (square cells, echoing the 16-unit module of the Frame F mark). */
const G: Record<string, string[]> = {
  "0": ["01110", "10001", "10011", "10101", "11001", "10001", "01110"],
  "1": ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
  "2": ["01110", "10001", "00001", "00110", "01000", "10000", "11111"],
  "3": ["11110", "00001", "00001", "01110", "00001", "00001", "11110"],
  "4": ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
  "5": ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
  "6": ["00110", "01000", "10000", "11110", "10001", "10001", "01110"],
  "7": ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
  "8": ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
  "9": ["01110", "10001", "10001", "01111", "00001", "00010", "01100"],
  ".": ["0", "0", "0", "0", "0", "0", "1"],
  ",": ["0", "0", "0", "0", "0", "1", "1"],
  " ": ["00", "00", "00", "00", "00", "00", "00"],
  "-": ["000", "000", "000", "111", "000", "000", "000"],
};

function layout(text: string) {
  const on: [number, number][] = [];
  let x = 0;
  for (const ch of text) {
    const g = G[ch] ?? G[" "];
    const w = g[0].length;
    for (let r = 0; r < 7; r++) for (let c = 0; c < w; c++) if (g[r][c] === "1") on.push([x + c, r]);
    x += w + 1;
  }
  return { on, cols: Math.max(1, x - 1) };
}

export interface DotNumberProps {
  value: number;
  digits?: number;
  pitch?: number;
  color?: string;
  dim?: string;
  label?: string;
  className?: string;
}

/** Counts up to `value` and renders it as lit dots on a faint grid. */
export function DotNumber({ value, digits = 0, pitch = 7, color = "#CBFF4D", dim = "rgba(244,246,239,.08)", label, className }: DotNumberProps) {
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

  const text = Number.isFinite(shown) ? shown.toFixed(digits) : "-";
  const final = Number.isFinite(value) ? value.toFixed(digits) : "-";
  const { on, cols } = layout(text);
  const total = layout(final).cols;
  const lit = new Set(on.map(([c, r]) => c * 8 + r));
  const cell = pitch * 0.78;
  const dots: React.ReactNode[] = [];
  for (let c = 0; c < total; c++)
    for (let row = 0; row < 7; row++) {
      const isOn = lit.has(c * 8 + row);
      dots.push(
        <rect key={c * 8 + row} x={c * pitch + (pitch - (isOn ? cell : cell * 0.5)) / 2} y={row * pitch + (pitch - (isOn ? cell : cell * 0.5)) / 2}
          width={isOn ? cell : cell * 0.5} height={isOn ? cell : cell * 0.5} fill={isOn ? color : c < cols ? dim : "transparent"} />,
      );
    }
  return (
    <svg role="img" aria-label={label ?? final} className={className} width={total * pitch} height={7 * pitch} viewBox={`0 0 ${total * pitch} ${7 * pitch}`}>
      {dots}
    </svg>
  );
}
