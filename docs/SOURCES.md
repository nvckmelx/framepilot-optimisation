# Источники

| Тема | Источник | Как используется |
|---|---|---|
| PresentMon: опции и колонки CSV | https://github.com/GameTechDev/PresentMon (README-ConsoleApplication.md) | Аргументы `PresentMonRunner`, парсер `PresentMonCsv` |
| PresentMon: релизы | https://github.com/GameTechDev/PresentMon/releases | Ссылка в настройках; пользователь скачивает сам |
| NVIDIA Reflex: On / On+Boost, приоритет над Ultra Low Latency | https://www.nvidia.com/en-us/geforce/news/reflex-low-latency-platform/ | Текст правила `game.nvidia-reflex` |
| NVIDIA: снижение системной задержки, V-Sync | https://www.nvidia.com/en-us/geforce/guides/system-latency-optimization-guide/ | Правило `session.frame-rate-capped` |
| Microsoft: оптимизация для оконных игр | https://support.microsoft.com/en-us/topic/3f006843-2c7e-4ed0-9a5e-f9389e535952 | Правило `windows.windowed-optimizations` |
| Riot: Multithreaded Rendering | https://support-valorant.riotgames.com/hc/en-us/articles/4408444846355-Multithreaded-Rendering | Правило `valorant.multithreaded-rendering` |
| Riot: поддержка VALORANT (Vanguard, VBS/HVCI) | https://support-valorant.riotgames.com/ | Правило `valorant.security-features-info` |
| Xbox Support: Game Mode | https://support.xbox.com/help/games-apps/game-setup-and-play/use-game-mode-gaming-on-pc | Правило `windows.game-mode-off` |

Технические значения реестра (используются только для чтения или в трёх действиях HKCU):
- `HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled` (DWORD) — Game Mode.
- `HKCU\Software\Microsoft\DirectX\UserGpuPreferences\DirectXUserGlobalSettings` (`SwapEffectUpgradeEnable=1;`) — оптимизация оконных игр.
- `HKCU\Software\Microsoft\DirectX\UserGpuPreferences\<путь к exe>` (`GpuPreference=2;`) — высокопроизводительный GPU для приложения.
- `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode` — только чтение (HAGS).
- `Win32_DeviceGuard.SecurityServicesRunning` / `...\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity\Enabled` — только чтение (HVCI).

Дата последней проверки ссылок: 2026-10-25. Перед релизом ссылки и значения проверяются повторно (см. RULES_POLICY, п. 8).
