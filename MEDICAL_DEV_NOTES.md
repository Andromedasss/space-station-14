# Заметки по экспериментальной медицине SS14 (ветка `feature/medical-physiology`)

Справочник для продолжения работы. Не заменяет код, но фиксирует всё, что неочевидно.

## 1. Окружение

| Что | Значение |
|---|---|
| Репозиторий | `C:\Users\Andromeda\Desktop\нейрослоп\тест\SS14` (git, ветка `feature/medical-physiology`) |
| Kilo-проект | `C:\Users\Andromeda\Desktop\нейрослоп\тест` (там `kilo.json` и `.kilo/`, **не** git-репозиторий) |
| dotnet | 10.0.402, SDK по `global.json` (`rollForward: latestFeature`) |
| Рабочий каталог в shell | `C:\Users\Andromeda\Desktop\нейрослоп\тест` |

⚠️ **Ловушка:** на рабочем столе две почти одинаковые папки — `нейрослоп` (правильная, полный SS14) и `нейросlop` (с `o` вместо `оп`, там только `Content.Client`). Пути с кириллицей в PowerShell капризничают: `dotnet build "SS14\Content.Server\Content.Server.csproj"` из корня `тест` работает, абсолютный путь с кириллицей — нет. **Работать относительными путями от `тест`.**

### Команды

```powershell
# сборка (из C:\Users\Andromeda\Desktop\нейрослоп\тест)
dotnet build SS14\Content.Shared\Content.Shared.csproj
dotnet build SS14\Content.Server\Content.Server.csproj
dotnet build SS14\Content.Client\Content.Client.csproj

# тесты
dotnet test SS14\Content.Tests\Content.Tests.csproj --filter "FullyQualifiedName~Medical"
dotnet test SS14\Content.IntegrationTests\Content.IntegrationTests.csproj --filter "FullyQualifiedName~Medical"

# запуск игры (workdir = SS14)
dotnet run --no-build --project Content.Server\Content.Server.csproj -- --config-file server_config.local.toml
dotnet run --no-build --project Content.Client\Content.Client.csproj -- --connect --connect-address 127.0.0.1:1212 --username Developer
```

**Перед сборкой убить запущенный сервер/клиент** — иначе DLL залочены:
`error MSB3027: Could not copy "Content.Shared.dll" ... locked by "Content.Server (pid)"`.
Все проекты пишут в **общую** папку `SS14\bin\<Project>\` (`<OutputPath>..\bin\Content.Server\</OutputPath>` в каждом csproj), поэтому в одном checkout две сборки всегда конфликтуют, а в разных worktree — нет.

`server_config.local.toml`: `map = "Box"`, `preset = "Extended"`, `defaultpreset = "Extended"`, `lobbyenabled = false`, `loginlocal = true`, `port = 1212`. В dev-режиме всё равно стартует правило **Sandbox** (build-пресеты `ConfigPresets/Build/development.toml` и `debug.toml` перебивают пресет раунда).

## 2. Git

- 28 коммитов только локально, **ничего не запушено**, ветки нет ни на одном ремоуте.
- `origin` = `https://github.com/space-wizards/space-station-14.git` (апстрим, писать нельзя).
- Форк пользователя: `https://github.com/Andromedasss/space-station-14.git`, `gh` авторизован как `Andromedasss` (scope `repo`).
- Рекомендуемое переименование: `git remote rename origin upstream` + `git remote add origin <форк>`.
- `RobustToolbox` — **git submodule**; в новом worktree требует `git submodule update --init --recursive`, иначе сборка не идёт.
- Не использовать `git stash` (общий для всех worktree). Не коммитить чужие правки через `git add -A` — указывать конкретные пути.
- 5 готовых worktree от Cline: `C:\Users\Andromeda\.cline\worktrees\*` (ветки `cline/*`), в них подмодуль инициализирован, `bin/` свой.

## 3. Карта кода медицины

