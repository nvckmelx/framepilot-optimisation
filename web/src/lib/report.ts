/* Types and parsing for the FramePilot diagnostic report (schema framepilot.report/1),
   exported from the Windows app: Settings → Reports → Export JSON. */

export interface Measured<T> {
  value: T | null;
  isAvailable: boolean;
  source: string;
  timestamp?: string;
  unavailableReason?: string | null;
  note?: string | null;
}

export interface LocalizedText {
  ru: string;
  en: string;
}

export type Lang = "ru" | "en";
export const tr = (t: LocalizedText | null | undefined, lang: Lang) => (t ? (lang === "ru" ? t.ru : t.en) : "");

export type RunGroup = "Unlabeled" | "Baseline" | "After";
export type GameId = "Cs2" | "Valorant";
export type Kind = "AutoApplicable" | "UserAction" | "DiagnosticOnly";
export type Confidence = "Low" | "Medium" | "High";
export type Verdict = "InsufficientRuns" | "IncomparableConditions" | "Improved" | "Regressed" | "Mixed" | "NoSignificantChange";

export interface ThresholdCount {
  thresholdMs: number;
  count: number;
  percent: number;
}

export interface FrameStats {
  frameCount: number;
  durationMs: number;
  averageFps: number;
  medianFrameTimeMs: number;
  p99FrameTimeMs: number;
  onePercentLowFps: number;
  stdDevFrameTimeMs: number;
  spikeCount: number;
  thresholds: ThresholdCount[];
  medianGpuBusyRatio: Measured<number>;
  medianCpuBusyRatio: Measured<number>;
  medianPcLatencyMs: Measured<number>;
  medianClickToPhotonMs: Measured<number>;
  dominantPresentMode: Measured<string>;
  droppedPercent: number;
}

export interface TestConditions {
  gameVersion: string | null;
  gpuDriverVersion: string | null;
  displayWidth: number | null;
  displayHeight: number | null;
  refreshHz: number | null;
  scenarioNote: string | null;
}

export interface SessionRecord {
  id: string;
  game: GameId;
  startedAt: string;
  requestedSeconds: number;
  group: RunGroup;
  conditions: TestConditions;
  telemetrySource: string;
  frameTimeColumn: string;
  stats: FrameStats;
  isDemo: boolean;
  warnings: string[];
}

export interface Evidence {
  label: LocalizedText;
  value: string;
  source: string;
}

export interface Recommendation {
  ruleId: string;
  ruleVersion: number;
  kind: Kind;
  confidence: Confidence;
  title: LocalizedText;
  symptom: LocalizedText;
  evidence: Evidence[];
  action: LocalizedText;
  limitations: LocalizedText;
  game: GameId | null;
}

export interface MetricComparison {
  metric: string;
  higherIsBetter: boolean;
  baselineRuns: number;
  afterRuns: number;
  baselineMedian: number;
  afterMedian: number;
  baselineMin: number;
  baselineMax: number;
  afterMin: number;
  afterMax: number;
  deltaPercent: number;
  verdict: Verdict;
}

export interface ComparisonResult {
  overall: Verdict;
  metrics: MetricComparison[];
  warnings: LocalizedText[];
  summary: LocalizedText;
}

export interface ChangeSummary {
  id: string;
  actionId: string;
  target: string;
  original: string;
  planned: string;
  state: string;
  updatedAt: string;
  error: string | null;
}

export interface GpuInfo {
  name: Measured<string>;
  vendor: string;
  driverVersion: Measured<string>;
  dedicatedMemoryBytes: Measured<number>;
  likelyIntegrated: Measured<boolean>;
}

export interface DisplayInfo {
  deviceName: string;
  isPrimary: boolean;
  width: Measured<number>;
  height: Measured<number>;
  currentRefreshHz: Measured<number>;
  maxRefreshHzAtCurrentResolution: Measured<number>;
}

