# Application updates

Moonrise checks the official `ZOONGG/Moonrise` GitHub Releases feed. The stable
channel ignores drafts and prereleases. Users can explicitly enable the
prerelease channel in Settings.

When a newer version is available, Moonrise shows its version and release notes.
It does not download or execute anything until the user confirms. The updater:

1. Downloads the exact `Moonrise-Setup-<version>-x64.exe` asset and its adjacent
   `.sha256` file over HTTPS from an approved GitHub host.
2. Validates the checksum syntax, filename, and SHA-256 digest.
3. Checks Authenticode trust when a signature is present.
4. Warns again when the checksum is valid but the release is unsigned.
5. Starts the normal visible installer and closes Moonrise gracefully.

A mismatched hash, a checksum naming a different file, an invalid signature, an
unapproved host, or a malformed release response aborts the update.

Releases are unsigned until a code-signing certificate exists. SHA-256 protects
against transfer corruption and unintended substitution, but it is not a
replacement for publisher identity. The UI and documentation state this
limitation rather than trusting a binary based only on its filename.

User data is outside the installation directory, so Setup upgrades preserve the
package library, settings, cache, sessions, and logs.
