import * as React from "react";
import type { Lang } from "./report";

const ru = {
  nav_product: "Как это работает",
  nav_report: "Открыть отчёт",
  nav_pricing: "Цены",
  nav_faq: "Вопросы",
  download: "Начать с диагностики",
  download_short: "Скачать",
  download_note: "Приложение для Windows 11 x64. Диагностика бесплатна.",
  open_viewer: "Как это работает",

  hero_label: "Оптимизация ПК под каждую игру",
  hero_title_1: "Каждая игра —",
  hero_title_2: "на максимум.",
  hero_lead: "Настройка ПК под твои игры, рабочие программы и повседневные задачи. TuneFPS измеряет поведение системы, объясняет, что мешает, и меняет только то, что умеет вернуть обратно.",
  hero_trace_caption: "Время кадра, мс. Демонстрационная запись.",
  hero_trace_p99: "P99",
  hero_trace_spike: "скачок",

  promise_title: "Нашли три проблемы, объяснили их, применили два безопасных изменения. Третье — твоё решение.",
  promise_sub: "Так выглядит результат TuneFPS: не «47 твиков», а план с доказательствами и повторный замер.",

  steps_title: "Три шага",
  steps: [
    ["Диагностика", "Определяем, что мешает комфортной работе: конфигурация, настройки Windows и короткая игровая сессия через PresentMon."],
    ["Настройка", "Подбираем изменения под твои задачи и конфигурацию. Перед применением ты видишь точное «было → станет» и подтверждаешь."],
    ["Проверка", "Оцениваем работу системы после настройки: три прогона до и три после. Если разница в пределах разброса, так и пишем. Откат — в один клик."],
  ] as [string, string][],

  never_title: "Чего TuneFPS не делает",
  never: [
    "Не отключает Defender, Firewall, обновления, Secure Boot, TPM и целостность памяти",
    "Не внедряется в игру, не читает её память и не трогает античит",
    "Не закрывает Discord, OBS и браузер",
    "Не «чистит RAM», не разгоняет и не лезет в BIOS",
    "Не обещает «+100 FPS всем» и «нулевую задержку»",
  ],

  viewer_cta_title: "Отчёт — в браузере, данные — у вас",
  viewer_cta_text:
    "Экспортируй JSON в приложении (Настройки → Отчёты) и открой его здесь. Файл читается прямо в браузере и никуда не отправляется.",

  pricing_title: "Диагностика бесплатна. Откат — тоже",
  plan_free: "Free",
  plan_free_price: "0 €",
  plan_free_items: ["Инвентаризация и диагностика", "Запись сессий (до 20)", "Все рекомендации и инструкции", "Экспорт отчётов", "Откат всех изменений"],
  plan_pro: "Pro",
  plan_pro_price: "24,99 €",
  plan_pro_per: "за 12 месяцев · 1 ПК",
  plan_pro_items: ["Всё из Free", "Применение в один клик", "Неограниченная история сравнений", "Новые проверенные правила в течение года"],
  plan_founders: "Founders",
  plan_founders_price: "14,99 €",
  plan_founders_per: "первый год · для первых тестировщиков",
  plan_founders_items: ["Всё из Pro", "Прямая связь с разработчиком", "Влияние на дорожную карту"],
  pricing_note: "Цены — стартовая гипотеза и могут измениться.",

  faq_title: "Вопросы",
  faq: [
    ["Это безопасно для VAC и Vanguard?", "TuneFPS не внедряется в игры, не читает их память и не меняет файлы — кадры измеряются через события Windows (ETW). Это снижает риски, но мы не заявляем об официальном одобрении Valve или Riot."],
    ["Сколько FPS я получу?", "До измерения — неизвестно, и мы не обещаем процент. Если улучшение не отделяется от разброса, TuneFPS так и напишет."],
    ["Нужны права администратора?", "Приложение работает без них. Права запрашиваются только для PresentMon (запись кадров) и для точки восстановления, если ты её включил."],
    ["Мои данные куда-то отправляются?", "Нет. Без регистрации, облака и телеметрии. Отчёт ты экспортируешь сам."],
  ] as [string, string][],

  footer_privacy: "Данные остаются на твоём ПК. Сайт не использует cookies и аналитику.",
  footer_name: "© 2026 TuneFPS",

  // viewer
  v_title: "Отчёт TuneFPS",
  v_drop: "Перетащи сюда JSON-отчёт",
  v_drop_or: "или",
  v_pick: "Выбрать файл",
  v_demo: "Показать демо",
  v_local: "Файл обрабатывается только в твоём браузере.",
  v_how: "Где взять: TuneFPS → Настройки → Отчёты → Экспорт JSON.",
  v_err_not_json: "Это не JSON-файл. Экспортируй отчёт в TuneFPS: Настройки → Отчёты → Экспорт JSON.",
  v_err_not_framepilot: "Это JSON, но не отчёт TuneFPS.",
  v_err_schema: "Отчёт создан другой версией TuneFPS. Обнови приложение и экспортируй отчёт снова.",
  v_close: "Открыть другой",
  v_demo_badge: "ДЕМО-ДАННЫЕ — не измерения твоего ПК",
  v_generated: "Создан",

  t_frametime: "Время кадра",
  t_frametime_sub: "последняя сессия · максимум и среднее по группам кадров",
  t_avg_fps: "средний FPS",
  t_verdict: "До и после",
  t_verdict_none: "Нет прогонов с группами «до» и «после». Отметь их в приложении — нужно минимум 3 + 3.",
  t_p99: "P99 времени кадра",
  t_low: "1% low",
  t_spikes: "Скачки",
  t_spikes_sub: "кадры длиннее 2× медианы",
  t_gpu: "GPU busy",
  t_gpu_sub: "медиана GPU busy / время кадра",
  t_hist: "Распределение",
  t_hist_sub: "все кадры сессии",
  t_ranges: "Диапазоны прогонов",
  t_ranges_sub: "каждая полоса — от худшего до лучшего прогона",
  t_runs: "Прогоны",
  t_runs_sub: "средний FPS по сессиям",
  t_recs: "Рекомендации",
  t_system: "Компьютер",
  t_changes: "Изменения TuneFPS",
  t_changes_none: "TuneFPS ничего не менял.",
  k_auto: "автоматически",
  k_user: "твоё решение",
  k_diag: "диагностика",
  unavailable: "Недоступно",
  no_frames: "В отчёте нет ряда кадров. Экспортируй отчёт из TuneFPS 1.0 после записи сессии.",
  no_sessions: "В отчёте нет игровых сессий.",
  before: "до",
  after: "после",
  ms: "мс",
  fps: "FPS",
  frames: "кадров",
  of: "из",
  confidence: { Low: "низкая", Medium: "средняя", High: "высокая" } as Record<string, string>,
  verdict: {
    Improved: "улучшение подтверждено",
    Regressed: "ухудшение",
    NoSignificantChange: "в пределах разброса",
    Mixed: "смешанный результат",
    InsufficientRuns: "мало прогонов",
    IncomparableConditions: "условия различаются",
  } as Record<string, string>,
  metric: { AverageFps: "Средний FPS", P99FrameTimeMs: "P99, мс", OnePercentLowFps: "1% low", SpikePercent: "Скачки, %", MedianPcLatencyMs: "PC latency, мс" } as Record<string, string>,
  sys_os: "Windows",
  sys_cpu: "Процессор",
  sys_gpu: "Видеокарта",
  sys_ram: "Память",
  sys_display: "Монитор",
  sys_gamemode: "Режим игры",
  sys_hags: "HAGS",
  sys_power: "Питание",
  on: "вкл.",
  off: "выкл.",
  state: { Verified: "применено и проверено", RolledBack: "откатано", Failed: "не применено", RollbackFailed: "откат не удался", Applied: "применено" } as Record<string, string>,
};

