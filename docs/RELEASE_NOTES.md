# Moonrise installation and update release

This release adds a complete Windows distribution experience:

- self-contained x64 Setup and Portable artifacts with SHA-256 checksums;
- a per-user Inno Setup installer with upgrade, shortcuts, protocol registration,
  clean uninstall, and optional user-data removal;
- separate installed and portable data roots with safe legacy migration;
- validated `moonrise://` catalog, package, and confirmed-install links;
- stable and opt-in prerelease update channels using official GitHub Releases;
- Setup checksum verification, release notes, explicit consent, and clear
  unsigned-release warnings.

Moonrise releases remain unsigned until a code-signing certificate is available.
