# Consolidated local user data (2026-10-09)

Normal dev, Release and portable discovery now uses `%LOCALAPPDATA%\Moonrise`.
EXE paths and `Moonrise.portable` no longer select a separate library. Explicit
AppPaths roots used by tests and visual QA remain isolated. InstallationDirectory
continues to point to the executable directory for bundled runtime resources.
Local update-feed builds use `%LOCALAPPDATA%\Moonrise.UpdateSmoke\<feed-hash>`;
versions sharing the same feed share smoke data, never the main library.

Packages remain under packages/weave, packages/agents, packages/unclassified,
and packages/metadata. Settings remain under settings. Enabled states remain
in packages/metadata/index.json; settings retain legacy disabled-name fields.
No cross-environment settings or metadata merge runs on startup. Shared-library
layout migrations, duplicate deletion and empty legacy directory deletion are
suppressed. Explicit user uninstall still removes the selected package.

Format checks reject future settings schemas, unsupported package index formats,
and malformed documents before mutation. Supported legacy settings migrate using
the existing settings logic. These checks cannot retrofit already shipped EXEs:
do not open the shared library with an older unguarded executable after upgrading
its format. Concurrent editing by different app instances is not newly supported.

## Local audit

Backup and CSV reports: artifacts/user-data-audit (ignored, never commit).
Audit-MoonriseUserData.ps1 creates a new report directory, verifies copied package
hashes, reports destination collisions, inventories settings by hash/size, and
never merges or deletes source data. No settings migration was necessary to make
the main library authoritative, so settings contents were not copied.

21 main package files preserved, 4 dev files identical, 5 dev conflicts, 2 unique
JARs. All 32 audited source file hashes remained unchanged after implementation.
Unique dev JARs requiring manual review before import:
- agents/Moonrise-Synthetic-Premain-Smoke.jar (technical smoke package)
- weave/Veyra-osteli-local.jar (may overlap an existing Veyra identity)

Conflicts retained in dev: metadata/index.json, metadata/.layout-migrated-v4,
metadata/layout-migration-v4.log, weave/bwh-march-14-weave-patched-v2-api.jar,
weave/unrestricted.jar. Dev settings remain separate; do not overwrite main
settings or enabled states with them.

## Cleanup classification

Candidates only after confirmation and checking active processes, shortcuts,
installed updater ownership, and bundled runtime dependencies:
- app-combat-overhaul-20260811
- app-config-fix-20260811
- app-final-pvp-ui-20260812
- app-jump-reset-20260811
- app-knockback-fix-20260811
- app-pvp-fix-20260811
- app-silent-clutch-20260812
- app-silent-clutch-burst-20260812
- app-sprint-reset-final-20260811

These nine contain old executables/runtime resources; no packages/settings
folders were observed. Their active use has not been established. Do not delete
runtime resources independently without proving the corresponding build unused.

Preserve: packages, settings, dev (unique/conflicting user data), adapters
(including BWH technical adapter), cosmetics, plugins, themes, cache/runtime,
logs and temp pending their ordinary ownership/lifetime checks. No additional
unknown top-level directories were observed; individual old runtime dependencies
still need manual usage verification. Nothing in AppData was deleted or moved.

Before cleanup: open the new ordinary build, confirm the expected library and
enabled states, import a disposable JAR and reopen another ordinary build,
confirm UpdateSmoke is empty/separate, compare alternative JAR identity/version
and conflicts, verify backup availability, and test the usual Lunar launch.
Real UI/Lunar/Minecraft smoke testing has not been performed for this change.

## Validation

Release solution build: succeeded, 0 warnings, 0 errors. Targeted normal-build
selection: 97 passed. Separate actual FriendPortable build test: 1 passed.
Separate embedded UpdateSmoke feed build test: 1 passed. git diff --check: clean.
No full suite, publish, push, merge or release was performed. Pre-existing
uncommitted changes were preserved; no commit was created.
