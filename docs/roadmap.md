# Moonrise — Roadmap

> Source: the previously agreed five-stage plan, preserved from the authoritative desktop Markdown document. This is its repository copy; the desktop original is retained. The earlier short README plan was not the full roadmap.
>
> Status reviewed: **2026-10-12** against the current implementation and recorded checks. This is a working plan, not a claim that all listed features are complete.

## Development principles

- Prioritize the working **main Moonrise application** with real Lunar Client over endless isolated test harnesses.
- Preserve stable launch behavior. Compatibility changes across Weave generations must be controlled and reversible.
- Preserve original JARs, user packages, settings, sessions and authentication. Operate on user data only when necessary and with a backup.
- Never infer universal compatibility from a single successful launch; record exact results and boundaries.
- Public server connections, publication, releases, push/merge and irreversible actions require separate authorization.
- **Do not develop new mod profiles yet**; they are outside the current priority. Existing loadout support does not authorize expansion.

## Stage 1 — Core: technical foundation and stability

**Status: IN PROGRESS. Do not close until the remaining acceptance checks pass.**

Goal: stable, predictable Lunar launches through Moonrise with supported Weave mods and Java agents, process management, error recovery and trustworthy diagnostics.

### Achieved / user-reported evidence

- Real Lunar Client with Minecraft 1.8.9 launches through Moonrise. Recorded baseline: Windows x64, Lunar Launcher 3.7.23-ow and Lunar Java 17.0.18.
- RavenWeave 1.1.4 + maintained BWH were confirmed together in the real client using one Weave Loader 1.3.4.
- The user confirmed Raven + BWH + unrestricted + Veyra + Cosmetics in the main build, and ordinary-mode operation. Exact adapters/maintained builds are involved; this does not certify arbitrary originals.
- Cosmetics startup was repaired using the separate runtime build 1.3.1; the current standalone packaging fix is **1.3.2**, with user-confirmed local cosmetics operation and an ordinary-mode regression PASS.
- Standalone Veyra operation was user-confirmed. Its recorded original artifact is the same pinned artifact supplied as catalog version 0.1.1-moonrise.1.
- Fifteen original user JARs were preserved and their hashes matched the library index. Earlier integration work reported 40 targeted passing tests.
- Tray lifecycle fixes are implemented. The earlier 25 targeted tests and subsequent lifecycle checks passed; full fresh visual and close-during-cleanup acceptance remains open.
- Ordinary-mode repeat launch from the same Moonrise instance passed: JVM exit 0, normal exit, graceful Lunar shutdown, no remaining owned processes and session removal.
- The final Core QA recorded 743 passing automated tests, a successful workspace Release build/publish and second-instance activation checks. These do not constitute a new real Minecraft certification. Its missing-artifact finding predates the subsequent Final Catalog & Build Fix: the current snapshot includes all four explicitly allowed, pinned catalog/runtime JARs and mixed-Weave license/source materials.

### Remaining work

1. Test remaining supported combinations from the main local library, including untested Weave mods and agents. Never automatically alter the user's selection. Keep exact original/runtime identities in evidence.
2. Complete the **Moonrise ↔ Lunar lifecycle** matrix: launch, repeat launch, already-running Lunar, close/hide Moonrise, close Lunar, launch after exit, crashes/timeouts, extra processes and blocking windows. The ordinary repeat-launch case passed; the matrix remains incomplete.
3. Complete a **visual tray smoke test** on the corrected NotifyIcon build: one icon, hide, restore, normal exit and update restart. Distinguish real duplicates from stale Explorer icons.
4. Verify clear errors for missing compatibility builds, incompatible JARs and failed bootstrap, and usable recovery after failure. Automated checks are not a substitute for real acceptance.
5. Preserve independence of ordinary mode and explicitly enabled experimental mixed-Weave mode. Reject unsupported legacy contracts. The current experimental legacy ABI allowlist is bounded to the exact supported Raven artifact.
6. Preserve known diagnostic boundaries: Raven's original `changeYaw` lacks a callsite in the inspected bytecode; BedPlates shader issues and some upstream authentication-service connectivity errors were reported. Do not hide or change these without reproduction.
7. Preserve the Final Catalog & Build Fix artifact inclusion and exact hash verification; accept the final clean-source/release pipeline using the current complete snapshot. The earlier QA missing-artifact finding is historical, not evidence that these files are still absent. Do not omit required functionality to obtain a build PASS.