**Shared** (`Content.Shared\Medical\`)
- `PhysiologyComponent.cs` — `[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]`, поля с `[AutoNetworkedField]`. Состояние игрока: кровь, температура, неврология, органные функции.
- `MedicalWoundVisualComponent.cs` — **новое**: реплицируемые данные раны **по части тела** (`TotalInjury`, `DominantGroup`, `Fractured`, `Necrotic`).
- `NeurologicalTrauma.cs` — `AphasicSpeech()` (каждое чётное слово → `...`) и `AlexicText()` (переворот букв в слове, теги `<...>` и `[...]` сохраняются).
- `MedicalPanelEvents.cs` — сетевые события хирургической панели.

**Server** (`Content.Server\Medical\`)
- `PhysiologySystem.cs` — главная система: сканы, обмен веществ, выделение, смерть мозга.
- `MedicalWoundSystem.cs` — раны по частям (`MedicalWoundComponent.Injury`), кровотечение, `SyncVisualComponent()` дублирует рану в сетевой компонент.
- `MedicalSurgerySystem.cs` — 16 хирургических действий (см. `MEDICAL_SURGERY_GUIDE.md`).
- `MedicalNeurologySystem.cs` — состояние мозга, `Refresh(host)` переносит мозг → `PhysiologyComponent`.
- `MedicalOrganComponent.cs` — `MedicalOrganKind` (Heart/Lungs/Kidney/Liver/Intestines), функция органа.
- `MedicalWoundComponent.cs` — **server-only**, состояние раны части.
- `CerebralHealthComponent.cs` — Aphasia, MuscleWeakness, ColorVision, CognitiveSymptoms, Phobia.

**Client** (`Content.Client\Medical\`)
- `MedicalWoundVisualsSystem.cs` — **новое**: ведёт per-part оверлеи из wound-данных.
- `HealthAnalyzer\UI\HealthAnalyzerControl.xaml.cs` — UI анализатора + `AlexicText` (для чтения).
- `MedicalPanelSystem.cs` / `MedicalPanelWindow.cs` — панель операций.
- `Content.Client\Damage\DamageVisualsSystem.cs` — штатные оверлеи урона; **для сущностей с `PhysiologyComponent` выход сразу** (ими управляет `MedicalWoundVisualsSystem`).

**Прототипы/арт**
- `Resources\Prototypes\Body\species_base.yml` — `targetLayers` + `damageOverlayGroups` (Brute `#FF0000`, Burn).
- `Resources\Prototypes\Body\Species\human.yml` — `MobHuman` получил `Physiology` + `PhysiologyState`; органы — `MedicalWound`/`MedicalOrgan`/`CerebralHealth`.
- `Resources\Prototypes\Medical\test_patient.yml` — `MobMedicalTestHuman` + все `MedicalTest*` (инструменты, реагенты, оборудование).
- `Resources\Textures\Mobs\Species\Human\parts.rsi` — per-part спрайты тела (`l_hand`, `l_foot`, …), 64×64 = 2×2 кадра по 4 направлениям.
- `Resources\Textures\Mobs\Effects\brute_damage.rsi` / `burn_damage.rsi` — оверлеи ран, **уже per-part**: `Head_Brute_*`, `LArm_Brute_*`, `LLeg_Brute_*`, `RArm_*`, `RLeg_*`, `Chest_*` (6 уровней: 10/20/30/50/70/100) + добавленные `LHand/RHand/LFoot/RFoot`.

## 4. Как устроена визуализация ран (самое тонкое место)

Арт оверлея — **серая маска затемнения**, красный цвет даёт тинт слоя (`color: "#FF0000"`). Пиксели оверлея = подмножество пикселей самой части тела с пониженной яркостью (brute: 145/172/192; burn: 214,142,128 / 162,128,94 / …).

Пайплайн:
1. Сервер: удар → `MedicalWoundSystem` пишет `Injury` в `MedicalWoundComponent` части → `SyncVisualComponent()` → `MedicalWoundVisualComponent` → **`Dirty(part, visual)`**.
2. Клиент: `MedicalWoundVisualsSystem.Update()` для каждой части с wound-данными:
   - слой части берётся из `VisualOrganComponent.Layer` (fallback — маппинг `OrganComponent.Category`);
   - оверлей-слой на спрайте тела ищется по ключу `"<Layer><Group>"` (например `LArmBrute`), состояние — `"<Layer>_<Group>_<threshold>"` (например `LArm_Brute_50`);
   - порог берётся из `DamageVisualsComponent.Thresholds` по урону **этой** части.
3. `DamageVisualsSystem` для физиологических мобов выключен, иначе он заливал бы кровь на всех частях сразу (это и было исходной жалобой).

## 5. Грабли (потратили время — не повторять)

1. **`Dirty()` обязателен** для сетевых компонентов. `PhysiologyComponent` обновляется через `Dirty(uid, physiology)`; без этого состояние не уходит клиенту (симптом: компонент есть, значения нулевые).
2. **Сетевые сообщения** — `[Serializable, NetSerializable]` + наследование от `EntityEventArgs`, а **не** `NetMessage` (у `NetMessage` ручные `ReadFromBuffer`/`WriteToBuffer`). Поля — `public`, не свойства с `private set`.
3. **Не называть поле тем же, что enum.** `enum Action` + `public Action Action` → конфликт; переименовал в `ActionType`.
4. **`EntityQuery<T1, T2>` как тип** не существует (только `EntityQuery<T>`), а `_entities.EntityQuery<T1, T2>(true)` — метод, возвращает пары. `AllEntityQuery<T1,T2>()` — другой API.
5. **Методы `SpriteSystem`** принимают `Entity<SpriteComponent?>`, не `SpriteComponent`. Собрать: `var spriteEnt = (uid, sprite);`
6. **`LayerMapTryGet(spriteEnt, key, out var layer, false)`** — 4 аргумента, последний `bool`.
7. **`OrganComponent.Category` и `VisualOrganComponent.Layer` — НЕ сетевые** (`[DataField]` без `AutoNetworkedField`). На клиенте берутся из прототипа. `OrganComponent.Body` — сетевое.
8. **`OrganComponent.Category` — `ProtoId<OrganCategoryPrototype>?`**, id-строка `"Torso"`, `"ArmLeft"` и т.п.
9. **`ProtoId<T>`** неявно конвертируется в `string` — `ContainsKey("Brute")` работает, а `EnumeratePrototypes<T>()` возвращает `IEnumerable<T>` (без tuple-деконструкции).
10. **`FixedPoint2`** → `float` только через `.Float()`, `int` через `.Int()`.
11. **PowerShell**: `-replace` с here-string не сработает (разные переводы строк) — использовать `Edit`/`Write`; `tail`/`grep`/`&&` отсутствуют; `Get-Content -Raw | ConvertFrom-Json` ломает форматирование meta.json, но JSON остаётся валидным; `New-Object System.Drawing.Bitmap 64,64,fmt` и `[Bitmap]::new(64,64,fmt)` — эквивалентны; **`return $frames` разворачивает вложенные коллекции** в конвейере — нужен `return ,$frames`.
12. **PowerShell 5.1 читает .ps1 без BOM как ANSI** — кириллица в путях внутри скрипта искажается; передавать путь аргументом.
13. **Консольные команды клиента нельзя регистрировать без проверки.** Тестовый пул создаёт несколько клиентских пар в одном процессе → второй `RegisterCommand("meddebug", ...)` бросает `Command already registered` и роняет SetUp **других** тестов. Проверять `_consoleHost.AvailableCommands.ContainsKey(name)`. То же с `CommandBinds.Builder.Bind(...).Register<T>()` — там регистрация идемпотентна по типу владельца, но команда остаётся привязанной к первому экземпляру, поэтому обработчик должен резолвить живую систему через `IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<T>()`.
14. **Память интеграционных тестов.** `testhost.exe` накапливает серверы между тестами и почти не отдаёт память: пик ~8.5 ГБ на 176 медицинских тестов, ~10 ГБ на `EntityTest`. `EntityTest.SpawnAndDeleteAllEntities*` — самый тяжёлый: спавнит **все** прототипы, а вариант `OnSeparateMaps` ещё и карту на каждый; занимает 7 минут и переживает несколько пиков (память падает между тестами, но не до нуля). **Тесты не застревают** — плато в ~50 секунд это тяжёлый тест. На машине в 16 ГБ, где ~6 ГБ занято другими приложениями, хост OOM-умирает с «Test host process crashed». Не запускать `EntityTest` ради рутинной проверки; для медицины достаточно фильтра `~Medical`. Перед прогоном убивать `testhost`/`dotnet`, иначе DLL залочены.
15. **Клиентский UI: подписка на событие ДО первого вызова.** В `OpenForTarget` порядок должен быть `OnRequest += handler` → `SetTarget()` → `OpenCentered()`. Иначе `SetTarget` вызовет запрос, а подписчика ещё нет — запрос уйдёт в никуда, окно откроется пустым, и симптом будет выглядеть как «сеть не работает».
16. **Дебаг-окно не должно иметь побочных эффектов.** Рефреш отправлял `Intervene "heart" 1` — то есть открытие окна меняло функцию сердца пациента. Для рефреша нужен отдельный action без изменений.
17. **Вертикальный `BoxContainer` отдаёт детям только минимальную высоту.** Контент вкладок схлопывался в 0: корневой BoxContainer давал `TabContainer` высоту одной лишь полоски вкладок. Нужны `VerticalExpand = true` / `HorizontalExpand = true` на TabContainer и на ScrollContainer внутри вкладок. Рабочий образец — `MedicalPanelWindow`: `ContentsContainer.AddChild(new ScrollContainer { Children = { _content } })`.
18. **Разделение «читает персонаж» и «инструмент игрока».** Алексия не должна мешать админу пользоваться админ-панелью. В `AlexiaTextSystem` исключены по неймспейсу: `Content.Client.Administration`, `Options`, `DebugMon`, `Construction`, `Sandbox`, `Info`, `CharacterInfo`, `LateJoin`, `Eui`, `Voting`, `RoundEnd`, `Guidebook`, `Commands`, `Tools`, `ContextMenu`, плюс по типу окна `MedicalDebugWindow`. Всё остальное (консоли, автоматы, документы, книги, вывески) — перемешивается. HUD лежит в `StateRoot`, а не в `WindowRoot`, поэтому обход его не трогает.
19. **Проверять исключения нужно по ОКНУ, а не по контролу.** `new Label { ... }` создаёт базовый `Robust...Controls.Label`, его неймспейс никогда не совпадёт с `Content.Client.*`. Исключение проверяется на окне: `if (control is BaseWindow or Popup) { if (IsExcludedWindow(control)) yield break; }` — всё поддерево окна пропускается. Иначе админ-панель останется перемешанной.
20. **При закрытии окна текст надо восстанавливать.** Окна в SS14 **переиспользуются** (например `ContextMenuUIController.RootMenu` — один экземпляр на всё время). Если просто забыть запись в трекинге, не восстановив текст, в переиспользованном окне останется перемешанный текст, и при следующем открытии он перемешается **второй раз** — двойной реверс даёт исходный вид. Отсюда «то плохо, то хорошо». Лечится восстановлением оригинала в момент ухода контрола из дерева.
21. **Фильтр тестов `~Medical` не покрывает всё.** Классы называются `PhysiologyTest`, `BedsideMonitorTest`, `EyeDamageTests` — их полное имя не содержит «Medical», и они молча выпадали из проверок. Полный фильтр: `FullyQualifiedName~Medical|FullyQualifiedName~Physiology|FullyQualifiedName~BedsideMonitor|FullyQualifiedName~Eye|FullyQualifiedName~Damage|FullyQualifiedName~Body` — это 796 тестов вместо 182.
22. **Модель витальных показателей (`PhysiologyModel.Vitals`).** Пульс = 80 + 78×кровопотеря + 52×гипоксия + 0.24×боль + 0.28×страх + 10×лихорадка − 2.2×холод − 2.4×седация − 30×(1−сердце) − 30×коллапс. Давление = 120/80 × (кровь × циркуляция) × тонус(боль, лихорадка). Пульс — это **собственный удар сердца**: вспомогательная циркуляция держит давление, но не создаёт пульс; смерть мозга пульс не отменяет; смерть сердца или почти нулевая кровь — пульс 0 и давление 0.
23. **Группы крови и переливание.** `BloodGroup` в `PhysiologyComponent` (сетевое поле, назначается один раз при `ComponentInit`). Мешок несёт группу и **долю полного объёма крови** (0.25), а не количество реагента — тогда работает для любого вида независимо от его эталонного раствора. Совместимость: донор не должен нести антиген, которого нет у реципиента, плюс Rh+ только в Rh+. Несовместимость → пациент теряет половину крови и начинает кровоточить. Группу видно **только через лабораторию**: проба берётся из пациента, прогоняется через анализатор, результат читается осмотром пробирки. `BloodstreamComponent` защищён `[Access]`, поэтому объём меняется только через `PhysiologySystem.TryIntervene(uid, "blood", value)`, а кровотечение через `BloodstreamSystem.TryModifyBleedAmount`.
24. **Хук `BuildChecker` падал на кириллице в пути.** `git worktree add` запускает хук `post-checkout`, который зовёт `BuildChecker/git_helper.py`; там `subprocess.run(..., text=True)` декодировал вывод git в системной кодировке cp1252 и падал с `UnicodeDecodeError` — worktree создавался, но операция считалась неуспешной. Починил: `encoding="utf-8", errors="replace"` в `run_command` и `sys.stdout.reconfigure(...)`. **Фикс должен быть в коммите** — worktree берёт код из коммита, а не из рабочей папки.
25. **Git не даёт одну ветку в двух worktree.** `git worktree add <path> feature/medical-physiology` → `fatal: 'feature/medical-physiology' is already used by worktree at ...`. Для параллельной работы нужен **новый** branch от нужной базы: `git worktree add <path> -b <new-branch> feature/medical-physiology`.

## 6. Что сделано и что нет

Готово:
- Медицина для **всех людей** (`MobHuman`), не только `MobMedicalTestHuman`.
- Алексия: перемешанные буквы при **чтении**. Реализовано **одной** клиентской системой `AlexiaTextSystem`: обходит дерево открытых окон (`IUserInterfaceManager.WindowRoot`), перемешивает буквы во всех `RichTextLabel`/`Label` и восстанавливает оригинал, когда симптом снят. Покрывает консоли, автоматы, книги, вывески, бумагу, анализатор. Чат **не трогается** (слух, а не чтение) — `ChatBox` исключён. Разметка (`<color>`, `[bold]`) сохраняется: `NeurologicalTrauma.AlexicMessage()` пересобирает `FormattedMessage` по узлам `MarkupNode`.
- Раны отображаются **только на части тела**, куда били (включая кисти/стопы).
- `MEDICAL_SURGERY_GUIDE.md` — все 16 операций, этапы, инструменты, команды `/spawn`.
- Медицинское дебаг-меню: команда `/meddebug` (или клавиша **O**) — 4 вкладки: обзор (все параметры сканера + метаболизм), органы, раны, неврология.

Не сделано / требует решения:
- **Пуш в форк** не выполнен (28 коммитов локально).
- `.kilo\setup-script.ps1` и `.kilo\run-script.ps1` лежат в `тест\.kilo\` — **вне git-репозитория**, поэтому не попадут в worktree. Дублировать в `SS14\.kilo\`, чтобы закоммитить.
- `build_err.txt` / `build_log.txt` — мусор от сборки, не коммитить.
- Папка `нейросlop` (с `o`) — лишняя, удалить.

## 7. Полезные команды в игре

```
/spawn MobHuman                /spawn MobMedicalTestHuman
/spawn HandheldHealthAnalyzer  /spawn MedicalScanner
/spawn MedicalTestOperatingTable /spawn MedicalTestExtractor
/spawn MedicalTestScalpel|Hemostat|Retractor|BoneSaw|Cautery|BoneSetter
/spawn MedicalTestVascularRepairTool|MedicalTestProstheticRepairTool
/spawn MedicalTestNeuralRehabilitationTool|MedicalTestAntiseptic
/spawn MedicalTestAntibioticMedipen|SedativeMedipen|AnalgesicMedipen|InotropeMedipen|NeurorestorativeMedipen|AnxiolyticMedipen
/spawn MedicalTestIntensiveCareBed|Sterilizer|OrganCase|AmbuBag|OrganRepairKit
/spawn Paper
```
Админ-отладка (консоль): `medical <entity>` — полный скан физиологии;
`medical <entity> cognition 2` — алексия; `medical <entity> aphasia 1`; `medical <entity> weakness 1`;
`medical <entity> heart|lungs|kidneys|liver|intestines <0..1>`; `medical <entity> blood|bleeding <значение>`;
`medical <entity> temperature <K>`; `medical <entity> wound <часть> <Blunt|Slash|Piercing|Heat> <урон>`.
