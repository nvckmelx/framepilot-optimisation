using FramePilot.Core.Models;

namespace FramePilot.Rules;

/// <summary>Base for rules that need a real (or, in demo mode, a demo) game session.</summary>
public abstract class SessionRuleBase : RuleBase
{
    protected static SessionRecord? Session(RuleContext ctx) =>
        ctx.LatestSession is { } s && s.IsDemo == ctx.Snapshot.IsDemo && s.Game == ctx.TargetGame ? s : null;

    protected static Evidence Fps(SessionRecord s) =>
        Ev("Средний FPS", "Average FPS", Fmt.N(s.Stats.AverageFps, "0"), "PresentMon → FramePilot metrics");

    protected static Evidence P99(SessionRecord s) =>
        Ev("P99 времени кадра", "P99 frame time", Fmt.N(s.Stats.P99FrameTimeMs, "0.00") + " ms", "PresentMon → FramePilot metrics");

    protected static int? Refresh(SessionRecord s) => s.Conditions.RefreshHz;

    protected static bool LooksCapped(SessionRecord s)
    {
        var st = s.Stats;
        var cv = st.StdDevFrameTimeMs / Math.Max(0.001, st.MedianFrameTimeMs);
        if (cv > 0.08) return false;
        if (Refresh(s) is int hz && Math.Abs(st.AverageFps - hz) / hz < 0.03) return true;
        int[] commonCaps = { 60, 120, 144, 165, 200, 240, 300, 360, 400, 500 };
        return commonCaps.Any(c => Math.Abs(st.AverageFps - c) / c < 0.01);
    }
}

public sealed class FrameRateCapRule : SessionRuleBase
{
    public override RuleMetadata Meta { get; } = new(
        "session.frame-rate-capped", 1, 22000, BothGames,
        "Any.",
        "Very low frame-time variation with average FPS at the refresh rate or a common limiter value.",
        RiskLevel.None, false, false, RecommendationKind.DiagnosticOnly, null,
        new[] { Sources.PresentMon, Sources.NvidiaLatencyGuide }, Validated,
        new LocalizedText(
            "Ограничитель или V-Sync — осознанный выбор между плавностью и минимальной задержкой. Мы не меняем его автоматически.",
            "A limiter or V-Sync is a deliberate choice between smoothness and minimal latency. We do not change it automatically."));

    public override Recommendation? Evaluate(RuleContext ctx)
    {
        var s = Session(ctx);
        // A deliberate cap serves the "stable FPS" goal; it is only a finding when the goal is minimal latency.
        if (s is null || ctx.Goal != PerformanceGoal.MinimalLatency || !LooksCapped(s)) return null;
        return Make(Confidence.Medium,
            new("Похоже, частота кадров ограничена", "Frame rate appears to be capped"),
            new("Время кадра почти не меняется, а FPS совпадает с частотой монитора или типовым лимитом.",
                "Frame time barely varies and FPS matches the refresh rate or a typical limit."),
            new[] { Fps(s), Ev("Частота монитора", "Refresh rate", Refresh(s)?.ToString() ?? "—", "EnumDisplaySettings") },
            new("V-Sync, fps_max/лимит в игре, лимит в драйвере или в другой программе.", "V-Sync, in-game fps limit, driver limit or another tool's limiter."),
            new("Если цель — минимальная задержка, попробуйте отключить V-Sync или поднять лимит и сравните прогоны. Если цель — плавность, текущая настройка может быть правильной.",
                "If your goal is minimal latency, try disabling V-Sync or raising the cap and compare runs. If your goal is smoothness, the current setting may be right."),
            new("Без лимита FPS и разброс времени кадра вырастут; это и подтвердит наличие ограничения.",
                "Without the cap FPS and frame-time variation will rise, confirming the limit."),
            s.Game);
    }
}

