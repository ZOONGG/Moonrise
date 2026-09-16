# AGENTS.md — Moonrise

## Project purpose

Moonrise is a Windows application for launching the current official Lunar Client with local Weave mods and Java agents.

The intended user experience is:

1. Install and authenticate through the official Lunar Launcher.
2. Open Moonrise.
3. Select an existing Lunar profile for Minecraft 1.8.9.
4. Install or import packages.
5. Enable the required packages.
6. Press Launch.
7. Play without manually editing JAR files, manifests, JVM arguments, Lunar folders, or account files.

Moonrise is not a replacement for Lunar authentication.

---

## Current priority

The project is currently in recovery mode.

The only priority is restoring a small, reliable launch core.

Do not add new product features until all of the following are proven on the user’s real PC:

- current Lunar Client launches successfully;
- Minecraft 1.8.9 launches successfully;
- a clean Weave mod loads successfully;
- a clean Java agent loads successfully;
- third-party JAR files remain byte-for-byte unchanged;
- Lunar and Microsoft account data remain untouched;
- temporary files are cleaned after launch;
- disk usage remains bounded.

Postpone:

- website;
- online player/session counter;
- telemetry;
- ratings;
- reviews;
- themes;
- external Moonrise plugins;
- multiple Minecraft versions;
- account systems;
- new catalog infrastructure;
- unrelated UI redesigns.

---

## Supported scope during recovery

Target environment:

- Windows x64;
- .NET 8;
- WPF;
- current official Lunar Client;
- Minecraft 1.8.9;
- current supported Weave Loader;
- per-user local installation.

Official Moonrise-owned packages:

- Veyra — Weave mod;
- Moonrise Cosmetics — Java agent.

Moonrise-owned packages may be modified normally.

Third-party packages must follow the immutability rules below.

---

## Non-negotiable JAR rules

### Third-party JAR files are immutable

Never modify an original third-party JAR.

Do not:

- rewrite Java class constant pools;
- replace package names;
- rewrite Weave API references;
- change entrypoints;
- edit `weave.mod.json`;
- edit `MANIFEST.MF`;
- inject bridge classes;
- add or remove files;
- rebuild the archive;
- normalize ZIP metadata;
- silently produce a patched copy;
- overwrite the original file.

Record and preserve the original SHA-256.

The original hash before and after installation must match.

### Compatibility strategies

Compatibility must use one of these approaches:

1. **Works directly**
   - Run the untouched original JAR.

2. **Works with adapter**
   - Keep the original JAR untouched.
   - Add a separate Moonrise adapter JAR.
   - Treat the adapter as a hidden technical dependency.

3. **Maintained compatibility build**
   - Allowed only when source code and license permit it.
   - Clearly identify it as a Moonrise-maintained build.
   - Never pretend it is the original upstream binary.

4. **Incompatible**
   - Show the package as unsupported.
   - Do not fake successful installation.

Users must never manually patch a JAR.

---

## Catalog policy

The catalog may contain all discovered mods and Java agents for manual testing.

Every package release must have one explicit compatibility status:

- `untested`
- `works-directly`
- `works-with-adapter`
- `maintained-build-required`
- `incompatible`
- `conflict`

New entries default to `untested`.

Do not call a package compatible until it has been launched and verified on the supported Lunar environment.

Do not call a package safe merely because its SHA-256 matches.

Do not silently redistribute third-party binaries when permission or licensing is unclear.

When automatic installation is not appropriate, use an external source link or mark the package unavailable.

---

## Account and credential safety

Moonrise must never read, copy, parse, store, transmit, log, modify, truncate, replace, or back up:

- Lunar access tokens;
- Microsoft access tokens;
- refresh tokens;
- session tokens;
- account credentials;
- passwords;
- Xbox authentication data;
- Minecraft ownership credentials;
- contents of `accounts.json`.

Do not manage authentication.

Authentication and ownership verification must remain inside official Lunar components.

Do not include token-bearing command lines in logs.

Redact values associated with names containing:

- `token`
- `accessToken`
- `refreshToken`
- `clientToken`
- `session`
- `password`
- `authorization`
- `credential`

Account-related files may only be observed through non-sensitive metadata when explicitly required for diagnostics:

- existence;
- file size;
- modification timestamp;
- hash of raw bytes.

Never print their contents.

---

## Filesystem policy

