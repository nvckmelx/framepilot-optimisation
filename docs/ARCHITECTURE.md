# Архитектура

## Решение (`FramePilot.slnx`, .NET 10 LTS)
| Проект | TFM | Ответственность | Зависимости |
|---|---|---|---|
| FramePilot.Core | net10.0 | Модели (`Measured<T>`, снимок, сессия, рекомендации), журнал и `ChangeEngine` (state machine, компенсирующий откат, восстановление) | — |
| FramePilot.Diagnostics | net10.0 | `MetricCalculator` (формулы метрик), `SessionComparer` | Core |
| FramePilot.Rules | net10.0 | Типизированные правила, каталог, движок | Core, Diagnostics |
| FramePilot.Inventory | net10.0-windows | WMI/CIM, EnumDisplaySettings, GetSystemTimes, PowerGetActiveScheme, чтение реестра | Core, System.Management |
| FramePilot.GameAdapters | net10.0-windows | Steam (`libraryfolders.vdf`, `appmanifest_730.acf`, `cs2_video.txt`), Riot (`valorant.live.product_settings.yaml`) | Core |
| FramePilot.Telemetry | net10.0-windows | Поиск и запуск PresentMon с фиксированным набором аргументов, парсер CSV | Core |
| FramePilot.Optimization | net10.0-windows | Allowlist реестра HKCU, `RegistryValueTarget`, 3 типизированных действия | Core |
| FramePilot.Storage | net10.0 | SQLite: snapshots, sessions, changes, settings, audit_log | Core, Microsoft.Data.Sqlite |
| FramePilot.Reports | net10.0 | JSON и HTML-отчёт | Core, Diagnostics |
| FramePilot.Licensing | net10.0 | Офлайн-лицензии ECDSA | — |
| FramePilot.PrivilegedHelper | net10.0-windows | Одноразовый elevated-процесс, единственный глагол `create-restore-point` | System.Management |
| FramePilot.App | net10.0-windows, WPF | MVVM-оболочка, 8 разделов, дизайн-система бренда | все выше |
| FramePilot.LicenseTool | net10.0 | Вендорская утилита `fp-license` (не поставляется пользователю) | Licensing |
| FramePilot.Tests | net10.0-windows | xUnit | — |

В Core нет зависимостей от WPF и Windows API.

## Поток данных
```
Inventory ─┐
GameAdapters ┼─► SystemSnapshot ─┐
PresentMon CSV ─► PresentMonCsv ─► MetricCalculator ─► SessionRecord ─┼─► RuleEngine ─► Recommendation[]
                                                                       │
Recommendation(ActionId) ─► ChangeEngine.Preview ─► план ─► подтверждение ─► ChangeEngine.Apply ─► журнал (SQLite)
```

## Модель правила
`RuleMetadata`: id, version, minWindowsBuild, supportedGames, hardwareConditions, requiredEvidence, risk, requiresElevation, requiresRestart, mode (Auto/User/Diagnostic), actionId, sources, lastValidatedAt, limitations.
`IRule.Evaluate(RuleContext)` → `Recommendation?`. `readCurrentState / buildPlan` — в `IChangeAction`; `apply / verify / rollback` — в `ChangeEngine`. Каталог правил — C#-код и идентификаторы действий, без shell-команд и без загрузки из сети.

## Изменения системы
- `ISettingTarget` (Read/Write) + `ISettingTargetResolver` (ключ журнала → цель, только allowlist).
- Ключ: `HKCU\{subkey}|{valueName}|{kind}`. Allowlist: `Software\Microsoft\GameBar`, `Software\Microsoft\DirectX\UserGpuPreferences`.
- Состояния: Planned → Prepared → Applied → Verified; Failed; RollbackPending → RolledBack / RollbackFailed.
- Журнал пишется до изменения (Planned, затем Prepared с исходным значением), затем запись, затем проверка чтением.
- Частичный сбой пакета → компенсирующий откат уже применённых элементов (не атомарная транзакция ОС — так и сообщается).
- Повторное применение к уже управляемой настройке → `AlreadyManaged`; повторный откат → `AlreadyRolledBack`.
- Откат при ручном изменении после FramePilot → `Conflict`, перезапись только с `force` после подтверждения.
- При запуске `Recover()` только читает и переклассифицирует незавершённые записи; в систему не пишет.

## Привилегии
- `FramePilot.exe` — `asInvoker`.
- PresentMon (ETW) запускается через `runas` по отдельности, с фиксированными аргументами (`PresentMonRunner.BuildArguments`, валидация имени процесса и пути).
- `FramePilot.PrivilegedHelper.exe` — `requireAdministrator`, принимает ровно один аргумент из allowlist, без путей и свободного текста, без shell, завершается сразу. Постоянного IPC-канала нет — поэтому нет поверхности атаки на канал (осознанный выбор вместо named pipe).

## Хранение
`%LOCALAPPDATA%\FramePilot\framepilot.db` (WAL, synchronous=FULL), `sessions\*.csv`, `logs\app.log`.
