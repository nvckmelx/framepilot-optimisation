using FramePilot.Core.Models;

namespace FramePilot.Rules;

public sealed class NvidiaReflexRule : RuleBase
{
    public override RuleMetadata Meta { get; } = new(
        "game.nvidia-reflex", 1, 22000, BothGames,
        "NVIDIA GeForce GPU and CS2 or VALORANT selected (both games support Reflex).",
        "GPU vendor from Win32_VideoController.",
        RiskLevel.None, false, false, RecommendationKind.UserAction, null,
        new[] { Sources.NvidiaReflex, Sources.NvidiaLatencyGuide }, Validated,
        new LocalizedText(
            "Эффект Reflex зависит от того, упирается ли игра в GPU. FramePilot не меняет настройки внутри игры: проверенного поддерживаемого способа для этого нет.",
            "Reflex's effect depends on whether the game is GPU-bound. FramePilot does not change in-game settings: there is no verified supported way to do so."));

    public override Recommendation? Evaluate(RuleContext ctx)
    {
        var nv = ctx.Snapshot.Gpus.FirstOrDefault(g => g.Vendor == GpuVendor.Nvidia);
        if (nv is null || ctx.TargetGame is null) return null;
        var gpuBound = ctx.LatestSession is { } s && s.Stats.MedianGpuBusyRatio.IsAvailable && s.Stats.MedianGpuBusyRatio.Value >= 0.9;
        var gameName = ctx.TargetGame == GameId.Cs2 ? "Counter-Strike 2" : "VALORANT";
        return Make(gpuBound ? Confidence.High : Confidence.Medium,
            new("Проверьте NVIDIA Reflex в настройках игры", "Check NVIDIA Reflex in the game settings"),
            new($"{gameName} поддерживает NVIDIA Reflex, а в системе видеокарта NVIDIA.",
                $"{gameName} supports NVIDIA Reflex and the system has an NVIDIA GPU."),
            new[]
            {
                Ev("Видеокарта", "GPU", nv.Name.Value ?? "NVIDIA", nv.Name.Source),
                Ev("Признаки упора в GPU", "GPU-bound signs", gpuBound ? "yes (latest session)" : "not confirmed", "PresentMon GPUBusy / frame time")
            },
            new("Reflex выключен или не проверялся.", "Reflex is off or was never checked."),
            new("В видеонастройках игры выберите NVIDIA Reflex: «Включено». Режим «Включено + Boost» — отдельный эксперимент: он не даёт видеокарте снижать частоты, заметно повышает энергопотребление, а выигрыш в задержке обычно небольшой и заметен в основном при упоре в CPU. Драйверный Low Latency Mode при активном Reflex не даёт дополнительного эффекта — у Reflex приоритет.",
                "In the game's video settings set NVIDIA Reflex to 'Enabled'. 'Enabled + Boost' is a separate experiment: it keeps GPU clocks high, noticeably raises power draw, and its latency benefit is usually modest and mostly visible when CPU-bound. The driver Low Latency Mode adds nothing while Reflex is active — Reflex takes priority."),
            new("Сравните 3+ прогона до и после. Если доступна метрика PC latency, сравнивайте и её; FPS сам по себе — не задержка.",
                "Compare 3+ runs before and after. If PC latency is available compare it too; FPS by itself is not latency."),
            ctx.TargetGame);
    }
}

public sealed class ValorantMultithreadedRenderingRule : RuleBase
{
    public override RuleMetadata Meta { get; } = new(
        "valorant.multithreaded-rendering", 1, 22000, new[] { GameId.Valorant },
        "VALORANT selected on a CPU with at least 6 logical processors.",
        "Logical processor count.",
        RiskLevel.None, false, false, RecommendationKind.UserAction, null,
        new[] { Sources.RiotMultithreaded }, Validated,
        new LocalizedText(
            "Riot описывает пользу настройки для достаточно производительных систем; прирост не гарантирован. Проверьте замером.",
            "Riot describes the setting as helpful on sufficiently capable systems; gains are not guaranteed. Verify by measuring."));

    public override Recommendation? Evaluate(RuleContext ctx)
    {
        var lp = ctx.Snapshot.Cpu.LogicalProcessors;
        if (!lp.IsAvailable || lp.Value < 6) return null;
        return Make(Confidence.Medium,
            new("VALORANT: проверьте «Многопоточный рендеринг»", "VALORANT: check 'Multithreaded Rendering'"),
            new("Процессор имеет достаточно потоков для многопоточного рендеринга.", "The CPU has enough threads for multithreaded rendering."),
            new[] { Ev("Логических процессоров", "Logical processors", lp.Value.ToString(), lp.Source) },
            new("Настройка выключена или не проверялась.", "The setting is off or was never checked."),
            new("Настройки → Видео → Качество графики → Многопоточный рендеринг: «Вкл.». Затем сравните прогоны.",
                "Settings → Video → Graphics Quality → Multithreaded Rendering: On. Then compare runs."),
            new("3+ сопоставимых прогона в Range до и после.", "3+ comparable runs in the Range before and after."),
            GameId.Valorant);
    }
}

public sealed class ValorantSecurityRequirementsRule : RuleBase
{
    public override RuleMetadata Meta { get; } = new(
        "valorant.security-features-info", 1, 22000, new[] { GameId.Valorant },
        "VALORANT selected.",
        "Memory Integrity (HVCI) state from the registry, read-only.",
        RiskLevel.None, false, false, RecommendationKind.DiagnosticOnly, null,
        new[] { Sources.RiotValorantSupport }, Validated,
        new LocalizedText(
            "Актуальные требования Vanguard определяет Riot. FramePilot никогда не отключает VBS/HVCI, TPM, Secure Boot, Defender или Firewall.",
            "Riot defines Vanguard's current requirements. FramePilot never disables VBS/HVCI, TPM, Secure Boot, Defender or Firewall."));

    public override Recommendation? Evaluate(RuleContext ctx)
    {
        var mi = ctx.Snapshot.Settings.MemoryIntegrity;
        return Make(Confidence.High,
            new("VALORANT: защитные функции Windows не трогаем", "VALORANT: Windows security features stay untouched"),
            new("Vanguard может требовать включённые функции безопасности Windows. Их отключение ради FPS не входит в оптимизацию.",
                "Vanguard may require Windows security features. Disabling them for FPS is not part of optimization."),
            new[] { Ev("Целостность памяти (HVCI)", "Memory Integrity (HVCI)", mi.IsAvailable ? (mi.Value ? "on" : "off") : "unavailable", mi.Source) },
            new("—", "—"),
            new("Если игра требует включить защиту, следуйте официальной инструкции Riot.", "If the game asks to enable protection, follow Riot's official instructions."),
            null, GameId.Valorant);
    }
}
