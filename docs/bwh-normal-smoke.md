# BWH normal OfficialLunar manual smoke

Prepared 2026-10-09. No Moonrise, Lunar or Minecraft process was started during preparation.

The selected library artifact is `bwh-march-14-weave-patched-v2-api.jar`, SHA-256
`6DA5B2A67E05E07DEAE2779B4AA3D1B70E5028DED227C41487FF783F5D609A57`.
This is the **existing local API migration build**, not a claim that the file is
an untouched upstream BWH release. This task neither patches nor rebuilds it.

The existing Compatibility Engine PackageInspector reports WeaveMod, Current,
Minecraft 1.8.9, minimum Java 17, no startup agent, no declared dependencies,
and no inspection errors. The namespace is `mcp-named`; entrypoint is
`lunarweavebridge.compat.BwhPreInitBridge`. Metadata declares five hooks,
`bwhelper.mixins.json` and `lunarweavebridge.compat.LoaderKickTweaker`.
The bounded class-reference inspection reports its inspection limit; this is
not complete proof of runtime linkage. Production WeaveModApiInspector separately
scans the mod and selects Current. Use the existing verified Weave 1.3.4 loader,
SHA-256 `013B7B9A4CA01A473A1FFC0B031C843C70FE61616E9048FB326E03794C8457B7`.

The previous attempt selected two Weave mods while using `--dev-weave-mod-smoke`.
Its explicit one-mod/zero-package-agent guard rejected preparation before JVM
startup. The technical network agent did not count as a package agent.

`tools/Start-BwhNormalSmoke.ps1 -PrepareOnly` has prepared only BWH; all other
package selections are disabled, including original Veyra, osteli and user agents.
Only Moonrise package/settings JSON changed, with original bytes saved for restore.
No account files are accessed. Existing profile selection is retained.

Offline preparation exercised MaintainedCompatibilityBuildService (no additional
replacement), EnabledModDirectoryService (identical copy), normal
CurrentLaunchPlanFactory.Create, CurrentWeaveSyntheticAdapter.Prepare and MNR4
serialization. Its temporary staging directory was removed. `offline-mnr4.txt`
is diagnostic evidence referencing that removed directory, not a reusable config.
Manual launch creates a fresh normal session through the existing pipeline.

The prepared injection order is current synthetic compatibility adapter,
BWH network adapter, Weave loader. All three are technical components;
there are zero `PackageAgent` slots. The network adapter has a separate hash-checked
JAR (`F0AA76E78EFF9C1A7894706A9335F2DEC5E715857BF24EA10B0CB29E6A8570AE`)
and the existing Moonrise loopback relay. Relay binding and actual network requests
remain untested here. No new runtime exception by package name was added, and no
general preparation blocker was observed.

Release build: zero warnings/errors. 156 targeted tests passed across
CompatibilityEngineTests, CurrentLaunchPlanFactoryTests,
CurrentWeaveSyntheticAdapterTests, BwhApiRelayServiceTests,
NativeBridgeConfiguration tests and WeaveLoaderCompatibilityTests.

Run from any directory:

```powershell
pwsh -NoProfile -File %WORKSPACE%\app\tools\Start-BwhNormalSmoke.ps1
```

This opens only Moonrise. Click Launch manually. The only process flag is
`--dev-jvm-startup-diagnostic`, enabling the existing sanitized native stdout/stderr
capture (2 MiB per stream), without any single-mod launch restriction. No linkage
diagnostic agent is selected. Production embedded bridge remains unchanged; capture
uses the existing separate verified diagnostic bridge.

Evidence is under `%LOCALAPPDATA%\Moonrise\dev\logs\bwh-normal-smoke-*`;
`bwh-normal-smoke.pending.txt` records the active directory. The collector records
observed JVM state/exit code, retains sanitized launch reports and JVM logs, and
restores original settings bytes after Moonrise exits. A JVM that exits before
monitor attachment is explicitly recorded without inventing an exit code.
Close Minecraft normally, then exit Moonrise including its tray host. Keep the
collector console open. After interruption, close Moonrise and run the same script
with `-RestoreOnly`; backups and evidence remain available.

Runtime acceptance still requires successful BWH discovery and initialization,
working BWH UI/features, network adapter/relay behavior, and normal game exit.
No Minecraft runtime PASS is claimed. Existing legacy/current paths,
ControlledGenesis and timeout/lifecycle implementation were not changed.
