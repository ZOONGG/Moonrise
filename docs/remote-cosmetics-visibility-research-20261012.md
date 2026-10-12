# Moonrise — Remote Cosmetics Visibility Research

Дата: 12 октября 2026. Результат: **NOT VERIFIED**.

В исследованных реализациях не найден воспроизводимый способ передавать локально разблокированную Moonrise косметику реальному игроку на другом немодифицированном Lunar без обхода проверок. Это не доказательство математической невозможности и не отрицательный результат проведённого multiplayer-теста: два клиента здесь не запускались. Условие для начала реализации не выполнено.

## Что проверено

- Исходники `extensions/Moonrise.Cosmetics`, manifest 1.3.1 и bytecode существующего `runtime/main-cosmetics-build/moonrise-cosmetics-1.3.1.jar`.
- Структура protobuf и отдельные обработчики в установленном `%USERPROFILE%\.lunarclient\offline\multiver\lunar.jar`, исключительно чтением ZIP и `javap`. Клиентские классы не исполнялись.
- Публичные исходники и документация перечисленных ниже проектов на закреплённых commits; поиск других публичных реализаций.
- [Статические свидетельства](remote-cosmetics-static-evidence-20261012.txt): реальные имена сообщений, getters, RPC и ссылки обработчиков. Это свидетельства схемы и реализации клиента, не дамп сетевого обмена.

SHA-256 проверенных файлов:

| Файл | SHA-256 |
|---|---|
| Moonrise Cosmetics 1.3.1 | `737837D9EF979D6864444620B306D5597AB0D2FC9AA823E0A268CD6F39777FD2` |
| Установленный lunar.jar | `F4A6A62DF2D548906F272D2D0DC906B6CAAE6F3592F9E2DAE918783324BF3CCA` |

Версия Lunar не подменяется придуманным номером: вывод ограничен указанным hash. Исходники уже имели пользовательские изменения до исследования. Они сохранены; агент, JAR, production и пользовательские данные не менялись. Добавлены только этот отчёт и статические свидетельства. Сборка, установка, RPC-запросы и двухклиентный smoke test не выполнялись.

## Локальный unlock и remote visibility

Локальный unlock меняет то, что один процесс считает доступным или экипированным. Даже успешный рендер в F5, сохранение outfit после перезапуска и ответ `STATUS_OK` на отправителе сами по себе не доказывают отображение у наблюдателя.

Remote visibility требует, чтобы независимый клиент B получил данные для настоящего UUID игрока A через поддерживаемый им канал, разрешил cosmetic IDs в доступные ресурсы и показал их на A. Установка агента только на A не меняет обработчики, каталог и настройки B.

### Moonrise 1.3.1

1. `CosmeticsUnlockTransformer` переписывает getter `com/lunarclient/websocket/cosmetic/v2/LoginResponse.getHasAllCosmeticsFlag(): boolean` на возврат `true`. Это изменение объекта внутри JVM; оно не выдаёт владение в Lunar backend.
2. `CosmeticsServiceRoute` очищает service overrides Authenticator/AssetServer/API/Styngr. Клиент продолжает использовать официальные сервисы; собственный сервер не становится источником данных для чистого B.
3. `CosmeticsPersistenceTransformer` добавляет захват `updateOutfit` и `selectOutfit` и обёртку login callback. `CosmeticsPersistenceTemplate` сохраняет protobuf локально и объединяет сохранённые Outfit/OutfitTree с LoginResponse перед передачей callback. «Replay» здесь — восстановление локального ответа login, а не подтверждённая повторная регистрация outfit на сервере. Исходный отправляющий RPC код сохраняется; факт отправки не означает принятие или рассылку сервером.
4. `CosmeticsStabilityTransformer` сохраняет локальные stores при disconnect и меняет reconnect delays. Это повышает устойчивость локального отображения, не добавляет публикацию для B.
5. `CosmeticsAgent` прямо выставляет `moonrise.cosmetics.visibility=local`. Проверка bytecode готового JAR подтверждает наличие этих transformers и локальной маркировки.

В этой реализации нет отдельной системы доставки новых моделей Moonrise чистым клиентам. Текущий unlock работает с косметикой Lunar; для произвольной новой модели потребовалось бы ещё доказать получение её ресурсов и поддержку идентификатора клиентом B.

## Получение и отображение в текущем Lunar

Подтверждено статически в установленном JAR:

