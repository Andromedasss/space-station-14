# Промт для другой модели: работа над экспериментальной медициной SS14

Скопируй всё из раздела «ТЕКСТ ПРОМТА» в свою сессию.

---

## ТЕКСТ ПРОМТА

Ты работаешь над экспериментальной системой медицины в форке Space Station 14 (SS14).
Проект уже частично сделан, его нужно продолжать, а не начинать заново.

### 1. Где лежит проект (ВАЖНО, есть два похожих пути!)

```
C:\Users\Andromeda\Desktop\нейрослоп\тест\SS14     ← ЭТО рабочая папка с кодом
C:\Users\Andromeda\Desktop\нейрослоп\тест\SS14     ← ЭТО пустая папка, в ней только Content.Client
```

Разница в одну букву (`нейрослоп` против `нейрослоп`). Если сомневаешься — проверь:
в рабочей папке должны быть `Content.Server`, `Content.Shared`, `Content.Client`,
`Resources`, `RobustToolbox`, `Content.IntegrationTests`, `Content.Tests`.

**Всегда работай относительными путями от `C:\Users\Andromeda\Desktop\нейрослоп\тест`:**
```
dotnet build SS14\Content.Server\Content.Server.csproj
```
Абсолютные пути с кириллицей в PowerShell ломаются непредсказуемо (то работают, то нет).

Kilo-проект (где `kilo.json` и `.kilo/`) — это папка `тест`, она **не** является git-репозиторием.
Git-репозиторий — вложенная папка `SS14`.

### 2. Состояние ветки

- Репозиторий: `SS14`, ветка `feature/medical-physiology`
- Рабочее дерево чистое, всё закоммичено
- **Ничего не запушено.** `origin` указывает на апстрим `space-wizards/space-station-14`, писать туда нельзя.
  Форк пользователя: `https://github.com/Andromedasss/space-station-14.git` (`gh` авторизован как `Andromedasss`, scope `repo`).
  Если нужно запушить — сначала спроси пользователя.
- `RobustToolbox` — **git submodule**. В новом worktree обязательно
  `git submodule update --init --recursive`, иначе сборка не пройдёт.

### 3. Что уже сделано

**Экспериментальная медицина для всех людей**
- `MobHuman` (и наследники: dwarf, moth, ...) получил `Physiology` + `PhysiologyState`.
  Раньше систему имел только `MobMedicalTestHuman` из `Resources/Prototypes/Medical/test_patient.yml`.
- Все части тела (`OrganHuman*`) имеют `MedicalWound`, `MedicalOrgan`, `CerebralHealth`,
  `MedicalOrganViability`.
- Раненые части получают **лениво** созданный сетевой `MedicalWoundVisualComponent`
  (только при реальной ране — чтобы не тащить лишнее сетевое состояние).

**Раны по частям тела и операции**
- 16 хирургических действий в `Content.Server/Medical/MedicalSurgerySystem.cs`:
  Disinfect, Incise, Clamp, Retract, Close, Approximate, SawBone, SpreadBone,
  ApproximateBone, RepairBone, RepairFracture, RepairVessel, RepairProsthetic,
  RehabilitateNeurology, Extract, Insert.
- Полное описание в `SS14/MEDICAL_SURGERY_GUIDE.md`.
- **Обычные** инструменты (`Scalpel`, `Hemostat`, `Retractor`, `Cautery`, `Saw`, `SawElectric`)
  получили `MedicalSurgeryTool` с соответствующим действием. Специализированные
  (`SurgicalExtractor`, `BoneSetter`, `VascularRepairTool`, `ProstheticRepairTool`,
  `NeuralRehabilitationProbe`, `SurgicalAntiseptic`) — новые обычные прототипы в
  `Resources/Prototypes/Entities/Objects/Specific/Medical/surgery.yml`.
- Обычный `OperatingTable` тоже подходит для операций.

**Визуализация ран по частям тела**
- `Content.Client/Medical/MedicalWoundVisualsSystem.cs` рисует красный оверлей
  на спрайте **конкретной** части тела (а не на всём теле).
