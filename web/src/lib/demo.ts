/* Demonstration report. Every object is flagged isDemo and the UI labels it "DEMO DATA".
   Statistics are computed with the same definitions as the app (docs/MEASUREMENT_PROTOCOL.md). */
import type { ComparisonResult, DiagnosticReport, FrameSeries, FrameStats, Measured, MetricComparison, RunGroup, SessionRecord } from "./report";

const D = <T,>(value: T): Measured<T> => ({ value, isAvailable: true, source: "DEMO DATA" });
const NA = <T,>(reason: string): Measured<T> => ({ value: null, isAvailable: false, source: "DEMO DATA", unavailableReason: reason });

function rng(seed: number) {
  let s = seed >>> 0;
  return () => {
    s = (s * 1664525 + 1013904223) >>> 0;
    return s / 4294967296;
  };
}

/** Synthetic CS2-like frame times: base pace, jitter, rare hitches and a short heavy fight. */
export function demoFrames(seed: number, baseMs: number, jitter: number, spikeChance: number, n = 9000): number[] {
  const r = rng(seed);
  const out: number[] = [];
  for (let i = 0; i < n; i++) {
    const fight = i > n * 0.55 && i < n * 0.62 ? 1.35 : 1;
    let ft = (baseMs + (r() - 0.5) * jitter + Math.sin(i / 340) * jitter * 0.35) * fight;
    if (r() < spikeChance) ft *= 2.4 + r() * 2.6;
    out.push(Math.max(1, ft));
  }
  return out;
}

