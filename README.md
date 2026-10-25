# FRAMEPILOT

Помощник для владельцев игровых ПК на Windows 11: показывает, что происходит с компьютером в CS2 и VALORANT, объясняет найденное, применяет только проверенные обратимые изменения и честно сравнивает результат.

**Measure → Diagnose → Explain → Approve → Apply → Verify → Compare → Rollback**

> Рабочее название. FramePilot не обещает «+N FPS» и «нулевую задержку». Если улучшение не отделяется от разброса, приложение так и пишет.

## Что умеет v1.0
- **Мой компьютер** — CPU, GPU (драйвер, VRAM из реестра без 4-ГБ ограничения WMI), RAM (заявленная и настроенная скорость), диски (HDD/SSD/NVMe, состояние, тома), мониторы (текущая и максимальная частота), игровые настройки Windows, фоновая нагрузка. Недоступное = «Недоступно» + причина + источник.
- **Игры** — CS2 через Steam (все библиотеки), VALORANT через Riot Client; версия, папка, запущена ли.
- **Анализ сессий** — запись PresentMon, средний FPS, медиана, P99, 1% low, скачки, пороги, GPU busy, PC latency (если есть), режим вывода; график времени кадра; импорт CSV; группы «до/после»; сравнение ≥3+3 прогонов по непересекающимся диапазонам.
- **Рекомендации** — 18 правил в трёх категориях (автоматически / ваше решение / диагностика) с доказательствами, уверенностью, ограничениями и источниками.
- **План и применение** — 3 обратимых действия HKCU без прав администратора: режим игры, оптимизация оконных игр, высокопроизводительный GPU только для exe игры. Предпросмотр «было → станет», журнал до изменения, проверка чтением, компенсирующий откат, опционально точка восстановления Windows.
- **История и восстановление** — откат по одному/всего (всегда бесплатно), обнаружение ручных изменений, конфликт без молчаливой перезаписи, разбор незавершённых операций после сбоя.
- **Отчёты** — JSON и HTML (бренд, график, определения метрик, ограничения).
- RU/EN, демо-режим с постоянной маркировкой, офлайн-лицензии Pro.

## Чего FramePilot никогда не делает
Инъекции, чтение памяти игр, изменение файлов игр, обход античитов, макросы, драйвер ядра; отключение Defender/Firewall/Update/Secure Boot/TPM/HVCI; BCD/HPET-твики; отключение служб и файла подкачки; «очистка RAM»; разгон; BIOS; установка драйверов; закрытие ваших программ; сетевые «ускорители».

## Сборка
Требуется Windows 10/11 x64 и .NET 10 SDK (https://dotnet.microsoft.com/download).

```powershell
dotnet restore FramePilot.slnx
dotnet build FramePilot.slnx -c Release
dotnet test FramePilot.slnx -c Release
.\build\publish.ps1            # самодостаточная сборка в artifacts\FramePilot
.\build\publish.ps1 -Installer # + установщик (нужен Inno Setup 6)
```

Запуск из исходников: `dotnet run --project src/FramePilot.App`.

## PresentMon
Для записи сессий нужен консольный Intel PresentMon (MIT). Скачайте `PresentMon-*-x64.exe` со страницы релизов и укажите путь в «Настройках» или положите файл в `tools\` рядом с `FramePilot.exe` (скрипт `publish.ps1 -PresentMon <путь>` сделает это вместе с лицензией).

## Лицензии Pro (для продавца)
```powershell
dotnet run --project src/FramePilot.LicenseTool -- keygen vendor-private.pem   # один раз; ключ хранить в секрете
# вставить выведенный публичный ключ в src/FramePilot.App/Services/AppServices.cs → LicenseConfig.PublicKey
dotnet run --project src/FramePilot.LicenseTool -- issue vendor-private.pem buyer@mail.com XXXX-XXXX-XXXX-XXXX-XXXX Pro 12
```
Код компьютера покупатель копирует из «Настройки → Лицензия».

## Документация
`docs/PRD.md`, `docs/ARCHITECTURE.md`, `docs/THREAT_MODEL.md`, `docs/MEASUREMENT_PROTOCOL.md`, `docs/RULES_POLICY.md`, `docs/TEST_PLAN.md`, `docs/ROADMAP.md`, `docs/SOURCES.md`.

## Данные
`%LOCALAPPDATA%\FramePilot` — база SQLite, CSV сессий, журнал приложения. Ничего не отправляется в сеть.