Moonrise must not create random project folders or temporary directories across the user’s drives.

### Installed mode

Mutable application data must live under one predictable per-user root, preferably:

```text
%LOCALAPPDATA%\Moonrise
```

Expected structure:

```text
Moonrise/
├── packages/
├── adapters/
├── cache/
├── settings/
├── logs/
└── temp/
```

### Portable mode

Portable mode is allowed only when explicitly enabled by a marker or configuration.

Portable data must remain inside one clearly named data directory beside the executable.

### Development mode

Running from source must not treat the repository root as permanent application storage.

Development runtime data must use a dedicated ignored directory.

Do not create mutable folders such as these in the repository root:

- `sessions`
- `managed`
- `bridge`
- `updates`
- `plugins`
- `themes`
- `language-packs`
- temporary publish audits
- temporary upstream audits

unless the current task explicitly requires them.

### Cleanup

Implement and verify limits:

- download cache: maximum 500 MB;
- logs: maximum 50 MB;
- stale temporary files: automatically deleted;
- completed launch sessions: automatically deleted;
- old installer/update artifacts: automatically deleted;
- failed downloads: automatically deleted;
- old compatibility output: automatically deleted when no longer referenced.

Never delete user-imported originals without explicit confirmation.

---

## Development workflow

Work on exactly one milestone at a time.

For every task:

1. Inspect the current implementation.
2. Reproduce the problem.
3. Collect sanitized evidence.
4. Identify the smallest supported root cause.
5. Propose the minimal change.
6. Implement only that change.
7. Add or update tests.
8. Build.
9. Run tests.
10. Perform a real smoke test when possible.
11. Report honestly what was and was not verified.

Do not combine recovery work with unrelated feature development.

Do not rewrite large subsystems when a local fix is sufficient.

Do not introduce new services, abstractions, databases, workers, repositories, background processes, caches, or frameworks without proving they are required.

Prefer deletion of unnecessary complexity over adding another compatibility layer.

---

## Source-driven development

Current source code, actual logs, real JAR metadata, and observed Lunar behavior are the sources of truth.

Do not rely on:

- assumptions from old README files;
- previous Codex reports;
- stale screenshots;
- remembered Lunar internals;
- guessed class names;
- invented API behavior;
- unverified compatibility claims.

When working with external projects:

- inspect the upstream repository;
- inspect tags and releases;
- inspect the license;
- inspect actual metadata;
- pin exact versions or commits;
- preserve attribution.

Do not fabricate release URLs, hashes, licenses, or supported versions.

---

## Launch pipeline rules

Keep the launch pipeline understandable and observable.

The pipeline should conceptually be:

```text
Existing Lunar profile
        ↓
Moonrise launch preparation
        ↓
Untouched enabled Weave mods
        ↓
Separate required adapter JARs
        ↓
Enabled Java agents
        ↓
Official Lunar/Minecraft process
```

Do not:

- copy authentication arguments into a custom launcher;
- reconstruct a Minecraft session;
- replace official ownership checks;
- downgrade Lunar automatically;
- modify Lunar installation files without explicit need;
- mutate package files during launch;
- leave permanent launch snapshots after Minecraft exits.

Every launch should produce a sanitized diagnostic result containing:

- selected profile;
- selected Minecraft version;
- enabled package IDs;
- adapter IDs;
- agent loading status;
- Weave loading status;
- child process status;
- failure stage;
- cleanup result.

Never include secrets.

---

## Package storage rules

Store package types separately:

```text
packages/
├── originals/
├── moonrise-owned/
└── imported/

adapters/
└── generated-or-versioned-adapters/
```

Original third-party packages must be content-addressed or tracked by SHA-256.

Avoid duplicate copies of identical JARs.

Do not create a new patched JAR for every launch.

Adapters must have:

- stable ID;
- version;
- target package ID;
- target package SHA-256;
- supported Moonrise version;
- supported Lunar version;
- supported Weave version.

If an adapter does not match the exact original hash, do not load it.

---

## UI principles

The main navigation is:

- Home
- Catalog
- Library
- Settings
- Developers, hidden unless Developer mode is enabled

### Home

Show:

- selected Lunar profile;
- Minecraft 1.8.9;
- launch readiness;
- enabled package summary;
- one clear Launch button;
- concise errors;
- diagnostics action only when useful.

### Catalog

Show all discovered entries during the testing phase.

