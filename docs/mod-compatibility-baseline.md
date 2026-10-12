# Local Mod Compatibility Baseline

Дата: 2026-10-09 (Asia/Omsk). Workspace: `%WORKSPACE%`; репозиторий `app`, ветка `codex/controlledgenesis-stage2`. Анализ текущих исходников, включая существующие незакоммиченные изменения, а не гарантия поведения установленного EXE.

## Границы и метод

Единственный созданный файл — этот отчёт. Minecraft, Moonrise и пользовательские JAR не запускались; JAR не декомпилировались. ZIP, manifest, JSON и constant pool/classfile headers читались Python в памяти. Все class entries проверены по версии; объявленные initializer, hook и Mixin classes существуют. Дубликатов ZIP entry names и вложенных JAR в шести Weave пакетах нет. Отсутствие таких ошибок не доказывает совместимость.

Источники: `%LOCALAPPDATA%\Moonrise\packages\{weave,agents}`, `packages\metadata\index.json`, метаданные `cosmetics`, текущие исходники и точечно сохранённые smoke evidence в `Moonrise\dev\logs`. Accounts, credentials и содержимое косметических protobuf не читались. Настройки и enabled states не изменялись. В index все 15 пакетов имеют `untested`; включён только BWH. SHA-256 всех 15 файлов совпадает с index.

Пользовательское уточнение: не создавать новые вспомогательные файлы/адаптеры для остальных пакетов. Существующие отдельные сборки BWH/unrestricted описаны как факт; рекомендации ниже не подразумевают создание дополнительных JAR или изменение системы.

Обнаружено **6 Weave модов, 9 startup Java agents**. `packages/unclassified` отсутствует, JAR там нет. Форматы: legacy Weave (`entrypoints`, либо hooks-only; API `net/weavemc/loader/api`), current Weave (`entryPoints`, `net/weavemc/api`), instrumentation agents с Premain-Class. У восьми сторонних агентов также Agent-Class; это не отдельные пакеты. Fabric/Forge metadata не обнаружена. Main-Class Fractal не делает его Java agent.

Обозначения: **S** — статический факт/путь в исходниках; **R** — ограниченное наблюдение старого реального запуска; **H** — гипотеза, требующая проверки. Java ниже — нижняя граница по classfile (major−44), не доказательство достаточности Java или доступности библиотек. Mixin `JAVA_8` не снижает Java 17 requirement самого мода.

## Матрица пользовательских пакетов

Для всех оригинальных Weave JAR `compiledFor` и точный диапазон версий loader не объявлены. Minecraft 1.8.9 — целевая среда Moonrise, а не доказанное metadata requirement каждого оригинала. Для агентов MC range также не объявлен: привязка к внутренним Lunar/Minecraft классам требует live проверки.

