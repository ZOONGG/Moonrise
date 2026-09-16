# Catalog and installer security

Catalog metadata is data only. Moonrise does not load assemblies, execute classes, invoke Java, or run package scripts while parsing a catalog or validating a JAR.

The catalog client requires HTTPS, limits catalog size, uses a bounded timeout, retries transient failures with backoff, validates downloaded JSON before replacing the cache, and falls back to the last validated cache while offline. Production metadata is accepted only after an Ed25519 signature verifies against the public key embedded in Moonrise. Unsigned catalogs are accepted only in developer mode. Logs contain operational summaries and pass through the existing token redactor.

The installer:

1. resolves the selected release;
2. checks dependencies and conflicts;
3. downloads to a temporary file over HTTPS;
4. enforces size limits and the declared file size;
5. verifies SHA-256;
6. opens the file as ZIP/JAR and rejects traversal entries;
7. verifies `weave.mod.json` or Java-agent manifest metadata;
8. checks that detected type matches catalog metadata;
9. atomically moves the file into per-user managed storage;
10. restores the previous managed version if replacement fails.

Manually imported packages live outside managed catalog storage and cannot be removed by the managed installer. Moonrise never reads, copies, logs, or modifies Lunar account, token, or session files.

Catalog risk labels and confirmation gates expose factual upstream behavior. Cheat clients and unfair-advantage packages remain visible for completeness, but they are restricted, never featured or recommended, and cannot install without the declared policy. Actual incompatibilities and invalid artifacts remain hard technical errors.