export function stats(times: number[]): FrameStats {
  const n = times.length;
  const sorted = [...times].sort((a, b) => a - b);
  const total = times.reduce((a, b) => a + b, 0);
  const median = n % 2 ? sorted[(n - 1) / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
  const rank = (p: number) => sorted[Math.min(n, Math.max(1, Math.ceil((p / 100) * n))) - 1];
  const k = Math.max(1, Math.ceil(n * 0.01));
  const low = 1000 / (sorted.slice(n - k).reduce((a, b) => a + b, 0) / k);
  const mean = total / n;
  const sd = Math.sqrt(times.reduce((a, t) => a + (t - mean) ** 2, 0) / n);
  const th = [8.33, 16.67, 33.33, 50].map((t) => {
    const c = times.filter((x) => x > t).length;
    return { thresholdMs: t, count: c, percent: (100 * c) / n };
  });
  return {
    frameCount: n,
    durationMs: total,
    averageFps: (1000 * n) / total,
    medianFrameTimeMs: median,
    p99FrameTimeMs: rank(99),
    onePercentLowFps: low,
    stdDevFrameTimeMs: sd,
    spikeCount: times.filter((t) => t > 2 * median).length,
    thresholds: th,
    medianGpuBusyRatio: D(0.71),
    medianCpuBusyRatio: D(0.83),
    medianPcLatencyMs: NA("Not reported by the telemetry source."),
    medianClickToPhotonMs: NA("Not reported by the telemetry source."),
    dominantPresentMode: { ...D("Hardware: Independent Flip"), note: "97% of frames" },
    droppedPercent: 0.4,
  };
}

export function series(id: string, times: number[]): FrameSeries {
  const bucket = Math.max(1, Math.ceil(times.length / 1500));
  const max: number[] = [];
  const mean: number[] = [];
  for (let i = 0; i < times.length; i += bucket) {
    const part = times.slice(i, i + bucket);
    max.push(Math.max(...part));
    mean.push(part.reduce((a, b) => a + b, 0) / part.length);
  }
  const counts = new Array(101).fill(0);
  for (const t of times) counts[Math.min(100, Math.floor(t / 0.5))]++;
  let bins = counts.map((c, i) => ({ fromMs: i * 0.5, toMs: i === 100 ? Infinity : (i + 1) * 0.5, count: c }));
  while (bins.length > 1 && bins[bins.length - 1].count === 0) bins = bins.slice(0, -1);
  return { sessionId: id, sourceFrameCount: times.length, bucketSize: bucket, durationMs: times.reduce((a, b) => a + b, 0), maxFrameTimeMs: max, meanFrameTimeMs: mean, histogram: bins };
}

function comparisonOf(metrics: MetricComparison[]): ComparisonResult {
  const improved = metrics.some((m) => m.verdict === "Improved") && !metrics.some((m) => m.verdict === "Regressed");
  return {
    overall: improved ? "Improved" : "NoSignificantChange",
    metrics,
    warnings: [],
    summary: improved
      ? { ru: "Улучшение подтверждено: диапазоны результатов «до» и «после» не пересекаются хотя бы по одной метрике, ухудшений нет.",
          en: "Improvement confirmed: before/after ranges do not overlap for at least one metric and nothing regressed." }
      : { ru: "В этих тестах улучшение не удалось отделить от обычного разброса результатов.",
          en: "In these tests the improvement could not be separated from normal run-to-run variation." },
  };
}

export function demoReport(): DiagnosticReport {
  const runs: { seed: number; group: RunGroup; base: number; jitter: number; spikes: number }[] = [
    { seed: 11, group: "Baseline", base: 4.35, jitter: 1.1, spikes: 0.007 },
    { seed: 12, group: "Baseline", base: 4.3, jitter: 1.2, spikes: 0.008 },
    { seed: 13, group: "Baseline", base: 4.4, jitter: 1.0, spikes: 0.0075 },
    { seed: 21, group: "After", base: 3.95, jitter: 0.75, spikes: 0.0022 },
    { seed: 22, group: "After", base: 3.9, jitter: 0.8, spikes: 0.0025 },
    { seed: 23, group: "After", base: 4.0, jitter: 0.7, spikes: 0.002 },
  ];
  const t0 = Date.parse("2026-10-24T18:00:00Z");
  let latestTimes: number[] = [];
  const sessions: SessionRecord[] = runs.map((r, i) => {
    const times = demoFrames(r.seed, r.base, r.jitter, r.spikes);
    latestTimes = times;
    return {
      id: `demo-${i + 1}`,
      game: "Cs2",
      startedAt: new Date(t0 + i * 7 * 60000 + (i > 2 ? 40 * 60000 : 0)).toISOString(),
      requestedSeconds: 60,
      group: r.group,
      conditions: { gameVersion: "Steam build 0000000 (demo)", gpuDriverVersion: "32.0.15.0000", displayWidth: 2560, displayHeight: 1440, refreshHz: 240, scenarioNote: "Dust II, T spawn → B site" },
      telemetrySource: "DEMO DATA (synthetic)",
      frameTimeColumn: "MsBetweenPresents",
      stats: stats(times),
      isDemo: true,
      warnings: ["Synthetic demonstration data"],
    };
  });
  const med = (xs: number[]) => {
    const s = [...xs].sort((a, b) => a - b);
    return s[1];
  };
  const metric = (name: string, hib: boolean, pick: (s: SessionRecord) => number) => {
    const b = sessions.filter((s) => s.group === "Baseline").map(pick).sort((x, y) => x - y);
    const a = sessions.filter((s) => s.group === "After").map(pick).sort((x, y) => x - y);
    const bm = med(b), am = med(a);
    const improved = hib ? a[0] > b[2] : a[2] < b[0];
    const regressed = hib ? a[2] < b[0] : a[0] > b[2];
    return {
      metric: name, higherIsBetter: hib, baselineRuns: 3, afterRuns: 3, baselineMedian: bm, afterMedian: am,
      baselineMin: b[0], baselineMax: b[2], afterMin: a[0], afterMax: a[2], deltaPercent: ((am - bm) / bm) * 100,
      verdict: (improved ? "Improved" : regressed ? "Regressed" : "NoSignificantChange") as MetricComparison["verdict"],
    } satisfies MetricComparison;
  };
  const metrics = [
    metric("AverageFps", true, (s) => s.stats.averageFps),
    metric("P99FrameTimeMs", false, (s) => s.stats.p99FrameTimeMs),
    metric("OnePercentLowFps", true, (s) => s.stats.onePercentLowFps),
    metric("SpikePercent", false, (s) => (100 * s.stats.spikeCount) / s.stats.frameCount),
  ];
  const last = sessions[sessions.length - 1];
  return {
    schema: "framepilot.report/1",
    generatedAt: new Date(t0 + 3 * 3600000).toISOString(),
    appVersion: "1.0.0",
    isDemo: true,
    language: "ru",
    snapshot: {
      isDemo: true,
      collectedAt: new Date(t0).toISOString(),
      os: { productName: D("Windows 11 Pro"), displayVersion: D("25H2"), build: D(26200), ubr: D(1000) },
      cpu: { name: D("Demo 8-Core Processor"), physicalCores: D(8), logicalProcessors: D(16) },
      gpus: [{ name: D("Demo GeForce GPU"), vendor: "Nvidia", driverVersion: D("32.0.15.0000"), dedicatedMemoryBytes: D(12 * 1024 ** 3), likelyIntegrated: D(false) }],
      memory: { totalBytes: D(32 * 1024 ** 3), modules: [{ configuredSpeedMts: D(6000), ratedSpeedMts: D(6000) }, { configuredSpeedMts: D(6000), ratedSpeedMts: D(6000) }] },
      displays: [{ deviceName: "\\\\.\\DISPLAY1", isPrimary: true, width: D(2560), height: D(1440), currentRefreshHz: D(240), maxRefreshHzAtCurrentResolution: D(240) }],
      settings: {
        gameMode: D(true),
        hardwareAcceleratedGpuScheduling: D(true),
        windowedGameOptimizations: D(true),
        activePowerScheme: D({ guid: "381b4222-f694-41f0-9685-ff5bb260df2e", name: "Balanced" }),
        memoryIntegrity: D(true),
      },
    },
    sessions,
    recommendations: [
      { ruleId: "windows.windowed-optimizations", ruleVersion: 1, kind: "AutoApplicable", confidence: "Medium", game: "Cs2",
        title: { ru: "Игра выводит кадры через копирование композитором", en: "The game presents through a compositor copy" },
        symptom: { ru: "В прогонах «до» кадры шли по устаревшему пути копирования.", en: "In the baseline runs frames used the legacy copy path." },
        evidence: [], action: { ru: "Включить оптимизацию для оконных игр.", en: "Enable optimizations for windowed games." },
        limitations: { ru: "Только DX10/DX11 в оконном режиме.", en: "DX10/DX11 windowed only." } },
      { ruleId: "game.nvidia-reflex", ruleVersion: 1, kind: "UserAction", confidence: "Medium", game: "Cs2",
        title: { ru: "Проверьте NVIDIA Reflex в настройках игры", en: "Check NVIDIA Reflex in the game settings" },
        symptom: { ru: "CS2 поддерживает Reflex, видеокарта NVIDIA.", en: "CS2 supports Reflex and the GPU is NVIDIA." },
        evidence: [], action: { ru: "Reflex: «Включено». Boost — отдельный эксперимент.", en: "Reflex: Enabled. Boost is a separate experiment." },
        limitations: { ru: "Эффект зависит от упора в GPU.", en: "Effect depends on GPU-bound load." } },
      { ruleId: "session.frame-time-spikes", ruleVersion: 1, kind: "DiagnosticOnly", confidence: "Medium", game: "Cs2",
        title: { ru: "Нестабильное время кадра (скачки)", en: "Unstable frame time (spikes)" },
        symptom: { ru: "Часть кадров заметно длиннее обычного.", en: "Some frames are much longer than usual." },
        evidence: [], action: { ru: "Повторите замер после прогрева.", en: "Measure again after a warm-up." },
        limitations: { ru: "Причину скачка по одной записи определить нельзя.", en: "One recording cannot pinpoint a spike's cause." } },
      { ruleId: "session.cpu-or-engine-limited", ruleVersion: 1, kind: "DiagnosticOnly", confidence: "Medium", game: "Cs2",
        title: { ru: "Признаки ограничения со стороны CPU или движка", en: "Signs of a CPU or engine limit" },
        symptom: { ru: "Видеокарта часть кадра простаивает.", en: "The GPU idles for part of each frame." },
        evidence: [], action: { ru: "Проверьте фоновые программы.", en: "Check background programs." },
        limitations: { ru: "Один порог загрузки не ставит диагноз.", en: "One threshold is not a diagnosis." } },
    ],
    comparison: comparisonOf(metrics),
    changes: [
      { id: "c1", actionId: "windows.windowed-game-optimizations.enable", target: "HKCU\\Software\\Microsoft\\DirectX\\UserGpuPreferences → DirectXUserGlobalSettings", original: "(not set)", planned: "\"SwapEffectUpgradeEnable=1;\"", state: "Verified", updatedAt: new Date(t0 + 30 * 60000).toISOString(), error: null },
    ],
    latestFrames: series(last.id, latestTimes),
  };
}