| Пакет / версия | Тип и требования (S) | Поддержка текущим Moonrise (S) | Прошлый smoke (R) | Ручная проверка |
|---|---|---|---|---|
| BWH, версия не объявлена | Legacy Weave; Java ≥17; 9 Mixins, 5 hooks; Kotlin/coroutines/SLF4J внутри JAR | Точное правило c39f → maintained 6da5, MC 1.8.9 / Weave 1.3.4; отдельный network agent + loopback relay | Для 6da5 одиночный запуск и combined: JVM exit 0, hash сохранён; это другой runtime artifact, не direct PASS c39f | Подтвердить применение правила, оба хеша, initializer, UI/функции, сеть/relay, Mixins и cleanup |
| fractal-weave-1.2; 1.2 только filename | Legacy Weave; Java ≥17; 6 Mixins | Legacy 0.2.6 путь; отдельного package-specific решения нет | Функциональное подтверждение не найдено | preInit/init, меню Fractal, rendering/network Mixins и функции в локальном мире |
| RavenWeave 1.1.4 (about.version) | Legacy Weave; Java ≥17; 11 required Mixins | Legacy 0.2.6 путь | Функциональное подтверждение не найдено | Инициализация клиента, required injections, GUI и отдельные функции |
| Stormy-1.0; 1.0 только filename | Legacy Weave; Java ≥17; 10 required Mixins, CPSHook | Legacy 0.2.6 + общие directory/mapping/service adapters | Exact 52ed: меню подтверждено пользователем, preInit returned, config registered, exit 0; late warning, ConfigPrepared=false, MixinsObserved=[] | Stormy.init, actual injection bytecode, CPSHook, функции; полнота работы не доказана |
| unrestricted; версия неизвестна | Legacy hooks-only; Java ≥11; 5 hooks; initializer/Mixin config не объявлены | Точное правило c9c9 → maintained adb8, MC 1.8.9 / Weave 1.3.4 | Функциональный PASS не найден | Все пять ограничений и native freelook maintained-сборки; StaffEnable overlap проверить отдельно |
| Veyra; filename 0, версия в JAR не объявлена | Current API; Java ≥17; namespace mcp-named; 17 required Mixins, hook, access widener | Weave 1.3.4 + существующий общий synthetic adapter; исходные байты сохраняются | Exact 7797 в combined с 6da5: exit 0 и сохранённый hash; старые linkage попытки не являются PASS | Полный Veyra.init после StartGameEvent.Post, Array List/Timer/SpeedMine, required injections |
| CrackedAccount; версия неизвестна | Java agent; Java ≥16; bundled ASM; Lunar/Minecraft references | Startup agent slot поддержан; account-related поведение не в контракте совместимости | PASS не найден | Статически разобрать точное вмешательство в account/ownership flow до ручного запуска; за рамками безопасного smoke данной задачи |
| CustomAutoGG; версия неизвестна | Java agent; Java ≥16; bundled ASM | Startup slot; Lunar layout и transform matching не проверены | PASS не найден | premain, регистрация transformer и фактическая настройка AutoGG; отсутствие дублирования сообщений |
| CustomLevelHead; версия неизвестна | Java agent; Java ≥16; bundled ASM | Startup slot; Lunar layout не проверен | PASS не найден | premain и отображение/настройки LevelHead в игре |
| FastPlace; версия неизвестна | Java agent; Java ≥16; external org.objectweb.asm/tree, не bundled | Slot есть; зависимость не обеспечивается автоматически | PASS не найден | Сначала доступность ASM в agent classloader; затем изменение задержки в локальном мире |
| LunarPerformance; версия неизвестна | Java agent; Java ≥16; external ASM; reference net/optifine/Config | Slot есть; ASM и OptiFine contract не доказаны | PASS не найден | ASM linkage, наличие нужного OptiFine layout, transform application, измеряемый эффект |
| Moonrise Cosmetics 1.3.0 (manifest) | Java agent; Java ≥8; Lunar-only; external Weave shaded ASM и Lunar protobuf v2 | Generic slot; Cosmetics-only не включает Weave автоматически | Один сохранённый JVM startup реально упал в CosmeticsAgent.premain из-за отсутствующего shaded ASM | Сначала dependency/classloader preflight; затем local unlock, outfit persistence/reconnect; подробности ниже |
| NickHiderLimitless; версия неизвестна | Java agent; Java ≥16; external ASM; lunar/ references | Slot есть; ASM не обеспечивается автоматически | PASS не найден | ASM linkage, NickHider GUI/лимиты и transform target |
| RemovePlus; версия неизвестна | Java agent; Java ≥16; bundled ASM | Startup slot; Lunar layout не проверен | PASS не найден | Реальное изменение визуального Plus и отсутствие побочных изменений |
| StaffEnable; версия неизвестна | Java agent; Java ≥16; bundled ASM | Startup slot; Lunar layout не проверен | PASS не найден | Target matching и нужная функция; совместимость с unrestricted StaffEnableHook |

## Сопоставление с engine и OfficialLunar

Источники: `src/Moonrise/Compatibility/{PackageInspector,CompatibilityPlanner}.cs`, `Services/{WeaveModApiInspector,CurrentLaunchPlanFactory,LaunchPlanBuilder,NativeBridgeLauncher,MaintainedCompatibilityBuildService}.cs`, `MainWindow.xaml.cs`.

PackageInspector распознаёт Premain-Class, проверяет существование entrypoint, читает Weave declarations/compiledFor/dependencies, проверяет current resources/Mixin classes, определяет Java и поколение API. Его лимиты — 4096 классов, 32 MiB class bytes, 2 MiB entry: BWH содержит 4160 классов, поэтому полный linkage этим inspector не доказан. Здесь class headers и pool просмотрены во всех оригинальных классах. Inspector не моделирует все внешние ASM зависимости, не доказывает сигнатуру premain, применимость transformers, действительность Lunar-only restriction, состояние cosmetics protobuf или все реальные конфликты. Raven about.version не читается как каноническое weave.version; отсутствие распознанной версии не равнозначно её отсутствию в JSON.

