# Атмосферная система SS14 — полный справочник по форку

Документ описывает, как устроена и работает атмосферка в этом репозитории:
что где лежит, как считается, как связана с трубами, как её видят игроки и
админы, где находятся точки расширения и какие грабли ждут того, кто её трогает.

Справочник не заменяет код: все утверждения снабжены ссылками `путь/файл:строка`,
по которым можно проверить детали.

**Статус по форку:** атмосферное ядро (`Content.Shared/Atmos`, `Content.Server/Atmos`,
`Content.Client/Atmos`, `Resources/Prototypes/Atmospherics`) в этом форке **не модифицировано** —
это апстримный код SS14 (ветка `stable`). Проверка:
`git log master..feature/medical-physiology -- Content.Server/Atmos Content.Shared/Atmos Content.Client/Atmos Resources/Prototypes/Atmospherics`
не возвращает ни одного коммита. Форк только **потребляет** API атмосферы (раздел 16).

---

## 1. Общая карта

Атмосфера в SS14 — это две независимые «физики», которые почти не пересекаются:

| Слой | Что моделирует | Где живёт | Тик |
|---|---|---|---|
| **Тайловая** | воздух в клетках (пол, стены, шлюзы) | `Content.Shared/Atmos/TileAtmosphere.cs`, `Content.Server/Atmos/EntitySystems/AtmosphereSystem.*` | стадия `TileEqualize`/`ActiveTiles` |
| **Трубная** | газ в сетях труб (HVAC, кан-истеры, скрубберы) | `Content.Server/NodeContainer/NodeGroups/PipeNet.cs`, `Content.Server/Atmos/Piping/**` | стадия `PipeNet` |

Границы между слоями **нет**: труба и тайл обмениваются газом только через
устройства (вент, скруббер, инжектор, пассивный вентиль) и порты
(`PortPipeNode` ↔ `PortablePipeNode`).

```
Content.Shared/Atmos/           модель: GasMixture, TileAtmosphere, константы, компоненты, сериализация
Content.Server/Atmos/           сервер: тик, солверы давления, реакции, огонь, герметичность
    EntitySystems/               AtmosphereSystem.* — основная система (частичная класс)
    Piping/{Unary,Binary,Trinary}/ устройства
    Portable/                    переносные скруббер и обогреватель
    Monitor/                     сенсоры, воздушные тревоги, приёмники тревог
    Reactions/                   реализации химических реакций
    Commands/                    админские команды
Content.Client/Atmos/           оверлеи (газы, огонь, тепло), UI тревог и приборов
Content.Server/NodeContainer/   граф труб: NodeGroupSystem, PipeNet, PipeNode
Resources/Prototypes/Atmospherics/  gases.yml, reactions.yml, Thresholds/airalarms.yml
Resources/Prototypes/Entities/Structures/Piping/Atmospherics/  прототипы устройств и труб
```

---

## 2. Модель данных

### 2.1 `GasMixture` — смесь газа

`Content.Shared/Atmos/GasMixture.cs`

| Поле | Строка | Назначение |
|---|---|---|
| `Moles` | 25 | `float[12]` — моли по газам (9 газов + паддинг под SIMD). Доступ закрыт `[Access]`, наружу только `GetMoles/SetMoles/AdjustMoles` |
| `SpaceGas` | 20 | иммутабельная смесь космоса: 2500 л, 2,7 K, 0 молей |
| `Immutable` | 34 | флаг: смесь нельзя менять (космос, атмосфера карты) |
| `TotalMoles` | 43 | сумма молей (SIMD) |
| `Pressure` | 50 | `TotalMoles · R · T / V` — идеальный газ |
| `Temperature` | 60 | Кельвины; сеттер **клампит** в `[TCMB(2,7 K), Tmax(262144 K)]` и не меняется у иммутабельных смесей |
| `Volume` | 73 | объём в литрах |

Мутации: `SetMoles` (124, бросает на отрицательное/не-конечное), `AdjustMoles`
(140, клампит в 0), `Remove`/`RemoveRatio` (161/167, **обнуляют моли ниже
`GasMinMoles = 5e-8`** — следы газа исчезают), `RemoveVolume` (200),
`CopyFrom` (206), `Clear` (217), `Multiply` (224), `Clone` (308).

### 2.2 `TileAtmosphere` — состояние одной клетки

`Content.Shared/Atmos/TileAtmosphere.cs`

| Поле | Строка | Назначение |
|---|---|---|
| `Temperature` | 35 | температура **плитки** (для сверхпроводимости), не газа |
| `HeatCapacity`, `ThermalConductivity` | 58, 65 | свойства плитки из `ContentTileDefinition` |
| `Air` | 146 | `GasMixture` клетки; `null` = клетка полностью заблокирована |
| `AirArchived` | 154 | снимок смеси текущего цикла — база для LINDA |
| `AdjacentTiles[4]`, `AdjacentBits` | 86, 93 | кэш графа соседей (N/S/E/W) |
| `Space`, `MapAtmosphere`, `NoGridTile` | 77, 185, 193 | «космос» / «смесь карты» / «виртуальный тайл вне грида» |
| `Excited`, `ExcitedGroup` | 71, 138 | активность тайла и его группа |
| `Hotspot` | 106 | состояние пожара в клетке |
| `AirtightData` | 205 | кэш герметичности (направления, `NoAirWhenBlocked`, `FixVacuum`) |

### 2.3 Компоненты хранения

**`GridAtmosphereComponent`** (`Content.Shared/Atmos/Components/GridAtmosphereComponent.cs`) — состояние атмосферы одного грида:

- `Tiles: Dictionary<Vector2i, TileAtmosphere>` (52) — все клетки;
- рабочие наборы: `MapTiles` (55), `ActiveTiles` (58), `ExcitedGroups` (64), `HotspotTiles` (70), `SuperconductivityTiles` (76), `HighPressureDelta` (82);
- `DeltaPressureEntities` (97) + курсор (112) + очередь результатов урона (118) — обработка урона пачками в параллельном job'е;
- `PipeNets: HashSet<IPipeNet>` (121) — сети труб этого грида;
- `AtmosDevices` (124) — атмос-устройства грида;
- `InvalidatedCoords` (139) — клетки, ждущие пересчёта;
- `State` (154) — текущая стадия state-machine;
- `UpdateCounter` (48) — счётчик полных циклов, «не обнулять» (иначе ломается раундстарт-атмосфера).

**`MapAtmosphereComponent`** (`Content.Shared/Atmos/Components/MapAtmosphereComponent.cs`) — атмосфера **карты**:
`Mixture` (по умолчанию `GasMixture.SpaceGas`, т.е. вакуум) и `Space = true`.
На сервере смесь помечается иммутабельной (`AtmosphereSystem.Map.cs:22`).

---

## 3. Газы

### 3.1 Перечисление

`Content.Shared/Atmos/Atmospherics.cs:380` — `enum Gas : sbyte`:
`Oxygen, Nitrogen, CarbonDioxide, Plasma, Tritium, WaterVapor, Ammonia, NitrousOxide, Frezon`.

Связанные константы там же: `TotalNumberOfGases = 9` (226), `AdjustedNumberOfGases = 12` (232, кратно 4 ради SIMD),
`MolesCellStandard ≈ 104,27 моль/тайл` (77), `MolesCellFreezer` (83), `MolesCellGasMiner` (88).

### 3.2 Прототипы

`Resources/Prototypes/Atmospherics/gases.yml`

