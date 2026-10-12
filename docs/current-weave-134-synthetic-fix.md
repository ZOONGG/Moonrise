# Current Weave 1.3.4 synthetic class loading bridge

## Evidence and cause

Diagnostic run `veyra-smoke-20261008-230231-0c6b3973783b4c5c978045d14fcf0b4a`
under `%LOCALAPPDATA%/Moonrise/dev/logs`, JVM PID 17520. At 23:03:49 Asia/Omsk,
`current-linkage.log` records the real Genesis game loader `@5cad8b7d` and:

```text
sandbox=mcp-named ... synthetic-registry=[net/weavemc/loader/impl/shaded/spongepowered/asm/synthetic/args/Args$1]
```

The other namespace registries are empty. The name is registered with a unique
owner, but no definition event exists for it. Sandbox injected resources contain
service/config resources only. The extra `WeaveLoader INSTANCE absent` belongs
to the other loaded copy of that class; it does not invalidate the active game's
non-null instance and registry snapshot.

`jvm-17520-stdout.log` lines 867–875 retain the original
`NoClassDefFoundError` for that exact name during `Reflector.<clinit>` /
`Minecraft.startGame`, as in the previous run. Weave bootstrap and registration
have already succeeded. This is not evidence of a bad Veyra resource name or of
failed Mixin registration.

The missing boundary is lazy generation/definition for synthetic classes in the
Genesis game class loader. Registration creates the generator's class descriptor,
not a JAR entry or JVM definition. Inspection of the original current Weave loader
and Genesis loadClass paths finds no handoff from a game synthetic-name request to
the namespace sandbox's `generateClass` API. An ordinary ClassFileTransformer
cannot supply a class absent from resource lookup: no definition event occurs to
transform. Publishing a Mixin config into the sandbox does not publish generated
game classes.

Offline reproduction uses the actual 1.3.4 sandbox/ArgsClassGenerator: registration
and generation succeed, untouched Genesis cannot resolve the resulting name, and
the adapted actual Genesis loader defines and executes those generated Args bytes.
This confirms the missing game-loader generation/definition bridge; it does not
claim the complete Minecraft launch now works.

## Implemented fix

`CurrentWeaveSyntheticAdapter.Prepare` adds one separate session-owned adapter
before current Weave in the OfficialLunar bridge plan. It pins the inspected
Weave 1.3.4 and Genesis hashes; Disabled, Legacy and Custom modes return unchanged.
The existing legacy Stormy adapter and launch mode are untouched.

The adapter adds a guarded synthetic-name branch to the in-memory game loader.
It finds the unique namespace registry owner, asks that sandbox's original Mixin
transformer to generate the requested class, remaps its descriptors from the owner
namespace into Weave's runtime namespace, verifies the generated name, and defines
it in the requesting game loader. Repeated/concurrent requests reuse the definition.
Unknown or ambiguous owners fail explicitly. Ordinary class loading retains its
existing path. This applies to the current relocated Mixin synthetic package,
without a Veyra name, mod ID, entrypoint, feature or SHA condition.

Bootstrap transformation dependencies stay in an isolated loader. The resolver
JAR is attached to the actual game loader at the WeaveLoader definition boundary.
Generated classes stay in memory; the separate adapter JAR follows existing session
cleanup. No original Veyra, Weave or Lunar archive is rewritten.

## Verification and limits

- 46 targeted .NET tests passed: current adapter isolation/packaging, diagnostic
  isolation, current planning, legacy mapping/compatibility and native configuration.
- Java `-Xverify:all` tests passed against untouched Weave and Genesis artifacts:
  real registry/Args generation; missing and ambiguous owners; descriptor remapping;
  original Genesis failure; adapted definition, Args invocation, repeated and
  concurrent loads, unknown synthetic rejection and ordinary delegation.
- The loader test composes this adapter with original Weave's URLClassLoader
  transformer in live agent order. Its class-loader harness uses a fixture byte
  provider instead of constructing a complete WeaveLoader/game runtime; production
  registry selection/generation is checked separately with the real sandbox.
- Actual production C# preparation passed with the original current loader and
  Veyra path: separate adapter first, unchanged loader second, original mod path.
- Release build: 0 warnings/errors. Output:
  `runtime/current-synthetic-build/Release/net8.0-windows/win-x64`.
- `Start-VeyraSmoke.ps1 -CheckOnly` and `git diff --check` passed.
- Veyra SHA remains `779760392BCA1F269D0EB325E78950249D355E94A09C90988108C3CCD94D95BD`;
  current Weave, legacy 0.2.6 and Genesis hashes match their pinned originals.
- No Minecraft launch, commit, merge or release. Existing dirty changes preserved.
  No UI, ControlledGenesis, authentication or timeout UX change.

## Next manual smoke

Close the existing Moonrise (including any previous collector), then run once:

```powershell
pwsh -NoProfile -File %WORKSPACE%\app\tools\Start-VeyraSmoke.ps1
```

If the previous collector is still waiting, finish it after closing Moonrise so
it restores its settings before starting the next script. The new script uses
the updated Release output, opens Moonrise only and retains linkage diagnostics.
Click Launch once manually. Expected JVM evidence:
`synthetic class loading bridge installed`, `synthetic-generated ... namespace=mcp-named`,
`synthetic-defined ... loader=Genesis`, plus the matching definition in
`current-linkage.log`. Confirm the Args$1 linkage exception disappears; review any
new first exception separately. A menu alone does not establish complete mod success.
Check Veyra initialization and Array List/Timer/SpeedMine, then close the game and
Moonrise normally and complete the collector to restore settings.

READY FOR MANUAL VEYRA SMOKE: YES.
