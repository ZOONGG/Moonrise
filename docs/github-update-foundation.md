# GitHub update foundation

Velopack SDK is pinned to 1.2.161. App.Main runs Velopack before WPF initialization and the existing single-instance activation. Automatic startup apply is disabled so a pending download cannot bypass the activity guard on next launch.

MainWindow.Updates exposes CheckAsync, DownloadAsync, PendingRestart and TryApplyAndRestart. There is no automatic network request or new UI wiring in this milestone. The existing legacy Setup UI/service is unchanged and is not the Velopack path.

GitHub source: https://github.com/ZOONGG/Moonrise, public access with null token, prereleases enabled. Explicit feed channel: beta; no downgrades. First package version must be 0.1.0-beta.1 (GitHub tag v0.1.0-beta.1). Subsequent beta.N versions use semantic version ordering. A stable-channel switch is future work.

The restart method must run on the WPF dispatcher. Async user operations hold activity leases across awaits. Apply also checks launch state, tracked games and any java/javaw process (conservative: unrelated Java also blocks). Failure to inspect processes blocks apply. Checks/downloads are serialized; an update cannot apply while a download is running. Future background operations must acquire an UpdateActivityGate lease.

Velopack owns its installation packages directory only; this service does not migrate settings, packages, or Lunar data. Packaging must include only publish output, never the user data root. Keep installed user data under the existing LocalAppData root and do not use portable packaging for this channel.

Targeted tests use SimpleFileSource and TestVelopackLocator with a generated local package/feed. They cover beta ordering, staging, checksum failure, game/activity exclusion and uninstalled dev mode. They do not execute Update.exe or publish a release.

Remaining: connect UI actions/progress/errors and graceful restart cleanup; pin the same vpk CLI version; package Windows x64 with package ID Moonrise and channel beta; publish releases.beta.json and full nupkg assets to the GitHub release; signing; test an installed beta.1 -> beta.2 update and preservation of real user data. No Setup.exe is produced by this task.

Reference: https://docs.velopack.io/integrating/overview and https://docs.velopack.io/integrating/update-sources.