| # | Газ | теплоёмкость | масса | роль | видимость | спрайт оверлея |
|---|---|---|---|---|---|---|
| 0 | Oxygen | 29 | 32 | **окислитель** | 0.25 | нет |
| 1 | Nitrogen | 29 | 28 | — | 0.25 | нет |
| 2 | CarbonDioxide | 37 | 44 | — | 0.25 | нет |
| 3 | Plasma | 200 | 120 | **топливо** | 0.25 | plasma |
| 4 | Tritium | 10 | 3 | **топливо** | 0.25 | tritium |
| 5 | WaterVapor | 40 | 18 | — | 0.25 | water_vapor |
| 6 | Ammonia | 20 | 44 | — | 2.0 | miasma |
| 7 | NitrousOxide | 40 | 44 | — | 0.25 | нет |
| 8 | Frezon | 600 | 50 | — | 0.6 | frezon |

Класс прототипа — `Content.Shared/Atmos/Prototypes/GasPrototype.cs`:
`MolarHeatCapacity` (46), `HeatCapacityRatio` (53, **не используется**), `MolarMass` (60),
`GasMolesVisible` (67), `GasMolesVisibleMax` (73), `GasVisibilityFactor` (79),
`GasOverlaySprite` (86, `null` → газ невидим), `Reagent` (92), `Color` (98),
`PricePerMole` (105), `IsFuel` (114), `IsOxidizer` (121).

**Жёсткие правила:**

1. ID прототипа обязан **дословно совпадать** с именем enum `Gas`, иначе газ не загрузится
   (`Content.Shared/Atmos/EntitySystems/SharedAtmosphereSystem.Gases.cs:72-76`).
2. `molarHeatCapacity` из YAML **делится на `atmos.heat_scale`** (дефолт 8) при загрузке
   (`SharedAtmosphereSystem.Gases.cs:91`). Считать «как в YAML» нельзя.
3. `IsFuel`/`IsOxidizer` компилируются в SIMD-маски `GasFuelMask` / `GasOxidizerMask` /
   `GasOxidiserFuelMask` (`SharedAtmosphereSystem.Gases.cs:94-101`) — для мгновенной проверки горючести.
4. Чтобы добавить газ: enum `Gas` + `TotalNumberOfGases` + `AdjustedNumberOfGases` +
   `GasReagents` (длина **9**, не 12!) + прототип с правильным ID + реинициализация газов.
   Авторы предупреждают: «The total number of gases is hardcoded in a bunch of places»
   (`GasPrototype.cs:12-14`).

### 3.3 Газ ↔ реагент

`GasPrototype.Reagent` → `SharedAtmosphereSystem.GasReagents[gasIdx]` (`SharedAtmosphereSystem.Gases.cs:63,78`).
Конвертация молей газа в реагенты — `Content.Shared/Body/Systems/LungSystem.cs:68-84`,
множитель `Atmospherics.BreathMolesToReagentMultiplier = 1144` (`Atmospherics.cs:359`).

---

## 4. Тик: state-machine из 10 стадий

`AtmosphereProcessingState` — `Content.Shared/Atmos/EntitySystems/SharedAtmosphereSystem.cs:62`.
Оркестрация — `Content.Server/Atmos/EntitySystems/AtmosphereSystem.Processing.cs:702`.

| # | Стадия | Файл:строка | Что делает |
|---|---|---|---|
| 1 | `Revalidate` | `Processing.cs:56` | пересчёт инвалидированных клеток, `UpdateTileData`/`UpdateTileAir`, обрезка отключённых тайлов |
| 2 | `TileEqualize` | `Processing.cs:299` | выравнивание давления (MONSTERMOS) |
| 3 | `ActiveTiles` | `Processing.cs:324` | обработка активных клеток: обмен газа (LINDA) + **химия** |
| 4 | `ExcitedGroups` | `Processing.cs:350` | сборка/разбор групп активных клеток |
| 5 | `HighPressureDelta` | `Processing.cs:389` | разница давлений: выброс мобов/предметтов + звуки |
| 6 | `DeltaPressure` | `Processing.cs:476` | урон от перепада давлений (параллельно) |
| 7 | `Hotspots` | `Processing.cs:420` | пожары |
| 8 | `Superconductivity` | `Processing.cs:446` | тепло в стены (по умолчанию выкл.) |
| 9 | `PipeNet` | `Processing.cs:537` | тик сетей труб |
| 10 | `AtmosDevices` | `Processing.cs:587` | тик атмос-устройств |

Ключевые механики:

- **Стадия = один тик.** Таймер `atmosphere.Timer` (`GridAtmosphereComponent.cs:39`) копится,
  и каждая стадия требует `AtmosTime = 1 / atmos.tickrate` (дефолт 15 Гц → 66 мс)
  (`Content.Server/Atmos/EntitySystems/AtmosphereSystem.CVars.cs:37`).
  Полный цикл ≈ **0,6–0,67 с**.
- **Бюджет времени.** `atmos.max_process_time` = 3 мс на кадр. Если стадия не уложилась,
  она возвращает `false`, ставится `ProcessingPaused = true`, и работа продолжается
  следующим тиком (`Processing.cs:82-84`, 306-319, 315-318). На тяжёлой станции атмос
  «растягивается» на кадры, и устройства срабатывают «через раз».
- **`UpdatesAfter.Add(typeof(NodeGroupSystem))`** (`Content.Server/Atmos/EntitySystems/AtmosphereSystem.cs:60`) —
  атмосфера всегда считается после перестройки графа труб.
- **Реальный dt для устройств** — не `frameTime`, а `RealAtmosTime()` =
  число_стадий × `AtmosTime` (`Processing.cs:573-585`). Устройства вне грида получают
  просто `AtmosTime` (`Content.Server/Atmos/Piping/EntitySystems/AtmosDeviceSystem.cs:132-138`).
- Порядок вызовов в `Update` (`AtmosphereSystem.cs:98`): `UpdateProcessing` → `UpdateHighPressure`
  → раз в секунду обновление `AtmosExposedComponent` (теплообмен тела с воздухом, стр. 50, 107-121).

---

## 5. Давление: два солвера

### 5.1 LINDA — пошаговый обмен

`Content.Server/Atmos/EntitySystems/AtmosphereSystem.LINDA.cs`

- `ProcessCell` (11) — такт по активной клетке: обмен с соседями → реакции → сверхпроводимость.
- `Share` (215) — ядро: разница молей по каждому газу делится на `(число соседей + 1)`,
  тепло переносится через теплоёмкости, ΔP считается по `PV = nRT` (319).
- `Archive` (117) — перед обработкой смесь копируется в `AirArchived`; LINDA работает
  по архиву, поэтому результат не зависит от порядка обхода клеток.
  Копирование смеси — **~1/5 времени LINDA** (комментарий в коде, стр. 121).
- `CompareExchange` (`AtmosphereSystem.Gases.cs:259`) — решает, есть ли смысл в обмене
  (пороги: `MinimumMolesDeltaToMove`, `MinimumTemperatureDeltaToSuspend`).
- `AddActiveTile` (156) / `RemoveActiveTile` (171) — клетка без воздуха выбрасывается из работы.

### 5.2 MONSTERMOS — зональная заливка

`Content.Server/Atmos/EntitySystems/AtmosphereSystem.Monstermos.cs`

- `EqualizePressureInZone` (29): обход в ширину по зоне (до `MonstermosHardTileLimit = 2000` клеток,
  рабочий лимит `MonstermosTileLimit = 200`), средние моли по зоне, деление клеток на
  «доноры» и «получатели», выравнивание.
- Если в зоне встретилась клетка космоса → `ExplosivelyDepressurize` (373) — взрывная разгерметизация.
- `FinalizeEq` (614), `AdjustEqMovement` (667), `HandleDecompressionFloorRip` (694).
- **Включён по умолчанию**: `atmos.monstermos_equalization = true`. Когда он включён,
  LINDA-обмен отключается (`LINDA.cs:86`), но химия остаётся.
- `atmos.monstermos_depressurization` (дефолт true) и `atmos.monstermos_rip_tiles`
  (**дефолт false**: рвёт плитки и делает сцену нечитаемой).