CompatibilityPlanner проверяет declared dependencies, MC ranges, типы/поколения, единственный loader, structured agent slots, дубли, hash snapshots и ordering constraints. Его результаты preflight не равны runtime PASS. OfficialLunar делегирует окончательную Java, classpath, game args и working directory официальному launcher. Custom Java/managed JVM memory/args не гарантированы; окончательный порядок относительно собственных Lunar agents/environment остаётся unverified. Нижнюю границу Java нужно сверять с фактически выбранной JVM.

Production OfficialLunar сначала применяет maintained-build resolution, затем API inspection **runtime JAR**. Поэтому BWH c39f и unrestricted c9c9 в обычном запуске становятся Current, хотя их оригиналы Legacy. Нельзя выводить production loader только по оригиналу. Проверены существование и точные SHA обеих maintained-сборок под `Moonrise/adapters/maintained-builds`. Без них либо с неверным hash запуск блокируется; оригиналы не переписываются. Для Fractal/Raven/Stormy правила замены отсутствуют. Legacy+Current runtime набор блокируется до запуска: например Stormy + Veyra или Stormy + maintained BWH/unrestricted. Это подтверждённый структурный конфликт пути, а не доказанный Minecraft crash.

Loader: current 1.3.4, hash `013b7b9a4ca01a473a1ffc0b031c843c70fe61616e9048fb326e03794c8457b7`; legacy 0.2.6. LaunchPlanBuilder ставит technical agents перед loader, package agents после него. NativeBridgeLauncher добавляет общие legacy mapping либо current synthetic components и сериализует MNR4 для официального процесса. Наличие loader раньше agent не доказывает доступность его shaded ASM в нужном classloader. При нуле Weave модов CurrentLaunchPlanFactory выбирает Disabled, даже если Cosmetics включён.

Maintained BWH metadata: `compiledFor=1.8.9`, `namespace=mcp-named`, initializer `lunarweavebridge.compat.BwhPreInitBridge`, `bwhelper.mixins.json`, пять hooks, tweaker `lunarweavebridge.compat.LoaderKickTweaker`, dependencies=[]; hash 6da5 ниже. Maintained unrestricted: `Lunar Unrestricted · Native Freelook`, modId `lwb-freelook`, initializer `lunarweavebridge.compat.FreelookPreInitBridge`, пять исходных hooks, Mixin configs=[], тот же tweaker/namespace/MC; hash adb8 ниже. Это отдельные локальные compatibility artifacts, не утверждение об upstream оригинальности или licensing.

## Реальные прошлые подтверждения и ограничения

Проверены result.json, а также ограниченные error markers в сохранённых JVM logs; backups настроек и account files не читались. Smoke docs описывают подготовку и могут отставать от последующих запусков.

- `dev/logs/legacy-weave-smoke-20261008-220944-ddfc0d4d168743538ab1711aab206941/result.json`: XSRG/service creation+selection/initComplete=true, menu confirmed-by-user, JVM exit 0. Доказывает loader baseline этого запуска.
- `dev/logs/legacy-stormy-smoke-20261008-222009-22bb0c31b31a4a9fa546743eb79cec77/result.json`: exact Stormy hash 52ed, discovery/preInit/config registration/menu/exit 0. ConfigPrepared=false, no observed Mixins, LateMixinWarning=true; StormyClientInit и MixinApplication оставлены pending. Не works-directly.
- `dev/logs/bwh-normal-smoke-20261009-000942-ee67cde43cd340b0bdcd0863065a9700/result.json`: exit 0, BWH runtime hash 6da5; Runtime=requires-user-and-log-review. Это свидетельство maintained runtime, а не функциональный PASS исходного c39f.
- `dev/logs/combined-weave-smoke-20261009-001415-fcae6df238584050ac9eeb2df87f7542/result.json`: exit 0; Veyra 7797 и BWH 6da5 after hashes; Runtime=requires-user-and-log-review. В директории пять разных launch reports: состав менялся (2/1/0 Weave; последний — 0 Weave и 1 package agent), встречаются minecraft-timeout и waiting-for-java. Result не покрывает все попытки.
- В этой же директории `jvm-18836-stderr.log`: `NoClassDefFoundError: net/weavemc/loader/impl/shaded/asm/ClassVisitor`, frame `moonrise.cosmetics.CosmeticsAgent.premain(CosmeticsAgent.java:45)`, cause ClassNotFoundException. Это **реальная ошибка Cosmetics premain**, не гипотеза и не доказательство BWH/Veyra конфликта. Без однозначной связи PID с launch report и hash этого конкретного agent нельзя приписывать ошибку точно нынешнему BFDC бинарнику или всем комбинациям. Нынешний бинарник статически содержит ту же внешнюю зависимость.
- Veyra поздние executable-remap logs содержат feature markers, но без подтверждения функций/exports они не принимаются за PASS. Отсутствие выбранных exception markers также не доказательство полного отсутствия ошибок.

