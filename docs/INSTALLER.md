# Moonrise installer

Moonrise stable releases include a per-user Inno Setup installer named
`Moonrise-Setup-<version>-x64.exe`. It contains a self-contained `win-x64`
Release publish, so a separate .NET installation is not required.

The default installation directory is:

```text
%LocalAppData%\Programs\Moonrise
```

The installer does not require administrator rights by default. It provides a
license page, destination selection, a Start Menu shortcut, an optional desktop
shortcut, Add/Remove Programs registration, upgrade support, and a launch
checkbox. The stable Inno `AppId` makes an install over an older version an
upgrade rather than a second application.

Application files and user data are separate. Normal upgrades and normal
uninstallation preserve `%LocalAppData%\Moonrise`. The uninstaller displays an
unchecked option to remove settings, the package library, downloads, cache,
sessions, and logs. Selecting it permanently removes that directory.

The installer registers `moonrise://` for the current user and removes the
registration on uninstall. Supported links are:

- `moonrise://catalog`
- `moonrise://package/<package-id>`
- `moonrise://install/<package-id>`

Package identifiers are strictly validated. An install link opens and selects
the catalog card, then asks for confirmation; it never silently installs code.

Releases are currently unsigned until a code-signing certificate is available.
Windows may therefore show a SmartScreen warning. Always compare the Setup
SHA-256 value with the adjacent checksum file from the official GitHub Release.

## Local release build

Install Inno Setup 6, then run:

```powershell
./tools/New-MoonriseRelease.ps1 -Version 1.0.0
```

The script restores locked packages, runs Release tests, publishes self-contained
`win-x64`, creates the portable ZIP, compiles Setup, and writes SHA-256 files.
It replaces the previous selected release output directory before building.