### 5.3 Excited groups

`Content.Server/Atmos/EntitySystems/AtmosphereSystem.ExcitedGroup.cs`:
`AddTile` (32), `RemoveTile` (46), `Merge` (62), `ResetCooldowns` (100),
`SelfBreakdown` (112), `DeactivateGroupTiles` (170), `Dispose` (185).

Группа — набор клеток с идущим обменом; усредняется и разбирается:
`ExcitedGroupBreakdownCycles = 4`, `ExcitedGroupsDismantleCycles = 16`
(`Content.Shared/Atmos/Atmospherics.cs:204,209`).

---

## 6. Тепло, космос, перепад давления

| Механизм | Файл:строка | Поведение | CVar (дефолт) |
|---|---|---|---|
| Сверхпроводимость | `AtmosphereSystem.Superconductivity.cs:9` | тепло уходит в стены по `tile.HeatCapacity/ThermalConductivity` | `atmos.superconduction` = **false** |
| Space wind | `AtmosphereSystem.HighPressureDelta.cs:106` | перепад давления выбрасывает мобов и предметы + звуки | `atmos.space_wind` = **false** |
| Урон от давления | `AtmosphereSystem.DeltaPressure.cs:268` | `PerformDamage` по `DeltaPressureComponent`, пачками в параллельном job'е | `atmos.delta_pressure_damage` = true |

Константы урона: `HazardLowPressure = 20 kPa`, `WarningLowPressure = 50 kPa`,
`WarningHighPressure = 385 kPa`, `HazardHighPressure = 550 kPa`,
`LowPressureDamage = 4`, `PressureDamageCoefficient = 4`, `MaxHighPressureDamage = 4`
(`Content.Shared/Atmos/Atmospherics.cs:308-334`).

**Вакуум в этой системе — это не давление, а температура.** Клетка без грид-тайла
получает иммутабельную смесь карты (`AtmosphereSystem.GridAtmosphere.cs:227`), а та —
`GasMixture.SpaceGas`: 0 молей, 2,7 K (`GasMixture.cs:20`). Для ощущения холода
вакууму присваивается фиктивная теплоёмкость `SpaceHeatCapacity = 7000`
(`Atmospherics.cs:197`, `AtmosphereSystem.Gases.cs:49-55`).

---

## 7. Химия (LINDA-реакции)

### 7.1 Прототипы

`Resources/Prototypes/Atmospherics/reactions.yml`

| id | priority | гейты | результат |
|---|---|---|---|
| `PlasmaFire` | −2 | T ≥ 373.149 K, O₂ ≥ 0.01, Plasma ≥ 0.01 | горение плазмы; при пересыщении O₂ (O₂/плазма > 32) даёт **тритий** вместо CO₂ |
| `TritiumFire` | −1 | T ≥ 373.149 K, O₂, Tritium | горение трития, продукт — водяной пар |
| `N2ODecomposition` | 0 | T ≥ 850 K, N₂O | N₂O → N₂ + O₂ (только в горящем пожаре) |
| `FrezonCoolant` | 1 | T ≥ 23.15 K, N₂, Frezon | фреон + N₂ → N₂O, **отбирает тепло** (−600e3 Дж/моль) |
| `FrezonProduction` | 2 | T ≤ 73.15 K, O₂, N₂, Tritium | тритий + O₂ + N₂ → фреон; N₂ — катализатор, эффективность падает с ростом T |
| `AmmoniaOxygenReaction` | 2 | T ≥ 323.149 K, O₂, Ammonia | NH₃ + O₂ → N₂O + H₂O, скорость ∝ NH₃²·O₂² |
| ~~`WaterVaporPuddle`~~ | 1 | T ≤ 373.13 K | **закомментирован** — вода в лужу не конденсируется |

Класс прототипа — `Content.Server/Atmos/Reactions/GasReactionPrototype.cs`:
`MinimumRequirements` (19, `float[12]`), `MaximumTemperatureRequirement` (25),
`MinimumTemperatureRequirement` (31), `MinimumEnergyRequirement` (37), `Priority` (44),
`React` (58-68).

### 7.2 Драйвер

`Content.Server/Atmos/EntitySystems/AtmosphereSystem.Gases.cs:284` — `React()`:

1. реакции отсортированы по убыванию приоритета (`CacheGases`, стр. 28-32) — **чем больше
   priority, тем раньше проверяется**;
2. отсев по энергии и температуре (292-295);
3. проверка минимальных молей (298-307);
4. вызов эффекта (312), ранний выход по `ReactionResult.StopReactions` (313-314).

Точки вызова `React`: каждая клетка в LINDA (`LINDA.cs:103`), hotspot
(`AtmosphereSystem.Hotspot.cs:271`), **сети труб** (`PipeNet.Update`), газ-танки
(`Content.Server/Atmos/EntitySystems/GasTankSystem.cs:60`), кан-истеры
(`Content.Server/Atmos/Piping/Unary/EntitySystems/GasCanisterSystem.cs:92`), крио-капсула,
портативный скруббер, ручной API `ReactTile` (`AtmosphereSystem.API.cs:389`).

### 7.3 Реализации эффектов

`Content.Server/Atmos/Reactions/`

- `PlasmaFireReaction.cs` — шкала горения по температуре (21-29), скорость по O₂ (33),
  пересыщение (40-44, 57-59), тепло `FirePlasmaEnergyReleased = 160e3` (61-62), поджог при T > 373.15 (74-81).
- `TritiumFireReaction.cs` — две ветки (нехватка энергии / нормальная, 22-39),
  защита от «сингл-кап» бомб через `MinimumTritiumOxyburnEnergy = 143000` (23),
  продукт — водяной пар (48).
- `FrezonCoolantReaction.cs` — инвертированный знак энергии (50-51:
  `if (energyReleased >= 0f) return NoReaction;`), расход N₂ ×5, продукт N₂O (41-45).
- `FrezonProductionReaction.cs` — эффективность `T / 73.15` (21), катализатор-лимит по N₂ (26),
  конверсия 1/50 за тик (33-35).
- `AmmoniaOxygenReaction.cs` — скорость ∝ NH₃²·O₂² (18-20), продукты N₂O и H₂O (27-30).
- `N2ODecompositionReaction.cs` — N₂O → N₂ + O₂ (18, 23-25).
- `WaterVaporReaction.cs` — реализация есть (22-43), но прототип закомментирован.

---

## 8. Огонь и горючесть

**Hotspot (пожар в клетке)** — `Content.Server/Atmos/EntitySystems/AtmosphereSystem.Hotspot.cs`:

- `HotspotExpose` (194): нужны **окислитель** (204) **и топливо** (207) в одной смеси
  (единственный окислитель — O₂) и `exposedTemperature > PlasmaMinimumBurnTemperature = 373.15 K` (223).
- `ProcessHotspot` (58): пожар гаснет при `T < 373.15 K`, при `Volume ≤ 1` или когда смесь
  перестала быть воспламеняемой (88-96); **распространяется** только при `T > FireMinimumTemperatureToSpread = 423.15 K` (135),
  соседям достаётся `CellVolume/4` тепла (147).
- `PerformHotspotExposure` (251): реакция идёт на «отрезанной» доле воздуха
  (`RemoveVolume(Hotspot.Volume)`), объём пожара = `ReactionResults[Fire] × FireGrowthRate = 40000` (274).

**Горючесть существ** — `Content.Server/Atmos/EntitySystems/FlammableSystem.cs`:

- `OnTileFire` (242-250) — из события пожара считается перегрев;
- число «стаков огня» = `max(log2(ΔT/100) + 1, 0)` (426);
- горение требует ≥ 1 моль O₂ в смеси, иначе огонь тухнет сам (471-475);
- тепло = `FirestackEnergy (37500 J) × FireStacks` раз в секунду (480),
  урон = `Damage × FireStacks × множитель защиты` (482-489);
