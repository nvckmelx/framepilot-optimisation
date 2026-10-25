using System.Globalization;
using FramePilot.Core.Changes;
using FramePilot.Core.Models;

namespace FramePilot.Rules;

internal static class Fmt
{
    public static string Gb(long bytes) => (bytes / 1024d / 1024d / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + " GB";
    public static string Pct(double v) => v.ToString("0.#", CultureInfo.InvariantCulture) + "%";
    public static string N(double v, string f = "0.#") => v.ToString(f, CultureInfo.InvariantCulture);
}

public sealed class DisplayRefreshRule : RuleBase
{
    public override RuleMetadata Meta { get; } = new(
        "display.refresh-below-max", 1, 22000, AnyGame,
        "Any monitor whose current mode is below the maximum refresh rate the driver reports for the same resolution.",
        "EnumDisplaySettings: current mode and all modes at the current resolution.",
        RiskLevel.Low, false, false, RecommendationKind.UserAction, null,
        new[] { Sources.MicrosoftDisplayRefresh }, Validated,
        new LocalizedText(
            "Список режимов сообщает драйвер. Кабель, порт или настройки монитора могут ограничивать реальную частоту.",
            "The mode list comes from the driver. Cable, port or monitor OSD settings can still limit the real refresh rate."));

    public override Recommendation? Evaluate(RuleContext ctx)
    {
        var d = ctx.Snapshot.Displays
            .Where(x => x.CurrentRefreshHz.IsAvailable && x.MaxRefreshHzAtCurrentResolution.IsAvailable)
            .OrderByDescending(x => x.IsPrimary)
            .FirstOrDefault(x => x.MaxRefreshHzAtCurrentResolution.Value - x.CurrentRefreshHz.Value >= 5);
        if (d is null) return null;
        var cur = d.CurrentRefreshHz.Value;
        var max = d.MaxRefreshHzAtCurrentResolution.Value;
        return Make(Confidence.High,
            new($"Монитор работает на {cur} Гц, доступно {max} Гц", $"Monitor runs at {cur} Hz, {max} Hz is available"),
            new($"{d.DeviceName}: активная частота ниже максимальной для текущего разрешения.",
                $"{d.DeviceName}: the active refresh rate is below the maximum for the current resolution."),
            new[]
            {
                Ev("Текущий режим", "Current mode", $"{d.Width.Value}×{d.Height.Value} @ {cur} Hz", "EnumDisplaySettings"),
                Ev("Максимум при этом разрешении", "Maximum at this resolution", $"{max} Hz", "EnumDisplaySettings")
            },
            new("Частота не выставлена после установки монитора или драйвера; ограничение кабеля/порта.",
                "Refresh rate was never raised after installing the monitor or driver; cable or port limitation."),
            new("Откройте Параметры → Система → Дисплей → Расширенные параметры дисплея и выберите максимальную частоту. FramePilot не меняет режим экрана сам.",
                "Open Settings → System → Display → Advanced display and choose the highest refresh rate. FramePilot does not change display modes itself."),
            new("После изменения нажмите «Проверить ПК» ещё раз: текущая частота должна совпасть с максимальной.",
                "After changing it, run the PC check again: the current rate should match the maximum."));
    }
}

public sealed class GameModeRule : RuleBase
{
    public override RuleMetadata Meta { get; } = new(
        "windows.game-mode-off", 1, 22000, AnyGame,
        "Game Mode explicitly disabled for the current user.",
        "HKCU\\Software\\Microsoft\\GameBar AutoGameModeEnabled = 0.",
        RiskLevel.Low, false, false, RecommendationKind.AutoApplicable, ActionIds.EnableGameMode,
        new[] { Sources.GameMode }, Validated,
        new LocalizedText(
            "Режим игры влияет на приоритеты Windows; измеримый эффект зависит от системы и может отсутствовать.",
            "Game Mode changes Windows scheduling priorities; a measurable effect depends on the system and may be absent."));

