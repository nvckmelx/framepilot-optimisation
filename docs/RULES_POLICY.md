# Политика правил

1. Каждое правило — класс `IRule` с полной `RuleMetadata` (проверяется тестом `EveryRule_HasCompleteMetadata`).
2. Правило не делает выводов по одному порогу загрузки; диагноз включает наблюдения, гипотезу, уверенность, альтернативные причины, способ проверки и ограничения.
3. Отсутствие данных → правило молчит или снижает уверенность; угадывание запрещено (тест `UnavailableMemorySpeed_ProducesNoGuess`).
4. Режим AutoApplicable допустим только при наличии реализованного, протестированного, обратимого `IChangeAction` в allowlist. Если поддерживаемого способа нет — показывается инструкция (UserAction). Автоматизация кликов и неподтверждённые значения реестра запрещены.
5. Настройки внутри игр (Reflex, Multithreaded Rendering, V-Sync, лимиты FPS) в v1 не изменяются автоматически.
6. Разрешение экрана никогда не меняется автоматически.
7. Защитные функции (Defender, Firewall, Update, Secure Boot, TPM, HVCI/VBS) не изменяются никаким правилом.
8. Источники указываются в `Sources`; дата проверки — `LastValidatedAt`. Правило пересматривается при выходе мажорной версии игры, Windows или драйвера.
9. Новое правило добавляется вместе с unit-тестами срабатывания и несрабатывания.

## Каталог v1
| Id | Режим | Действие |
|---|---|---|
| display.refresh-below-max | UserAction | — |
| windows.game-mode-off | Auto | windows.game-mode.enable |
| windows.windowed-optimizations | Auto (нужна сессия с «Composed: Copy») | windows.windowed-game-optimizations.enable |
| gpu.app-preference-high-performance | Auto (iGPU + dGPU) | windows.app-gpu-preference.high-performance |
| power.power-saver-plan | UserAction | — |
| memory.configured-below-rated | UserAction, Low | — |
| memory.single-module | Diagnostic | — |
| storage.game-on-hdd | Diagnostic | — |
| storage.low-free-space | UserAction | — |
| gpu.driver-older-than-year | Diagnostic, Low | — |
| background.cpu-heavy-processes | UserAction | — (никогда не закрываем программы) |
| game.nvidia-reflex | UserAction | — |
| valorant.multithreaded-rendering | UserAction | — |
| valorant.security-features-info | Diagnostic | — |
| session.frame-rate-capped | Diagnostic (только цель «минимальная задержка») | — |
| session.gpu-limited | Diagnostic | — |
| session.cpu-or-engine-limited | Diagnostic | — |
| session.frame-time-spikes | Diagnostic | — |

HAGS, схемы питания «Ультимативная», оверлеи, фоновые записи — не включены до разработки отдельного правила и тестового протокола.