- `Ignite` (354), `TryExtinguish` (333), `SetFireStacks` (294), `Resist` (405).

---

## 9. Герметичность

- `Content.Server/Atmos/Components/AirtightComponent.cs` — `InitialAirBlockedDirection`,
  `CurrentAirBlockedDirection`, `AirBlocked`, `NoAirWhenBlocked`, `FixVacuum`.
- `Content.Server/Atmos/EntitySystems/AirtightSystem.cs`:
  `OnAirtightInit` (26), `SetAirblocked` (98-111) → `UpdatePosition` (113) →
  `InvalidatePosition` (127) → `_atmosphereSystem.InvalidateTile(...)` (128-131);
  событие `AirtightChanged` (163).
- Кэш `AirtightData` (`SharedAtmosphereSystem.cs:90`): `BlockedDirections`, `NoAirWhenBlocked`,
  `FixVacuum`. `FixVacuum` нужен, чтобы после разгерметизации воздух подкачивался обратно —
  иначе Airlock высасывал бы комнату каждый цикл.
- Соседство клеток — двустороннее: `UpdateAdjacentTiles`
  (`Content.Server/Atmos/EntitySystems/AtmosphereSystem.GridAtmosphere.cs:163`):
  если блокирует хотя бы одна сторона — две клетки не считаются соседями (199-206).
- Тонкие окна блокируют одну грань — у клетки остаётся воздух.
- Двери дёргают `SetAirblocked` при открывании/закрывании.

---

## 10. Космос и стартовый воздух

### 10.1 Космос

- `MapAtmosphereComponent` по умолчанию = `SpaceGas` → **все карты вакуумные**, кроме двух
  исключений с собственной смесью: `Resources/Maps/Nonstations/wizardsden.yml:208` и
  `Resources/Maps/Nonstations/nukieplanet.yml:864`.
- Клетка без грид-тайла → `MapAtmosphere = true` → иммутабельная смесь карты
  (`AtmosphereSystem.GridAtmosphere.cs:227-235`, `AtmosphereSystem.Processing.cs:188-218`).
- `IsTileSpace` (`AtmosphereSystem.API.cs:486`) по умолчанию возвращает `true` — «это же космическая игра».
- `SetMapAtmosphere` / `SetMapGasMixture` / `SetMapSpace` (`AtmosphereSystem.Map.cs:37,46,64`) —
  смена атмосферы карты с обновлением всех гридов (`RefreshAllGridMapAtmospheres`, 81).

### 10.2 Стартовая атмосфера хранится в файле карты

Компонент `GridAtmosphere` в YAML карты (`Resources/Maps/box.yml:579` — наша dev-карта):

```yaml
- type: GridAtmosphere
  version: 2
  data:
    uniqueMixes:
    - temperature: 293.15
      volume: 2500
      moles: { Oxygen: 21.824879, Nitrogen: 82.10312 }   # обычный воздух
    - volume: 2500
      moles: {}
      immutable: True                                    # вакуум
    - temperature: 235
      volume: 2500
      moles: { Oxygen: 27.225372, Nitrogen: 102.419266 } # крио-воздух
    ...
```

Формат: список уникальных смесей + чанки клеток 4×4 с битовыми масками индексов.
Сериализатор — `Content.Shared/Atmos/Serialization/TileAtmosCollectionSerializer.cs`:
чтение (22), legacy version 1 (32-56), version 2 (57-103), запись (108-155), `chunkSize = 4` (113),
дедупликация смесей (119-125). Маппинг «имя газа → индекс» — `Content.Shared/Atmos/GasArraySerializer.cs:64-104`.

Так хранятся **все** карты (`Resources/Maps/*.yml`) и **все шаттлы** (`Resources/Maps/Shuttles/*.yml`).

### 10.3 Служебное

- `AtmosFixMarkerComponent` + `/fixgridatmos <euid>` — пересборка атмосферы грида
  по «раундстартовым» смесям (режимы 0..8: воздух, вакуум, O₂/N₂/плазма для GM,
  мгновенный плазменный пожар, холодильник, чистый N₂, воздух GM) —
  `Content.Server/Atmos/EntitySystems/AtmosphereSystem.Commands.cs:70`.
- `AutomaticAtmosSystem.cs:22-38` — гридам массой ≥ 7 «плиток» выдаётся
  `GridAtmosphereComponent` (для шаттлов и построенных вручную станций); снятия нет.

---

## 11. Трубы и HVAC

### 11.1 Модель: газ хранится на сеть, а не на трубу

`Content.Server/NodeContainer/NodeGroups/PipeNet.cs` — `PipeNet : BaseNodeGroup, IPipeNet`:

| Член | Строка | Смысл |
|---|---|---|
| `Air` | 13 | **одна** смесь на всю сеть труб |
| `Initialize` | 19 | снимает `GridUid`; без сетки сеть невидима для атмосферы |
| `Update` | 35 | вызывает `AtmosphereSystem.React(Air, this)` — **химия идёт в трубах** |
| `LoadNodes` | 40 | объём сети = сумма объёмов труб |
| `RemoveNode` | 51 | при удалении узла газ сжимается пропорционально |
| `AfterRemake` | 64 | при пересборке графа газ делится между новыми сетями (`DivideInto`) |

`PipeNode.Air` — лишь прокси на `PipeNet.Air`
(`Content.Server/NodeContainer/Nodes/PipeNode.cs:97-105`); если сети нет, возвращается
иммутабельный `GasMixture.SpaceGas`, и любой `Merge` в него **молча** ничего не сделает.

### 11.2 Построение графа

- `Content.Server/NodeContainer/EntitySystems/NodeGroupSystem.cs`:
  `QueueRemakeGroup` (99), `QueueReflood` (118), `QueueNodeRemove` (127),
  `CreateSingleNetImmediate` (132), `Update` (142) → `DoGroupUpdates` (160).
- Соединение узлов — `PipeNode.GetReachableNodes` (160): соседние по тайлу + совпадение
  `NodeGroupID`, **слоя трубы** (`CurrentPipeLayer`, 29-30) и встречного `PipeDirection`
  (`LinkableNodesInDirection`, 208-224).
- Манифолд соединяет узлы в обход геометрии через `AddAlwaysReachable`
  (`Content.Server/Atmos/Piping/EntitySystems/GasPipeManifoldSystem.cs:38-39`) —
  сеть может оказаться связной «через стену».
- Слои: `Primary` / `Alt1` / `Alt2`, переключение — `AtmosPipeLayersSystem.cs:36`,
  запрет наложения — `PipeRestrictOverlapSystem.cs`.

### 11.3 Устройства

Граница «труба ↔ клетка» реализуется только устройствами и портами
(`PortPipeNode` ↔ `PortablePipeNode`).