## Moonrise Cosmetics

Текущий файл: `packages/agents/Moonrise-Cosmetics.jar`, manifest Implementation-Version 1.3.0 / Moonrise-Clients=lunar / Premain-Class=moonrise.cosmetics.CosmeticsAgent. Восемь class entries, major 52. Нет bundled ASM, Weave mod metadata, Mixin config или retransformation capability; Can-Redefine-Classes=false и Can-Retransform-Classes=false.

По `extensions/Moonrise.Cosmetics/src` и строкам бинарника premain задаёт local visibility и регистрирует Unlock, Persistence, Stability и DebugConfiguration transformers. Unlock меняет Lunar v2 LoginResponse getter `getHasAllCosmeticsFlag`; DebugConfiguration ищет production configuration по markers/структуре. Persistence внедряет self-contained template в `com/lunarclient/websocket/cosmetic/v2/CosmeticService$Stub`, захватывает updateOutfit/selectOutfit protobuf requests и объединяет outfit с ответом cosmetics login через protobuf reflection/RpcCallback. Stability сохраняет stores при disconnect и изменяет reconnect delay/backoff по protocol/log markers. Это зависит от актуального Lunar v2 layout, Genesis classloader, наличия protobuf методов и порядка определения классов. Неизвестный layout может оставить transform без эффекта. Готовый proof marker не равен успешному применению всех transformers.

Services остаются official: CosmeticsServiceRoute очищает serviceOverrideAuthenticator/AssetServer/Api/Styngr JVM properties. Не обещает распространение косметики другим игрокам. Нет отдельной launcher UI интеграции Cosmetics в текущих src/Moonrise: подключается обычным package-agent slot. README с `user-agents` описывает старый output convention; фактическое пользовательское хранение — packages/agents.

Build script компилирует against pinned Weave 1.3.4 с `--release 8`, но не помещает ASM в Cosmetics JAR. Runtime references: `net/weavemc/loader/impl/shaded/asm/*`. Engine не выводит из них обязательную dependency на Weave, а Cosmetics-only план loader не включает. Реальная ошибка выше подтверждает, что standalone запуск нельзя считать готовым; наличие loader в комбинации тоже требует проверки classloader visibility. Новых dependency JAR или adapters здесь не предлагается создавать.

Локальная persistence: `moonrise.cosmetics.stateDir` override, иначе `%LOCALAPPDATA%/Moonrise/cosmetics`, fallback `~/.moonrise/cosmetics`. Файлы `updateOutfit.pb`, `selectOutfit.pb`, временные `*.pb.tmp`; максимум payload 1 MiB, atomic move с fallback replace. Сейчас найден только **updateOutfit.pb, 135 bytes**. Содержимое не читалось; наличие не доказывает валидность, успешный replay или работу версии 1.3.0. Proof files при moonrise.proofDir: cosmetics-agent.ready, cosmetics-debug-class.txt, cosmetics-persistence-class.txt; не являются обязательными пользовательскими данными.

В Minecraft проверить: успешный premain и все нужные transforms; открытие cosmetics UI и локальный render модели; выбор outfit, сохранение и восстановление после повторного запуска; reconnect без сброса/дубликатов; отсутствие regression официальных services; нормальный выход. Изменение outfit закономерно записывает cosmetics state: это только будущий ручной тест с разрешением пользователя, в текущем baseline данные не менялись. Accounts проверяются пользователем визуально, без чтения account files.

## Проблемы и гипотезы

