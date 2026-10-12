# Existing-account session snapshot: safety decision

2026-10-08, branch `codex/controlledgenesis-stage2`. User-confirmed runtime baseline:
Lunar 2.23.1-2641 / repository commit `90572e6`. Existing pending changes preserved.
Uses the account-lifecycle and Stage 2 auth/resource reports; no general audit,
real credential-file read, game launch, auth RPC, or production integration.

## Decision

**REAL SMOKE: NO. READY FOR PRODUCTION ACCOUNT INTEGRATION: NO.**

A private disposable copy is technically feasible, but safe authenticated bootstrap
under the requested contract is not established. Keeping the official launcher as
auth broker does not make its refresh operation read-only. The existing lifecycle
evidence says RefreshAccount/initAuth can select/refresh and write the official
store before returning success. The isolated game subsequently reloads its own
snapshot, which has not received those new credentials. Selection may therefore
both change official state and leave the game stale. No real selection was tested.

If refresh instead occurs exclusively against a copied refresh token, a provider
may rotate or invalidate that token or its family. Discarding the new token at
cleanup can then leave the original store with unusable authorization. Immediate
invalidation, grace periods, reuse detection, and actual provider behavior are
unknown here. Read-only source access and unchanged source hashes do not prove
continued authorization validity. Copy-back is prohibited and would not solve
the ownership contract. There is no established official non-mutating bootstrap
contract that excludes either failure mode; no real experiment is justified.

## Synthetic evidence

Run `tools/Test-ControlledGenesisAccountSnapshot.ps1` locally on Windows. It has
no source-path parameter, reads no installed Lunar data, starts no JVM, and makes
no network requests. Ten checks passed:

- read-only source handle denies concurrent write while copying;
- snapshot bytes initially match source;
- inherited snapshot DACL grants access only to the current user SID;
- writes to session snapshot leave synthetic source unchanged;
- modeled rotation invalidating the old token leaves original authorization stale;
- modeled official broker write changes source despite game filesystem isolation;
- allowlisted diagnostic fields omit synthetic secret values;
- failed and successful session cleanup remove the snapshot directory;
- final cleanup removes the entire synthetic tree.

The broker/rotation checks are explicit counterexample models, not actual Lunar
execution or a token-server test. The synthetic broker deliberately writes only
the generated synthetic source to demonstrate the RPC side-effect boundary.
No actual JVM lifecycle/refresh/selection/UI behavior is claimed by these checks.

## Limits of filesystem safety

The DACL excludes other ordinary users; it does not isolate same-user processes,
administrators, SYSTEM, or malicious code in the JVM. A read handle blocks writes
only while open; retaining it for the session would interfere with the official
owner and may cause failed persistence after a successful remote rotation.

The synthetic harness exports fixed diagnostic names only, never runtime output
or credential files. This does not establish that Lunar logs, JVM/native dumps,
WER, pagefile, backup/indexing tools, or existing crash bundles cannot retain
credentials in a real run. Key-based log redaction is insufficient for arbitrary
secret values. Those real-runtime containment requirements remain unverified.

Deletion proves pathname absence after normal code/finally completion only, not
secure erasure from storage or RAM. Abrupt harness death, OS crash and power loss
can bypass finally. A real implementation would need verified process-lifetime
ownership and recovery, without collecting credential-bearing crash artifacts.
No claim of complete secret removal after such failures is made.

## Outcome and validation

Existing accounts in game, switching, Singleplayer and Multiplayer screens:
**NOT TESTED**. Official account store: not accessed or modified by this task.
Synthetic files: absent after cleanup; real-runtime residual secrets unverified.
Production auth gate, launch Java/Weave/mod behavior and Moonrise account UI:
unchanged. No request for real-account consent because the safety prerequisite
failed. No push, merge or release.

Only an offline PowerShell probe and this report were added. The probe passed
10 checks; `git diff --check` passed. No application code changed, so no .NET
build was run. This snapshot is not an account system or production-ready work.
