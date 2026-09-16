# Moonrise catalog architecture

Moonrise keeps the Lunar launch path independent from package discovery and installation.

## Launch pipeline

1. Moonrise reads the selected Lunar profile and Minecraft version from its own settings.
2. The official Lunar Launcher remains the only owner of account authentication and session data.
3. Enabled Weave mods are copied into an isolated launch-session directory. The original imported JARs are not modified.
4. Enabled Java agents and the verified Weave loader are passed to the native bridge.
5. Moonrise starts the official Lunar executable and waits for Lunar to confirm the selected profile in its normal launcher log.
6. Moonrise dispatches the official `lunarclient://launch` deep link and observes the resulting game process.

The catalog client never participates in authentication and never executes JARs while reading metadata.

## Services

- `ICatalogService` provides a versioned `CatalogIndex` and reports whether it came from the offline cache.
- `CatalogClient` handles HTTPS, timeout, retry/backoff, ETag, `If-Modified-Since`, atomic cache replacement, and offline fallback.
- `CatalogClient` verifies signed production metadata against an embedded Ed25519 trust root before replacing the cache; developer catalogs are explicitly unsigned.
- `CatalogManifestService` performs strict typed deserialization and validation.
- `ManagedPackageInstaller` downloads into a temporary file, checks size and SHA-256, validates the ZIP structure, detects the package type, resolves dependencies/conflicts, and atomically replaces a managed package with rollback.
- `PackageCatalogService` continues to own manually imported JARs.
- `DeveloperPackageInspector` reads archive metadata and class headers without decompiling bytecode.

Mutable production data is stored under the current user's local application-data directory. Repository-local paths remain available when running directly from a source checkout so development does not affect an installed copy.

The public catalog endpoint is maintained in `ZOONGG/Moonrise-Catalog`. `sample-catalog.json` remains a neutral, metadata-only development fallback with no JAR.
