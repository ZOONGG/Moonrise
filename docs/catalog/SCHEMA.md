# Catalog schema

The current `CatalogIndex.schemaVersion` is `1`. Unknown versions are rejected before any entry is displayed or installed. `signatureStatus` distinguishes `unsignedDevelopment` from `signedProduction`.

## CatalogIndex

- `schemaVersion`
- `generatedAt`
- `signatureStatus`
- `packages`: array of `PackageManifest`

## PackageManifest

- identity: `id`, `slug`, `type`, `name`
- presentation: `summary`, `description`, `authors`, `tags`, `icon`, `screenshots`
- upstream: `homepage`, `repository`, `license`
- compatibility: `supportedMinecraftVersions`, `supportedClientVersions`
- lifecycle and policy: `archived`, `riskLevel`, `installPolicy`, `restricted`, `featured`, `recommended`, `releases`
- relationships: `dependencies`, `conflicts`
- informational notes: `warnings`
- optional counts: `statistics.downloads`, `statistics.moonriseInstalls`

## PackageRelease and PackageArtifact

A release includes `version`, `publishedAt`, `changelog`, an `artifact`, and optional release-specific dependencies/conflicts. An artifact supports these download modes:

- `githubRelease`
- `upstreamDirect`
- `upstreamRaw`
- `moonriseRelease`
- `sourceOnly`
- `externalPage`
- `unavailable`

Downloadable artifacts require an HTTPS `assetUrl`, a `.jar` `fileName`, positive `fileSize`, and a 64-character SHA-256 value. Source-only and external-page entries stay visible without pretending that a JAR is available.

Risk and install-policy fields describe factual behavior and required handling. Restricted entries cannot be featured or recommended. Integrity checks are not presented as proof that third-party code is harmless.