    public override Recommendation? Evaluate(RuleContext ctx)
    {
        var gm = ctx.Snapshot.Settings.GameMode;
        if (!gm.IsAvailable || gm.Value) return null;
        return Make(Confidence.High,
            new("Режим игры Windows выключен", "Windows Game Mode is turned off"),
            new("Для текущего пользователя явно отключён режим игры.", "Game Mode is explicitly disabled for the current user."),
            new[] { Ev("AutoGameModeEnabled", "AutoGameModeEnabled", "0", gm.Source) },
            new("Отключён вручную или сторонней программой «оптимизации».", "Turned off manually or by another tweaking tool."),
            new("Включить режим игры (значение для текущего пользователя, без прав администратора, откатывается).",
                "Turn Game Mode on (current-user setting, no admin rights, reversible)."),
            new("Сравните 3+ прогона до и после изменения.", "Compare 3+ runs before and after the change."));
    }
}

public sealed class WindowedOptimizationsRule : RuleBase
{
    public override RuleMetadata Meta { get; } = new(
        "windows.windowed-optimizations", 1, 22000, BothGames,
        "DX10/DX11 game presenting in windowed/borderless mode through a composed copy (legacy blt) path.",
        "PresentMon PresentMode dominated by 'Composed: Copy …' in the latest session, and the setting not explicitly enabled.",
        RiskLevel.Low, false, true, RecommendationKind.AutoApplicable, ActionIds.EnableWindowedOptimizations,
        new[] { Sources.WindowedOptimizations, Sources.PresentMon }, Validated,
        new LocalizedText(
            "Применимо только к DX10/DX11 в оконном и безрамочном режиме. В полноэкранном режиме и на Vulkan эффекта нет. Требуется перезапуск игры.",
            "Only applies to DX10/DX11 in windowed or borderless mode. No effect in exclusive fullscreen or on Vulkan. Requires a game restart."));

    public override Recommendation? Evaluate(RuleContext ctx)
    {
        var s = ctx.LatestSession;
        if (s is null || s.IsDemo != ctx.Snapshot.IsDemo) return null;
        if (!s.Stats.DominantPresentMode.IsAvailable) return null;
        var mode = s.Stats.DominantPresentMode.Value ?? "";
        if (!mode.StartsWith("Composed: Copy", StringComparison.OrdinalIgnoreCase)) return null;
        var setting = ctx.Snapshot.Settings.WindowedGameOptimizations;
        if (setting.IsAvailable && setting.Value) return null;
        return Make(Confidence.Medium,
            new("Игра выводит кадры через копирование композитором", "The game presents through a compositor copy"),
            new("В последней сессии кадры в основном шли по устаревшему пути копирования (оконный/безрамочный режим).",
                "In the latest session frames mostly went through the legacy copy path (windowed/borderless)."),
            new[]
            {
                Ev("Режим вывода", "Present mode", mode + (s.Stats.DominantPresentMode.Note is { } n ? $" ({n})" : ""), "PresentMon PresentMode"),
                Ev("Оптимизация оконных игр", "Windowed game optimizations", setting.IsAvailable ? (setting.Value ? "on" : "off") : "not set (Windows default)", setting.Source)
            },
            new("Оптимизация для оконных игр не включена; игра в оконном/безрамочном режиме.",
                "Optimizations for windowed games are not enabled; the game runs windowed/borderless."),
            new("Включить «Оптимизацию для игр в оконном режиме» (текущий пользователь, откатывается). Перезапустите игру и повторите замер.",
                "Enable 'Optimizations for windowed games' (current user, reversible). Restart the game and measure again."),
            new("В новой сессии режим вывода должен смениться на Flip (например, «Hardware: Independent Flip» или «Composed: Flip»).",
                "In a new session the present mode should change to a flip model (e.g. 'Hardware: Independent Flip' or 'Composed: Flip')."),
            s.Game);
    }
}

public sealed class HybridGpuRule : RuleBase
{
    public override RuleMetadata Meta { get; } = new(
        "gpu.app-preference-high-performance", 1, 22000, BothGames,
        "Two or more GPUs, at least one likely integrated and one likely discrete.",
        "Win32_VideoController list; detected game executable path.",
        RiskLevel.Low, false, true, RecommendationKind.AutoApplicable, ActionIds.SetHighPerformanceGpu,
        new[] { Sources.WindowsGraphicsSettings }, Validated,
        new LocalizedText(
            "Меняется предпочтение только для одного исполняемого файла игры, глобальные настройки не трогаются. На настольном ПК с монитором, подключённым к дискретной карте, эффект часто отсутствует.",
            "Only the preference of the single game executable is changed, never global settings. On desktops with the monitor on the discrete card the effect is often nil."));

