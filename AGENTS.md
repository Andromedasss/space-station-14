# AGENTS.md — правила работы в этом репозитории

Форк Space Station 14 с **экспериментальной системой медицины** (физиология, органы, раны по
частям тела, операции, алексия, группы крови, лаборатория). Медицина уже влита в `master`.

## Сначала прочитай

1. `HANDOFF_PROMPT.md` — что сделано, где что лежит, команды.
2. `MEDICAL_DEV_NOTES.md` — **грабли** (26+ пунктов). Читай до правок кода.
3. `MEDICAL_SYSTEM_OVERVIEW.md` — обзор системы.
4. `MEDICAL_SURGERY_GUIDE.md` — 16 хирургических действий.

## Правила git (критично, потому что работа идёт параллельно в нескольких worktree)

- **Не используй `git stash`** — он общий для всех worktree, ломает чужие работы.
- **Не делай `git add -A`** — коммить только свои файлы: `git add <путь> ...`.
- **Не делай `git push`** без явной просьбы пользователя. `origin` — наш форк
  (`Andromedasss/space-station-14`), `upstream` — апстрим Space Wizards (только для спроса, cherry-pick).
- Не переключай ветку в основном checkout (`SS14`) — ты работаешь в своём worktree.
- Не удаляй чужие ветки и worktree.
- Один worktree — один агент. Если нужен общий контракт (интерфейс, модель, прототип) — сначала
  скажи пользователю, потом делай в своей ветке.

## Общие файлы — не трогать без нужды

`Content.Shared/Medical/PhysiologyModel.cs`, `Content.Server/Medical/PhysiologySystem.cs`,
`Content.Shared/Medical/PhysiologyComponent.cs` — **ядро**, его меняют все. Правило:

- Не меняй ядро, если твоя задача этого не требует.
- Если без изменения ядра не обойтись — сделай минимальный поступок, задокументируй его в коммите
  и в `MEDICAL_DEV_NOTES.md`.
- Не переименовывай поля ядра и не меняй их типы без явной просьбы: от этого зависят другие агенты.

## Сборка и тесты

```powershell
# из корня репозитория (папка worktree агента)
dotnet build Content.Shared\Content.Shared.csproj
dotnet build Content.Server\Content.Server.csproj
dotnet build Content.Client\Content.Client.csproj
```

Тесты **по чанкам**, не одним большим фильтром (один фильтр на 800+ тестов роняет testhost
по памяти на машине 16 ГБ):

```powershell
dotnet test Content.IntegrationTests\Content.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~Tests.Body"
dotnet test Content.IntegrationTests\Content.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~Physiology|FullyQualifiedName~BedsideMonitor|FullyQualifiedName~HealthAnalyzer"
dotnet test Content.IntegrationTests\Content.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~Organ|FullyQualifiedName~Surgery|FullyQualifiedName~Wound|FullyQualifiedName~Alexia|FullyQualifiedName~Blood|FullyQualifiedName~Transfusion"
dotnet test Content.Tests\Content.Tests.csproj --filter "FullyQualifiedName~Medical|FullyQualifiedName~Physiology"
```

`EntityTest` жрёт 8-10 ГБ — не запускай его ради рутинной проверки.

## Порт сервера

`server_config.local.toml` в каждом worktree переписан setup-скриптом на уникальный порт
(`1213 + hash(путь)`). Не правь его руками и не запускай два сервера на одном порту.

## Код

- Комментарии и документация — **на английском**, UI-строки — через `Loc.GetString` с FTL-файлами
  в `Resources/Locale/en-US/medical/`.
- Комментарий объясняет «почему», а не «что».
- Стиль — как в соседних файлах, без новых зависимостей и без переписывания чужих файлов.

## Перед сдачей

- Все три проекта собираются без ошибок.
- Прогнаны медицинские тесты (чанками) — 0 упавших.
- `MEDICAL_DEV_NOTES.md` дополнен новыми граблями.
- Коммит: одна тема — один коммит, сообщение в стиле репозитория (английский, императив).
