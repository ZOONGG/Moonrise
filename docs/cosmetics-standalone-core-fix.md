# Cosmetics standalone Core fix — 2026-10-12

## Reproduced failure

The actual Lunar 1.8.9 standalone launch used zero Weave mods, one Cosmetics
package agent and Weave mode Disabled. Cosmetics 1.3.1 failed in premain with
`NoClassDefFoundError: net/weavemc/loader/impl/shaded/asm/ClassVisitor`.
JVM PID 24704 exited 1; Moonrise recorded `java-exited-nonzero`, gracefully
closed its Lunar root with no remaining processes, cleaned the session and
returned to Ready. Evidence is under
`%LOCALAPPDATA%\Moonrise\dev\experiments\bwh-baseline\logs\core-cosmetics-only-20261012`.

The agent had compiled against Weave's shaded ASM without packaging that
dependency. Its successful combined launches relied on another package loading
Weave first. The readiness marker was also emitted before transformer
construction, so it could exist even after failed premain.

## Minimal change

- Separate Moonrise-owned runtime agent 1.3.2 packages only the ASM class subtree
  from the existing SHA-256-pinned Weave 1.3.4 dependency, with ASM's BSD notice.
  No Weave bootstrap/loader code is bundled and Weave is not added to clean launches.
- Readiness is written after all three transformers register successfully.
- Exact original Cosmetics hash resolves to the new exact runtime hash.
- Original user Cosmetics 1.3.0 remains untouched, SHA-256
  `bfdcfc1e5e00bb922708885ce35159db0b1e3de989240bf6f98bd1f269b55ab6`.
- Ordinary/experimental selection rules and Raven/BWH adapter are unchanged.

## Targeted validation

- Standalone premain with no Weave on the classpath: PASS on JDK 25 and actual
  installed Lunar Java 17.0.18.
- Existing transformer/bytecode verification against installed Lunar: PASS.
- Existing isolated outfit persistence runtime and official service-route probes: PASS.
- 26 .NET maintained-build/launch-plan/experimental-selection tests: PASS.
- Rebuilt application and republished the same local validation directory.
  First publish attempt hit the live smoke process file lock; after normal
  Alt+F4 exit, the retry succeeded. No forced process termination occurred.
- Runtime JAR SHA-256:
  `efbac1dc8f6b28ee1a13e294ff7dc03880c384306f1ffab88738e3dcd1dc4354`.

Real fixed standalone smoke is running from the same isolated root; report
records zero mods, one package agent, Weave Disabled and the sole 1.3.2 agent.
`cosmetics-agent.ready` exists and the missing ASM failure has not recurred.
Actual Lunar window responded and the user confirmed cosmetics availability
and correct standalone operation. After the user closed both apps, no game or
Moonrise processes remained, but the launch report retained `java-detected`
and the session directory remained: host shutdown interrupted monitoring.
Normal-close hiding previously depended solely on BWH relay. The minimal
lifecycle correction preserves the existing tray host while launch UI is busy
or graceful cleanup is pending, including zero-Weave launches. Explicit tray
Exit remains available. In the new real smoke, the log confirms normal window
close hid Moonrise while monitoring remained active; the user confirmed the
tray behavior. The game subsequently exited and graceful Lunar cleanup began.
The user then explicitly selected tray Exit, interrupting the asynchronous
cleanup before report finalization. This is distinct from ordinary window
close; explicit host shutdown stops its in-process monitor. The remaining
experiment-owned launcher was closed via the supported deeplink without force.
Completed standalone cleanup with the host retained remains unverified.
The subsequent ordinary launch observed the old hidden launcher and blocked
before JVM startup. The experiment-owned launcher was closed gracefully through
the supported deeplink. Updated policy tests (29 targeted lifecycle tests)
passed, and the same validation output was rebuilt before a new standalone
lifecycle smoke at `logs\core-standalone-lifecycle-20261012`.
Evidence: `logs\core-cosmetics-only-fixed-20261012` in the isolated root above.

## Ordinary-mode regression after both fixes

The user confirmed the ordinary three-mod/one-agent set works with Cosmetics
1.3.2. The completed report for session
`launch-8b477ff5172e49f6ad973d3fb46de6ce` records `normal-exit`, JVM exit 0,
cleanup success, graceful Lunar shutdown success, no remaining owned processes
and zero visible launcher windows. The session directory is absent. Moonrise
remained running, proving completed monitoring when explicit Exit is deferred.
Evidence: `%LOCALAPPDATA%\Moonrise\dev\logs\core-cosmetics-132-regression-retry-20261012`.

A second ordinary launch from the same Moonrise instance also completed with
user-confirmed functionality, JVM exit 0, successful session removal and
graceful Lunar shutdown with no remaining processes/windows. Report:
`launch-20261012-134121-14d789444ff04b869febe50e8bc296d6.json`.