- Арт: `Resources/Textures/Mobs/Effects/brute_damage.rsi` и `burn_damage.rsi` содержат
  per-part состояния (`LArm_Brute_50`, `Chest_Brute_10`, …). Кисти и стопы
  (`LHand/RHand/LFoot/RFoot`) сгенерированы скриптом, состояния добавлены в meta.json.
- `DamageVisualsSystem` (штатный) **пропускает** сущности с `PhysiologyComponent`,
  иначе он заливал бы кровь на всех частях сразу.

**Алексия (нарушение чтения)**
- `Content.Shared/Medical/NeurologicalTrauma.cs`:
  - `AphasicSpeech()` — каждое чётное слово пропадает (речь).
  - `AlexicText()` — буквы переворачиваются внутри слова, теги `<...>` и `[...]` сохраняются.
  - `AlexicMessage()` — пересобирает `FormattedMessage` по узлам `MarkupNode`, сохраняя разметку.
- `Content.Client/Medical/AlexiaTextSystem.cs` обходит дерево открытых окон
  (`IUserInterfaceManager.WindowRoot`) и перемешивает текст всех `RichTextLabel`/`Label`.
  **Исключения** (остаются читаемыми) — по неймспейсу: `Content.Client.Administration`,
  `Options`, `DebugMon`, `Construction`, `Sandbox`, `Info`, `CharacterInfo`, `LateJoin`,
  `Eui`, `Voting`, `RoundEnd`, `Guidebook`, `Commands`, `Tools`, `ContextMenu`,
  `UserInterface.Controls`, `UserInterface.Systems.Actions`, `.Ghost`, `.Sandbox`,
  `.Character`, `.Chat`, `.Storage`; по типу: `ChatBox`, `MedicalDebugWindow`.
- Чат и речь **не трогаются** — алексия про чтение, а не слух.

**Метаболизм и витальные показатели**
- `Content.Shared/Medical/PhysiologyModel.cs` — формулы.
  ```
  MetabolicDemand(K) = clamp(2^((K-310.15)/10), 0.25, 2)      // Q10
  TemperaturePump(K) = clamp((K-288.15)/15, 0.1, 1)
  TemperatureMobility(K) = clamp((K-293.15)/15, 0.2, 1)
  Delivery = кровь × сердце × лёгкие × дыхательный резерв
  ```
- `Vitals(blood, heart, circulation, kelvin, pain, sedation, oxygenDebt, fear)`:
  пульс = 80 + 78×кровопотеря + 52×гипоксия + 0.24×боль + 0.28×страх
            + 10×лихорадка − 2.2×холод − 2.4×седация
            − 30×(1−сердце) − 30×коллапс
  давление = 120/80 × (кровь × циркуляция) × тонус(боль, лихорадка)
  Пульс — это **собственный удар сердца**: вспомогательная циркуляция держит давление,
  но не создаёт пульс; смерть мозга пульс не отменяет; смерть сердца или почти нулевая
  кровь — пульс 0 и давление 0.
- Отходы: `RenalWaste += elapsed × (0.10 − 0.30 × почки)`,
  `HepaticWaste += elapsed × (0.08 − 0.24 × печень)`,
  `Contamination += elapsed × (0.20 при перфорации − 0.04 × печень)`.
  Баланс почек при функции 1/3, печени при 1/3. Перфорация всегда перевешивает печень.
  Сумма отходов > 30 даёт `systemicPenalty` до −80% на циркуляцию → порочный круг.

**Группы крови, переливание, лаборатория**
- `Content.Shared/Medical/BloodGroup.cs`: enum `BloodGroup` (8 значений) + правила
  совместимости `BloodGroups.CanDonate` (антигены донора ⊆ антигенов реципиента, плюс
  Rh+ только в Rh+), компоненты `BloodBagComponent` и `MedicalBloodSampleComponent`.
- Группа пациента — в `PhysiologyComponent.BloodGroup` (сетевое поле), назначается
  один раз при `ComponentInit`. **Видна только через лабораторию.**
- `Content.Server/Medical/MedicalBloodSystem.cs`: забор крови (пробирка на пациента),
  переливание (мешок на пациента), анализ (пробирка в анализатор), результат при осмотре.
- Мешок несёт **долю полного объёма крови** (0.25), а не количество реагента —
  тогда работает для любого вида независимо от его эталонного раствора.