| Направление / назначение | Сообщения и поля |
|---|---|
| Login косметики | `cosmetic.v2.CosmeticService.login(LoginRequest, LoginResponse)`; owned cosmetics, has-all flag, outfits, outfit tree, plus colors, ownership visibility |
| Изменение outfit | `createOutfit`, `updateOutfit`, `deleteOutfit`, `selectOutfit`; UpdateOutfitResponse содержит status и outfit, SelectOutfitResponse — status |
| Получение данных других UUID | `subscription.v1.SubscriptionService.subscribeV2`; SubscribeV2Request содержит target UUIDs, SubscribeV2Response — cosmetic pushes |
| Данные игрока | `cosmetic.v2.PlayerCosmeticsPushV2`: player UUID, default/conditional cosmetics, outfit UUID, badge/logo/plus fields |
| Экипированный предмет | `EquippedCosmetic`: cosmetic ID и параметры отображения, включая cloth cloak, поверх брони/skin layer, height offset и metadata |
| Обновление состояния | `RefreshCosmeticsPush` |

Обработчик подписок перебирает `SubscribeV2Response.getCosmeticPushesList()` и передаёт `PlayerCosmeticsPushV2` в клиентскую цепочку. Обработчик cosmetics читает UUID и передаёт push менеджерам косметики/связанного состояния. Менеджер читает default/conditional cosmetics и IDs. В bytecode также присутствует загрузка индексов ресурсов косметики, shader textures и morphs. Подробная работа каждого render layer и весь transport envelope здесь не реконструировались.

Рабочая схема, поддержанная этими наблюдениями: официальный косметический сервис → ответы подписки/push для UUID → stores/менеджеры на B → разрешение IDs в ресурсы → рендер. Обычный Minecraft-сервер предоставляет мир и сущности, но не становится автоматически официальным CosmeticService.

**Граница доказательств:** LoginResponse показывает, что backend сообщает владение и outfits; push показывает, что клиент умеет принимать состояние другого UUID. Серверный исходный код Lunar недоступен в исследованных проектах. Точное правило проверки владения, фильтрации не принадлежащих аккаунту IDs, момент рассылки и сетевые ошибки не наблюдались. Нельзя утверждать, что конкретный непринадлежащий ID был отвергнут, либо что он был принят. `UpdateOutfitResponse` и `SelectOutfitResponse` имеют только STATUS_UNSPECIFIED/STATUS_OK в просмотренной схеме; это не свидетельство отсутствия серверной проверки — ошибка может идти через транспорт или другое поведение.