Make compatibility status explicit.

Installation must never imply that a package is known to work when it is still untested.

### Library

Allow:

- enable;
- disable;
- update;
- uninstall;
- reveal source;
- verify hash;
- view adapter dependency;
- view compatibility result.

### Settings

Include:

- language;
- Lunar path;
- data directory;
- disk usage;
- clear cache;
- open data folder;
- diagnostics;
- Developer mode.

### Developers

Include only technical tools that help package verification:

- inspect JAR metadata;
- show manifest;
- show `weave.mod.json`;
- show entrypoints, hooks, and mixins;
- calculate SHA-256;
- test launch;
- generate an adapter manifest;
- export sanitized diagnostics.

Do not automatically decompile or mutate selected JARs.

---

## Testing requirements

Before considering launch recovery complete, test at minimum:

- untouched Veyra loads;
- untouched Moonrise Cosmetics agent loads;
- untouched third-party Weave JAR remains unchanged;
- untouched third-party agent remains unchanged;
- invalid JAR is rejected;
- mismatched SHA-256 is rejected;
- mismatched adapter target hash is rejected;
- disabled package is not loaded;
- enabled package is loaded exactly once;
- conflicting packages are reported;
- temp files are removed after successful launch;
- temp files are removed after failed launch;
- cache limits work;
- log rotation works;
- no account file is written;
- no secret appears in logs;
- no mutable data is written into the repository root;
- installed and portable modes use the correct data root.

Unit tests must not perform real network requests.

Do not claim Lunar integration works based only on unit tests.

A real Lunar/Minecraft smoke test is required for runtime claims.

---

## Build commands

Inspect the repository and use its actual solution and project paths.

Typical validation commands are:

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release --no-build
```

Publish only after tests pass.

Do not repeatedly create self-contained publish directories during debugging.

Use one disposable output directory and remove it after validation when safe.

Do not run large publish or installer matrices unless the current task is specifically about releases.

---

## Git rules

Before editing:

- check `git status`;
- record the current branch;
- inspect uncommitted changes;
- do not overwrite unrelated user work.

Do not:

- reset;
- force checkout;
- force push;
- rewrite history;
- delete branches;
- discard uncommitted changes;
- automatically stash user work.

Commits must be small and task-specific.

Do not commit generated:

- logs;
- caches;
- runtime data;
- temporary JARs;
- imported private JARs;
- account data;
- audit folders;
- installer test folders;
- self-contained runtimes;
- local settings.

Do not push until:

- build succeeds;
- tests pass;
- the user has tested the real Lunar launch scenario when runtime behavior changed.

Runtime fixes should remain local until the user confirms they work.

---

## Reporting rules

Final reports must distinguish:

- implemented;
- unit-tested;
- built;
- published;
- smoke-tested;
- tested with real Lunar;
- tested with real Minecraft;
- not tested.

Never state “fixed” merely because compilation succeeds.

When a task fails, report:

- exact failure stage;
- evidence;
- what was ruled out;
- what remains unknown;
- smallest next diagnostic action.

Do not hide failures behind fallback behavior.

Do not silently show demo data when production data fails.

---

## Prohibited actions

Unless the user explicitly requests and approves them, do not:

- add new product features during recovery;
- create a website;
- deploy cloud services;
- add telemetry;
- create new GitHub repositories;
- generate signing keys;
- modify registry outside installer work;
- change system-wide environment variables;
- install global tools;
- run destructive cleanup;
- delete suspicious directories;
- scan private file contents;
- upload local JARs;
- publish third-party binaries;
- modify Lunar account files;
- modify third-party JARs;
- commit or push runtime changes before user testing.

---

## Definition of recovery success

The recovery milestone is complete only when a normal user can:

1. Install official Lunar Client.
2. Authenticate in the official Lunar Launcher.
3. Launch Lunar 1.8.9 once.
4. Install Moonrise.
5. Open Moonrise without creating random folders.
6. Install or import an untouched supported package.
7. Enable it without manually editing anything.
8. Launch Minecraft 1.8.9.
9. Confirm the mod or agent loaded.
10. Close the game.
11. Confirm temporary files were cleaned.
12. Confirm the original package hash did not change.
13. Confirm Lunar accounts still work.
14. Confirm disk usage did not grow unexpectedly.

Until this succeeds, prioritize recovery over expansion.
