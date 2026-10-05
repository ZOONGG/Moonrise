# Moonrise: универсальный launch contract

Дата: 2026-10-05. Research/design; runtime не изменён.

Рекомендация: развивать ControlledGenesis как предпочтительный backend для проверенных runtime contracts. OfficialLunar оставить для provisioning и явного fallback. Открытие официального приложения — отдельное действие пользователя. Отказ от обязательного Electron UI технически обоснован LCQt, но ещё не подтверждён smoke на текущем Lunar. Переключать default можно только после проверки актуального Genesis, авторизации и ownership без перехвата credentials.

## 1. Проверенная база

Moonrise: branch `test/mnr4-runtime-candidate-v2`, HEAD `9b8427c497878e9c0ef9d8580b195f1b8ad771c9`; изучен рабочий checkout. До работы уже изменены `runtime/bridge/Moonrise.Native.dll` и `NativeBridgeDeploymentService.cs`; эти изменения сохранены.

Upstream изучен по исходникам и истории Git:

| Reference | Проверенный revision / evidence |
| --- | --- |
| [Youded LCQt](https://github.com/Youded-byte/lunar-client-qt/tree/cab04d1464abdb3cdb1b3ce748a53f89ea6bbcb8) | `cab04d1464abdb3cdb1b3ce748a53f89ea6bbcb8`, 2024-02-27: «Update Weave-Loader to v0.2.6»; меняет bundled JAR. HEAD `8a5a8f9a12a9d620301cf0428154ef8551e0650b` также просмотрен |
| [Nilsen LCQt](https://github.com/Nilsen84/lunar-client-qt/tree/7c71733e320f851c4418860f7a10a953d5f7c998) | `7c71733e320f851c4418860f7a10a953d5f7c998`; иной, более ранний offline launcher |
| [Nilsen agents](https://github.com/Nilsen84/lunar-client-agents/tree/9169a0dd9959cb41f21ebd6ede3b26e3c6fd16ce) | `9169a0dd9959cb41f21ebd6ede3b26e3c6fd16ce`; Gradle manifest и premain implementations |
| [Youded agents](https://github.com/Youded-byte/youded-lunar-client-agents/tree/2a6453c0de29849eaca48dff4eb45d407ebb9941) | `2a6453c0de29849eaca48dff4eb45d407ebb9941`; README и manifests трёх опубликованных JAR; исходников этих JAR в repo нет |
| [Weave 0.2.6](https://github.com/Weave-MC/Weave-Loader/tree/1fa9afe9da187efe8694e881c8547ded982b25a2) | tag `v0.2.6`, commit `1fa9afe9da187efe8694e881c8547ded982b25a2`; Agent.kt, WeaveClassLoader.kt, WeaveLoader.kt, service descriptor |
| [Weave 1.3.4 metadata](https://github.com/Weave-MC/Weave-Loader/blob/1.3.4/internals/src/main/kotlin/net/weavemc/internals/Mods.kt) | версия, выбранная текущим Moonrise; schema проверена отдельно от legacy |

LCQt, Nilsen agents и Weave имеют GPL-3.0; Youded agents — GPL-2.0. Это исследование контрактов, без копирования кода или включения upstream binaries в Moonrise.

Локально, без запуска и без чтения account files, обнаружены:

- `.lunarclient/offline/multiver`: Genesis, common/legacy/lunar artifacts, mappings, Forge/OptiFine artifacts, native/resource ZIPs.
- Genesis manifest: `Implementation-Version: 9.10.1`, main `com.moonsworth.lunar.genesis.Genesis`. Его `IMixinService` descriptor содержит обфусцированный Lunar/Ichor provider, LaunchWrapper и ModLauncher providers. Наличие descriptor не доказывает, какой provider фактически выбран.
- `.lunarclient/jre`: Azul x64 Java 17.0.3 и 17.0.18, по `release` metadata. Это доступные кандидаты, не доказательство подходящего runtime для любого окружения. Natives с `x86` в имени требуют проверки реальной архитектуры, а не вывода по имени.
- `profiles.db` используется существующим profile service. В этой задаче его содержимое не читалось; loader/profile mapping ещё требует проверки.
- Bundled Youded Weave JAR: SHA-256 `c4200d145c0d3d78f57eab4ddee3a64ff1a3176e65f2e2b15a31777a0142b524`, совпадает с ожидаемым legacy artifact Moonrise; manifest содержит `Premain-Class: net.weavemc.loader.bootstrap.AgentKt`, `Can-Retransform-Classes: true`.

## 2. Точный текущий порядок Moonrise

Источники: `MainWindow.xaml.cs:2940–3300`, `CurrentLaunchPlanFactory`, `LaunchPlanBuilder`, `NativeBridgeLauncher`, `native/Moonrise.Native/bridge.c`.

1. Recovery gate: Lunar / MC 1.8.9; reconcile и resolve enabled packages.
2. Moonrise-owned successor и exact-hash maintained-build resolution; затем inspection API generation и запрет mixed legacy/current. Эти преобразования предшествуют выбору loader.
3. Проверки процессов: текущий путь требует закрытого Lunar Launcher; затем `BeginExactProfileSelection` временно записывает `settings.gameProfile` в `launcher.json`, фиксируется checkpoint launcher log.
4. Создаётся launch session; проверяется/скачивается выбранный Weave artifact; готовится directory с enabled mods. Для legacy добавляется directory adapter. При специальных BWH условиях готовится network adapter.
5. Формируется нынешний частичный LaunchPlan. Порядок: network adapter, если есть → compatibility adapter, если есть → Weave Loader → package agents в переданном порядке. Factory сейчас не передаёт package agent options.
6. Без пакетов стартует официальный launcher. С пакетами launcher создаётся suspended, внедряется native bridge, ожидается readiness, процесс возобновляется. Bridge перехватывает `CreateProcessW`, дополняет child environment, создаёт descendants suspended и распространяет bridge до resume.
7. Moonrise скрывает принадлежащие Lunar окна, ждёт подтверждения выбранного profile в renderer log. Затем запускает hidden IPC sender с `lunarclient://launch`; временный выбор profile восстанавливается после dispatch.
8. Официальный Lunar определяет Java, classpath, Genesis/Ichor и game arguments, создаёт JVM. Bridge добавляет структурированные агенты и managed properties через `JAVA_TOOL_OPTIONS`, перед существующим значением этой переменной. Это startup premain injection, не поздний Java Attach API.
9. JVM выполняет agents до Genesis main. Однако Moonrise не задаёт весь порядок относительно agents в официальном command line, других JVM environment variables и classloader/service initialization.
10. Genesis/Ichor загружает game; Moonrise обнаруживает JVM и usable game window, отслеживает завершение и cleanup. Готовность bridge/видимость окна сами по себе не доказывают загрузку mod.

Текущий `LaunchPlan` описывает mod paths, agents, JVM args/properties и режим Weave. В нём нет Java, полного classpath, Ichor inputs, main/game args, backend, base loader или snapshot runtime. `record` с `IReadOnlyList` также не гарантирует глубокую неизменяемость.

## 3. Как Youded LCQt подключал 0.2.6

В [offlinelauncher.cpp на cab04d1](https://github.com/Youded-byte/lunar-client-qt/blob/cab04d1464abdb3cdb1b3ce748a53f89ea6bbcb8/src/launch/offlinelauncher.cpp) последовательность такая:

1. Java = custom JRE path либо поиск Lunar JRE; working dir = `offline/multiver`.
2. `Utils::getClassPath(files, version, modLoader)` строит список; его копия становится `ichorClassPath`. Дополнительные launcher libs добавляются только в JVM classpath.
3. Определяется native path; добавляются module/open/export, memory и native JVM options, `-cp`.
4. `-javaagent:NativesPrepare=<native-path>`.
5. Enabled пользовательские agents в порядке `QList<Agent>`, каждый `path=option`.
6. При `useWeave` — bundled WeaveLoader как `-javaagent`.
7. Пользовательские JVM args, разобранные `QProcess::splitCommand`.
8. Genesis main; version, asset index, game dir, working/classpath dirs, `--ichorClassPath` и `--ichorExternalFiles`. Последние два используют comma-separated contract, JVM classpath — platform separator.
9. Отдельно строится child environment: убираются JVM option environment variables; JVM запускается `startDetached` без официального Electron launcher.

`Agent` содержит path, option, enabled; порядок списка сохраняется. `modLoader` влияет на [classpath/external files](https://github.com/Youded-byte/lunar-client-qt/blob/cab04d1464abdb3cdb1b3ce748a53f89ea6bbcb8/src/util/utils.cpp). LCQt применяет исторические filename filters, которые нельзя считать актуальным универсальным discovery contract.

У Nilsen на проверенном HEAD тоже direct Genesis, custom Java и premain agents, но natives распаковываются самим launcher; там нет той же bundled-Weave/NativesPrepare цепочки. Нельзя смешивать semantics двух revisions.

[Youded README](https://github.com/Youded-byte/lunar-client-qt/blob/8a5a8f9a12a9d620301cf0428154ef8551e0650b/README.md) прямо описывает отключение bundled Weave и добавление другого loader как agent. Для Moonrise это применимо, если external loader занимает ровно один структурированный Weave slot.

## 4. Что доказано о Mixin failure

[Legacy Agent.kt](https://github.com/Weave-MC/Weave-Loader/blob/1fa9afe9da187efe8694e881c8547ded982b25a2/src/main/kotlin/net/weavemc/loader/bootstrap/Agent.kt) в premain проверяет `--version` из `sun.java.command`, устанавливает transformers. Полный loader bootstrap отложен до загрузки первого `net/minecraft/client/` класса: добавляет Weave URL в game loader и отражённо вызывает `WeaveLoader.init(inst)`.

В [WeaveLoader.kt](https://github.com/Weave-MC/Weave-Loader/blob/1fa9afe9da187efe8694e881c8547ded982b25a2/src/main/kotlin/net/weavemc/loader/WeaveLoader.kt) сначала выполняются `MixinBootstrap.init()` и проверка `MixinService.getService() is WeaveMixinService`, и только затем читаются mods и вызываются entrypoints. Ошибка означает нарушение loader/service contract до обработки Stormy; она не доказывает дефект самого mod.

Premain attachment ≠ немедленная Weave/Mixin initialization. Исторический LCQt тоже использует отложенный bootstrap. Раннее premain необходимо, но недостаточно: важны видимость service resources, classloader delegation, происхождение Mixin classes, native/technical agents и первые обращения к Mixin singleton.

Возможные generic причины: другой выбранный provider, уже созданный singleton, несовместимая Ichor classloader topology, различия JVM/ichor classpath, ordering agents или runtime contract drift. Точное имя активного provider, его defining loader/code source и момент первой инициализации пока неизвестны. Поменять agents местами без evidence недостаточно.

Stormy failure взят из предоставленного задания; новый smoke не выполнялся. Следующий диагностический эксперимент: на одном snapshot текущего Lunar сравнить минимальный legacy environment без mods, затем synthetic legacy fixture, с одинаковым Java и явным порядком agents; получить sanitized class-loading/provider evidence. Только после этого — representative mod smoke. Никаких package-name/SHA runtime branches.

## 5. Package inspection и rules matrix

Inspection возвращает evidence, detected capabilities, declared requirements и неизвестные поля; override хранится отдельно и не стирает evidence.

| Тип | Проверяемые признаки / действие |
| --- | --- |
| JavaAgent | Main manifest `Premain-Class`; проверка класса, bytecode level, Can-Redefine/Can-Retransform, Boot-Class-Path dependencies. Только `Agent-Class` означает attach capability и не допускает startup `-javaagent` |
| WeaveMod | Корневой `weave.mod.json`; name/id, compiledFor, namespace, entrypoints, hooks, mixins, dependencies и реальные schema hints |
| ForgeMod | `META-INF/mods.toml`, legacy `mcmod.info` и дополнительные Forge declarations; парсить loader/MC ranges, side и dependencies |
| FabricMod | `fabric.mod.json`; id/version, depends.minecraft/fabricloader, environment, nested JAR declarations |
| Unknown / ambiguous | Нет достаточных признаков либо несколько ecosystem capabilities; не активировать молча |

Override: `Auto / Java Agent / Weave Mod / Forge Mod / Fabric Mod`. Override выбирает interpretation, но не отменяет проверку premain, dependencies или environment compatibility. ZIP/JSON/manifest parsing ограничен по размеру и не выполняет код из импортируемого JAR.

Legacy 0.2.6 schema уже допускает `modId`; значит нынешний `WeaveModApiInspector` fallback «modId → current» неверен. `compiledFor` — MC target, не версия loader. API references `net/weavemc/loader/api/` против `net/weavemc/api/` являются evidence generation, но строки/затенённые библиотеки могут дать false positive. Нужен разбор class constant pool/references и schema вместе; неизвестное requirement остаётся неизвестным. Нельзя выводить точную 0.2.6 только из legacy namespace.

| Requirements × environment | Решение |
| --- | --- |
| Premain agent + удовлетворённые MC/Java/capabilities | Ordered agent pipeline; отсутствие declarations означает untested warning, не обещание совместимости |
| Legacy Weave + проверенный Lunar contract | Один legacy loader; удовлетворить API, MC и mod-directory contract |
| Current Weave + проверенный Lunar contract | Один current loader с подходящей namespace/mappings environment |
| Legacy и current одновременно | Preflight error, перечислить конфликтующие requirements |
| Forge / Fabric mod | Соответствующий обнаруженный base loader и проверенный механизм загрузки внешних mods; одного названия base недостаточно |
| Несовместимый MC/Java/dependency range | Preflight error до spawn |
| Unknown generation / base contract | Compatibility selection либо explicit unsupported result; не угадывать и не downgrade автоматически |

Для Forge/Fabric ещё не доказано, что текущий Lunar принимает произвольные пользовательские mods. Их классификация обязательна; запуск поддерживать только после отдельной проверки соответствующего loader contract.

## 6. Предлагаемый immutable LaunchPlan

Настройки → inspection → snapshot artifacts/profile → resolver → preflight → frozen plan → backend execution. Execution не выбирает заново loader/Java/packages и не исправляет план на лету.

```text
LaunchPlan
  SchemaVersion, PlanId, ContractId, RuntimeSnapshotId
  MinecraftVersion, LunarProfileId, BaseLoader(id, version, evidence)
  Backend(ControlledGenesis | OfficialLunar), BackendCapabilities
  Java(mode, executable, runtimeRoot, version?, vendor?, architecture?, evidence,
       compatibilityResult)
  WorkingDirectory, GameDirectory, NativeArtifacts, AssetIndex
  ClassPath[], IchorClassPath[], IchorExternalFiles[]
  RequiredJvmArgs[], MemoryRuntimeArgs[], UserJvmArgs[]
  Agents[](id, artifact, role, enabled, order, options, orderingRequirements)
  Weave(strategy, artifact?, version?, generation?, requirements, modDirectory)
  EnabledPackages[], EnabledWeaveMods[]
  MainClass, GenesisGameArgs[], ChildEnvironmentPolicy
  ArtifactFingerprints[], Errors[], Warnings[], ArgumentProvenance[]
```

Использовать immutable collections и immutable вложенные записи. Canonical identity исключает timestamps/secrets; при одинаковых settings, requirements и artifact snapshot сериализация/порядок одинаковы. Перед spawn проверять, что artifacts не заменены; update между planning и execution означает rebuild plan, не запуск другой сборки под старым PlanId.

Classpath и external files — раздельные списки с provenance, без складывания всех JAR подряд. Runtime adapter интерпретирует проверенный Lunar layout/manifest; неизвестный Genesis revision не получает молча исторические filters LCQt. LauncherVersion, module flags, native extraction и mappings принадлежат versioned contract, не глобальным константам.

OfficialLunar сейчас не может честно заполнить executable/classpath/game args: это delegated unknown. Его compatibility projection явно фиксирует такие поля как unresolved и capabilities как unsupported/unverified; это не полноценный deterministic JVM plan. Нельзя декларировать применение Custom Java/args в fallback без доказанного механизма.

Sanitized diagnostic view содержит источники решений и последовательность, но не account/session arguments. Не читать accounts.json, не захватывать чужую token-bearing command line. Auth/provisioning status — только разрешённый официальный non-secret contract.

## 7. Java и пользовательские JVM args

Настройки advanced: `JavaRuntimeMode = Auto | Custom`, `CustomJavaExecutable`, memory min/max, `JvmArgumentsText` либо editable token list. Исходный текст сохраняется; определённая грамматика quotes/escaping преобразует его в аргументы без shell evaluation. Agent options/order хранятся структурированно.

Auto выбирает Java по требованиям конкретного runtime contract, MC и enabled packages: architecture, minimum classfile version, JVM capabilities, modules/options и проверенный supported range. Не выбирать просто первый/newest JRE по времени папки. Custom разрешает executable или runtime root → executable; отсутствующий/несовместимый runtime даёт явную ошибку, без скрытого перехода на Auto. `release` и bounded version probe дают version/vendor/architecture, если доступны; unknown отражается в preflight.

Финальный argv:

```text
required JVM args + memory/runtime args + user JVM args
+ ordered -javaagent flags + -cp <classpath>
+ Genesis main + Genesis/game arguments
```

Порядок premain задаётся dependency constraints (`before/after`) и стабильным пользовательским order. Цикл → ошибка. Для исследовательского LCQt baseline: native preparation → user agents → Weave; directory adapter, если нужен, обязательно до legacy loader. Это baseline конкретного contract, не универсальное правило всех агентов. Agent, который сам инициирует Mixin до Weave, требует отдельной проверки.

| Пользовательский аргумент | Preflight handling |
| --- | --- |
| `-Xms/-Xmx`, `-XX:InitialHeapSize/MaxHeapSize`, percentage heap controls | Свести aliases в одну memory model; duplicates/conflicts с managed memory → error с указанием обоих источников; предложить изменить memory settings. Проверить min ≤ max |
| `-cp`, `-classpath`, `--class-path`, `-Djava.class.path` | Error: classpath принадлежит plan; не применять last-wins |
| `-javaagent` | Error с предложением явного импорта в ordered-agent list; не молча удалять/переупорядочивать |
| `-Dweave.*`, managed natives/game/bootstrap properties | Конфликт с конкретным managed requirement → error; совпадающий duplicate → warning и явное resolution до freeze |
| `-Dfoo=bar` | Разрешить обычные properties с provenance; нынешний blanket запрет всех `-D` заменить |
| `-jar`, main-class tokens, module entry selectors, скрытый `@argfile` | Error: могут изменить launch entrypoint/скрыть agents и classpath; только отдельная inspectable expansion может разрешить argfile в будущем |
| Несовместимые GC/module/runtime options | Проверить по выбранному Java, конфликтующие значения → error; непроверяемые vendor options → явный warning |
| `-agentlib/-agentpath`, boot/module path overrides | Не считать обычными harmless flags; проверяемые native-agent/runtime requirements либо explicit unsupported error |

Inherited `JAVA_TOOL_OPTIONS`, `_JAVA_OPTIONS`, `JDK_JAVA_OPTIONS` могут нарушить план. Controlled child получает явную policy: показать конфликт и потребовать resolution/import; исключение из child environment отображается пользователю, системное окружение не меняется. Никакого silent discard. Для Official fallback неизвестные launch inputs остаются ограничением preflight.

## 8. Weave и граница backend

Две независимые оси: strategy `Off / Bundled / ExternalAgent` и generation/version requirement. Bundled означает управляемый Moonrise artifact, не обязательно latest; ExternalAgent — выбранный пользователем loader с inspectable metadata и capabilities. Off → ноль loader agents; active strategy → ровно один. Даже разные paths одного loader не разрешают двойную загрузку. Mixed-generation protection сохраняется.

Legacy directory contract — `~/.weave/mods`; текущий отдельный adapter меняет runtime `user.home` reference loader на `weave.dir`, не disk JAR. Это explicit loader-version dependency с bounded validation, не обязательная часть каждого legacy environment и не средство исправления Mixin. Не менять глобальный `user.home` ради mods.

Общие planning services независимы от UI. Backend boundary: `Capabilities`, validate/prepare, execute frozen compatible plan → handle точного созданного процесса, exit/status/cleanup. Provisioning — отдельная операция, а не скрытая фаза каждого Launch.

OfficialLunar действительно нужен сегодня для установки/обновления runtime artifacts, входа/reauth/ownership flow, создания отсутствующих profiles и fallback для непроверенных runtime layouts. Какие из этих функций требуют постоянного UI при direct launch, ещё не доказано. Исторический LCQt передаёт `accessToken=0`; это не основание отключать ownership либо переносить account tokens в Moonrise. Нужно проверить, что текущий official runtime сам обеспечивает разрешённый account flow без извлечения credentials. Если нет, ControlledGenesis остаётся gated для такого contract.

ControlledGenesis создаёт JVM напрямую и мониторит её handle/PID+start identity и доказанных descendants. Ему не нужны Lunar HWND hide/restore, Show Lunar, deeplink readiness и broad Electron lifecycle. Official backend сохраняет существующий путь до доказательства замены; fallback предлагается явно с причиной и ограничениями, без молчаливого повторного запуска после частичного старта JVM.

Рекомендация UX: после доказанного ControlledGenesis smoke deprecated «Show Lunar» как launch-control. Сохранить отдельное «Открыть Lunar» для обслуживания/входа; normal Launch не должен управлять чужими Lunar окнами. На этом этапе UI не меняется.

## 9. Переиспользование и удаляемые assumptions

Оставить: storage paths/cache bounds, `LocalPackageLibrary` и integrity, `JarMetadataParser` как основу, `PackageCompatibilityAnalyzer`, `WeaveAgentService`, `EnabledModDirectoryService`, launch sessions/cleanup, sanitized reports/logging. Переиспользовать read-only profile discovery; временную запись launcher.json оставить только внутри Official backend. Process tracker использовать с прямым ownership handle; native bridge/config/readiness/window services изолировать в Official backend.

Заменить: partial LaunchPlan на полный contract; фиксированный порядок loader-before-all-user-agents на constraints; metadata casing/modId heuristics на evidence; Agent-Class-as-premain; blanket raw `-D` rejection; обязательный legacy adapter вне зависимости от environment; recovery-only MC gate на scoped capability rule по мере проверенных contracts.

Убрать из generic launch resolution exact-hash successor/maintained-build substitution и SHA-specific BWH network selection (`IsBwhPackage` в MainWindow). Maintained builds, если сохранены как явно выбранные отдельные packages, имеют собственные requirements; integrity SHA остаётся, выбор поведения по SHA исчезает. Не заменять это filename whitelist. Существующие code paths в этой задаче не удалялись.

## 10. Три последовательных implementation stages

1. **Первый stage: pure package requirements + full plan/preflight contract.** Расширить inspection и immutable model, добавить Java Auto/Custom и user args/provenance/conflict resolver; snapshot fixture discovery и backend capability projection. Без JVM spawn, нового UI и package fixes. Existing Official launch не объявлять deterministic там, где inputs неизвестны.
2. **ControlledGenesis для одного проверенного Lunar/MC contract.** Read-only artifact discovery, Java resolution, точные JVM/Ichor/external inputs, native preparation и ordered agents; direct process ownership и cleanup. Проверить авторизацию/ownership без credentials extraction. Unknown runtime/update → explicit provisioning/fallback. Сначала clean/synthetic smoke, затем небольшой representative набор.
3. **Сделать direct backend предпочтительным после acceptance.** Нормальный UX import → classify → resolve → Launch; advanced Java/args/type/Weave/order controls. Official оставить provisioning/fallback, открыть Lunar отдельно, вывести Show Lunar и Electron window management из normal path. Forge/Fabric включать только для подтверждённых external-mod contracts.

Stage 1 acceptance fixtures: premain manifest и Agent-Class-only; agent options; несколько agents/order cycle; legacy/current Weave metadata и ambiguous modId; mixed generations; Forge/Fabric ranges; unknown/multi-marker/invalid JAR; missing dependency и wrong MC; Custom Java отсутствует/неподходящая architecture/classfile; heap aliases, reserved classpath/property, raw agent, quotes/paths with spaces, inherited environment conflict. Plan tests: identical inputs → identical frozen plan; disabled package excluded; loader once; artifact change invalidates snapshot; diagnostics redact secrets.

Поздний real smoke: clean Genesis и legacy loader без mods, один legacy mod, один current mod, один premain agent, один проверенный Forge/Fabric case; intact originals, unchanged accounts, game exit cleanup. Test fixtures не доказывают Lunar integration.

## 11. Статус проверки

Выполнены source/history research, локальная инвентаризация runtime metadata и JAR manifest/service/hash inspection. Создан только этот отчёт. Runtime rewrite, package fix, UI/site/payments edits, build/publish/merge/deploy/release и real Lunar/Minecraft smoke не выполнялись. Compatibility текущего Genesis с legacy Weave и UI-free authenticated launch остаются непроверенными; это acceptance gates stage 2, а не обещанные результаты.
