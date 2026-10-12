# Final catalog and clean build verification — 2026-10-12

## Implemented

The existing Catalog page is visible. Only Moonrise Cosmetics 1.3.2 (Java
Agent) and Veyra 0.1.1-moonrise.1 (Weave Mod) are listed. Both use bundled,
SHA-256-pinned JARs, without remote downloads. Existing search, type filter,
sorting, details, install actions and the local Library are retained. Empty
download/install-count sort options were removed. Installation opens Library;
new packages stay disabled until the user enables them. Duplicate installation
preserves the existing enabled state. Veyra's absent metadata version is supplied
for its exact reviewed SHA-256 only; its JAR bytes remain unchanged.

Exact ignore exceptions retain the three formerly missing runtime artifacts
and the reviewed Veyra artifact as files eligible for Git. Repository and
release safety checks pin them individually; arbitrary JARs remain forbidden.
Normal .NET builds no longer require private input JARs or a JDK. The mixed
adapter's GPL license, corresponding upstream source snapshot, modification
sources and build script accompany the published application. The user
confirmed permission to distribute Veyra in this chat.

## Verification

- Exported only existing files enumerated by
  `git ls-files --cached --others --exclude-standard` into
  `artifacts/catalog-final/clean-source`, without ignored runtime/build files.
  This is a clean source export of pending changes, not a checkout of the old
  committed HEAD. No commit, staging, push or GitHub Release was performed.
- Locked restore and Release solution build in this export: PASS, zero
  warnings/errors. All four reviewed JARs are included by the source export.
- 75 targeted Release tests: PASS. Bundled installation of both real artifacts,
  kind/version, disabled default, enable/disable, persisted selection, duplicate
  import, original byte preservation and corrupt-bundle rejection are covered.
  Existing catalog, Library, launch-plan, mixed adapter and maintained-build
  tests also passed. The full 743-test suite was not repeated.
- Self-contained, single-file win-x64 publish: PASS, output
  `artifacts/catalog-final/Moonrise/Moonrise.exe`. Keep the entire Moonrise
  directory, including runtime JARs and license/source files, when distributing.
- All required runtime JARs and the Veyra bundle matched their pinned hashes.
- Actual release script safety block: PASS. A temporary wrong-hash Veyra in
  the disposable publish was rejected; original bytes were restored and the
  safety check passed again.
- Published EXE Catalog UI startup/render/clean exit: PASS (exit 0), using
  isolated visual QA data. Final screenshot visually inspected:
  `artifacts/catalog-final/catalog-final.png`.
- Repository safety and task-file diff whitespace checks: PASS.

## Limits

No new real Lunar/Minecraft session was started. Prior standalone Veyra and
Cosmetics evidence in stage1-core-validation-20261012.md applies to the same
exact hashes. Preserved Raven+BWH behavior has targeted automated coverage;
its real integration was not repeated. Working installation, local user JARs,
settings and accounts were not touched. No technical build/catalog publication
blocker remains in this pass; fresh real-game acceptance and prior installer/
update acceptance checks are not newly certified here.
