# Security policy

## Supported versions

Security fixes target the latest published Moonrise release and the current `main` branch. Older builds may be asked to reproduce on the latest release before receiving a fix.

## Reporting a vulnerability

Use GitHub's **Report a vulnerability** option in this repository's **Security** tab. This creates a private report for the maintainers. If private vulnerability reporting is unavailable, open a public issue that asks for a private contact channel without including technical details.

Please include:

- affected Moonrise version and Windows version;
- a concise impact assessment;
- safe reproduction steps;
- sanitized Moonrise diagnostics, if relevant;
- whether the issue is already public or actively exploited.

Do not include passwords, tokens, account/session files, personal data, private JARs, proprietary client files, or full unredacted command lines. Never send a live credential as proof; revoke or rotate exposed credentials immediately.

## Security boundaries

Moonrise delegates account login, ownership checks, downloads, and session creation to the official Lunar Launcher. It must not read, copy, store, log, or modify Microsoft, Lunar, or Minecraft credentials, tokens, or session data.

Imported Weave mods and Java agents are executable code. Moonrise validates package structure and known loader integrity, not author intent. A valid signed-catalog entry confirms integrity, not safety. Users must obtain packages from trusted sources and remain responsible for the code they load.

## Response expectations

Maintainers will acknowledge a complete private report when available, assess severity and reproducibility, and coordinate disclosure after a fix or mitigation is ready. Response timing depends on maintainer availability and issue complexity.