У B также должны быть включены соответствующие категории косметики. Даже официальный механизм не даёт буквальной гарантии «у всех», если наблюдатель отключил рендер. [Официальные настройки косметики](https://www.lunarclient.com/news/how-to-equip-cosmetics-on-lunar-client).

## Открытые реализации

| Проект / закреплённый commit | Фактический механизм | Доказательство чистого второго клиента |
|---|---|---|
| [lcqt2](https://github.com/Nilsen84/lcqt2/tree/4f29af000a6430a09b8dfe740d78699292c2add4) | Hook protobuf RPC send/receive; локальное дополнение login; удаление selected IDs из исходящего update | Не найдено; исходящий запрос специально очищается |
| [ordzy/LCT](https://github.com/ordzy/LCT/tree/a1323d10195dab14faa9de4cee228255c5dcef99) | Проверенный Proxy.kt текстуально совпадает с lcqt2 | Не найдено |
| [lunar-client-agents](https://github.com/Nilsen84/lunar-client-agents/tree/9169a0dd9959cb41f21ebd6ede3b26e3c6fd16ce) | В полном дереве этого commit отсутствует cosmetics unlocker; набор других agents | Не найдено |
| [Lunar-Client-Unlockall](https://github.com/SmtimesIWndr/Lunar-Client-Unlockall/tree/b8df59e39cfdb33ca3a3e9410115fb96faf068dd) | Java instrumentation getters login flags, локальных colors/prefs и response behavior | Не найдено; patch входящего push на своём клиенте не меняет push у B |
| [Apollo](https://github.com/LunarClient/Apollo/tree/32079b41308873ed5344f9e169e2dece3f44da3a) | Официальные серверные messages для cosmetics NPC, emotes и sprays | Документирован иной сценарий, не экипировка настоящего игрока Moonrise |

Главное доказательство для lcqt2 — [Proxy.kt](https://github.com/Nilsen84/lcqt2/blob/4f29af000a6430a09b8dfe740d78699292c2add4/agent/src/main/kotlin/io/github/nilsen84/lcqt/cosmetics/Proxy.kt). На `lunarclient.websocket.cosmetic.v1.CosmeticService.UpdateCosmeticSettings` он сохраняет active IDs в локальный config и очищает active IDs/plus color перед отправкой. На Login он заменяет owned IDs содержимым локального asset index и восстанавливает выбор из config. Аналогично очищает equipped emotes на отправке и дополняет локальный login. [CosmeticsPatch.kt](https://github.com/Nilsen84/lcqt2/blob/4f29af000a6430a09b8dfe740d78699292c2add4/agent/src/main/kotlin/io/github/nilsen84/lcqt/patches/CosmeticsPatch.kt) устанавливает эти hooks в RpcChannel. Это старый v1 протокол, не готовый адаптер к текущему v2.

Старый [lunar-client-qt](https://github.com/Nilsen84/lunar-client-qt/tree/7c71733e320f851c4418860f7a10a953d5f7c998) также содержит бинарный `UnlockCosmetics.jar`; его наличие не считается доказательством remote visibility. Бинарник не запускался. Поиск README/открытых исходников не дал документированного теста A с агентом → B без агента с подтверждённым рендером. Непроверяемые заявления и сообщения форумов в качестве доказательств не используются.

### Почему Apollo не решает цель

[Официальная документация Cosmetic Module](https://lunarclient.dev/apollo/developers/private-modules/cosmetic) описывает `EquipNpcCosmeticsMessage`, unequip/reset, emotes NPC и sprays. Модуль private: доступ должен предоставить Lunar. NPC UUID обязан иметь least significant bits равными нулю; клиент отвергает cosmetics packets для другого формата. В открытом `CosmeticModuleImpl` действительно формируется EquipNpcCosmeticsMessage с NPC UUID и cosmetic IDs.

Это поддерживаемая серверная интеграция в пределах выданного доступа. Она не документирует выдачу outfit реальному Minecraft UUID и не обещает глобальную видимость на любых серверах. Подмена UUID игрока NPC-UUID была бы другим сценарием, не проверкой поставленной цели; она не предлагается. Private access не обходился.

## Минимальный тест с двумя независимыми клиентами

Тест пока **не выполнен**. Для его проведения нужны два официально авторизованных разных Minecraft-аккаунта и два независимых окружения; предпочтительно два ПК. Сервер — обычный тестовый vanilla 1.8.9-compatible сервер с `online-mode=true`, без cosmetics plugin, proxy и подмен UUID. Зафиксировать версию сервера и hash Lunar на обоих ПК.

1. B запускает официальный Lunar из официального launcher без Moonrise, agents, Weave, service overrides и изменённых ресурсов. Проверка состава запуска должна исключать сохранение token-bearing command line. Включить нужные категории cosmetics.
2. Положительный контроль: A также запускает чистый Lunar и экипирует уже официально принадлежащий его аккаунту предмет. B делает снимок/видео A на сервере, после relog повторяет наблюдение. Если предмет не виден, сначала восстановить обычную работу Lunar; опыт невалиден для оценки Moonrise.
3. A запускает имеющийся неизменённый Moonrise 1.3.1 в отдельном тестовом окружении и выбирает **тот же принадлежащий ему предмет** штатным UI. B остаётся чистым. Снять A на экране B, повторить после relog A/B и перезапуска A. Пользовательское рабочее outfit-хранилище не использовать для этого опыта.
4. Записать: build hashes, состав клиентов без секретов, cosmetic ID/название, был ли предмет официально выдан A, время действия/наблюдения, результат B и контроль. Скриншот F5 или locker на A не засчитывается. Не добавлять в B диагностический агент: это нарушит условие чистого наблюдателя.
5. Положительный результат шага 3 доказывает совместимость Moonrise с **официально разрешённой** косметикой. Он не доказывает распространение локального unlock на непринадлежащие предметы или новые Moonrise-модели.

Дополнительная проверка произвольной Moonrise косметики допустима только если Lunar официально предоставит тестовое entitlement/каталог или авторизованный integration path. При отказе/ограничении остановиться, не обходить проверку, не фабриковать requests и не модифицировать ответы B. Положительный результат должен повториться на чистом B для настоящего UUID A после очистки условий эксперимента через отдельное новое окружение и relog. Даже он ограничен проверенными версиями, предметами и условиями.

## Решение и практический следующий шаг

**NOT VERIFIED для поставленной цели.** Подтверждена локальная архитектура Moonrise и исследованных unlockers, а не отображение локально разблокированной косметики на втором чистом Lunar.

Первый практический шаг — выполнить описанный двухклиентный положительный контроль с уже принадлежащим A предметом. Для расширения на собственные/невыданные Moonrise предметы нужен официальный путь регистрации и разрешения распространения от Lunar; текущие источники его не предоставляют. Никакая крупная реализация или новый cosmetics backend пока не обоснованы.

Неизвестным остаётся реальный результат B на текущих сборках и точное поведение закрытого backend на outfit updates. Исследование не объявляет цель невозможной, но оснований обещать её пользователям нет.