    public override Recommendation? Evaluate(RuleContext ctx)
    {
        var gpus = ctx.Snapshot.Gpus.Where(g => g.Vendor != GpuVendor.Microsoft).ToList();
        if (gpus.Count < 2) return null;
        var hasIntegrated = gpus.Any(g => g.LikelyIntegrated.IsAvailable && g.LikelyIntegrated.Value);
        var hasDiscrete = gpus.Any(g => g.LikelyIntegrated.IsAvailable && !g.LikelyIntegrated.Value);
        if (!hasIntegrated || !hasDiscrete || ctx.TargetGame is null) return null;
        var game = ctx.Snapshot.Games.FirstOrDefault(g => g.Game == ctx.TargetGame && g.Installed && g.ExecutablePath is not null);
        if (game is null) return null;
        return Make(Confidence.Medium,
            new("Несколько видеокарт: закрепить игру за производительной", "Multiple GPUs: pin the game to the high-performance GPU"),
            new("В системе есть встроенная и дискретная графика; Windows может выбрать не ту видеокарту.",
                "The system has integrated and discrete graphics; Windows may pick the wrong one."),
            gpus.Select(g => Ev("Видеокарта", "GPU", (g.Name.Value ?? "?") + (g.LikelyIntegrated.Value ? " (integrated?)" : " (discrete?)"), g.Name.Source)).ToList(),
            new("Нет явного предпочтения GPU для исполняемого файла игры.", "No explicit GPU preference for the game executable."),
            new($"Задать «Высокая производительность» только для {Path.GetFileName(game.ExecutablePath)} (текущий пользователь, откатывается).",
                $"Set 'High performance' only for {Path.GetFileName(game.ExecutablePath)} (current user, reversible)."),
            new("Перезапустите игру; в сессии сравните GPU busy и FPS.", "Restart the game; compare GPU busy and FPS in a new session."),
            ctx.TargetGame,
            new Dictionary<string, string> { [ActionIds.ParamExecutablePath] = game.ExecutablePath! });
    }
}

public sealed class PowerSaverRule : RuleBase
{
    public static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");

    public override RuleMetadata Meta { get; } = new(
        "power.power-saver-plan", 1, 22000, AnyGame,
        "Active power scheme is the built-in Power saver plan.",
        "PowerGetActiveScheme.",
        RiskLevel.Low, false, false, RecommendationKind.UserAction, null,
        new[] { Sources.WindowsGraphicsSettings }, Validated,
        new LocalizedText(
            "FramePilot не создаёт и не изменяет схемы питания. Режим «Сбалансированный» — нормальный выбор для игр на настольном ПК.",
            "FramePilot does not create or modify power plans. 'Balanced' is a normal choice for desktop gaming."));

    public override Recommendation? Evaluate(RuleContext ctx)
    {
        var p = ctx.Snapshot.Settings.ActivePowerScheme;
        if (!p.IsAvailable || p.Value is null || p.Value.Guid != PowerSaver) return null;
        return Make(Confidence.High,
            new("Включена схема питания «Экономия энергии»", "The 'Power saver' plan is active"),
            new("Энергосберегающая схема ограничивает частоты процессора.", "The power saver plan limits processor clocks."),
            new[] { Ev("Активная схема", "Active scheme", $"{p.Value.Name} ({p.Value.Guid})", p.Source) },
            new("Схема выбрана вручную или сторонним приложением.", "Selected manually or by another application."),
            new("Выберите «Сбалансированный» в Панели управления → Электропитание.", "Choose 'Balanced' in Control Panel → Power Options."));
    }
}

public sealed class SingleMemoryModuleRule : RuleBase
{
    public override RuleMetadata Meta { get; } = new(
        "memory.single-module", 1, 22000, AnyGame,
        "Exactly one memory module reported on a desktop.",
        "Win32_PhysicalMemory module count.",
        RiskLevel.None, false, false, RecommendationKind.DiagnosticOnly, null,
        Array.Empty<SourceRef>(), Validated,
        new LocalizedText(
            "Один модуль обычно означает одноканальный режим, но точный режим работы контроллера Windows не сообщает.",
            "One module usually means single-channel operation, but Windows does not report the controller's actual mode."));