- Несовместимая кровь → пациент теряет половину крови и начинает кровоточить.
- Прототипы: `Resources/Prototypes/Medical/blood.yml`, локализация
  `Resources/Locale/en-US/medical/medical-blood.ftl`.
- Разложено по станции: шкаф доктора и парамедика (пробирки, все 8 мешков, ресциратор,
  кейс органов, набор для ремонта), ящик `CrateMedicalSurgery` (инструменты, пробирки, анализатор).

**Пульс/давление и отказ почек/печени**
- Отказ почек и печени роняет давление через `systemicPenalty` (тест
  `PhysiologyTest` проверяет `Systolic < 60` при отказе обоих).

**Медицинское дебаг-меню (для админа)**
- Команда `/meddebug` или клавиша **O**, окно в 4 вкладки: обзор (все параметры
  сканера + метаболизм), органы, раны, неврология.
- Файлы: `Content.Shared/Medical/MedicalDebugMessages.cs`,
  `Content.Server/Medical/MedicalDebugSystem.cs`,
  `Content.Client/Medical/MedicalDebugClientSystem.cs`, `MedicalDebugWindow.cs`.

### 4. Ключевые файлы

```
SS14/Content.Shared/Medical/PhysiologyComponent.cs      состояние пациента (сетевое)
SS14/Content.Shared/Medical/PhysiologyModel.cs          все формулы
SS14/Content.Shared/Medical/BloodGroup.cs               группы крови + компоненты
SS14/Content.Shared/Medical/NeurologicalTrauma.cs       AphasicSpeech / AlexicText / AlexicMessage
SS14/Content.Shared/MedicalScanner/PhysiologyScan.cs     снимок наблюдений (то, что видно в сканере)
SS14/Content.Server/Medical/PhysiologySystem.cs         главная система: Tick, Sample, Simulate
SS14/Content.Server/Medical/MedicalWoundSystem.cs       раны по частям, кровотечение
SS14/Content.Server/Medical/MedicalSurgerySystem.cs    16 операций
SS14/Content.Server/Medical/MedicalNeurologySystem.cs  состояние мозга, Refresh()
SS14/Content.Server/Medical/MedicalBloodSystem.cs      группы крови, переливание, лаборатория
SS14/Content.Server/Medical/MedicalDebugSystem.cs       серверная часть дебаг-окна
SS14/Content.Client/Medical/MedicalWoundVisualsSystem.cs  оверлеи ран по частям
SS14/Content.Client/Medical/AlexiaTextSystem.cs        перемешивание читаемого текста
SS14/Content.Client/Medical/MedicalDebugWindow.cs      окно дебага
SS14/MEDICAL_DEV_NOTES.md                             грабли и заметки (ЧИТАЙ ПЕРВЫМ ДЕЛОМ)
SS14/MEDICAL_SURGERY_GUIDE.md                         все операции и этапы
```

### 5. Грабли (проверено на практике — не повторяй)

1. **`Dirty()` обязателен** для сетевых компонентов. `PhysiologyComponent` обновляется
   через `Dirty(uid, physiology)`; без этого состояние не уходит клиенту (симптом:
   компонент есть, значения нулевые).
2. **Сетевые сообщения** — `[Serializable, NetSerializable]` + наследование от
   `EntityEventArgs`, а **не** `NetMessage` (у `NetMessage` ручные
   `ReadFromBuffer`/`WriteToBuffer`). Поля `public`, не `private set`.
3. **Не называй поле тем же, что enum.** `enum Action` + `public Action Action` → конфликт,
   переименовал в `ActionType`.
4. **`EntityQuery<T1, T2>` как тип не существует** (только `EntityQuery<T>`), а
   `_entities.EntityQuery<T1, T2>(true)` — метод, возвращает пары. `AllEntityQuery<T1,T2>()`
   — другой API.
5. **Методы `SpriteSystem`** принимают `Entity<SpriteComponent?>`, а не `SpriteComponent`.
   Собрать: `var spriteEnt = (uid, sprite);`
6. **`LayerMapTryGet(spriteEnt, key, out var layer, false)`** — 4 аргумента.
7. **`OrganComponent.Category` и `VisualOrganComponent.Layer` — НЕ сетевые** (`[DataField]`
   без `AutoNetworkedField`), на клиенте берутся из прототипа. `OrganComponent.Body` — сетевое.
