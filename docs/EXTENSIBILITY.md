# Moonrise extensibility and secure distribution

Moonrise keeps user-installed extensions outside the application package. The directories below are created at runtime and are intentionally excluded from Git and releases.

## Portable loadouts

Files ending in `.moonrise.json` contain only package metadata, enabled state, and SHA-256 values. They never contain JAR bytes or account information. A profile applies an entry only when a locally installed package has the same kind and SHA-256.

## Legacy user-selected signed package catalogs

A catalog is distributed as three files:

```text
catalog.json
catalog.json.sig
catalog.json.pub
```

This legacy import format uses a Base64-encoded RSA-PSS/SHA-256 signature over the exact UTF-8 bytes of `catalog.json`. The public-key file is a PEM SubjectPublicKeyInfo RSA key.

Moonrise's production discovery catalog is separate: it uses Ed25519 over `index.json` and an application-embedded public key verified through an independent trusted channel. Unsigned development indexes are rejected outside developer mode.

Each package entry contains `identifier`, `name`, `version`, `kind`, an HTTPS `downloadUrl`, `sha256`, and `gameVersions`. Moonrise verifies the catalog signature before displaying entries and verifies the downloaded JAR hash before importing it. Catalogs do not ship with Moonrise, so the core remains mod-agnostic.

## Client plugins

Client adapters use an external process protocol. Moonrise does not load plugin assemblies into its own process. Place a plugin executable and a `*.moonrise-plugin.json` manifest under `plugins/`:

```json
{
  "schemaVersion": 1,
  "id": "example-client",
  "name": "Example Client",
  "executable": "Example.Adapter.exe",
  "supportedGameVersions": ["1.8.9"]
}
```

On launch, the adapter receives `--moonrise-request <path>`. The JSON request contains the client id, Minecraft version, safe-mode state, working directory, and enabled local package paths. It does not contain launcher accounts, sessions, passwords, or tokens. The adapter is responsible for using its client's official authentication and launch route.

## Language and theme packs

Language files use the suffix `.moonrise-language.json` under `language-packs/`:

```json
{
  "schemaVersion": 1,
  "code": "de",
  "name": "Deutsch",
  "translations": { "Launch": "Starten", "Settings": "Einstellungen" }
}
```

Missing translations fall back to English. Russian and English remain built in and have matching controls.

Theme files use the suffix `.moonrise-theme.json` under `themes/` and define `id`, `name`, `windowBackground`, `panel`, `panelSecondary`, `border`, `muted`, and `accent` as `#RRGGBB` or `#AARRGGBB`. Moonlight, Midnight, and High contrast are built in.

## Signed releases and automatic updates

Tagged releases require these GitHub Actions secrets:

- `WINDOWS_SIGNING_CERT_BASE64`: Base64 representation of a trusted code-signing PFX.
- `WINDOWS_SIGNING_CERT_PASSWORD`: the PFX password.
- `PRIVATE_NAME_PATTERNS`: optional comma/newline-separated private names that must not appear in tracked text.

The release workflow signs `Moonrise.exe` with Authenticode and a trusted timestamp, verifies the signature, creates a deterministic ZIP, and publishes a SHA-256 file. The updater requires HTTPS, the matching SHA-256, a path-safe archive, and a valid trusted Authenticode signature through Windows WinTrust before scheduling replacement after Moonrise exits.
