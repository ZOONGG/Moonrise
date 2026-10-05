# ControlledGenesis Stage 2 resource contract

Verified on Windows x64, 2026-10-05, branch `codex/controlledgenesis-stage2`.
Scope: Genesis 9.10.1, Minecraft 1.8.9, OptiFine HD_U_M6_pre2,
Lunar 2.23.0-2640. No packages, Weave or user agents; official Lunar UI was not started.

## Primary contract evidence

- Installed Genesis SHA-256:
  `a86d3446fdce9012bcb4715ca7ea2045db58913dd2e89d3b94e3cf226b2fac80`.
- Installed lunar.jar SHA-256:
  `0763a381214da346e889565c695b12c794e08cfa2eb4053c3c46e3208774630e`.
- `ClientGameBootstrap` bytecode builds `--webosrDir` from the **last**
  `java.library.path` element plus `/web` (concat bootstrap constant).
- Current GameUI bytecode checks `<webosrDir>/resources/icudt67l.dat`,
  then `<uiDir>/ul-resources/resources/icudt67l.dat`. Its FileSystem handler
  resolves resources against the UI and WebOSR roots. UltralightConfig uses
  the resulting relative resource path.
- Installed `ul-resources.zip` contains exactly `resources/cacert.pem`,
  `resources/icudt67l.dat`, `resources/mediaControls.css`,
  `resources/mediaControls.js`, `resources/mediaControlsLocalizedStrings.js`.
  Provisioning extracted these under `natives/resources`; the old Genesis
  invocation instead pointed to `natives/web`. This is an extraction/view
  mismatch, not evidence that `webosrDir` should name `resources` directly.
- Installed launcher's `resources/app.asar`, `dist-electron/electron/main.js`,
  extracts UI into `ui/<ui.sourceSha1>` and passes that directory as `uiDir`.
  Resource-only `[Launch] [Ui]` records in `logs/launcher/main.log` repeatedly
  confirm source archive `b6d39b271abae35e95be9bdadf2270e908d59548` on October 4/5.
  This mapping is pinned to the above lunar.jar snapshot. The other installed
  bundle is not selected by guessing folder timestamps.
- `lunarBuildData.txt` declares UI Git revision
  `9015823432909d14d2fbda52aa2041f72ba90d13`. This is **not** the UI archive SHA-1.
- Genesis resolves textures, UI and JIT roots from `lunar.dataDir`.
  The installed texture tree and `texturesIndex.txt` include `lunar/jit_index`,
  `.bobj`, `.fsh`, `.mcmeta`, `.molang`, images and JSON assets.
- Lunar's language loader uses `ClassLoader.getResourceAsStream` for
  `lang/lunar/en_US.json`; the installed `lunar-lang.jar` supplies those entries.
  It belongs on the bootstrap classpath. Merely listing it as an Ichor external
  input did not make the language resource visible in the second smoke.
- Genesis bytecode creates its bake cache under
  `<classpathDir>/cache/<module hash>/<baker hash>/bake.zip`.

## Frozen preparation and ownership

Installed sources are discovered read-only. The LaunchPlan freezes all selected
files, hashes, roots, purposes, source ownership, prepared ownership, write access,
version evidence, native-path property, data-path property and runtime file directory.
Execution performs no folder selection after freeze. Unknown Genesis/Lunar snapshots
and missing essential resources are explicit unsupported/provisioning errors.

The per-launch view is:

```text
owned work/
  .moonrise-resource-owner
  natives/web/resources/       five Ultralight resources
  lunar-data/ui/               selected UI bundle contents
  lunar-data/textures/assets/  provisioned texture/model/JIT-index assets
  lunar-data/jit/              fresh runtime-generated data
  lunar-data/settings/         fresh isolated settings/account state, if generated
  runtime/                    copies of frozen runtime and Minecraft JAR inputs
    cache/                    generated Genesis bake cache
  .ichor/, logs/, default/, overrides/  runtime-generated working data
```

The last native-path element is `owned work/natives`; installed native DLLs remain
in the first read-only library-path element. `classpathDir` is `owned work/runtime`.
The isolated UI root exposes its selected bundle at `index.html`; bundlePath is `.`.

Only UI/texture asset roots, the five named Ultralight resources and explicit frozen
JAR inputs are copied. No existing settings, account DB, auth files, session state,
or generated user caches are imported. Asset extensions and the provisioned `index`
and `jit_index` filenames are allowed; account/secret-looking path components are
excluded by discovery and rejected again during preparation. Reparse points,
out-of-root paths, changed inputs, reused output roots and views over 500 MiB are rejected.

Cleanup requires the owned marker, a strict descendant of the allowed temporary
parent, and a tree without reparse points. It deletes the entire owned working view
only after the owned JVM exits. Sanitized diagnostic directories are retained separately
from the disposable resource/data view for this development audit.

## Smoke evidence and remaining blockers

1. Resource view removed the Ultralight fatal exception and created `Lunar Context`.
   The next fatal resource failure was `Could not find jit index file: lunar:jit_index`,
   with a missing `.bobj` model. Both were present in the provisioned texture tree and
   were added to the bounded asset policy.
2. The second smoke finished Lunar initialization, but showed untranslated menu keys.
   Adding the language JAR to bootstrap classpath resolved the language errors.
3. JVM PID 4652 reached a rendered main menu with Singleplayer/Multiplayer labels;
   the user supplied screenshots. Computer Use capture failed and was subsequently
   stopped by the user with Escape; screenshots provided the visual confirmation.
4. Final isolation smoke: JVM PID 3920, Java Auto selected Azul Zulu 17.0.18 x64;
   frozen plan `58ff95d0611d909a04eec93f882b7b0aa662e10b8b1e692f9e2e106088c84534`.
   WebOSR context and Lunar initialization completed; the harness observed a visible,
   responding LWJGL window. The user confirmed the same normal menu and working
   Lunar settings. 555 frozen original files were rehashed: zero mismatches.
   Installed bake-cache file names, sizes and timestamps were unchanged in this
   final smoke; bake output was observed under the owned runtime view.

Singleplayer/Multiplayer/account actions are blocked by the current Lunar requirement
that its launcher be open for account sign-in. Auth/ownership and playable state remain
**UNVERIFIED**. The auth flow is unchanged. No server was joined and no world opened.

Ordinary window-close requests remove the game window but leave the owned JVM alive.
This is a separate lifecycle blocker; cleanup uses identity-checked termination after
the normal-close attempt. No foreign process is terminated. Early exploratory smoke
runs wrote generated bake cache into the installed root via the pre-existing Stage 2
classpathDir behavior; the final view fixes that location. Original JAR/native/resource
bytes remained unchanged. Those earlier installed-cache writes are reported explicitly,
not described as proof that the installed root was untouched throughout the task.

Validation: 51 targeted tests (ControlledGenesis, resource contract, Stage1RecoveryTests,
UniversalLaunchPlanTests); Release solution build, zero warnings/errors;
`git diff --check`. Full suite was not run.

READY FOR CONTROLLEDGENESIS PACKAGE INJECTION: **NO**.
Resource bootstrap and menu rendering are proven; auth/playability and normal JVM
shutdown remain separate unresolved concerns.

Cleanup verification: all four owned JVMs (13768, 21276, 4652, 3920) exited;
all four owned working/resource/data trees were absent afterward. Final JVM 3920
required identity-checked termination after ordinary close; its exit code was -1.
The development harness prints `NORMAL EXIT` for any exit returned by WaitForExitAsync;
that label does not establish a graceful exit. Sanitized audit diagnostics remain;
foreign processes were not closed or terminated.