8. **`BloodstreamComponent` защищён `[Access]`** — объём крови меняется только через
   `PhysiologySystem.TryIntervene(uid, "blood", value)`, а кровотечение через
   `BloodstreamSystem.TryModifyBleedAmount`.
9. **Одна подписка на пару (компонент, событие).** `PhysiologyComponent + MapInitEvent` уже
   занят `MedicalWoundSystem` → повторная подписка роняет сервер с
   `Duplicate Subscriptions`. Своё событие бери другое (`ComponentInit`).
10. **Анализатор доступа (RA0002)** блокирует сборку: запись в защищённые поля
    (`BloodstreamComponent.BleedAmount`, `BloodReferenceSolution`) — только через
    публичные методы. Ищи ошибки по слову `error `, а не только `error CS` — RA-ошибки
    тоже не дают собраться.
11. **Клиентская команда не должна регистрироваться дважды.** Тестовый пул создаёт
    несколько клиентских пар в одном процессе → второй `RegisterCommand` бросает
    `Command already registered` и роняет SetUp **чужих** тестов. Проверяй
    `_consoleHost.AvailableCommands.ContainsKey(name)`.
12. **Клиент и сервер должны быть из одного состояния репозитория.** Иначе
    `Failed to deserialize packet` (разные хэши сериализаторов). Собрал сервер →
    пересобери и клиент.
13. **Фильтр тестов `~Medical` не покрывает всё.** Классы называются `PhysiologyTest`,
    `BedsideMonitorTest`, `EyeDamageTests` — их полное имя не содержит «Medical».
    Полный фильтр:
    `FullyQualifiedName~Medical|FullyQualifiedName~Physiology|FullyQualifiedName~BedsideMonitor|FullyQualifiedName~Eye|FullyQualifiedName~Damage|FullyQualifiedName~Body`
    — это 796 тестов вместо 182.
14. **Перед сборкой убить запущенный сервер/клиент** — иначе `MSB3027: файл залочен`.
    Все проекты пишут в **общую** папку `SS14\bin\<Project>\` (`<OutputPath>..\bin\Content.Server\</OutputPath>`
    в каждом csproj). В разных worktree она своя, в одном checkout — общая.
15. **Тесты жгут память.** `testhost` накапливает серверы: ~60 МБ на тест, пик 6 ГБ на
    796 тестов. `EntityTest` — самый тяжёлый (спавнит все прототипы), пик 10+ ГБ и
    OOM-смерть на машине в 16 ГБ. Перед прогоном убивай `testhost`/`dotnet`.
    Один упавший тест делает пул «грязным» и **каскадом** пропускает остальные —
    прогон может показать 0 упавших и 7 пропущенных или 1 упавший и 8 пропущенных
    в зависимости от порядка. Смотри всегда TRX, а не только сводку.
16. **Вертикальный `BoxContainer` даёт детям только минимальную высоту.** TabContainer
    внутри схлопывался в 0 → нужны `VerticalExpand`/`HorizontalExpand` на TabContainer
    и на ScrollContainer внутри вкладок.
17. **Исключения алексии проверяются по ОКНУ, а не по контролу.** `new Label { ... }`
    создаёт базовый `Robust...Controls.Label`, его неймспейс никогда не совпадёт с
    `Content.Client.*`. Проверяй окно и пропускай всё его поддерево.
18. **Окна в SS14 переиспользуются** (например `ContextMenuUIController.RootMenu` — один
    экземпляр). При закрытии надо **восстанавливать** оригинальный текст, иначе в
    переиспользованном окне останется перемешанный текст и следующее открытие
    перемешает его второй раз — двойной реверс даёт исходный вид.
19. **PowerShell**: `-replace` с here-string не срабатывает (разные переводы строк) —
    используй Edit/Write; `tail`/`grep`/`&&` отсутствуют; `Get-Content -Raw |
    ConvertFrom-Json` ломает форматирование meta.json, но JSON остаётся валидным;
    `return $frames` разворачивает вложенные коллекции — нужен `return ,$frames`;
    PowerShell 5.1 читает .ps1 без BOM как ANSI → кириллица в путях искажается.
20. **Анализатор `RA0009`/`RA0010`** — сервер и клиент должны быть собраны из одного
    состояния, иначе рассинхрон хэшей сериализаторов.

### 6. Команды

```powershell
# из C:\Users\Andromeda\Desktop\нейрослоп\тест
dotnet build SS14\Content.Shared\Content.Shared.csproj
dotnet build SS14\Content.Server\Content.Server.csproj
dotnet build SS14\Content.Client\Content.Client.csproj