| Устройство | Файл:строка | Что делает | Ограничение |
|---|---|---|---|
| `GasVentPump` (вент) | `Piping/Unary/EntitySystems/GasVentPumpSystem.cs:67` | качает труба↔клетка в режимах Releasing/Siphoning | `supplyPressure = min(pipe.Pressure × PumpPower, ExternalPressureBound)` (122) — **вент не создаёт давление выше давления в трубе**; при < 80 kPa — lockout и «протекание» (104, 135-142) |
| `GasVentScrubber` (скруббер) | `.../GasVentScrubberSystem.cs:53,91` | берёт пробу из клетки, фильтрует, сливает в трубу | отказ при давлении в трубе ≥ 50 атм (103); `WideNet` чистит соседние клетки (74-82); при Danger-тревоге **выключается** (130-142) |
| `GasPassiveVent` | `.../GasPassiveVentSystem.cs:24` | мгновенное выравнивание трубы и клетки без насоса | — |
| `GasOutletInjector` | `.../GasOutletInjectorSystem.cs:51` | строго труба → клетка | однонаправленный; упирается в `MaxPressure` (67) |
| `GasThermoMachine` (морозилка/нагреватель) | `.../GasThermoMachineSystem.cs:37` | гистерезис ±2 K, `dQ = HeatCapacity·Cp·dt` | режим `Atmospheric` греет окружающую атмосферу (82-86), иначе газ в трубе + утечка `EnergyLeakPercentage` (87-96) |
| `GasCondenser` | `.../GasCondenserSystem.cs:30` | конденсирует газ с реагентом в раствор бака | — |
| `GasCanister` | `.../GasCanisterSystem.cs:90`, `MixContainerWithPipeNet` (137) | смешивает содержимое с сетью труб | safety valve (`GasMaxPressureHolderComponent`) |
| `PortableScrubber` | `Content.Server/Atmos/Portable/PortableScrubberSystem.cs:48` | чистит свою клетку (и соседние при `WideNet`) | при уничтожении выливает газ в окружение (125-134) |
| `SpaceHeater` | `Content.Server/Atmos/Portable/SpaceHeaterSystem.cs:40` | `GasThermoMachine` с `Atmospheric: true` | режимы Heat/Cool/Auto (76-90, 124-137) |

Binary (2 узла) и Trinary (3 узла): `GasPassiveGate` (25-48, односторонний клапан по знаку ΔP),
`GasValve`, `SignalControlledValve`, `GasVolumePump` (39-101), `GasPressurePump`,
`GasPressureRegulator`, `GasRecycler`, `GasPort`, `GasFilter` (23,29), `GasMixer` (23,29),
`PressureControlledValve` (24-26,34) — всё в `Content.Server/Atmos/Piping/{Binary,Trinary}/EntitySystems/`.

Регистрация устройств в гриде — `Content.Server/Atmos/Piping/EntitySystems/AtmosDeviceSystem.cs`:
`OnDeviceInitialize` (91), `OnDeviceParentChanged` (113), `RejoinAtmosphere` (144),
`IsJoinedOffGrid` (151); off-grid устройства тикаются отдельно (128-149).

### 11.4 Газ-майнеры и газ-танки

- `Content.Server/Atmos/EntitySystems/GasMinerSystem.cs`: `OnMinerUpdated` (24-54),
  `CapSpawnAmount` (73-90) не даёт превысить `MaxExternalPressure` (дефолт 6500 kPa) и
  `MaxExternalAmount`; `GetValidEnvironment` (56-71) — **в космосе майнер не работает**.
  Прототипы: `Resources/Prototypes/Entities/Structures/Piping/Atmospherics/miners.yml`.
- `Content.Server/Atmos/EntitySystems/GasTankSystem.cs`: `DeviceUpdated` (42-73),
  `ReleaseGas` (100-140) через `FlowGas` с `ReleaseArea = 0.0005`, `SafetyMeasures` (156-166)
  открывает клапан при перегреве, реакции идут внутри бака (60).

### 11.5 Прототипы устройств

- `Resources/Prototypes/Entities/Structures/Piping/Atmospherics/unary.yml` — `GasVentPump` (25),
  `GasPassiveVent` (83), `GasVentScrubber` (115), `GasOutletInjector` (173),
  `GasThermoMachineFreezer` (272), `GasThermoMachineHeater` (317), `BaseGasCondenser` (392).
- `.../binary.yml`, `.../trinary.yml`, `.../special.yml`, `.../pipes.yml`, `.../alt_layers.yml`,
  `.../miners.yml`, `.../portable.yml`, `.../gas_pipe_sensor.yml`.
- Рецепты: `Resources/Prototypes/Recipes/Construction/atmospherics.yml`.

---

## 12. Игрок: дыхание, давление, тепло

### 12.1 Дыхание

`Content.Server/Body/Systems/RespiratorSystem.cs` — раз в 2 с чередуются вдох/выдох.

- `InhaleLocationEvent` (147-151) — **источник воздуха**: клетка, контейнер (шкафчик),
  крио-капсула, ИВЛ-подмена. Если никто не подменил — берётся смесь тайла (153).
- Из смеси снимается `BreathVolume = 0.5 л` (158), летит `InhaledGasEvent` (160).
- **Если никто не вдохнул — газ возвращается в клетку** (163-167).
- Газ → реагенты: `LungSystem.GasToReagent` (`Content.Shared/Body/Systems/LungSystem.cs:68-84`),
  множитель 1144; раствор «Lung», `canReact = false`, maxVol 100
  (`Resources/Prototypes/Body/base_organs.yml:415-425`).
- Метаболизм раствора (стадия `Respiration`, `Resources/Prototypes/Chemistry/metabolism_stages.yml:3-5`):
  O₂ → `Oxygenate` (в крови, `Resources/Prototypes/Reagents/gases.yml`);
  **CO₂ → `Oxygenate` с factor −4 в метаболизме крови** (вытесняет O₂; сам эффект —
  `Content.Shared/EntityEffects/Effects/Body/OxygenateEntityEffect.cs:13`, где `Factor` — «сатурация
  на единицу переваренного количества»), плюс `HealthChange` → Poison в стадии `Respiration`
  с `ignoreResistances` (выдохнутый CO₂ не должен травить);
  Plasma → яд 0.1/тик; Tritium → Radiation 1; N₂O → яд + замедление;
  Frezon → Cellular 0.05 + drunk; Ammonia — `Resources/Prototypes/Reagents/botany.yml:204`.
  Рецепты: `Resources/Prototypes/Reagents/gases.yml`.
- `Saturation` — **«сколько секунд запаса осталось»**, а не SpO2: падает на `UpdateInterval` за тик.
  Удушье: `SuffocationThreshold` → урон + эмоция «Gasp», алерт `LowOxygen` после 3 циклов (120-137).
- `CanMetabolizeInhaledAir` (222-258) требует `Saturation > UpdateInterval.TotalSeconds` (257).
- Маски и баллоны: `Content.Shared/Atmos/Components/BreathToolComponent.cs` (18, 27),
  `Content.Shared/Atmos/EntitySystems/SharedAtmosphereSystem.BreathTool.cs:20-50`.

### 12.2 Баротравма

`Content.Server/Atmos/EntitySystems/BarotraumaSystem.cs` — тик раз в секунду (24-25, 201-208).

- Давление берётся из окружающей смеси (226-229) и корректируется снаряжением
  (`GetFeltLowPressure` 133-142, `GetFeltHighPressure` 147-156).
- Защита применяется, только если закрыты **все** слоты из `ProtectionSlots`
  (по умолчанию `head`, `outerClothing`) — иначе защита = 0 (86-101, 104-107).
- Урон **всегда игнорирует резисты** (242, 257): низкое давление — фикс 4/сек,
  высокое — `min((P/550 − 1) · 4, 4)`.
- Иммунитет — статус-эффект `StatusEffectPressureImmunity`
  (`Content.Server/Atmos/EntitySystems/PressureImmunityStatusEffectSystem.cs:13-33`).

### 12.3 Теплообмен тела с воздухом

`AtmosExposedComponent` (`Content.Server/Atmos/Components/AtmosExposedComponent.cs:25`, `ExposedArea = 2 м²`),
тик раз в секунду (`AtmosphereSystem.cs:50,107-121`) → `AtmosExposedUpdateEvent`
(`Content.Shared/Atmos/AtmosExposedEvents.cs:16`). Потребитель:
`Content.Server/Temperature/Systems/TemperatureSystem.cs:61-72`.
Для существ в контейнерах — `AtmosExposedGetAirEvent` (36-63): шкафчики, крио-капсулы,
мехи, утилизатор.

---

## 13. Как игрок видит опасности (клиент)

### 13.1 Оверлеи

