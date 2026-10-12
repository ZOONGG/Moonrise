# Final release validation

Status: READY WITH LIMITATIONS. Version 0.1.0-beta.1; suggested tag v0.1.0-beta.1.
The owner creates the prerelease and tag manually. No Release, draft, tag, asset upload or deploy is performed by this preparation workflow.

## Distribution

Velopack CLI/SDK 1.2.161, Windows x64, self-contained .NET 8.
Production ID Moonrise.App, display name Moonrise, beta channel.
Install root defaults to %LOCALAPPDATA%\Moonrise.App, user data stays in %LOCALAPPDATA%\Moonrise.
The local-feed acceptance ID is Moonrise.UpdateSmoke.App; its data root remains Moonrise.UpdateSmoke/<feed-hash> outside that installation.
Production packages contain no MoonriseLocalUpdateFeed assembly metadata.
Cosmetics 1.3.2 and Veyra 0.1.1-moonrise.1 are bundled with reviewed SHA-256 pins, as are the mixed adapter JARs and corresponding GPL source/license archive. No third-party license was invented.

Final assets are in artifacts/final-release. Upload these six files together:

- Moonrise.App-beta-Setup.exe
- Moonrise.App-beta-Portable.zip
- Moonrise.App-0.1.0-beta.1-beta-full.nupkg
- releases.beta.json
- RELEASES-beta
- SHA256SUMS.txt

Setup, full package and releases.beta.json provide installation and the current beta updater. RELEASES-beta is the generated legacy feed included for tooling compatibility. The ZIP is optional for users; extract the whole archive. It uses the shared Moonrise data root, not an EXE-relative portable library, and automatic updates are disabled. assets.beta.json is a CLI upload index, not required by Moonrise's GithubSource, and is not a release asset.
Release Notes are in docs/RELEASE_NOTES.md and copied into the final directory. PUBLICATION.md in that directory is the owner checklist, not an upload asset.

## Evidence from this pass

- Repository safety scan: PASS, approved binary paths and exact JAR/source-archive hashes, text secret-signature scan without exposing matched values.
- 89 targeted Release tests: PASS, covering updater, update UI state, actual NotifyIcon disposal, catalog, launch UI and service behavior. The previously completed 743-test full suite was not repeated locally.
- Clean export of the staged Git tree: locked restore and Release solution build PASS with zero warnings/errors; 142 targeted tests PASS, including normal launch-plan selection, experimental mixed-Weave and package persistence. All four pinned runtime JARs and the GPL source archive are present without ignored/private input assets.
- Production release publish and Velopack packaging: PASS; native bridge, four runtime JARs and license/source archive included. Package version and EXE ProductVersion match.
- Real production Setup in an isolated repository artifacts directory: PASS. Installed QA UI launch and graceful exit, uninstall and external-data sentinel preservation: PASS. Evidence: artifacts/installer-smoke-860f5e0b3dcf43329980123f1a00bd74/result.json and installed-launch.png.
- Real installed beta.1 to beta.2 update: PASS. The helper runs from the installed current directory, uses the actual WindowsVelopackLocator, SimpleFileSource, Moonrise UpdateService and native Update.exe. It detects and downloads beta.2, stages it, applies and restarts the installed Moonrise executable. The restarted UI screenshot is from beta.2; a subsequent check finds no newer/pending update. External settings/package sentinel hashes remain unchanged. Uninstall: PASS. Evidence: artifacts/update-smoke-ce118f7582544105a0bc2e75f4fdffb0/result.json, apply.stdout.txt and restarted-beta2.png.
- Corrupt local package: PASS, ChecksumFailedException, no pending update, beta.1 remained installed; normal download subsequently succeeded.
- Updater UI, tray handoff and real library/settings loading are bypassed by the QA restart. Sentinels prove file retention, not real settings schema loading. This is an actual installed native update smoke, with those specific limits.
- No official Lunar or user Moonrise process, account store or user library was modified. The installer scripts refuse existing Moonrise.App registrations or Moonrise shortcuts. All app UI launches used explicit isolated QA roots.
- No new real Lunar/Minecraft launch was performed. Earlier recorded normal/experimental runtime evidence and source paths are retained.
- Both CI workflows only build/test/package and upload Actions artifacts. release.yml is workflow_dispatch only, contents: read, with no release/tag publication step.

## Remaining acceptance checks

Installers are unsigned; SmartScreen and behavior on a Windows host without .NET are not certified here. Automatic rollback after a failed native apply is not proven.

Before public publication, use a disposable Windows VM/account to perform the interactive local-feed plan in installed-update-smoke.md. Rebuild its packages with New-MoonriseInstalledUpdateSmoke.ps1 into a fresh root; do not publish them. Exercise the actual Updates UI, tray exit, settings/language persistence, disabled/enabled test package state, Minecraft/activity apply guard and failed-apply recovery from a VM snapshot. If available, use a VM without .NET.

After owner publication:

1. Create prerelease v0.1.0-beta.1 for the final main commit and upload exactly the six assets above; paste RELEASE_NOTES.md. Do not upload beta.2 smoke builds or source/publish folders.
2. In a clean disposable Windows account install the public Setup and verify ProductVersion, catalog versions, first launch and normal tray exit. Change language, install/enable only disposable test packages; record hashes and back up that test data.
3. Manual Check for updates should reach the public ZOONGG/Moonrise GitHub Releases beta feed and show no newer update. With no newer published version, public detection/download/apply cannot be certified.
4. When the owner later publishes a real higher beta.N with the same Moonrise.App ID/channel, use Settings > Updates: discover, download, check pending restart, apply/restart, compare package hashes and settings/library state, then confirm no repeat download. Test offline-feed retry separately; use a disposable VM snapshot for corrupt-download and apply-failure recovery.
5. Do not describe manual reinstall/snapshot restoration as successful automatic rollback.

Reproduce the service/native smoke locally, only when no production registration/shortcuts exist:

```powershell
dotnet build tools/ReleaseUpdateSmoke/ReleaseUpdateSmoke.csproj -c Release -o artifacts/update-helper
./tools/New-MoonriseRelease.ps1 -Version 0.1.0-beta.2 -Velopack -VpkPath artifacts/velopack-tools/vpk.exe -OutputRoot artifacts/final-update-test -SkipTests
./tools/Test-MoonriseInstalledUpdate.ps1 -SetupPath artifacts/final-release/Moonrise.App-beta-Setup.exe -FeedDirectory artifacts/final-update-test/velopack
```

The test helper is never a release payload. Beta.2 is never published by these scripts.
