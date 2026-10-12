# Installed update smoke — локальный Velopack

Исторический план local-feed acceptance. Новая production-проверка beta.1 → beta.2 прошла: см. final-release-validation.md. Интерактивные пункты и rollback ниже всё ещё требуют VM/отдельного сеанса.

## Изоляция и артефакты

Используйте отдельную Windows VM (предпочтительно со snapshot) либо отдельную тестовую Windows-учётную запись с отдельным входом в сеанс. Не запускайте Setup из рабочей учётной записи. Не переносите рабочие AppData, Lunar/accounts или библиотеку. Minecraft для этого smoke не нужен. Не используйте старый Test-MoonriseInstaller.ps1: он предназначен для Inno Setup.

Корень артефактов: `%WORKSPACE%\app\artifacts\installed-update-smoke`.
Setup для установки: `beta1\velopack\Moonrise.UpdateSmoke.App-beta-Setup.exe`.
Оба пакета: ID `Moonrise.UpdateSmoke.App`, channel `beta`, RID `win-x64`; версии `0.1.0-beta.1` и `0.1.0-beta.2`. Дополнительный beta2 Setup не нужен для штатного update smoke. Portable ZIP не использовать: portable updates отключены.

Feed: `%WORKSPACE%\app\artifacts\installed-update-smoke\feed`.
Это файловый feed SimpleFileSource, HTTP-сервер не нужен. Он содержит только beta2 full.nupkg, `releases.beta.json` и `RELEASES-beta`. Путь встроен в обе сборки; ошибка чтения не переключает их на GitHub. Оставьте путь неизменным и дайте тестовой учётной записи право чтения. В VM скопируйте feed по этому же абсолютному пути. Не публикуйте эти сборки.

## Одна последовательность smoke

1. В отдельном тестовом сеансе временно переименуйте `feed` в `feed.offline`. Установите beta1 Setup обычным способом, без elevation. Установщик может сам открыть приложение. Проверьте установленный путь `%LOCALAPPDATA%\Moonrise.UpdateSmoke.App`, наличие `Update.exe`, `current` и `packages`; версия EXE в `current` должна иметь ProductVersion `0.1.0-beta.1`. При недоступном feed приложение должно оставаться работоспособным и показывать ошибку проверки без pending update.
2. Измените язык/настройки. Для библиотеки импортируйте только специально подготовленный несекретный тестовый JAR (не рабочие моды), не запускайте игру. Закройте Moonrise. Зафиксируйте список и SHA256 файлов `%LOCALAPPDATA%\Moonrise.UpdateSmoke\<feed-hash>\packages` и настройки в `%LOCALAPPDATA%\Moonrise.UpdateSmoke\<feed-hash>\settings`. Этот data root находится вне установки. Сделайте VM snapshot/копию только этих тестовых данных.
3. Верните имя `feed`. Запустите установленный Moonrise через тестовый ярлык. Убедитесь, что автоскачивание включено. Дождитесь фоновой проверки (или используйте штатную проверку обновлений), обнаружения beta2, прогресса скачивания, уведомления и pending restart. В установочной `packages` должен появиться beta2 nupkg с SHA256 из `feed\releases.beta.json`. До рестарта должна продолжать работать beta1.
4. Нажмите штатную кнопку применения/перезапуска. Дождитесь закрытия старого процесса и появления нового. Проверьте ProductVersion установленного `current\Moonrise.exe`: `0.1.0-beta.2`. Настройки и библиотека должны сохраниться; сравните SHA256 тестового JAR и состояние включения. Допустимы штатные изменения настроек, но не их потеря.
5. Выполните повторную проверку: обновлений и pending быть не должно. Зафиксируйте LastWriteTime/размер beta2 nupkg, закройте и снова откройте Moonrise, проверьте ещё раз: beta2 запускается, повторного скачивания beta2 нет, библиотека и настройки сохранены. Запишите PASS/FAIL каждого пункта.

## Отдельные негативные проверки

Выполняйте на отдельной копии VM/snapshot beta1, чтобы не смешивать со штатным smoke.

- Активная JVM: при уже запущенной тестовой JVM и staged beta2 нажмите применение. Перезапуск/применение должны блокироваться; beta1 продолжает работать. Закройте Moonrise и откройте заново, пока JVM активна: startup apply также должен блокироваться. Не запускайте Minecraft и не останавливайте существующие рабочие игровые процессы. Если тестовой JVM нет, отметьте этот runtime-пункт NOT RUN; unit gate tests уже PASS.
- Ошибка скачивания: сохраните оригинальный feed, измените SHA256 beta2 в тестовой копии releases.beta.json, проверьте обновление на чистой beta1. Ожидается ошибка, без pending и без смены версии; beta1 запускается снова. Затем восстановите feed. Это проверяет отбрасывание повреждённого пакета, а не rollback после начала apply.
- Ошибка apply/восстановление: только в disposable VM после snapshot с pending beta2 искусственно запретите тестовому пользователю запись в установочную `current` (сохраните исходные ACL). Попробуйте применить обновление; снимите запрет и проверьте запуск через Update.exe/ярлык. При ошибке должна остаться запускаемая версия. Зафиксируйте фактическую версию и Velopack log; автоматический rollback пока не подтверждён. Если запуск не восстановился, результат FAIL; для восстановления стенда используйте snapshot либо beta1 Setup с сохранёнными отдельно тестовыми settings/packages. Не называйте ручную переустановку успешным автоматическим rollback.
- Недоступный feed: переименуйте feed, проверьте работу уже установленной beta2, отсутствие ложного pending и повторного скачивания. Верните feed и повторите проверку.

## Проверено при подготовке

- Release self-contained single-file win-x64 publish обеих версий; включены Microsoft.NETCore.App и Microsoft.WindowsDesktop.App 8.0.31. Отдельная установка .NET не требуется.
- vpk/SDK 1.2.161; штатная проверка VelopackApp при pack прошла; Main вызывает Velopack до WPF, auto apply Velopack отключён, приложение использует существующий guarded apply.
- 27 targeted UpdateService/UpdateUiState tests PASS после изменения фабрики feed.
- Publish safety check прошёл для обеих версий; в nupkg нет dev logs/PDB/settings/account stores/credential files. Пакуются только publish assets и служебные файлы Velopack. Embedded ресурсы приложения остаются существующими; это не полный аудит содержимого исходников на секреты.
- Проверены nuspec identity/version/channel/RID и SHA256 обоих nupkg. Inventory, nuspec, packaging-checks.json и sha256.json находятся в корне артефактов.
- Установщики не подписаны; реальный запуск на машине без .NET, установленное обновление, активная JVM и rollback ещё не проверены.

Повторная сборка: `tools\New-MoonriseInstalledUpdateSmoke.ps1 -OutputRoot artifacts/installed-update-smoke-new`. Скрипт отказывается перезаписывать существующий корень, использует текущий Release pipeline в Velopack-режиме и фиксирует новый абсолютный feed в обеих сборках. Ничего не устанавливает и не публикует.

Дополнительная PE-проверка: Moonrise.Native.dll импортирует только KERNEL32.dll; apphost — системные Windows DLL и Universal CRT. Внешняя MinGW/VC runtime DLL в импортах этих файлов не требуется.
