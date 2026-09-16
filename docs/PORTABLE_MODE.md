# Portable mode

The portable artifact is named `Moonrise-Portable-<version>-x64.zip`. Extract
the complete archive to a writable directory and run `Moonrise.exe`; do not run
the executable from inside the ZIP.

Portable mode is enabled only when this marker is beside the executable:

```text
Moonrise.portable
```

With the marker present, Moonrise keeps mutable data beside the application.
Without it, a packaged build is treated as installed and stores data under
`%LocalAppData%\Moonrise`.

Both modes use this logical layout:

```text
Moonrise data root
├─ packages
│  ├─ user-mods
│  ├─ user-agents
│  └─ managed
├─ cache
├─ sessions
├─ logs
└─ settings
```

On first use after upgrading from the legacy layout, Moonrise copies recognized
Moonrise-owned settings, packages, appearance packs, profiles, cache, and logs
into the new layout. Existing destination files win, and the old copy is left
in place as a recovery source. Migration does not inspect or copy Lunar account,
session, or token files.

To switch a portable extraction to installed behavior, use Setup rather than
deleting the marker from an existing portable copy. Back up the portable data
root before moving between modes.
