# Lunar account projection — stage 1

Source inspected: installed Lunar Client `resources/app.asar!/dist-electron/electron/main.js` (2026-10-08), code only; real account-store contents were not inspected.

`IR` validates `localId` and `minecraftProfile.id/name`; `LR` defines the `accounts` object and optional `activeAccountLocalId`. `BR` adds a profile and selects its local ID, `VTe` selects it, and `zR` removes it. No skin field is established by this schema, so no skin URL is invented or fetched.

`Dg` defaults to `homedir()/.lunarclient`; `IT()` appends `settings/game`; `PR` is `accounts.json`. `jg` applies `LUNAR_CLIENT_DIRECTORY`, resolving relative paths against the launcher's cwd. Moonrise honors an inherited absolute override; relative overrides produce ACCOUNT_SYNC_FAILED because the launcher's cwd is unknown. An override present only in a separately launched Lunar process cannot be discovered from Moonrise's environment. Default-root integration is implemented; that custom-process case remains unsupported.

The provider opens the source read-only, extracts only UI fields, and never persists data or exposes parser exceptions. JSON parsing necessarily traverses the source document in memory; credential properties are not accessed or projected. Polling every two seconds covers file replacement and missing parent directories without watcher lifecycle complexity. Reads require stable length/timestamp over 150 ms, deny concurrent write/delete during parsing, and retry three times. No observer can prove a writer has finished a semantically valid intermediate document without writer cooperation. Invalid/unreadable/missing input retains the last valid immutable projection and shows its diagnostic. Only a successfully parsed empty source clears the list. Polling continues for recovery. UI publishes each projection in one dispatcher turn; selection is display-only.

No official store writes, copies, links, OAuth, token handoff, game IPC changes or launch changes. Existing Stage 2 edits remain unmodified and uncommitted.

## Acceptance attempt — 2026-10-08

- Launched the Release executable from the source tree in normal development mode; runtime root is `%LOCALAPPDATA%/Moonrise/dev`.
- The real WPF accessibility tree shows 9 existing accounts from the default root, with valid UUIDs and nicknames, no repeated UUIDs, and no selected entry. Independent verification against Lunar and a manual selection change remain pending.
- Computer Use window capture timed out after recovery; clicks failed with `coordinate input geometry is unavailable`. Visual layout and interactive responsiveness are NOT VERIFIED.
- A temporary exclusive **read-only** file handle was held and released without reading or changing source bytes. The observed UI still contained 9 accounts, but no unreadable diagnostic was observed. This does not prove polling/recovery; real live updates remain NOT VERIFIED.
- Fixed loss of the last valid list when the store temporarily disappears beyond the retry window. A synthetic regression test verifies retention, recovery, selection change, and clearing only after a valid empty source.
- 10 LunarAccountProvider targeted tests pass. Release solution build passes with 0 warnings/errors. `git diff --check` passes. The first rebuild attempt collided with the running executable; checks were rerun successfully after stopping that dev instance.
- Public official documentation confirms the default store path: https://support.lunarclient.com/solutions/external-software-interfering-with-lunar-client . No documented discovery configuration for another process's custom root was found. The inherited absolute environment override is supported; process memory/environment scanning is intentionally excluded.
- Account-sync scope: this document, `LunarAccountProvider.cs`, `MainWindow.Accounts.xaml.cs`, `LunarAccountProviderTests.cs`, and the accounts-only hunk in `MainWindow.xaml`. `MainWindow.xaml.cs` and all ControlledGenesis/auth/storage/recovery edits are outside this scope.
- Initial acceptance was pending manual confirmation; see the result below.

## User-confirmed acceptance — 2026-10-08

The user confirmed two consecutive selections of existing accounts in the official Lunar Launcher, reported as `mythgratz` and then `vibecdder`. Each selection appeared in the still-running Moonrise UI. These are the user's reported spellings, not an independent nickname/UUID audit. This establishes real default-root synchronization and selected-state updates without restarting Moonrise, and usable UI during the observed polling interval.

Real account UI smoke: PASS for discovery and live selection updates. Nine existing accounts and no duplicate UUIDs were observed through accessibility. Retention/recovery for invalid, locked, and missing input is covered by synthetic tests; the real read-only lock probe did not establish a diagnostic transition. Automated screenshot/layout verification remains unavailable because of the Computer Use capture/geometry failures. No real accounts were added, removed, or written by Moonrise during this acceptance.

Stage 1 acceptance is complete with these stated verification limits. Ready for Lunar account management Stage 2: YES. No Stage 2 implementation is part of this milestone. Commit scope is limited to the five account-sync files listed above; unrelated pending changes remain in the working tree.
