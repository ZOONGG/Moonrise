# Synthetic Lunar file compatibility

This suite is stacked on runtime candidate commit
`06067513d51eee3bdd6720cd3c347c5249b0bbf1`. It exercises file contracts only;
it does not launch Lunar/Minecraft, validate packages, or replace Stage 3 smoke testing.

## Fixtures and coverage

`tests/Moonrise.Tests/Fixtures/SyntheticLunarFiles.cs` generates disposable SQLite,
JSON and log files in one unique temporary directory. Every service receives explicit
fixture paths; no default user paths, downloaded data, real profile databases, private
assets or third-party JARs are needed. Connections disable pooling so cleanup releases
the database. The fixture deletes only its own directory.

| Contract | Coverage |
| --- | --- |
| Profile schema | Required-only legacy schema; observed optional `loaders`, `loader_version`, `lunar_module`; extra future columns; each missing required column; missing, zero-byte, no-table and no-row DBs |
| Read-only discovery | DB bytes and directory inventory unchanged after reads and failed selection; other client types excluded |
| Loader evidence | Empty, whitespace, malformed, null, scalar, object, empty array and mixed array JSON; only nonempty strings retained; unknown names never classified as Forge/OptiFine; missing/null/empty optional metadata |
| Selection | Duplicate names, exact case-sensitive ID, missing ID failure; original absent/null/string `gameProfile` restored; nested arrays/objects and unrelated properties retained, including unrelated changes made while selected; idempotent restore/dispose |
| Invalid settings | Malformed/empty/non-object root and non-object `settings` fail before writes; no temporary file remains |
| Private siblings | Synthetic empty account/session/token sentinels held with `FileShare.None` around reads, selection/restore and readiness; inventory unchanged; sentinel lengths unchanged. Other cases have no such siblings and do not create any. Source inspection confirms both services open only the explicit DB/settings/log paths (plus the atomic settings temporary file). The lock test is a regression guard, not an OS audit of swallowed access attempts. |
| Log parsing | Known previous/current structured messages, changed wording, last complete observation, mismatched client/version reported to the caller, partial appends held until newline |
| Log lifecycle | Byte checkpoints with UTF-8 preceding content, old evidence ignored, negative offsets, absent log appearing later, truncation, larger rotated replacement, same-length rewrite, old partial text/ready state discarded after replacement |
| Readiness | Exact profile plus ready, similar IDs excluded from the fast path, ready-only grace fallback, negative/non-ready messages, cancellation and timeout |
| Compatibility boundary | Readiness returns only client/version; diagnostic compatibility remains `untested` and usable game window remains `not-observed` |

## Narrow defects reproduced and fixed

The initial 66-case run against the unchanged services failed 14 cases:

- Existing array/string/null `settings` was silently replaced. Validate the root
  and existing settings object before either selection or restoration writes.
  Non-object roots now consistently raise `InvalidDataException`.
- Substring matching accepted `already` and `not ready` as readiness. Require a
  complete `ready` word and reject explicit negation.
- A profile ID substring or differently cased ID bypassed the ready grace delay.
  Match a complete, case-sensitive ID token, consistent with exact DB selection.
- Length-only tailing missed larger rotations and same-length rewrites. Retain
  creation time and a bounded 128-byte boundary anchor; clear pending text and
  accumulated readiness on detected replacement, truncation or disappearance.
  Capture the anchor synchronously at EOF before yielding to avoid a replacement race.

The boundary anchor is deliberately bounded, not a complete historical log hash.
An indistinguishable replacement retaining the same creation time and identical
boundary bytes cannot be detected through this mechanism. The existing public
checkpoint API is a byte offset, so replacement before the waiter starts is also
not identifiable unless the new file is shorter. These tests do not claim otherwise.

## Validation

Run on Windows with .NET 8:

```powershell
dotnet restore Moonrise.sln --locked-mode
dotnet build Moonrise.sln -c Release --no-restore
dotnet test Moonrise.sln -c Release --no-build --filter "Category!=PrivateAssets"
```

The `Synthetic Lunar compatibility` workflow runs the same locked restore,
Release build and all public tests for PRs targeting `test/mnr4-runtime-candidate-v2`.
It uploads TRX results and does not build release packages, run installer smoke,
rebuild the runtime bridge, or launch Lunar/Minecraft. See the PR for the exact
validated commit, result counts and CI link.