    public override Recommendation? Evaluate(RuleContext ctx)
    {
        var mods = ctx.Snapshot.Memory.Modules;
        if (mods.Count != 1) return null;
        return Make(Confidence.Medium,
            new("Установлен один модуль памяти", "Only one memory module is installed"),
            new("Вероятен одноканальный режим памяти, который снижает пропускную способность.", "Single-channel memory is likely, which reduces bandwidth."),
            new[] { Ev("Модулей", "Modules", "1", "Win32_PhysicalMemory") },
            new("Сборка с одним модулем.", "The build uses a single module."),
            new("Аппаратное решение: два одинаковых модуля в слотах, рекомендованных инструкцией платы.",
                "Hardware decision: two identical modules in the slots recommended by the motherboard manual."));
    }
}

public sealed class MemorySpeedRule : RuleBase
{
    public override RuleMetadata Meta { get; } = new(
        "memory.configured-below-rated", 1, 22000, AnyGame,
        "Configured memory clock clearly below the module's reported speed.",
        "Win32_PhysicalMemory Speed and ConfiguredClockSpeed for all modules.",
        RiskLevel.None, false, false, RecommendationKind.UserAction, null,
        Array.Empty<SourceRef>(), Validated,
        new LocalizedText(
            "Windows не сообщает, включён ли XMP/EXPO. Поле Speed может содержать базовую JEDEC-скорость, поэтому вывод предварительный. FramePilot никогда не меняет BIOS.",
            "Windows does not report whether XMP/EXPO is on. Speed may hold the base JEDEC value, so this is tentative. FramePilot never changes BIOS settings."));

    public override Recommendation? Evaluate(RuleContext ctx)
    {
        var mods = ctx.Snapshot.Memory.Modules.Where(m => m.RatedSpeedMts.IsAvailable && m.ConfiguredSpeedMts.IsAvailable
                                                          && m.RatedSpeedMts.Value > 0 && m.ConfiguredSpeedMts.Value > 0).ToList();
        if (mods.Count == 0) return null;
        var m0 = mods[0];
        if (m0.ConfiguredSpeedMts.Value >= m0.RatedSpeedMts.Value * 0.9) return null;
        return Make(Confidence.Low,
            new("Память может работать ниже заявленной скорости", "Memory may run below its rated speed"),
            new("Настроенная частота памяти ниже скорости, которую сообщает модуль.", "The configured memory speed is below what the module reports."),
            new[]
            {
                Ev("Скорость модуля", "Module speed", $"{m0.RatedSpeedMts.Value} MT/s", "Win32_PhysicalMemory.Speed"),
                Ev("Настроенная скорость", "Configured speed", $"{m0.ConfiguredSpeedMts.Value} MT/s", "Win32_PhysicalMemory.ConfiguredClockSpeed")
            },
            new("Профиль XMP/EXPO не включён в BIOS или выбран консервативный режим.", "XMP/EXPO is not enabled in BIOS or a conservative mode is selected."),
            new("Проверьте вручную в BIOS/UEFI профиль XMP/EXPO по инструкции к плате. Включайте только если система стабильна.",
                "Check the XMP/EXPO profile manually in BIOS/UEFI per your motherboard manual. Enable it only if the system stays stable."));
    }
}

public sealed class GameOnHddRule : RuleBase
{
    public override RuleMetadata Meta { get; } = new(
        "storage.game-on-hdd", 1, 22000, BothGames,
        "The selected game is installed on a volume that resides on a rotational disk.",
        "Game install path → volume letter → MSFT_Partition → MSFT_PhysicalDisk.MediaType.",
        RiskLevel.None, false, false, RecommendationKind.DiagnosticOnly, null,
        Array.Empty<SourceRef>(), Validated,
        new LocalizedText(
            "Диск влияет в основном на загрузку и подгрузку ресурсов, а не на средний FPS.",
            "Storage mostly affects loading and asset streaming, not average FPS."));

