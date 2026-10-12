# ControlledGenesis auth/provisioning investigation

Investigated 2026-10-05 from commit `25e9174`, Genesis 9.10.1,
Lunar 2.23.0-2640, Minecraft 1.8.9, Java Zulu 17.0.18 x64.

## Two separate boundaries

The game auth utility checks the GameIPC client's `READY` state. Missing or
disconnected IPC yields `LAUNCHER_NOT_OPEN`. It does not check an Electron HWND.
The launcher Electron main process hosts a WebSocket on IPv4 loopback, starting
at 28190 and trying up to 28219. `lc-handshake` carries a protobuf Handshake with
installation ID, launch correlation ID and process ID. The launcher checks a
nonempty launch ID and matching installation ID before registering its services.
This is observed implementation, not a published third-party auth API.

The pinned game constructs no IPC handshake when launch ID is `not supplied`
and `production=true`. Its installation-id file fallback is nonproduction only.
The pinned `lunarBuildData.txt` declares `production=true`.

Accepted IPC does **not** provision the game's account store. GameIPC
`AddAccountResponse` contains success/error, not an account credential payload.
Launcher `completeMcLogin` performs ownership and multiplayer-privilege checks,
then stores the account through `BR`/`HR`. `VR` and `HR` resolve
`IT()/accounts.json`; `IT()` is `.lunarclient/settings/game`.

Game account manager
`com.moonsworth.lunar.client.CRICRCRHRRICCICRHICIIIOHRIRHRH.HHICICROOCIIROOHOORRCIHOICHOIH.IOHOHIHHHHORRCCRHCRCHCRIOIHRHH`
resolves `accounts.json` under `HHHCHRIHOCRRCIOHHCHOOHHCCCIROH.CRHIIHIRCCIIIOROOOIOOIIICCRRCR`,
which is `<lunar.dataDir>/settings/game`. Its reload path reads that store.
Thus the isolated game and the official launcher use different stores.
Static inspection found no separate account/settings-root override in this
pinned implementation. No account file contents were inspected to establish this.

## Real observations

* The earlier closed-launcher baseline is recorded in the resource-contract audit.
  A new closed-launcher gameplay smoke has not been performed in this investigation.
* JVM 18820: unchanged direct plan, official launcher open and signed in;
  user confirmed the same launcher-open error. Exit code 0; owned view removed.
* JVM 24060: frozen standalone installation UUID copied into the owned view;
  user confirmed unchanged error. Game recorded `Unable to construct IPC handshake`.
  This exploratory copy was removed from the final implementation after the
  production-only branch was identified. Exit code 0; owned view removed.
* JVM 20200: explicit nonsecret installation ID, fresh launch correlation UUID
  and verified IPC port. Launcher accepted a new handshake and registered nine
  services. No handshake-construction failure in this run. User could open the
  official login flow, entered two accounts manually, and reported neither
  appeared in the game's account list. Screenshot shows `Add account` and
  `Connecting...`. Neither Singleplayer nor Multiplayer playability is verified.
  JVM subsequently exited with code -1073741819 (0xC0000005); its owned view
  was removed. This is not a verified graceful shutdown; lifecycle was not changed.
* All 556 frozen originals in that exploratory run (the previous 555 plus the
  standalone installation metadata) were rehashed: zero mismatches.
* Computer Use window captures failed twice with timeout. Visual account-state
  evidence is the user's observation/screenshot, not automated screen validation.

## Final policy under the original no-secret-read constraints

Observe the official executable's owned loopback listener through Windows TCP
metadata. No socket connection, handshake, auth RPC, process command line,
account file, cookie, memory or credential value is read by this observer.
`BrokerAvailable` means transport availability only. For a playable launch of
this pinned isolated-data contract, it becomes `Unsupported` with evidence
`SharedAccountStoreRequired`. No `Ready` state is inferred from process presence
or successful login in the launcher. Missing service remains
`AuthBrokerUnavailable`; missing installation is `ProvisioningRequired`.

The factory/executor fail before starting a JVM for an unsupported playable
contract. They never fall back to OfficialLunar game dispatch. The existing
resource discovery, frozen planning and direct JVM machinery are retained.
The explicit provisioning helper starts only the official EXE and can wait for
listener availability; it neither manages windows nor launches Minecraft.
This experimental backend has no production Home-screen integration yet.

No account-store copy, link, token argument, patched production check, fake
auth response or installed-data-root substitution was added. The exploratory
installation copy and generated handshake arguments are absent from the final
factory. Authentication succeeds inside official components but does not reach
the isolated account store under the observed contract.

The official main process is required for add/refresh operations. No supported
headless broker option was established. Renderer necessity and closing the
launcher after a successful authenticated handshake remain unverified because
no playable authenticated session was established.

