# Moonrise Cosmetics extension

Moonrise-owned Java agent **1.3.2** for local Lunar cosmetics. The current
built-in Moonrise Catalog supplies the SHA-256-pinned artifact for offline
installation: choose Moonrise Cosmetics, confirm installation, then enable it
in Library → Java agents. Manual trusted JAR import is also supported. Current
runtime evidence targets Lunar Launcher 3.7.23-ow / Minecraft 1.8.9 with Lunar
Java 17.0.18; future Lunar versions require revalidation. Standalone local
cosmetics and ordinary-mode regression were user-confirmed; full standalone
close-during-cleanup acceptance remains open.

The implementation is original source code. It bundles the BSD-licensed ASM
dependency from the SHA-256-pinned Weave archive, including its license notice,
so startup does not depend on enabled Weave mods. It does not bundle Weave
bootstrap/loader code or another launcher. The extension keeps Lunar's
Authenticator, AssetServer, API, and Styngr services on their official defaults
and unlocks the current v2 cosmetics response locally. This avoids protocol
breakage when a third-party WebSocket service falls behind Lunar updates.

All cosmetics become available in the local Lunar instance. This is a local
visual feature; clients without the extension are not promised to receive the
same models.

The current outfit and selected outfit tree are stored under
`%LOCALAPPDATA%\Moonrise\cosmetics` and replayed after the next successful
cosmetics login. Only protobuf outfit requests are stored; account and session
files are not read. Persistence code is transplanted into Lunar's v2 service
stub so it remains visible inside the isolated Genesis classloader.

## Build

Run from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File extensions/Moonrise.Cosmetics/build.ps1
```

The script downloads the SHA-256-pinned official Weave Loader 1.3.4 only as a
build dependency, bundles its shaded ASM classes, builds
`user-agents/Moonrise-Cosmetics.jar`, checks standalone premain without Weave,
and runs a
structural probe against the locally installed Lunar JAR when it is available.

## Disconnect stability

Current Lunar clears cosmetics stores whenever the AssetServer WebSocket drops.
The extension preserves those stores during transient reconnects, reduces the
initial retry delay from 15 seconds to 1 second, and caps backoff at 5 seconds.
The transform is selected by stable protocol/log markers rather than obfuscated
class names, and safely becomes a no-op when a future Lunar layout is unknown.