public sealed class GpuLimitedRule : SessionRuleBase
{
    public override RuleMetadata Meta { get; } = new(
        "session.gpu-limited", 1, 22000, BothGames,
        "Any.",
        "Median GPU busy / frame time ≥ 0.90 and the session does not look capped.",
        RiskLevel.None, false, false, RecommendationKind.DiagnosticOnly, null,
        new[] { Sources.PresentMon }, Validated,
        new LocalizedText(
            "Отношение GPU busy к времени кадра — косвенный признак. В PresentMon отмечены ограничения GPU-метрик при аппаратном планировании GPU (HAGS).",
            "GPU busy relative to frame time is indirect. PresentMon notes limitations of GPU metrics with hardware-accelerated GPU scheduling (HAGS)."));

    public override Recommendation? Evaluate(RuleContext ctx)
    {
        var s = Session(ctx);
        if (s is null || LooksCapped(s) || !s.Stats.MedianGpuBusyRatio.IsAvailable) return null;
        var ratio = s.Stats.MedianGpuBusyRatio.Value;
        if (ratio < 0.90) return null;
        return Make(Confidence.Medium,
            new("Признаки упора в видеокарту", "Signs of a GPU limit"),
            new("Видеокарта занята почти всё время каждого кадра.", "The GPU is busy for almost the whole duration of each frame."),
            new[] { Fps(s), Ev("GPU busy / время кадра (медиана)", "GPU busy / frame time (median)", Fmt.N(ratio, "0.00"), "PresentMon MsGPUBusy") },
            new("Высокое разрешение или качество графики для этой видеокарты.", "Resolution or graphics quality is high for this GPU."),
            new("Чтобы снизить нагрузку, понизьте графику или включите апскейлинг, если он есть в игре. Разрешение — ваш выбор, мы его не меняем.",
                "To reduce load, lower graphics settings or enable upscaling if the game offers it. Resolution is your choice; we never change it."),
            new("Повторите тест с более низким разрешением: если FPS заметно вырос, ограничение было в GPU.",
                "Repeat the test at a lower resolution: if FPS rises noticeably, the GPU was the limit."),
            s.Game);
    }
}

public sealed class CpuOrEngineLimitedRule : SessionRuleBase
{
    public override RuleMetadata Meta { get; } = new(
        "session.cpu-or-engine-limited", 1, 22000, BothGames,
        "Any.",
        "Median GPU busy / frame time ≤ 0.65 while the session does not look capped.",
        RiskLevel.None, false, false, RecommendationKind.DiagnosticOnly, null,
        new[] { Sources.PresentMon }, Validated,
        new LocalizedText(
            "Простой GPU может означать упор в CPU, в игровой движок, синхронизацию или ограничитель. Один порог загрузки не ставит диагноз.",
            "An idle GPU may mean a CPU, engine, synchronization or limiter bound. One utilization threshold is not a diagnosis."));

    public override Recommendation? Evaluate(RuleContext ctx)
    {
        var s = Session(ctx);
        if (s is null || LooksCapped(s) || !s.Stats.MedianGpuBusyRatio.IsAvailable) return null;
        var ratio = s.Stats.MedianGpuBusyRatio.Value;
        if (ratio > 0.65) return null;
        var ev = new List<Evidence> { Fps(s), Ev("GPU busy / время кадра (медиана)", "GPU busy / frame time (median)", Fmt.N(ratio, "0.00"), "PresentMon MsGPUBusy") };
        if (s.Stats.MedianCpuBusyRatio.IsAvailable)
            ev.Add(Ev("CPU busy / время кадра (медиана)", "CPU busy / frame time (median)", Fmt.N(s.Stats.MedianCpuBusyRatio.Value, "0.00"), "PresentMon MsCPUBusy"));
        return Make(Confidence.Medium,
            new("Признаки ограничения со стороны CPU или игрового движка", "Signs of a CPU or game-engine limit"),
            new("Видеокарта значительную часть кадра простаивает в ожидании работы.", "The GPU idles for a large part of each frame waiting for work."),
            ev,
            new("Процессор, однопоточная часть движка, фоновая нагрузка, частота памяти.", "CPU, the single-threaded part of the engine, background load, memory speed."),
            new("Проверьте фоновые программы и схему питания. Повышение графики в такой ситуации часто почти не снижает FPS.",
                "Check background programs and the power plan. Raising graphics in this situation often barely lowers FPS."),
            new("Повторите тест с более низким разрешением: если FPS почти не изменился, ограничение не в GPU.",
                "Repeat the test at a lower resolution: if FPS barely changes, the limit is not the GPU."),
            s.Game);
    }
}

