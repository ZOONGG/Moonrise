# Recovery Stage 3: compatibility evidence

Stage 2 was accepted by the user after real lifecycle and modal smoke testing.
Stage 3 runtime compatibility remains unconfirmed until each case is tested in-game.
Keep PR #31 open and unmerged until the next recovery milestone and real smoke are complete.

## Diagnostic contract

`logs/launch-*.json` schema version 2 records:

- selected profile ID, name, client type, game version and available loader/module fields;
- selected original packages and runtime packages with identifiers, kinds, filenames and SHA-256;
- source paths and actual launch-copy paths;
- Weave API inspection and selected loader family/version/SHA;
- maintained builds and ordered launch agents with IDs, roles, paths and SHA;
- monotonic elapsed stage timeline, effective launch timeout, Java exit code,
  usable-window observation and cleanup result.

A usable window does not prove that a package loaded or worked. Compatibility stays
`untested` until manual smoke evidence is reviewed. A timeout does not prove a crash.

Export matrix rows from the sanitized reports:

```powershell
./tools/Export-CompatibilityMatrix.ps1 -LogsDirectory <logs> -OutputPath <matrix.json>
```

The export preserves original-package identities separately from maintained builds.
It leaves manual results `pending`; it never converts a window or zero exit code to PASS.

On early Java exit, Lunar exit, or Minecraft timeout, inspect `logs/crashes/<timestamp>/`.
The bundle contains bounded sanitized fragments of recent crash/hs_err files when found,
plus their original paths. Standard Lunar log/crash directories are searched; custom game
directories may require follow-up evidence. Missing captured files are not proof of no crash.
Never request or attach account files or raw token-bearing Java command lines.

## Profile evidence

The observed local Lunar DB has optional `loaders`, `loader_version`, `lunar_module` columns.
The observed Lunar 1.8.9 row has `["ichor"]`, NULL loader version and module `lunar`.
No real Forge row was present during this inspection. Do not infer Forge/OptiFine from names.
Same-name profiles are distinguished and selected by exact ID. Optional-column absence and
unknown loader JSON use a safe fallback. Profile DB access remains read-only.

## Sequential real smoke

Start with one case, collect its result, identify the failure stage, then proceed:

1. Stormy alone, ordinary Lunar 1.8.9.
2. Fractal alone, ordinary Lunar 1.8.9.
3. RavenWeave alone, ordinary Lunar 1.8.9.
4. BWH alone, ordinary Lunar 1.8.9.
5. Moonrise Cosmetics alone, ordinary Lunar 1.8.9.
6. BWH + Cosmetics, ordinary Lunar 1.8.9.
7. Veyra + Cosmetics, ordinary Lunar 1.8.9.
8. BWH with a real Lunar-installed Forge 1.8.9 profile, if available.

Disable unrelated packages, press Launch once, record startup time and window count.
After PASS, verify package functionality in-game, close the game and wait for cleanup.
Return the launch JSON for every case; for FAIL/TIMEOUT also return the crash bundle.
Do not label BWH/Cosmetics a conflict without crash evidence.

Stormy's observed SHA is
`52ED9DA4C4F570FD43B32671E0D94FA5FC9E441168645D2E3DE054169374B021`.
Its original classes reference `net/weavemc/loader/api/`, explaining current selection
of legacy Weave 0.2.6. The cause of Lunar Unexpected Error still needs fresh runtime evidence.
Do not modify the JAR or replace Stormy with Veyra.
