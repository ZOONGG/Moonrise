## Summary

Describe the problem and the focused solution.

## Validation

- [ ] `dotnet build Moonrise.sln -c Release`
- [ ] `dotnet test Moonrise.sln -c Release`
- [ ] Clean `win-x64` publish, when packaging or runtime files changed
- [ ] Launch, Library, Diagnostics, and Settings pages exercised, when UI changed
- [ ] English and Russian user-facing text remain in parity

## Public-release safety

- [ ] No JARs, account/session files, tokens, passwords, personal paths, logs, settings, caches, private code, or unlicensed binaries are included
- [ ] Authentication remains inside the official Lunar Launcher
- [ ] The core remains mod-agnostic
- [ ] Documentation and screenshots contain only sanitized data

## Compatibility and risk

List affected client/Minecraft versions, behavior changes, and any known limitations.