1. **S/R:** скрытые ASM dependencies: FastPlace/LunarPerformance/NickHiderLimitless требуют unshaded org.objectweb.asm/tree, отсутствующий в их JAR; Cosmetics требует отдельного shaded namespace, в старом запуске реально не найденного. Достаточность Lunar classpath неизвестна; не подставлять bundled ASM другого агента как доказанный контракт.
2. **S:** смешивать legacy Fractal/Raven/Stormy с current Veyra/maintained BWH/unrestricted текущий pipeline запрещает. Пять legacy оригиналов не означают пять production legacy runtimes.
3. **R:** Stormy late Mixin warning и неполное применение остаются открытыми; menu/preInit не принимаются за functional compatibility.
4. **S:** original BWH filename `patched-v2-api` вводит в заблуждение: bytes c39f всё ещё Legacy; smoke использовал отдельный 6da5. Проверять source/runtime hashes раздельно.
5. **H:** общие Minecraft/network/player/render targets у нескольких Weave mods могут конфликтовать; сравнение config names недостаточно. StaffEnable и unrestricted StaffEnableHook могут пересекаться. Bundled unrelocated ASM у пяти onils agents может создавать classpath/version collision при совместном использовании; collision не доказан.
6. **S/H:** LunarPerformance имеет OptiFine Config reference; требуемый конкретный layout неизвестен. CrackedAccount вмешивается в account-related область по именам и references; точный эффект не установлен, запуск без отдельного статического разбора не входит в предлагаемый smoke.
7. **S:** MC ranges почти отсутствуют, точные loader version ranges отсутствуют; требования по API поколению и Java не заменяют runtime contract. Generic agent acceptance не решает внутреннюю Lunar obfuscation/layout drift.

## Короткий порядок ручных smoke

Эти действия в текущей задаче не выполнялись. Не использовать автоматически старые smoke scripts: они меняют enabled/settings и некоторые создают diagnostic files. Не создавать новые вспомогательные JAR для остальных пакетов; для evidence использовать уже существующую штатную диагностику.

1. Чистый OfficialLunar 1.8.9 без пользовательских пакетов как контроль; фиксировать фактическую JVM, время старта, exit и cleanup. Затем **CustomAutoGG.jar**, **CustomLevelHead.jar**, **fractal-weave-1.2.jar** по одному — первые 2–3 новых кандидата. У первых двух bundled ASM, у Fractal проверяется legacy путь с уже имеющимся loader baseline. Проверять конкретную функцию, не только меню.
2. Raven отдельно, Stormy с проверкой оставшихся Mixins, maintained BWH отдельно, Veyra отдельно, maintained unrestricted отдельно. Для BWH/unrestricted фиксировать исходный и effective runtime SHA. Затем RemovePlus/StaffEnable отдельно. FastPlace/LunarPerformance/NickHider — только после подтверждения существующего ASM visibility и, где применимо, OptiFine contract; при missing dependency отметить blocked, не создавать новый helper. CrackedAccount отложить до отдельного разбора.
3. После одиночных функциональных PASS — legacy пары (Fractal+Stormy, затем Raven), current BWH+Veyra и unrestricted. Не запускать заведомо запрещённые mixed-generation наборы. Затем один прошедший agent + один mod, затем ограниченные пары agents. StaffEnable+unrestricted выделить отдельным пересечением.
4. Cosmetics сначала dependency/preflight, затем standalone только если текущая среда действительно обеспечивает ASM; при воспроизведении premain crash остановиться. После успешного подключения — комбинация с прошедшим current модом, затем BWH+Cosmetics, Veyra+Cosmetics и совместный набор. Сохранение/replay/reconnect проверяются отдельно от unlock.

Для каждого случая: exact source/runtime hashes, profile/version, effective loader/agents, initializer/transform evidence, функциональное наблюдение, ошибка и её стадия либо PASS функции, normal exit, cleanup. Разделять NOT RUN, BLOCKED, FAIL, PARTIAL и functional PASS. Не считать exit 0 или usable window PASS. Не собирать raw token-bearing command lines или account data.

## Воспроизводимые идентификаторы и declarations

Следующие данные получены непосредственно из оригинальных пользовательских JAR. Версии по filename отдельно отмечены в матрице; пустые metadata dependencies означают отсутствие декларации, а не отсутствие runtime dependencies.

### agents/CrackedAccount.jar

- Размер: 182017 bytes; class major counts: `{60: 8, 49: 75}`.
- SHA-256: `70daeaddc1d7ceafa95f0f1e1043b2cf10881b294ba443543296963445645b82`.

- Manifest: `Manifest-Version: 1.0`; `Can-Retransform-Classes: true`; `Premain-Class: me.onils.crackedaccount.Agent`; `Agent-Class: me.onils.crackedaccount.Agent`.

