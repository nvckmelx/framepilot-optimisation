import * as React from "react";

/** Width of an element, updated on resize. */
export function useWidth<T extends HTMLElement>(): [React.RefObject<T | null>, number] {
  const ref = React.useRef<T>(null);
  const [w, setW] = React.useState(0);
  React.useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    setW(el.clientWidth);
    const ro = new ResizeObserver(([e]) => setW(Math.round(e.contentRect.width)));
    ro.observe(el);
    return () => ro.disconnect();
  }, []);
  return [ref, w];
}

/** 0 → 1 over `ms` after mount or when `key` changes (instant with reduced motion). */
export function useProgress(ms: number, key: unknown = 0) {
  const [p, setP] = React.useState(0);
  React.useEffect(() => {
    if (window.matchMedia?.("(prefers-reduced-motion: reduce)").matches) {
      setP(1);
      return;
    }
    let raf = 0;
    const t0 = performance.now();
    setP(0);
    const f = (now: number) => {
      const t = Math.max(0, Math.min(1, (now - t0) / ms));
      setP(1 - Math.pow(1 - t, 3));
      if (t < 1) raf = requestAnimationFrame(f);
    };
    raf = requestAnimationFrame(f);
    return () => cancelAnimationFrame(raf);
  }, [ms, key]);
  return p;
}