# тесты
dotnet test SS14\Content.Tests\Content.Tests.csproj --filter "FullyQualifiedName~Medical"
dotnet test SS14\Content.IntegrationTests\Content.IntegrationTests.csproj --no-build `
  --filter "FullyQualifiedName~Medical|FullyQualifiedName~Physiology|FullyQualifiedName~BedsideMonitor|FullyQualifiedName~Eye|FullyQualifiedName~Damage|FullyQualifiedName~Body" `
  --logger "trx;LogFileName=result.trx"

# запуск игры (workdir = SS14)
dotnet run --no-build --project Content.Server\Content.Server.csproj -- --config-file server_config.local.toml
dotnet run --no-build --project Content.Client\Content.Client.csproj -- --connect --connect-address 127.0.0.1:1212 --username Developer
```

`server_config.local.toml`: `map = "Box"`, `preset = "Extended"`, `lobbyenabled = false`,
`loginlocal = true`, `port = 1212`. В dev-режиме всё равно стартует правило **Sandbox**
(build-пресеты `ConfigPresets/Build/development.toml` и `debug.toml` перебивают пресет раунда) —
это не мешает тестированию медицины.

### 7. Правила работы

- **Не коммить, не пушь без явной просьбы.** Коммить только свои файлы:
  `git add <путь>`, а **не** `git add -A` (иначе заберёшь чужие правки).
- **Не используй `git stash`** — он общий для всех worktree.
- Не трогай апстримные файлы без нужды; если меняешь — объясни зачем.
- Стиль кода: комментарии объясняют «почему», не «что». Комментарии и документация
  в этом проекте — на английском, UI-строки — через `Loc.GetString` с FTL-файлами в
  `Resources/Locale/en-US/medical/`.
- Перед сдачей: собрать все три проекта без ошибок (включая `error RA...`),
  прогнать медицинские тесты, обновить `MEDICAL_DEV_NOTES.md` новыми граблями.
- Спрашивай пользователя, если задача неоднозначна, но не задавай вопросов по мелочам.

### 8. Текущее состояние

- Всё закоммичено, рабочее дерево чистое, ничего не запушено.
- Тесты: 789 из 796 проходят, 0 упавших, 7 пропущено (пропуски — каскад от
  одного падавшего теста; после фикса самоубийства надо перепроверить).
- Известный нерешённый момент: команда `/suicide` убивала моба «плоским» уроном по
  порогу `MobThresholds`, а с новой медициной урон уходит в органы и порог не
  пересекается. Сделано: `SuicideSystem.KillExperimentalPatient()` уничтожает мозг
  (`brain.Damage = PhysiologyModel.MaximumBrainDamage`) для сущностей с
  `PhysiologyComponent`. **Проверь, что `SuicideCommandTests` теперь зелёные.**

### 9. Что можно делать дальше (идеи, не обязательства)

- Анализы крови: гемоглобин, глюкоза, лактат, электролиты (пользователь пока
  просил **не** делать гемоглобин и разделение объёма/ёмкости — только базовую
  лабораторию, группы крови и переливание, они и сделаны).
- Связка медицины с механиками станции: декомпрессия, ожоги, взрывная травма,
  электрошок (фибрилляция уже есть в модели), лучевая болезнь.
- Инфекция → сепсис → полиорганная недостаточность.
- Трансплантология: донор-труп, консервация, подбор, отторжение.
- Психика и хроника: ПТСР, депрессия, сон и усталость (усталый хирург оперирует хуже).
- Триаж при массовых поступлениях.
- Модель крови: расширить `BloodGroups` (реальные системы переливания), Rh-носители.

---

## КОНЕЦ ПРОМТА