**Definition of Done:** stable current main build; real targeted mod/agent checks; repeatable launch/exit/error lifecycle; visual tray acceptance; preserved user data and ordinary mode; explicit known limitations. Record unsupported third-party boundaries without presenting the entire library as universally compatible.

## Stage 2 — Experience: interface and settings

**Status: PLANNED; broader work waits for Stage 1 acceptance.**

- Polish design, UI states, accessibility and feedback.
- Themes and languages. Existing English/Russian controls and appearance settings provide a foundation, not completion of the stage.
- UI performance and responsiveness.
- Convenient Java, JVM argument and RAM configuration.
- Consider other Minecraft/Lunar versions after a separate feasibility review; current support remains 1.8.9.

## Stage 3 — Catalog & Cosmetics: content and ecosystem

**Status: PLANNED, with a limited implemented catalog foundation.**

- Catalog of Weave mods, Java agents and cosmetics. The current offline catalog contains Cosmetics 1.3.2 and Veyra 0.1.1-moonrise.1; broader discovery/compatibility remains future work.
- Installation, management and updates of supported packages. Local installation/enable/disable/removal exists; this does not establish a complete automatic package-update ecosystem.
- **Moonrise Cosmetics Network**: a future voluntary system to synchronize and display cosmetics **between Moonrise users**, using the Cosmetics agent and a Moonrise-owned service with an authorized data channel.
- Define consent, privacy, disabling network cosmetics and version compatibility before implementing the service.
- **Do not promise** that locally unlocked cosmetics are visible on ordinary Lunar **without the agent**. Research dated 2026-10-12 remains `NOT VERIFIED`: LunarQT/LCT demonstrated local cosmetics changes, not this remote visibility. Official Lunar/Apollo did not establish a confirmed public route for arbitrary real-player cosmetics.

**Moonrise Cosmetics Network remains a future goal, not an implementation in progress.**

## Stage 4 — Expansion: other clients and platforms

**Status: LONG-TERM PLAN.**

- Research other clients, particularly Badlion Client.
- Evaluate macOS and other platforms.
- Perform a separate technical/product review for every platform; do not promise compatibility in advance.

## Stage 5 — Launch: public product

**Status: LONG-TERM PLAN, with release foundations implemented and acceptance outstanding.**

- Final branding/name and website. No public website URL is promised.
- Stable installer, updates, failure recovery and release process. Inno/ZIP packaging and Velopack beta UI/service exist; production Velopack packaging/feed and actual installed-update recovery remain to be accepted.
- CI/CD, targeted automated checks, diagnostics and monitoring. Existing workflows and local diagnostics are foundations; they do not imply a telemetry service or complete reproducibility.
- Prepare the full public release after Core and critical scenarios stabilize. The owner publishes GitHub Releases manually; no public release is currently available.

## Future backlog and research

These directions remain research, not supported runtime features, and do not authorize work before Core acceptance:

- Investigate **Meowtils** and third-party **Java-agent loaders**, their contracts, licensing and interaction with Lunar/Weave. No universal agent compatibility claim.
- Evaluate **additional mod formats** through separate feasibility and licensing reviews. Forge/Fabric are not currently supported.
- **Dynamic Extensions / hot-reload research**: lifecycle, isolation, unload safety and compatibility. No runtime hot-reload guarantee.
- Broader automated coverage of official launcher/profile format changes.
- Clearer privacy review before exporting diagnostic bundles.
- Expanded end-to-end UI and clean-checkout release tests.

## What to do now

**Stage 1 only.** Find reproducible remaining technical issues in main Moonrise and address them one at a time using targeted checks and real local smoke tests. Do not rewrite the working Raven+BWH adapter or begin Stage 2–5 in parallel. Documentation preparation does not close Core. After acceptance, report PASS/BLOCKED against Stage 1 criteria and separately agree the transition to Stage 2.

## Roadmap maintenance

1. Treat this as the complete previously agreed five-stage plan, rather than the earlier short README list.
2. Maintain **one canonical repository document**, `docs/roadmap.md`; do not create competing plans or overwrite unrelated decisions.
3. Both READMEs link here; preserve the desktop source document.
4. Keep Stage 1 **in progress** and do not start Cosmetics Network now.
5. Return to the active Core milestone after documentation preparation; roadmap availability alone is not a reason to interrupt recovery.
