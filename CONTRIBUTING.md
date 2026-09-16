# Contributing to Moonrise

Thank you for helping improve Moonrise.

## Before you start

- Search existing issues and pull requests.
- Use a focused branch and keep unrelated changes separate.
- For a security vulnerability, follow [SECURITY.md](SECURITY.md) instead of opening a public issue.
- Keep the core application mod-agnostic. Package-specific compatibility belongs in the package or a separately distributed extension.
- Preserve the official Lunar Launcher boundary: no custom login, token extraction, session recreation, or account-file access.

## Development setup

Requirements:

- Windows 10 or 11 x64
- .NET 8 SDK
- Git and PowerShell

Run the standard validation:

```powershell
dotnet restore Moonrise.sln
dotnet build Moonrise.sln -c Release --no-restore
dotnet test Moonrise.sln -c Release --no-build
dotnet publish src/Moonrise/Moonrise.csproj -c Release -r win-x64 --self-contained true -o release/Moonrise
```

For UI changes, launch the Release build and exercise Launch, Library, Diagnostics, Settings, language switching, window controls, and the notification-area menu.

## Change requirements

- Add or update tests for metadata parsing, settings, package safety, launch options, and other changed behavior.
- Add English and Russian text together for every user-facing control or message.
- Keep paths relative in project, workflow, and documentation files.
- Update screenshots only from the real application with sanitized demo data.
- Regenerate branding with `tools/generate-brand-assets.ps1`; do not hand-edit raster exports.
- Do not weaken token redaction, package boundaries, or official authentication behavior.

## Public-release safety

Never commit or attach:

- mod or Java-agent JARs;
- Microsoft, Lunar, or Minecraft account/session files;
- tokens, passwords, credentials, private keys, or full command lines;
- user settings, logs, crash dumps, caches, local paths, or personal identifiers;
- proprietary client files, decompilations, game assets, or unlicensed binaries.

Before committing:

```powershell
git status --short
git diff --check
git diff --cached
git ls-files "*.jar" "*.exe" "*.zip" "*.log"
```

The tracked native launch component is the only intentional runtime binary exception. Do not stage generated release output.

## Pull requests

Describe the problem, the scope of the solution, tests performed, and any remaining compatibility risk. Small, reviewable commits are preferred. By contributing, you agree that your contribution is licensed under the repository's MIT License.