type Dict = typeof ru;

const en: Dict = {
  nav_product: "How it works",
  nav_report: "Open a report",
  nav_pricing: "Pricing",
  nav_faq: "FAQ",
  download: "Start with a diagnosis",
  download_short: "Download",
  download_note: "Windows 11 x64 app. The diagnosis is free.",
  open_viewer: "How it works",

  hero_label: "PC tuning for every game",
  hero_title_1: "Every game,",
  hero_title_2: "at its best.",
  hero_lead: "Tune your PC for your games, work apps and everyday tasks. TuneFPS measures how the system behaves, explains what gets in the way and only changes what it can put back.",
  hero_trace_caption: "Frame time, ms. Demonstration recording.",
  hero_trace_p99: "P99",
  hero_trace_spike: "spike",

  promise_title: "We found three problems, explained them and applied two safe changes. The third one is your call.",
  promise_sub: "That is what a TuneFPS result looks like: not “47 tweaks” but a plan with evidence and a re-measurement.",

  steps_title: "Three steps",
  steps: [
    ["Diagnose", "Find what gets in the way: configuration, Windows settings and a short game session recorded with PresentMon."],
    ["Tune", "Pick changes for your tasks and hardware. Before anything is applied you see the exact before → after values and confirm."],
    ["Check", "Measure the system after tuning: three runs before, three after. If the gain is within noise, we say so. Rollback is one click."],
  ],

  never_title: "What TuneFPS never does",
  never: [
    "Turn off Defender, Firewall, updates, Secure Boot, TPM or Memory Integrity",
    "Inject into games, read their memory or touch anti-cheat",
    "Close Discord, OBS or your browser",
    "“Clean RAM”, overclock or change BIOS",
    "Promise “+100 FPS for everyone” or “zero latency”",
  ],

  viewer_cta_title: "Your report in the browser, your data with you",
  viewer_cta_text:
    "Export JSON in the app (Settings → Reports) and open it here. The file is read inside your browser and is never uploaded.",

  pricing_title: "The diagnosis is free. So is rollback",
  plan_free: "Free",
  plan_free_price: "€0",
  plan_free_items: ["Inventory and diagnostics", "Session recording (up to 20)", "All recommendations and instructions", "Report export", "Rollback of every change"],
  plan_pro: "Pro",
  plan_pro_price: "€24.99",
  plan_pro_per: "per 12 months · 1 PC",
  plan_pro_items: ["Everything in Free", "One-click apply", "Unlimited comparison history", "New verified rules for a year"],
  plan_founders: "Founders",
  plan_founders_price: "€14.99",
  plan_founders_per: "first year · for early testers",
  plan_founders_items: ["Everything in Pro", "Direct line to the developer", "A say in the roadmap"],
  pricing_note: "Prices are a launch hypothesis and may change.",

  faq_title: "Questions",
  faq: [
    ["Is it safe for VAC and Vanguard?", "TuneFPS does not inject into games, read their memory or modify files — frames are measured from Windows events (ETW). That reduces risk, but we make no claim of official approval by Valve or Riot."],
    ["How many FPS will I gain?", "Unknown until measured, and we don't promise a percentage. If the gain can't be separated from noise, TuneFPS says so."],
    ["Does it need admin rights?", "The app runs without them. Rights are requested only for PresentMon (frame capture) and for a restore point if you turn it on."],
    ["Is my data sent anywhere?", "No. No sign-up, no cloud, no telemetry. You export reports yourself."],
  ],

  footer_privacy: "Your data stays on your PC. This site uses no cookies and no analytics.",
  footer_name: "© 2026 TuneFPS",

  v_title: "TuneFPS report",
  v_drop: "Drop a JSON report here",
  v_drop_or: "or",
  v_pick: "Choose file",
  v_demo: "Show demo",
  v_local: "The file is processed only in your browser.",
  v_how: "Where to get it: TuneFPS → Settings → Reports → Export JSON.",
  v_err_not_json: "This is not a JSON file. Export a report in TuneFPS: Settings → Reports → Export JSON.",
  v_err_not_framepilot: "This is JSON, but not a TuneFPS report.",
  v_err_schema: "This report comes from another TuneFPS version. Update the app and export it again.",
  v_close: "Open another",
  v_demo_badge: "DEMO DATA — not measurements of your PC",
  v_generated: "Generated",

  t_frametime: "Frame time",
  t_frametime_sub: "latest session · max and mean per frame group",
  t_avg_fps: "average FPS",
  t_verdict: "Before and after",
  t_verdict_none: "No runs labelled before and after. Label them in the app — at least 3 + 3 are needed.",
  t_p99: "P99 frame time",
  t_low: "1% low",
  t_spikes: "Spikes",
  t_spikes_sub: "frames longer than 2× median",
  t_gpu: "GPU busy",
  t_gpu_sub: "median GPU busy / frame time",
  t_hist: "Distribution",
  t_hist_sub: "all frames of the session",
  t_ranges: "Run ranges",
  t_ranges_sub: "each bar spans worst to best run",
  t_runs: "Runs",
  t_runs_sub: "average FPS per session",
  t_recs: "Recommendations",
  t_system: "Computer",
  t_changes: "TuneFPS changes",
  t_changes_none: "TuneFPS has not changed anything.",
  k_auto: "automatic",
  k_user: "your decision",
  k_diag: "diagnostic",
  unavailable: "Unavailable",
  no_frames: "This report has no frame series. Export it from TuneFPS 1.0 after recording a session.",
  no_sessions: "This report has no game sessions.",
  before: "before",
  after: "after",
  ms: "ms",
  fps: "FPS",
  frames: "frames",
  of: "of",
  confidence: { Low: "low", Medium: "medium", High: "high" },
  verdict: {
    Improved: "improvement confirmed",
    Regressed: "regression",
    NoSignificantChange: "within noise",
    Mixed: "mixed",
    InsufficientRuns: "too few runs",
    IncomparableConditions: "conditions differ",
  },
  metric: { AverageFps: "Average FPS", P99FrameTimeMs: "P99, ms", OnePercentLowFps: "1% low", SpikePercent: "Spikes, %", MedianPcLatencyMs: "PC latency, ms" },
  sys_os: "Windows",
  sys_cpu: "CPU",
  sys_gpu: "GPU",
  sys_ram: "Memory",
  sys_display: "Display",
  sys_gamemode: "Game Mode",
  sys_hags: "HAGS",
  sys_power: "Power",
  on: "on",
  off: "off",
  state: { Verified: "applied and verified", RolledBack: "rolled back", Failed: "not applied", RollbackFailed: "rollback failed", Applied: "applied" },
};

const dicts: Record<Lang, Dict> = { ru, en };

interface Ctx {
  lang: Lang;
  t: Dict;
  setLang: (l: Lang) => void;
}

const LangContext = React.createContext<Ctx>({ lang: "ru", t: ru, setLang: () => {} });

function initialLang(): Lang {
  try {
    const saved = localStorage.getItem("fp.lang");
    if (saved === "ru" || saved === "en") return saved;
  } catch {
    /* storage unavailable */
  }
  return navigator.language?.toLowerCase().startsWith("ru") ? "ru" : "en";
}

export function LangProvider({ children }: { children: React.ReactNode }) {
  const [lang, setLangState] = React.useState<Lang>(initialLang);
  const setLang = React.useCallback((l: Lang) => {
    setLangState(l);
    try {
      localStorage.setItem("fp.lang", l);
    } catch {
      /* storage unavailable */
    }
  }, []);
  React.useEffect(() => {
    document.documentElement.lang = lang;
  }, [lang]);
  const value = React.useMemo(() => ({ lang, t: dicts[lang], setLang }), [lang, setLang]);
  return <LangContext.Provider value={value}>{children}</LangContext.Provider>;
}

export const useLang = () => React.useContext(LangContext);