| Оверлей | Файл | Что рисует |
|---|---|---|
| Газы | `Content.Client/Atmos/Overlays/GasTileVisibleGasOverlay.cs` | RSI-текстуры видимых газов с alpha = opacity; анимация (95-115); для карты — `DrawMapOverlay` (212-246) |
| Огонь | `.../GasTileFireOverlay.cs` | `/Textures/Effects/fire.rsi`, 3 состояния (41-42, 159) |
| Тепловое искажение | `.../GasTileHeatBlurOverlay.cs` | screen-space шейдер, сила от 300 K до 2000 K (58-59, 241-251), отдельный render target (88-109), учёт `CCVars.DisableHeatDistortion` (73-81) |
| Тепловая карта | `.../GasTileDangerousTemperatureOverlay.cs` | цвет по ThermalByte, пороги 123/223/273/323/373/573 K (45-62, 69-145) |
| Дебаг (админ) | `.../AtmosDebugOverlay.cs` | heatmap по молям/температуре (89-133), индикаторы блок-дирекций, групп, space (135-177), тултип (237-259) |

**O₂, N₂, CO₂ и N₂O не имеют спрайта** — они не видны на клетке ни при какой концентрации.
Видимы только Plasma, Tritium, WaterVapor, Ammonia, Frezon.

### 13.2 Что уходит клиенту

1. Сервер считает оверлей-данные — `Content.Server/Atmos/EntitySystems/GasTileOverlaySystem.cs`:
   `GetOpacity` (158-164, квантование в 20 уровней), `GetOverlayData` (166-198),
   `UpdateChunkTile` (203-283) — ThermalByte температуры + opacity по видимым газам,
   `UpdateOverlayData` (285-307), `Update` (309-331) — **двухфазно**: сначала считаем,
   на следующем тике шлём; `UpdateSessions` (333-358) и `UpdatePlayerJob` (379-475) —
   параллельная рассылка только чанков в зоне видимости игрока.
   CVar: `net.gasoverlaytickrate` = 3/с, `net.gasoverlaythresholds` = 20, `net.pvs` (479-484).
2. Формат — `Content.Shared/Atmos/EntitySystems/SharedGasTileOverlaySystem.cs`:
   `GasOverlayData` (67-110) = `FireState` + `Opacity[]` + `ThermalByte`;
   ThermalByte: 0…1000 K → 0…250 (разрешение **4 K**), 254 = вакуум, 255 = стена (150-255).
3. Чанки 8×8 — `Content.Shared/Atmos/GasOverlayChunk.cs` (`TileData` — 64 записи, 19;
   `GetDataIndex` 43; `GasChunkEnumerator` 58).
4. Компонент — `Content.Shared/Atmos/Components/GasTileOverlayComponent.cs`:
   `InvalidTiles` (13), `Chunks` (18), `ForceTick` (23), `GasTileOverlayState` (27),
   `GasTileOverlayDeltaState` (33-71).
5. Клиент — `Content.Client/Atmos/EntitySystems/GasTileOverlaySystem.cs`: `OnHandleState` (19-56),
   `HandleGasOverlayUpdate` (58-85).

**Клиент физику не считает.** `Content.Client/Atmos/EntitySystems/AtmosphereSystem.Gases.cs:21-25` —
`React()` на клиенте бросает `NotImplementedException`.

---

## 14. Мониторинг и тревоги

### 14.1 Сенсор (AtmosMonitor)

`Content.Server/Atmos/Monitor/Systems/AtmosMonitoringSystem.cs`:

- берёт смесь из трубы (`MonitorsPipeNet` → `PipeNode.Air`, 70-79) или из клетки (78);
- пороги грузятся из `AtmosAlarmThresholdPrototype` (81-104);
- `UpdateState` (258-326): **газ сравнивается как доля** `moles / total` (301),
  давление и температура — абсолютные значения;
- пожар на контролируемой клетке → сразу Danger (210-215);
- без заданных порогов мониторинга нет (238-241);
- `Alert` (333-344) → `BroadcastAlertPacket` (367-390) всем зарегистрированным устройствам.

Пороги по умолчанию — `Resources/Prototypes/Atmospherics/Thresholds/airalarms.yml`:
температура 393.15/193.15 K, давление 550/85 kPa, O₂ 10–30 %, N₂ ≥ 10 %, CO₂ 0.6 %,
Plasma 0.5 %, Tritium 0.4 %, N₂O 1 %, Ammonia 5 %, плюс `ignore`/`danger`/vox/freezer.
**Warning-пороги — множители danger-границы**, а не абсолютные значения (комментарий в файле, стр. 3-8).

### 14.2 Air alarm

`Content.Server/Atmos/Monitor/Systems/AirAlarmSystem.cs` + `AirAlarmModes.cs`.

- Режимы: `None, Filtering, WideFiltering, Fill, Panic`
  (`Content.Shared/Atmos/Monitor/Components/SharedAirAlarmComponent.cs:12-19`).
  Реализации: `None` 98-117, `Filtering` 119-136, `WideFiltering` 138-155 (WideNet),
  `Panic` 157-174 (венты выключены, скраберы в режиме siphoning + WideNet),
  `Fill` 176-193 (внешний предел 50 атм + lockout override).
- Авто-режим: Danger → WideFiltering, Normal/Warning → Filtering (419-429).
- Panic-провод → принудительно Panic (474-477); в сети только один alarm может владеть
  «обновляемым» режимом (485-509).
- Доступ — `AccessCheck` (384-404): без AccessReader проходят все, иначе popup и админ-лог.
- UI-состояние: средние P и T по сенсорам (616, 623), средние концентрации газов (629-642),
  ре-синк сенсоров раз в 8 с (682-696).
- Клиент: `Content.Client/Atmos/Monitor/UI/AirAlarmWindow.xaml.cs` (52-64 — список режимов,
  77-79 — вкладки, 98-115 — обновление; в Panic селектор скрывается, 113-114).

### 14.3 Приёмники тревог

`Content.Server/Atmos/Monitor/Systems/AtmosAlarmableSystem.cs`: фильтрация по тегам источника (94-99)
и по типам (111-114), `TryUpdateAlert` (169-185), `ForceAlert` (211-239), `Reset` (246-266),
`TryGetHighestAlert` (285-301), `PlayAlertSound` (303-311).

### 14.4 Сеть устройств

`Content.Server/Atmos/Monitor/Systems/AtmosDeviceNetwork.cs` — команды `atmos_register_device` (17),
`atmos_deregister_device` (22), `atmos_sync_data` (27), методы (31-70). Обычный `DeviceNetwork`.

---

## 15. Тюнинг (CVar)

`Content.Shared/CCVar/CCVars.Atmos.cs`