public sealed class FrameTimeSpikesRule : SessionRuleBase
{
    public override RuleMetadata Meta { get; } = new(
        "session.frame-time-spikes", 1, 22000, BothGames,
        "Any.",
        "More than 1% of frames longer than 2× the median frame time, or P99 above 2.5× the median.",
        RiskLevel.None, false, false, RecommendationKind.DiagnosticOnly, null,
        new[] { Sources.PresentMon }, Validated,
        new LocalizedText(
            "Причину скачка по одной записи определить нельзя. Первые минуты после обновления игры или драйвера часто содержат компиляцию шейдеров.",
            "One recording cannot pinpoint a spike's cause. The first minutes after a game or driver update often include shader compilation."));

    public override Recommendation? Evaluate(RuleContext ctx)
    {
        var s = Session(ctx);
        if (s is null) return null;
        var st = s.Stats;
        var spikePct = 100.0 * st.SpikeCount / Math.Max(1, st.FrameCount);
        if (spikePct <= 1.0 && st.P99FrameTimeMs <= 2.5 * st.MedianFrameTimeMs) return null;
        return Make(spikePct > 3 ? Confidence.High : Confidence.Medium,
            new("Нестабильное время кадра (скачки)", "Unstable frame time (spikes)"),
            new("Часть кадров заметно длиннее обычного — это ощущается как микрофризы даже при высоком среднем FPS.",
                "Some frames are much longer than usual — felt as stutter even with high average FPS."),
            new[]
            {
                Fps(s), P99(s),
                Ev("Медиана времени кадра", "Median frame time", Fmt.N(st.MedianFrameTimeMs, "0.00") + " ms", "FramePilot metrics"),
                Ev("Кадров длиннее 2× медианы", "Frames longer than 2× median", $"{st.SpikeCount} ({Fmt.Pct(spikePct)})", "FramePilot metrics"),
                Ev("1% low", "1% low", Fmt.N(st.OnePercentLowFps, "0") + " FPS", "FramePilot metrics")
            },
            new("Компиляция шейдеров, фоновые задачи, подгрузка с диска, нехватка памяти, оверлеи.",
                "Shader compilation, background tasks, disk streaming, memory pressure, overlays."),
            new("Повторите замер после прогрева (2–3 минуты на той же карте). Если скачки остались — проверьте фоновые программы и свободную память.",
                "Repeat the measurement after a warm-up (2–3 minutes on the same map). If spikes persist, check background programs and free memory."),
            new("Сравните долю скачков в 3 прогонах; стабильная доля указывает на системную причину, а не на разовую подгрузку.",
                "Compare the spike share across 3 runs; a stable share points to a systemic cause rather than one-off loading."),
            s.Game);
    }
}

public static class RuleCatalog
{
    public static IReadOnlyList<IRule> All { get; } = new IRule[]
    {
        new DisplayRefreshRule(),
        new GameModeRule(),
        new WindowedOptimizationsRule(),
        new HybridGpuRule(),
        new PowerSaverRule(),
        new MemorySpeedRule(),
        new SingleMemoryModuleRule(),
        new GameOnHddRule(),
        new LowDiskSpaceRule(),
        new OldGpuDriverRule(),
        new BackgroundCpuRule(),
        new NvidiaReflexRule(),
        new ValorantMultithreadedRenderingRule(),
        new ValorantSecurityRequirementsRule(),
        new FrameRateCapRule(),
        new GpuLimitedRule(),
        new CpuOrEngineLimitedRule(),
        new FrameTimeSpikesRule()
    };
}