- Premain class entry существует: `me/onils/crackedaccount/Agent.class`; Weave/Mixin metadata отсутствует.

### agents/CustomAutoGG.jar

- Размер: 177718 bytes; class major counts: `{60: 5, 49: 75}`.
- SHA-256: `d6db746986aeb17bee3d9a4cdf48c5579b0d5a7e7474e95f718601665e5a21dd`.

- Manifest: `Manifest-Version: 1.0`; `Can-Retransform-Classes: true`; `Premain-Class: me.onils.customautogg.Agent`; `Agent-Class: me.onils.customautogg.Agent`.

- Premain class entry существует: `me/onils/customautogg/Agent.class`; Weave/Mixin metadata отсутствует.

### agents/CustomLevelHead.jar

- Размер: 177606 bytes; class major counts: `{60: 5, 49: 75}`.
- SHA-256: `465fabf60b61ec2a6d1554e51d72db5ad9bb3ab54c9a0f237968323f7ef37927`.

- Manifest: `Manifest-Version: 1.0`; `Can-Retransform-Classes: true`; `Premain-Class: me.onils.customlevelhead.Agent`; `Agent-Class: me.onils.customlevelhead.Agent`.

- Premain class entry существует: `me/onils/customlevelhead/Agent.class`; Weave/Mixin metadata отсутствует.

### agents/FastPlace.jar

- Размер: 3486 bytes; class major counts: `{60: 2}`.
- SHA-256: `c1c8c2a952d5139081d68fca7d7d688eec7506c7728f9862b88666d77fde5ff4`.

- Manifest: `Manifest-Version: 1.0`; `Agent-Class: me.youded.fastplace.Agent`; `Premain-Class: me.youded.fastplace.Agent`; `Can-Retransform-Classes: true`.

- Premain class entry существует: `me/youded/fastplace/Agent.class`; Weave/Mixin metadata отсутствует.

### agents/LunarPerformance.jar

- Размер: 4558 bytes; class major counts: `{60: 2}`.
- SHA-256: `a60e3f4014ec8e6c2b853aada490b6ee27196129e8791a847557f50e388910ae`.

- Manifest: `Manifest-Version: 1.0`; `Agent-Class: me.youded.LunarPerformance.Agent`; `Premain-Class: me.youded.LunarPerformance.Agent`; `Can-Retransform-Classes: true`.

- Premain class entry существует: `me/youded/LunarPerformance/Agent.class`; Weave/Mixin metadata отсутствует.

### agents/Moonrise-Cosmetics.jar

- Размер: 18657 bytes; class major counts: `{52: 8}`.
- SHA-256: `bfdcfc1e5e00bb922708885ce35159db0b1e3de989240bf6f98bd1f269b55ab6`.

- Manifest: `Manifest-Version: 1.0`; `Premain-Class: moonrise.cosmetics.CosmeticsAgent`; `Implementation-Title: Moonrise Cosmetics`; `Implementation-Version: 1.3.0`; `Can-Redefine-Classes: false`; `Can-Retransform-Classes: false`; `Moonrise-Clients: lunar`.

- Premain class entry существует: `moonrise/cosmetics/CosmeticsAgent.class`; Weave/Mixin metadata отсутствует.

### agents/NickHiderLimitless.jar

- Размер: 3575 bytes; class major counts: `{60: 2}`.
- SHA-256: `3d2558990fb823106810498e3cb3e46f16619c4748f69c4956ab661273ea3586`.

- Manifest: `Manifest-Version: 1.0`; `Agent-Class: me.youded.nickhiderlimitless.Agent`; `Premain-Class: me.youded.nickhiderlimitless.Agent`; `Can-Retransform-Classes: true`.

- Premain class entry существует: `me/youded/nickhiderlimitless/Agent.class`; Weave/Mixin metadata отсутствует.

### agents/RemovePlus.jar

- Размер: 178167 bytes; class major counts: `{60: 5, 49: 75}`.
- SHA-256: `b2191e0b2a39940178287344a2a5a135e357bffd62de7c3facb6c1cfcc18853e`.

- Manifest: `Manifest-Version: 1.0`; `Can-Retransform-Classes: true`; `Premain-Class: me.onils.removeplus.Agent`; `Agent-Class: me.onils.removeplus.Agent`.

