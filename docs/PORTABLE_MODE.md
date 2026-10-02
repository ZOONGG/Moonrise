# Portable mode

The portable artifact is named `Moonrise-Portable-<version>-x64.zip`. Extract
the complete archive to a writable directory and run `Moonrise.exe`; do not run
the executable from inside the ZIP.

Portable mode is enabled only when this marker is beside the executable:

```text
Moonrise.portable
```

With the marker present, Moonrise keeps mutable data in `Moonrise-data/` beside the executable.
Without it, a packaged build is treated as installed and stores data under
`%LocalAppData%\Moonrise`.

Development discovery takes priority over the marker: when discovery finds
`Moonrise.sln` in the executable directory or an ancestor, it retains the existing
`%LocalAppData%\Moonrise\dev` root and uses the repository for bundled resources.
Otherwise only a marker file in the executable directory enables portable mode;
markers in parent directories do not count. Discovery checks file existence only
and does not read marker contents, Lunar profiles, accounts, or tokens, create
directories, or migrate data.

Both packaged modes use this layout (directories are created as needed):

```text
Moonrise data root
├─ packages
│  ├─ weave
│  ├─ agents
│  ├─ unclassified
│  └─ metadata
├─ adapters
├─ cache
│  ├─ runtime
│  ├─ catalog
│  └─ updates
├─ temp                  temporary launch sessions and plugin requests
├─ logs
│  └─ crashes
├─ settings
│  ├─ moonrise-settings.json
│  ├─ profiles
│  └─ language-packs
├─ themes
└─ plugins               legacy migration destination
```

On first use after upgrading from the legacy layout, Moonrise copies recognized
Moonrise-owned settings, packages, appearance packs, profiles, cache, and logs
into the new layout. Existing destination files win, and the old copy is left
in place as a recovery source. Migration does not inspect or copy Lunar account,
session, or token files.

To switch a portable extraction to installed behavior, use Setup rather than
deleting the marker from an existing portable copy. Back up the portable data
root before moving between modes.

Setup installs binaries under `%LocalAppData%\Programs\Moonrise` by default and
does not include the portable marker. Upgrade and normal uninstall preserve the
installed data root; optional user-data removal deletes it.

The in-app updater downloads Setup and its checksum into `cache/updates` under
the active data root and runs the per-user installer. It does not update a
portable extraction in place or transfer its data into the installed root.
To keep portable mode, close Moonrise, back up `Moonrise-data`, and replace the
application files with the new Portable ZIP, retaining the marker and data directory.
Data previously written by marker-ignoring builds stays in `%LocalAppData%\Moonrise`;
enabling portable mode does not automatically import that installed data.