Public Lunar guidance describes authentication in the official launcher:
[Getting Started](https://www.lunarclient.com/news/getting-started-with-lunar-client).
It does not establish a credential-free third-party account-store handoff.

Auth blocker remains. Commit/push requires the requested successful playable
smoke and has not been performed. No merge/release.

Validation: 66 targeted tests (ControlledGenesis/Auth/Resources, Stage1RecoveryTests,
UniversalLaunchPlanTests) passed; Release solution build succeeded with zero
warnings/errors; `git diff --check` passed. Full suite was not run.
The user explicitly reaffirmed the no-token-read constraint after suggesting
token import as an alternative. No such import was implemented.

READY FOR CONTROLLEDGENESIS LIFECYCLE FIX: **NO**.

## Follow-up: shared-store write boundary (2026-10-05)

Re-inspected the installed binaries, rather than relying on the previous report.
`lunar.jar!/lunarBuildData.txt` identifies Lunar `2.23.0-2640`, full Git hash
`86f9c396b61f694a21e744af1b24761912dc408a`, production true. The installed
Genesis manifest identifies `9.10.1`. Only program archives were read; no
account-store bytes, account identifiers, credential values or content hashes
were accessed.

### Path evidence

Installed `app.asar!/dist-electron/electron/main.js` defines
`Dg = join(homedir(), '.lunarclient')`, `IT = () => join(Dg, 'settings', 'game')`,
and `PR = 'accounts.json'`. `Og` changes the entire launcher root, not an
independent account-store root. Electron appData paths in the inspected path
helpers do not control `IT`. The exact default account path on this installation
is `%USERPROFILE%\.lunarclient\settings\game\accounts.json`. Metadata-only
inspection found the file and its game/settings/Lunar-root parents present and
without the ReparsePoint attribute. This does not establish a general discovery
contract for installations using a different launcher root.

Game `util.HHHCHRIHOCRRCIOHHCHOOHHCCCIROH` static initialization resolves
`lunar.dataDir` (falling back to user.home/.lunarclient), then settings/game,
and assigns final field `CRHIIHIRCCIIIOROOOIOOIIICCRRCR`. The account manager's
`RIOCOHRRROIIHIIIOICOHCHOOOCHIO()` builds its path from that field plus the
invokedynamic constant `\u0001\u0001accounts.json`. Its zero-argument constructor
does not accept a path. If the file does not exist, it attempts createNewFile
and returns Minecraft-data-directory/launcher_accounts.json for that invocation.
Thus the latter filename is a fallback, not the official launcher store.

No independent account/settings override was found in these path-producing
methods. A static scan of installed Genesis class constants also found
`lunar.dataDir`, without accountStore/accountPath/accountsPath/settingsDir/
settingsRoot candidates. This scan alone cannot prove the absence of every
possible dynamic interface; no supported independent path contract was established.

### Shared writes: stop before connecting real stores

The launcher is not the sole writer. In the installed main.js:

* `VR` reads the store with flag r+, validates it, and calls `HR` with an empty
  account map on invalid JSON or a missing file.
* `HR` calls Node fs promises writeFile with UTF-8 JSON and no supplied flag or
  shared lock; its default write behavior truncates and rewrites the file.
* `BR`, selection and account-update helpers use read/modify/write through VR/HR.

Game account manager overrides `RROOOCORCIHOHRROCCIHCIIIRHICII(boolean)` and
delegates to interface `CRICRCRHRRICCICRHICIIIOHRIRHRH.OCCRIICCOCOIORRORHIRHCHCOHHHHC`
after a writable check. That interface's save method constructs FileWriter(File)
at bytecode offsets 223-228 and serializes JSON directly before closing the
writer. It does not use an atomic replacement or a common filesystem lock.
The account manager's JSON serialization method
`IHCCRCCOHRCCIORHHCROORROCICRHI(JsonObject)` replaces the accounts member with a
new object at offsets 33-45, populated from its in-memory account map. `close()`
calls the save method, as does account removal. Its writable check attempts
File.setWritable(true), so a read-only alias is not a supported solution.

**Shared-write safety is not established.** A launcher read during the game's
truncate/write interval can encounter incomplete JSON and initialize an empty
store. Either writer can overwrite an account update based on an older in-memory
snapshot. These are static failure scenarios, not reproduced damage to the real
account store. No cross-process serialization was found in the inspected read/
write paths. A link would inherit these native writes but would not make them
safe. Per the requested stop condition, no AuthStoreBridge was installed and no
authenticated real smoke was attempted. The next prerequisite is evidence of
Lunar-native coordination covering both complete read/modify/write operations;
Moonrise must not implement a token-aware merge or substitute authentication.

### Windows capability and retained boundary

A synthetic empty file under Moonrise/temp was used to test
CreateSymbolicLinkW with SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE. It failed
with error 1314 (required privilege not held). The disposable fixture was removed.
No privilege elevation, Developer Mode change, registry change, hardlink, directory
junction or alias to an actual account file was attempted. Junctions alias whole
directories and were not substituted for the requested file-only boundary.

Existing resource/reparse-point policy and isolated lunar.dataDir were unchanged.
Existing BrokerAvailable remains transport-only and does not establish account
readiness. No AccountReady evidence was obtained. Existing runtime source changes
present at task entry were preserved. This follow-up changes documentation only.

Validation repeated: Release solution build passed with zero warnings/errors;
66 targeted ControlledGenesis/Auth/Resources, Stage1RecoveryTests and
UniversalLaunchPlanTests passed. No bridge-specific implementation or test claims.
Singleplayer, Multiplayer screen, launcher closure after login and continuing IPC
requirements remain unverified. No commit/push: the required gameplay gates were
not passed. READY FOR CONTROLLEDGENESIS LIFECYCLE FIX: **NO**.