- Premain class entry существует: `me/onils/removeplus/Agent.class`; Weave/Mixin metadata отсутствует.

### agents/StaffEnable.jar

- Размер: 179239 bytes; class major counts: `{60: 5, 49: 75}`.
- SHA-256: `e2d91265cb56afcf6d4ac691fbea68c8daeaf50d6d37ed6717fc2bd4d6ab74f2`.

- Manifest: `Manifest-Version: 1.0`; `Can-Retransform-Classes: true`; `Premain-Class: me.onils.staffenable.Agent`; `Agent-Class: me.onils.staffenable.Agent`.

- Premain class entry существует: `me/onils/staffenable/Agent.class`; Weave/Mixin metadata отсутствует.

### weave/bwh-march-14-weave-patched-v2-api.jar

- Размер: 14997233 bytes; class major counts: `{61: 144, 52: 4015, 53: 1}`.
- SHA-256: `c39f6f1c891825cd309f543d1ee112522cac3660ac60d9e96a5a21f20756752d`.

- Manifest: `Manifest-Version: 1.0`.


```json
{
  "entrypoints": [
    "xyz.mangal.bwhelper.BWHelper"
  ],
  "mixinConfigs": [
    "bwhelper.mixins.json"
  ],
  "hooks": [
    "xyz.mangal.bwhelper.asm.WorldTransformer",
    "xyz.mangal.bwhelper.asm.MinecraftTransformer",
    "xyz.mangal.bwhelper.asm.ItemBucketMilkTransformer",
    "xyz.mangal.bwhelper.asm.GuiChatTransformer",
    "xyz.mangal.bwhelper.asm.MinecraftTransformer2"
  ]
}
```


Mixin config `bwhelper.mixins.json`:

```json
{
  "compatibilityLevel": "JAVA_8",
  "package": "xyz.mangal.bwhelper.mixins",
  "mixins": [
    "AbstractClientPlayerMixin",
    "EntityLivingBaseMixin",
    "EntityPlayerMixin",
    "FontRendererMixin",
    "GuiIngameMixin",
    "LayerCapeMixin",
    "MinecraftMixin",
    "NetworkManagerMixin",
    "S1DPacketEntityEffectMixin"
  ]
}
```

### weave/fractal-weave-1.2.jar

- Размер: 3525099 bytes; class major counts: `{61: 118}`.
- SHA-256: `f05c74fec2305e64e58ccb1fed7d6b7b31be2444876c59bf7ba7eff7e404a6b4`.

- Manifest: `Manifest-Version: 1.0`; `Main-Class: xyz.flapjack.container.Container`.


```json
{
  "mixinConfigs": [
    "fractal.mixins.json"
  ],
  "entrypoints": [
    "xyz.flapjack.fractal.Fractal"
  ]
}
```


Mixin config `fractal.mixins.json`:

```json
{
  "compatibilityLevel": "JAVA_8",
  "package": "xyz.flapjack.fractal.events.mixin",
  "mixins": [
    "MixinRenderLivingEntity",
    "MixinNetworkManager",
    "MixinEntityPlayerSP",
    "MixinEntityRenderer",
    "MixinGuiInGame",
    "MixinMinecraft"
  ]
}
```

### weave/RavenWeave-1.1.4.jar

- Размер: 1494383 bytes; class major counts: `{61: 251}`.
- SHA-256: `c6edd2979121df4f7c2585abee8da0b84e3b7b5496b0b1a4a684edad179727a1`.

- Manifest: `Manifest-Version: 1.0`.


```json
{
  "mixinConfigs": [
    "ravenweave.mixins.json"
  ],
  "entrypoints": [
    "me.pianopenguin471.RavenWeave"
  ],
  "about": {
    "name": "RavenWeave",
    "version": "1.1.4",
    "author": "PianoPenguin471",
    "description": "Raven B++ ported to Weave and improved",
    "website": "https://github.com/PianoPenguin471/RavenWeave"
  }
}
```


Mixin config `ravenweave.mixins.json`:

```json
{
  "required": true,
  "package": "me.pianopenguin471.mixins",
  "compatibilityLevel": "JAVA_8",
  "mixins": [
    "EntityMixin",
    "EntityPlayerMixin",
    "EntityPlayerSPMixin",
    "EntityRendererMixin",
    "MinecraftMixin",
    "NetworkManagerMixin",
    "PlayerControllerMPMixin",
    "RendererLivingEntityMixin",
    "RenderGlobalMixin",
    "IS12PacketEntityVelocity",
    "WorldMixin"
  ]
}
```