| CVar | Дефолт | Смысл |
|---|---|---|
| `atmos.tickrate` | 15 | период стадии атмосферы (TPS) |
| `atmos.speedup` | 8 | ускорение механики (насосы, скрубберы) |
| `atmos.heat_scale` | 8 | ускорение тепла реакций; **теплоёмкости из YAML делятся на него** |
| `atmos.max_process_time` | 3 мс | бюджет атмоса на кадр |
| `atmos.monstermos_equalization` | true | зональный солвер давления |
| `atmos.monstermos_depressurization` | true | взрывная разгерметизация |
| `atmos.monstermos_rip_tiles` | **false** | рвать плитки при разгерметизации (ломает читаемость сцены) |
| `atmos.grid_impulse` | false | импульс гриду при разгерметизации |
| `atmos.mmos_spacing_speed` | 0.15 | доля воздуха, утекающая из клетки за тик |
| `atmos.mmos_min_gas` | 2.0 kPa | ниже порога воздух обнуляется сразу |
| `atmos.mmos_max_wind` | 500 | ограничение потока при спейсинге |
| `atmos.excited_groups` | true | группы активных клеток |
| `atmos.excited_groups_space_is_all_consuming` | false | очистка группы при контакте с космосом |
| `atmos.superconduction` | **false** | тепло в стены |
| `atmos.superconduction_tile_loss` | 30 | потери тепла излучением при 20 °C, Вт |
| `atmos.space_wind` | **false** | выброс мобов и предметов перепадом давления |
| `atmos.space_wind_pressure_force_divisor_throw` | 15 | делитель силы броска |
| `atmos.space_wind_pressure_force_divisor_push` | 2500 | делитель силы толчка |
| `atmos.space_wind_max_velocity` | 30 | макс. скорость от перепада давления |
| `atmos.space_wind_max_push_force` | 20 | макс. сила толчка |
| `atmos.delta_pressure_damage` | true | урон от перепада давлений |
| `atmos.delta_pressure_parallel_process_per_iteration` | 1000 | сколько сущностей уходит в параллельную обработку за проход |
| `atmos.delta_pressure_parallel_batch_size` | 10 | размер батча в job'е |
| `atmos.max_explosion_range` | 0 | ограничение взрыва от разрыва бака (0 = без ограничения) |
| `net.gasoverlaytickrate` | 3 | частота отправки оверлеев клиенту |
| `net.gasoverlaythresholds` | 20 | уровней прозрачности газа |

---

## 16. Кто в репозитории вызывает API атмосферы

Ядро атмосферы форком не менялось, но API используется в нескольких местах:

| Потребитель | Файл:строка | Что вызывает |
|---|---|---|
| Ручная вентиляция | `Content.Server/Medical/MedicalVentilationSystem.cs:84-89` | `RemoveVolumeAtPressure`, `Merge` |
| Кровать жизнеобеспечения | `Content.Server/Medical/MedicalLifeSupportSystem.cs:39-40` | `RemoveVolumeAtPressure` |
| Крио-капсула | `Content.Server/Medical/CryoPodSystem.cs:70-78` | `AtmosDeviceUpdateEvent`, `PortablePipeNode`, `React` |
| Внутри крио | `Content.Server/Medical/InsideCryoPodSystem.cs:16-21` | `AtmosExposedGetAirEvent` |
| Спавн-техника | `Content.Server/Medical/MedicalVentilationComponent.cs:9` | `GasMixture` как поле компонента |

Практическое следствие: при изменении `RemoveVolumeAtPressure`, `FlowGas`, `ScrubInto`,
`Merge` или `React` нужно пересобрать и прогнать медицинские тесты — сломается
ИВЛ, крио и лайф-поддержка.

---

## 17. API для работы с атмосферой из кода

`Content.Server/Atmos/EntitySystems/AtmosphereSystem.API.cs`

| Задача | Метод | Строка |
|---|---|---|
| Есть ли атмосфера у грида | `HasAtmosphere` | 30 |
| Включить/выключить симуляцию грида | `SetSimulatedGrid` / `IsSimulatedGrid` | 42 / 56 |
| Смесь клетки по сущности | `GetTileMixture` | 112, 239 |
| Смесь, в которой находится сущность | `GetContainingMixture` | 130, 149 |
| Смеси нескольких клеток | `GetTileMixtures` | 174 |
| Все смеси грида | `GetAllMixtures` | 71 |
| Добавить смесь в клетку | `MergeTileMixture` / `MergeContainingMixture` | 275 / 266 |
| Изменить количество газа | `AdjustTileMixture` / `AdjustContainingMixture` | 289 / 283 |
| Провести реакцию в клетке | `ReactTile` | 389 |
| Заблокирован ли тайл | `IsTileAirBlocked` / `IsTileAirBlockedCached` | 412 / 438 |
| Направления потока воздуха | `GetAirflowDirections` | 463 |
| Космос ли тайл | `IsTileSpace` | 486 |
| Безопасен ли состав | `IsTileMixtureProbablySafe` | 513 |
| Теплоёмкость клетки | `GetTileHeatCapacity` | 526 |
| Поджечь тайл | `HotspotExpose` | 567, 596 |
| Потушить / проверить пожар | `HotspotExtinguish` / `IsHotspotActive` | 615 / 628 |
| Зарегистрировать сеть труб | `AddPipeNet` / `RemovePipeNet` | 644 / 656 |
| Зарегистрировать устройство | `AddAtmosDevice` / `RemoveAtmosDevice` | 676 / 697 |
| Инвалидировать клетку | `InvalidateTile` | 96 |

Shared-слой — `Content.Shared/Atmos/EntitySystems/SharedAtmosphereSystem.API.cs`:
`TryGetExposedMixture` (74), `GetOverPressure` (99).

Полезные утилиты смеси — `Content.Server/Atmos/EntitySystems/AtmosphereSystem.Gases.cs`:
`AddHeat` (89), `DivideInto` (101), `ReleaseGasTo` (147), `PumpGasTo` (180),
`ScrubInto` (203), `IsMixtureProbablySafe` (222), `GetMass` (34, 39).

---

## 18. Грабли

### Данные и прототипы

1. `TotalNumberOfGases = 9`, `AdjustedNumberOfGases = 12` захардкожены в куче мест.
   Добавление газа = enum + обе константы + `GasReagents` (длина **9**, не 12!) +
   прототип с ID, совпадающим с enum'ом.
2. `molarHeatCapacity` из YAML автоматически делится на `atmos.heat_scale` (8).
3. `GasMixture.Moles` защищены `[Access]` — писать можно только через
   `GetMoles/SetMoles/AdjustMoles`, иначе анализатор доступа (RA0002) не даст собраться.
4. `GasMixture.Temperature` клампится в [2,7 K; 262144 K] и **не меняется** у иммутабельных смесей.
5. `Merge` в иммутабельную смесь (космос, атмосфера карты) — **молча теряет газ**.
6. `Remove`/`RemoveRatio` уничтожают моли ниже `GasMinMoles = 5e-8`.
7. `RemoveVolumeAtPressure` делит на `Temperature` смеси → при T = 0/NaN получим Inf/NaN молей.
8. `RemoveVolumeAtPressure` + `Remove` снимают **долю** всей смеси, а не абсолютные моли:
   из 10-л пакета (10 моль) один вдох (0,5 л при 1 атм ≈ 0,0208 моль) забирает ≈ 0,21 %.
9. `MinimumRequirements` = 0 **не работает** как гейт: проверка `if (!(moles < req)) continue;`
   всегда проходит при req = 0. Чтобы запретить — нужно req > 0.

### Реакции и огонь

10. Порядок реакций — по убыванию `priority`: `TritiumFire (−1)` проверяется **раньше**
    `PlasmaFire (−2)`.
11. `FrezonCoolantReaction` использует инвертированный знак энергии и ранний выход
    `if (energyReleased >= 0f) return NoReaction;` — легко сломать знак при правке.
12. Плазменный пожар даёт **тритий** вместо CO₂ при пересыщении O₂ (O₂/плазма > 32).
13. Hotspot требует **и окислителя, и топлива**; единственный окислитель — O₂,
    значит в чистом фрезоне или N₂ огонь не заведётся.
14. Пожар гаснет при `T < 373.15 K` или `Volume ≤ 1`; распространяется только при `T > 423.15 K`.
15. `FlammableSystem` требует ≥ 1 моль O₂ в окружающей смеси, иначе огонь тухнет сам.

### Давление и герметичность

16. Давление всегда игнорирует резисты; спасает только снаряжение и статус-эффект иммунитета.
17. Защита по давлению работает, только если закрыты **все** слоты `ProtectionSlots`.
18. В клетках `Space` по умолчанию `true` — без `MapAtmosphereComponent` всё будет вакуумом.
19. `FixVacuum` обязателен у дверей: без него Airlock высасывает комнату каждый цикл.
20. Изменение герметичности → `InvalidateTile` → стадия `Revalidate` — до этого
    атмосфера работает со старой раскладкой.