    public override Recommendation? Evaluate(RuleContext ctx)
    {
        var game = ctx.Snapshot.Games.FirstOrDefault(g => g.Game == ctx.TargetGame && g.Installed && g.InstallPath is not null);
        if (game is null) return null;
        var letter = Path.GetPathRoot(game.InstallPath!)?.TrimEnd('\\', '/').ToUpperInvariant();
        if (string.IsNullOrEmpty(letter)) return null;
        var disk = ctx.Snapshot.Disks.FirstOrDefault(d => d.Volumes.Any(v => string.Equals(v.Letter.TrimEnd('\\', '/'), letter, StringComparison.OrdinalIgnoreCase)));
        if (disk is null || disk.MediaType != DiskMediaType.Hdd) return null;
        return Make(Confidence.Medium,
            new("Игра установлена на жёсткий диск (HDD)", "The game is installed on a hard disk (HDD)"),
            new("Загрузка карт и подгрузка ресурсов с HDD медленнее, это может давать фризы при подгрузке.",
                "Map loading and asset streaming from an HDD is slower and can cause hitches while loading."),
            new[]
            {
                Ev("Папка игры", "Game folder", game.InstallPath!, game.DetectionSource),
                Ev("Диск", "Disk", $"{disk.FriendlyName.Value} (HDD)", "MSFT_PhysicalDisk")
            },
            new("Игра установлена на механический накопитель.", "The game is on a mechanical drive."),
            new("Рассмотрите перенос игры на SSD средствами Steam/Riot Client.", "Consider moving the game to an SSD using Steam / Riot Client."),
            null, ctx.TargetGame);
    }
}

public sealed class LowDiskSpaceRule : RuleBase
{
    public override RuleMetadata Meta { get; } = new(
        "storage.low-free-space", 1, 22000, AnyGame,
        "System volume (or the game volume) with less than 10% or 15 GB free.",
        "Volume free and total bytes.",
        RiskLevel.None, false, false, RecommendationKind.UserAction, null,
        Array.Empty<SourceRef>(), Validated,
        new LocalizedText(
            "FramePilot не удаляет файлы и не чистит кэши — решение об освобождении места за вами.",
            "FramePilot does not delete files or clear caches — you decide what to free up."));

    public override Recommendation? Evaluate(RuleContext ctx)
    {
        var sysDrive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\').ToUpperInvariant();
        var gameDrive = ctx.Snapshot.Games.FirstOrDefault(g => g.Game == ctx.TargetGame && g.InstallPath is not null)?.InstallPath is { } p
            ? Path.GetPathRoot(p)?.TrimEnd('\\').ToUpperInvariant()
            : null;
        var vols = ctx.Snapshot.Disks.SelectMany(d => d.Volumes)
            .Where(v => v.TotalBytes.IsAvailable && v.FreeBytes.IsAvailable && v.TotalBytes.Value > 0)
            .Where(v =>
            {
                var l = v.Letter.TrimEnd('\\').ToUpperInvariant();
                return l == sysDrive || l == gameDrive;
            })
            .Where(v => v.FreeBytes.Value < v.TotalBytes.Value * 0.10 || v.FreeBytes.Value < 15L * 1024 * 1024 * 1024)
            .ToList();
        if (vols.Count == 0) return null;
        return Make(Confidence.High,
            new("Мало свободного места на диске", "Low free disk space"),
            new("На системном или игровом томе мало свободного места: обновления игр и файл подкачки могут работать хуже.",
                "The system or game volume is low on space: game updates and the page file may suffer."),
            vols.Select(v => Ev("Том", "Volume", $"{v.Letter} — {Fmt.Gb(v.FreeBytes.Value)} free of {Fmt.Gb(v.TotalBytes.Value)}", "DriveInfo")).ToList(),
            new("Накопитель почти заполнен.", "The drive is nearly full."),
            new("Освободите место вручную (Параметры → Система → Память).", "Free up space manually (Settings → System → Storage)."));
    }
}

public sealed class OldGpuDriverRule : RuleBase
{
    public override RuleMetadata Meta { get; } = new(
        "gpu.driver-older-than-year", 1, 22000, AnyGame,
        "Discrete NVIDIA/AMD/Intel GPU driver dated more than 12 months ago.",
        "Win32_VideoController.DriverDate.",
        RiskLevel.None, false, false, RecommendationKind.DiagnosticOnly, null,
        Array.Empty<SourceRef>(), Validated,
        new LocalizedText(
            "Дата драйвера — грубый признак. Новый драйвер не гарантирует прирост; FramePilot не скачивает и не устанавливает драйверы.",
            "Driver date is a rough signal. A newer driver does not guarantee gains; FramePilot does not download or install drivers."));