export interface SystemSnapshot {
  isDemo: boolean;
  collectedAt: string;
  os: { productName: Measured<string>; displayVersion: Measured<string>; build: Measured<number>; ubr: Measured<number> };
  cpu: { name: Measured<string>; physicalCores: Measured<number>; logicalProcessors: Measured<number> };
  gpus: GpuInfo[];
  memory: { totalBytes: Measured<number>; modules: { configuredSpeedMts: Measured<number>; ratedSpeedMts: Measured<number> }[] };
  displays: DisplayInfo[];
  settings: {
    gameMode: Measured<boolean>;
    hardwareAcceleratedGpuScheduling: Measured<boolean>;
    windowedGameOptimizations: Measured<boolean>;
    activePowerScheme: Measured<{ guid: string; name: string }>;
    memoryIntegrity: Measured<boolean>;
  };
}

export interface HistogramBin {
  fromMs: number;
  toMs: number;
  count: number;
}

export interface FrameSeries {
  sessionId: string;
  sourceFrameCount: number;
  bucketSize: number;
  durationMs: number;
  maxFrameTimeMs: number[];
  meanFrameTimeMs: number[];
  histogram: HistogramBin[];
}

export interface DiagnosticReport {
  schema: string;
  generatedAt: string;
  appVersion: string;
  isDemo: boolean;
  language: string;
  snapshot: SystemSnapshot | null;
  sessions: SessionRecord[];
  recommendations: Recommendation[];
  comparison: ComparisonResult | null;
  changes: ChangeSummary[];
  latestFrames?: FrameSeries | null;
}

export class ReportError extends Error {}

const num = (v: unknown): number => {
  if (typeof v === "number") return v;
  if (v === "Infinity") return Number.POSITIVE_INFINITY;
  if (v === "-Infinity") return Number.NEGATIVE_INFINITY;
  if (v === "NaN") return Number.NaN;
  return Number(v);
};

/** Parses and lightly validates a report. Throws ReportError with a user-facing reason. */
export function parseReport(text: string): DiagnosticReport {
  let raw: unknown;
  try {
    raw = JSON.parse(text);
  } catch {
    throw new ReportError("not-json");
  }
  if (!raw || typeof raw !== "object") throw new ReportError("not-json");
  const r = raw as Partial<DiagnosticReport>;
  if (typeof r.schema !== "string" || !r.schema.startsWith("framepilot.report/")) throw new ReportError("not-framepilot");
  if (r.schema !== "framepilot.report/1") throw new ReportError("schema");
  const report: DiagnosticReport = {
    schema: r.schema,
    generatedAt: String(r.generatedAt ?? ""),
    appVersion: String(r.appVersion ?? ""),
    isDemo: Boolean(r.isDemo),
    language: String(r.language ?? "ru"),
    snapshot: (r.snapshot as SystemSnapshot) ?? null,
    sessions: Array.isArray(r.sessions) ? r.sessions : [],
    recommendations: Array.isArray(r.recommendations) ? r.recommendations : [],
    comparison: (r.comparison as ComparisonResult) ?? null,
    changes: Array.isArray(r.changes) ? r.changes : [],
    latestFrames: r.latestFrames ?? null,
  };
  if (report.latestFrames) {
    report.latestFrames.histogram = (report.latestFrames.histogram ?? []).map((b) => ({ fromMs: num(b.fromMs), toMs: num(b.toMs), count: num(b.count) }));
    report.latestFrames.maxFrameTimeMs = (report.latestFrames.maxFrameTimeMs ?? []).map(num);
    report.latestFrames.meanFrameTimeMs = (report.latestFrames.meanFrameTimeMs ?? []).map(num);
  }
  report.sessions.sort((a, b) => a.startedAt.localeCompare(b.startedAt));
  return report;
}

export const gameName = (g: GameId | null | undefined) => (g === "Valorant" ? "VALORANT" : g === "Cs2" ? "Counter-Strike 2" : "—");
