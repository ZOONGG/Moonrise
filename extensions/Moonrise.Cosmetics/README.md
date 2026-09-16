# Moonrise Cosmetics extension

Optional Java agent for Lunar Client cosmetics. Moonrise itself stays
mod-agnostic; users who want this feature build the extension and place its JAR
in `user-agents/`. Its manifest explicitly declares the agent as Lunar-only so
incompatible Lunar versions can reject it instead of loading the transformers.

The implementation is original source code. It does not bundle, launch, load,
or copy another launcher, JAR, or its classes. The extension keeps Lunar's
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
compile-time dependency, builds `user-agents/Moonrise-Cosmetics.jar`, and runs a
structural probe against the locally installed Lunar JAR when it is available.

## Disconnect stability

Current Lunar clears cosmetics stores whenever the AssetServer WebSocket drops.
The extension preserves those stores during transient reconnects, reduces the
initial retry delay from 15 seconds to 1 second, and caps backoff at 5 seconds.
The transform is selected by stable protocol/log markers rather than obfuscated
class names, and safely becomes a no-op when a future Lunar layout is unknown.
