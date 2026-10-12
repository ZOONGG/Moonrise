# ZIP distributions and data storage

The production Velopack branch creates `Moonrise.App-beta-Portable.zip` (the legacy Inno branch uses `Moonrise-Portable-<version>-x64.zip`). When published, extract the entire archive to a writable directory and run `Moonrise.exe`. Do not run inside the ZIP or move the EXE away from its bundled resources.

**Current discovery uses `%LOCALAPPDATA%\Moonrise` for normal, development and ZIP builds.** The historical `Moonrise.portable` marker no longer selects an executable-relative data root. Therefore this ZIP is a no-install distribution, not a fully self-contained portable library. Copies share the normal library; avoid running older unguarded builds against newer data formats.

```text
Moonrise data root
├─ packages/weave
├─ packages/agents
├─ packages/metadata
├─ settings
├─ adapters
├─ cache
├─ cosmetics
├─ temp
└─ logs/crashes
```

Explicit roots used by tests/visual QA can be isolated. Local update-feed smoke builds use `%LOCALAPPDATA%\Moonrise.UpdateSmoke/<feed-hash>` instead of the main library.

ZIP and FriendPortable builds do not receive integrated Velopack updates. Replace application files manually from a trusted future release with Moonrise closed, preserving bundled resource layout and backing up the separate data root. Do not delete the marker as a migration procedure: installing through Setup is a distinct path. See [updates](UPDATES.md) and [consolidated storage](consolidated-user-data.md).

No public ZIP download is currently available; release assets will be published manually by the owner.