### weave/Stormy-1.0.jar

- Размер: 212361 bytes; class major counts: `{61: 127}`.
- SHA-256: `52ed9da4c4f570fd43b32671e0d94fa5fc9e441168645d2e3de054169374b021`.

- Manifest: `Manifest-Version: 1.0`.


```json
{
  "mixinConfigs": [
    "stormy.mixins.json"
  ],
  "entrypoints": [
    "me.tryfle.stormy.Main"
  ],
  "hooks": [
    "me.tryfle.stormy.hooks.CPSHook"
  ]
}
```


Mixin config `stormy.mixins.json`:

```json
{
  "required": true,
  "compatibilityLevel": "JAVA_8",
  "package": "me.tryfle.stormy.mixins",
  "mixins": [
    "EntityLivingBaseMixin",
    "EntityPlayerMixin",
    "EntityPlayerSPMixin",
    "IS12PacketEntityVelocity",
    "MinecraftMixin",
    "MixinEntityPlayerSP",
    "NetworkManagerMixin",
    "RendererLivingEntityMixin",
    "RenderGlobalMixin",
    "IMixinMinecraft"
  ]
}
```

### weave/unrestricted.jar

- Размер: 10565 bytes; class major counts: `{55: 5}`.
- SHA-256: `c9c9dc9e75e37291e2992ca7fbd0ff6eb851b811391d9c938dec14ab73315c05`.

- Manifest: `Manifest-Version: 1.0`.


```json
{
  "hooks": [
    "me.youded.unrestricted.hooks.StaffEnableHook",
    "me.youded.unrestricted.hooks.BlacklistEnableHook",
    "me.youded.unrestricted.hooks.ReachUnrestrictHook",
    "me.youded.unrestricted.hooks.PollingrateUnrestrictHook",
    "me.youded.unrestricted.hooks.TexturePackUnrestrictHook"
  ]
}
```

### weave/Veyra-0.jar

- Размер: 1054056 bytes; class major counts: `{61: 332}`.
- SHA-256: `779760392bca1f269d0eb325e78950249d355e94a09c90988108c3ccd94d95bd`.

- Manifest: `Manifest-Version: 1.0`.


```json
{
  "name": "Veyra",
  "modId": "veyra",
  "entryPoints": [
    "dev.veyra.weave.Main"
  ],
  "mixinConfigs": [
    "veyra.mixins.json"
  ],
  "hooks": [
    "dev.veyra.weave.mixins.ExperimentalTimerFreezeMixin"
  ],
  "accessWideners": [
    "veyra.accesswidener.txt"
  ],
  "namespace": "mcp-named"
}
```


Mixin config `veyra.mixins.json`:

```json
{
  "required": true,
  "compatibilityLevel": "JAVA_8",
  "package": "dev.veyra.weave.mixins",
  "mixins": [
    "BridgeMovementMixin",
    "EntityLivingBaseMixin",
    "EntityPlayerMixin",
    "EntityPlayerSPMixin",
    "EntityRendererMixin",
    "IC03PacketPlayer",
    "IS14PacketEntity",
    "IS12PacketEntityVelocity",
    "MinecraftMixin",
    "MixinEntityPlayerSP",
    "NetworkManagerMixin",
    "RendererLivingEntityMixin",
    "RenderGlobalMixin",
    "IMixinMinecraft",
    "ClutchMinecraftAccess",
    "ClutchMouseInputMixin",
    "SpeedMineMixin"
  ]
}
```


Access widener: `veyra.accesswidener.txt`, ресурс присутствует.


Maintained runtime identities (не входят в 15 originals):

- BWH: `6da5b2a67e05e07deae2779b4aa3d1b70e5028ded227c41487ff783f5d609a57`.
- unrestricted: `adb858f75da30eace656d380021dca0fd0a047ed6aabdaaa68f758f7965a4833`.

## Проверка выполнения этой задачи

Повторное чтение SHA-256 после анализа: все 15 originals без изменений; каждый совпадает с packages metadata index. Изменён только этот Markdown отчёт; прежняя незакоммиченная работа сохранена. Build/full-suite/smoke/push/merge/release не выполнялись: runtime/code не изменялись. Результат — статическая baseline с явно ограниченными предыдущими runtime evidence, не сертификация совместимости.
