# Windows installation

The first public beta uses Velopack 1.2.161: `Moonrise.App-beta-Setup.exe`.
Package ID `Moonrise.App`, display name Moonrise, channel `beta`, version `0.1.0-beta.1`.
The ID deliberately differs from the data directory to prevent install/update/uninstall from owning user data.
Default installation: `%LOCALAPPDATA%\Moonrise.App`; settings and packages: `%LOCALAPPDATA%\Moonrise`.
Setup is per-user, self-contained win-x64 and unsigned. No administrator rights or separate .NET runtime are required.
Check its SHA-256, run Setup, then use the Moonrise shortcut. Normal uninstall uses Windows Installed Apps.
Do not install into the Moonrise data directory. Old Inno installations are a separate distribution; they do not gain Velopack updates automatically.
The Velopack installer does not promise the old Inno wizard or moonrise:// registration.

Build using the pinned local CLI:

```powershell
dotnet tool install vpk --version 1.2.161 --tool-path artifacts/velopack-tools
./tools/New-MoonriseRelease.ps1 -Version 0.1.0-beta.1 -Velopack -VpkPath artifacts/velopack-tools/vpk.exe -OutputRoot artifacts/final-package
```

The script verifies the bundled runtime JAR digests and corresponding source archive before packaging.
Production builds must omit LocalUpdateFeed. Builds that specify it are local-only Moonrise.UpdateSmoke packages and must never be published.
See [updates](UPDATES.md), [ZIP behavior](PORTABLE_MODE.md), and [final validation](final-release-validation.md).