    public override Recommendation? Evaluate(RuleContext ctx)
    {
        var old = ctx.Snapshot.Gpus.FirstOrDefault(g => g.Vendor is GpuVendor.Nvidia or GpuVendor.Amd or GpuVendor.Intel
                                                        && g.DriverDate.IsAvailable
                                                        && g.DriverDate.Value < DateTime.Now.AddDays(-365));
        if (old is null) return null;
        return Make(Confidence.Low,
            new("Драйвер видеокарты старше года", "GPU driver is older than a year"),
            new("Новые версии игр могут быть лучше протестированы на свежих драйверах.", "New game versions may be better tested on newer drivers."),
            new[]
            {
                Ev("Видеокарта", "GPU", old.Name.Value ?? "?", old.Name.Source),
                Ev("Версия драйвера", "Driver version", old.DriverVersion.Value ?? "—", old.DriverVersion.Source),
                Ev("Дата драйвера", "Driver date", old.DriverDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), old.DriverDate.Source)
            },
            new("Драйвер давно не обновлялся.", "The driver has not been updated for a long time."),
            new("При желании обновите драйвер с официального сайта производителя видеокарты и повторите замер.",
                "Optionally update the driver from the GPU vendor's official site and measure again."));
    }
}

public sealed class BackgroundCpuRule : RuleBase
{
    public override RuleMetadata Meta { get; } = new(
        "background.cpu-heavy-processes", 1, 22000, AnyGame,
        "Non-game processes using noticeable CPU during the observation window.",
        "Per-process CPU time sampled over the observation window.",
        RiskLevel.None, false, false, RecommendationKind.UserAction, null,
        Array.Empty<SourceRef>(), Validated,
        new LocalizedText(
            "Наблюдение короткое и сделано вне игры. FramePilot никогда не закрывает программы (Discord, OBS, браузер и др.).",
            "The observation is short and taken outside the game. FramePilot never closes programs (Discord, OBS, browser, etc.)."));

    private static readonly HashSet<string> Ignore = new(StringComparer.OrdinalIgnoreCase)
    {
        "Idle", "System Idle Process", "FramePilot", "FramePilot.App", "cs2", "VALORANT-Win64-Shipping", "PresentMon"
    };

    public override Recommendation? Evaluate(RuleContext ctx)
    {
        var heavy = ctx.Snapshot.Background.TopProcesses
            .Where(p => p.CpuPercent >= 8 && !Ignore.Contains(p.Name))
            .Take(5)
            .ToList();
        if (heavy.Count == 0) return null;
        return Make(Confidence.Medium,
            new("Фоновые программы заметно нагружают процессор", "Background programs use noticeable CPU"),
            new($"За {Fmt.N(ctx.Snapshot.Background.WindowSeconds, "0")} с наблюдения эти процессы потребляли заметную долю CPU.",
                $"During the {Fmt.N(ctx.Snapshot.Background.WindowSeconds, "0")} s observation these processes used a noticeable share of CPU."),
            heavy.Select(p => Ev("Процесс", "Process", $"{p.Name} — {Fmt.Pct(p.CpuPercent)} CPU", "Process CPU time")).ToList(),
            new("Обновления, синхронизация, запись видео, браузерные вкладки.", "Updates, sync, video capture, browser tabs."),
            new("Решите сами, нужно ли что-то из этого во время игры. Мы ничего не закрываем автоматически.",
                "Decide yourself whether you need these while gaming. Nothing is closed automatically."));
    }
}