### Тайминг и производительность

21. Атмос не тикает каждый кадр: 15 Гц × 10 стадий ≈ 0,6 с на полный цикл.
22. `dt` у устройств = `RealAtmosTime()`, а не `frameTime`.
23. Стадия может «размазаться» по кадрам из-за бюджета 3 мс → устройство срабатывает через раз.
24. Копирование смеси в `AirArchived` — ~1/5 времени LINDA; не вызывать лишний раз.

### Трубы

25. Газ в трубе = `PipeNet.Air` на **сеть**, а не на трубу.
26. `PipeNode.Air` при отсутствии сети отдаёт иммутабельный `SpaceGas` — `Merge` в него молчит.
27. Реакции идут в трубах, баках и кан-истерах.
28. Вент не создаёт давление выше давления в трубе; скруббер откажется при ≥ 50 атм в трубе.
29. Слои труб (Primary/Alt1/Alt2) — часть связности; манифолд соединяет в обход геометрии.
30. `AutomaticAtmosSystem` выдаёт `GridAtmosphereComponent` гридам массой ≥ 7 «плиток»;
    снятия компонента не предусмотрено.

### Клиент

31. Клиент **никогда** не реактит: `React()` бросает `NotImplementedException`.
32. O₂, N₂, CO₂, N₂O невидимы — у них нет overlay-спрайта.
33. ThermalByte: разрешение 4 K, потолок 1000 K; оверлей искажения считает до 2000 K,
    но по сети выше 1000 K разницы не видно.
34. `MapAtmosphereComponent.Overlay` — одна смесь на всю карту, пер-тайловой разницы нет.
35. Переключение PVS заставляет сервер переслать полный state.

---

## 19. Тесты

`Content.IntegrationTests/Tests/Atmos/TileAtmosphereTest.cs` (базовый класс `AtmosTest`):

- `GasSpreading` (24) — газ, подложенный в закрытую зону, выравнивается; сумма молей сохраняется.
- `FireSpreading` (67) — воспламенение в клетке распространяется по всей зоне.
- Отдельные прогоны с выключенным MONSTERMOS (LINDA-режим): `TileAtmosphereTest_LINDA_X` (161),
  `TileAtmosphereTest_LINDA_Snake` (172); у LINDA-режима свой допуск по массе (0.1),
  потому что `PlasmaFireReaction` не сохраняет массу.

Фильтр для прогона атмосферных тестов:

```powershell
dotnet test SS14\Content.IntegrationTests\Content.IntegrationTests.csproj --no-build `
  --filter "FullyQualifiedName~Atmos|FullyQualifiedName~TileAtmosphere" `
  --logger "trx;LogFileName=atmos.trx"
```

---

## 20. Точки расширения

| Задача | Куда копать |
|---|---|
| Новый газ | enum `Gas` + константы в `Atmospherics.cs` + `Resources/Prototypes/Atmospherics/gases.yml` + `GasReagents` |
| Новая реакция | `Resources/Prototypes/Atmospherics/reactions.yml` + класс эффекта в `Content.Server/Atmos/Reactions/` |
| Новое атмос-устройство | компонент в `Content.Shared/Atmos/Piping/...` + серверная система в `Content.Server/Atmos/Piping/...` + прототип в `Resources/Prototypes/Entities/Structures/Piping/Atmospherics/` |
| Новый порог тревоги | `Resources/Prototypes/Atmospherics/Thresholds/airalarms.yml` + `AtmosAlarmThresholdPrototype` |
| Новый клиентский оверлей | `Content.Client/Atmos/Overlays/` + регистрация в `Content.Client/Atmos/EntitySystems/GasTile*OverlaySystem.cs` |
| Новый режим air alarm | `Content.Shared/Atmos/Monitor/Components/SharedAirAlarmComponent.cs` + `Content.Server/Atmos/Monitor/Systems/AirAlarmModes.cs` |
| Новый источник воздуха для дыхания | подписаться на `InhaleLocationEvent` и подменить `Gas` |
| Админский инструмент | команды в `Content.Server/Atmos/Commands/` (паттерн — `AtmosphereSystem.Commands.cs`) |

---

## 21. Быстрый указатель «задача → файл»

| Задача | Файл:строка |
|---|---|
| Тик атмосферы, стадии | `Content.Server/Atmos/EntitySystems/AtmosphereSystem.Processing.cs:702` |
| Модель клетки | `Content.Shared/Atmos/TileAtmosphere.cs` |
| Модель смеси | `Content.Shared/Atmos/GasMixture.cs` |
| Константы и enum газов | `Content.Shared/Atmos/Atmospherics.cs` |
| Давление (зональный солвер) | `Content.Server/Atmos/EntitySystems/AtmosphereSystem.Monstermos.cs:29` |
| Давление (пошаговый обмен) | `Content.Server/Atmos/EntitySystems/AtmosphereSystem.LINDA.cs:215` |
| Химия (драйвер) | `Content.Server/Atmos/EntitySystems/AtmosphereSystem.Gases.cs:284` |
| Огонь (клетка) | `Content.Server/Atmos/EntitySystems/AtmosphereSystem.Hotspot.cs:58` |
| Огонь (существа) | `Content.Server/Atmos/EntitySystems/FlammableSystem.cs:420` |
| Герметичность | `Content.Server/Atmos/EntitySystems/AirtightSystem.cs:98` |
| Соседство клеток | `Content.Server/Atmos/EntitySystems/AtmosphereSystem.GridAtmosphere.cs:163` |
| Космос / смесь карты | `Content.Server/Atmos/EntitySystems/AtmosphereSystem.GridAtmosphere.cs:227` |
| Сеть труб и её газ | `Content.Server/NodeContainer/NodeGroups/PipeNet.cs:13` |
| Узел трубы | `Content.Server/NodeContainer/Nodes/PipeNode.cs:97` |
| Граф труб | `Content.Server/NodeContainer/EntitySystems/NodeGroupSystem.cs:142` |
| Вентиляция комнаты | `Content.Server/Atmos/Piping/Unary/EntitySystems/GasVentPumpSystem.cs:67` |
| Очистка воздуха | `Content.Server/Atmos/Piping/Unary/EntitySystems/GasVentScrubberSystem.cs:91` |
| Газ-майнер | `Content.Server/Atmos/EntitySystems/GasMinerSystem.cs:24` |
| Газ-танк | `Content.Server/Atmos/EntitySystems/GasTankSystem.cs:42` |
| Дыхание | `Content.Server/Body/Systems/RespiratorSystem.cs:79` |
| Газ → реагенты | `Content.Shared/Body/Systems/LungSystem.cs:68` |
| Баротравма | `Content.Server/Atmos/EntitySystems/BarotraumaSystem.cs:201` |
| Тепло тела от воздуха | `Content.Server/Temperature/Systems/TemperatureSystem.cs:61` |
| Сенсор тревоги | `Content.Server/Atmos/Monitor/Systems/AtmosMonitoringSystem.cs:258` |
| Air alarm | `Content.Server/Atmos/Monitor/Systems/AirAlarmSystem.cs:467` |
| Режимы air alarm | `Content.Server/Atmos/Monitor/Systems/AirAlarmModes.cs:66` |
| Оверлеи (клиент) | `Content.Client/Atmos/Overlays/` |
| Отправка оверлеев | `Content.Server/Atmos/EntitySystems/GasTileOverlaySystem.cs:309` |
| Сериализация тайлов в YAML | `Content.Shared/Atmos/Serialization/TileAtmosCollectionSerializer.cs:22` |
| CVar атмосферы | `Content.Shared/CCVar/CCVars.Atmos.cs` |
| Админ-команды | `Content.Server/Atmos/Commands/`, `Content.Server/Atmos/EntitySystems/AtmosphereSystem.Commands.cs` |
