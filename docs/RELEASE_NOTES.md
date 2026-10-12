# Moonrise 0.1.0-beta.1

First public Windows x64 beta of Moonrise, a companion launcher and package manager for existing official Lunar Client 1.8.9 profiles.

- Offline catalog includes Moonrise Cosmetics 1.3.2 and Veyra 0.1.1-moonrise.1 with pinned SHA-256 checks.
- Import and manage local Weave mods and Java agents; third-party originals remain unchanged.
- English and Russian interface, local diagnostics, launch lifecycle and tray controls.
- Self-contained Windows x64 Velopack Setup and complete no-install ZIP; no separate .NET installation required.
- Installed builds support the beta update feed on this repository's GitHub Releases. ZIP builds do not support automatic updates.

Install using `Moonrise.App-beta-Setup.exe`. For the ZIP, extract the entire archive before running Moonrise.exe. Both use `%LOCALAPPDATA%\Moonrise` for settings and packages. Official Lunar Launcher and authentication are required; Moonrise does not supply Minecraft or Lunar accounts.

This beta is unsigned. Check SHA-256 before installation. Live GitHub update delivery and automatic rollback require the post-publication acceptance checks in docs/final-release-validation.md. Experimental mixed-Weave support remains experimental; compatibility depends on the package and Lunar build.
