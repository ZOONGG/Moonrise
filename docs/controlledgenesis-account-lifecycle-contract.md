# ControlledGenesis / official Lunar account lifecycle

Read-only code investigation, 2026-10-08. Builds on the Stage 2 auth/resource
reports; no game launch, real account-file access, authentication RPC, runtime
change, commit or push was performed. Existing pending changes were preserved.

## Version boundary

The installed game has changed since the previous audit:

- Lunar 2.23.1-2641, fullGitHash 2ec98ab2c42227206e3c132417b7e6441044dad2.
- lunar.jar SHA-256 bb37980cdf62e4576adcd700268dc24359a7d7ca48090b9b698696df9d89932f.
- app.asar SHA-256 ade7fcd6d2def6a8688e21d5cad04a74a5db858fb281c7accf7b2249ef3227fb.

The previous runtime/resource smoke and Genesis 9.10.1 contract belong to
2.23.0-2640. They are not smoke evidence for this new game binary. Obfuscated
account-manager names changed. Launcher JS below was inspected directly inside
the installed app.asar; only program archives were read.

## Official lifecycle versus isolated ControlledGenesis

The official contract is shared disk state plus launcher auth RPC completion,
not an account-list/credential payload delivered by the handshake.

| Operation | Official components | ControlledGenesis consequence |
| --- | --- | --- |
| Initial list / selected account | Game manager init reads accounts and activeAccountLocalId from its settings/game/accounts.json. Default game root is user.home/.lunarclient. Launcher VR reads IT()/accounts.json; IT is launcher-root/settings/game. | lunar.dataDir points to the owned lunar-data root, so the game reads a different file. |
| Add from game | GameIPC AddAccount invokes launcher auth.addAccount. completeMcLogin checks ownership and multiplayer privileges, then BR/HR adds and selects the account. Response contains success/error. Game UI success callback reloads its disk store. | Successful official login cannot populate the isolated file. |
| Select / refresh from game | RefreshAccount sends UUID; launcher initAuth may select via VTe and refresh via refreshAccountInternal. Updated data is written before the success response. Game success callbacks reload disk; game selection can also save locally. | Same path mismatch; RPC is not a replacement store. |
| Remove | Launcher zR reads/modifies/writes its file. Game removes from its in-memory map and saves the complete accounts member. | Sharing a path introduces two writers; separate paths diverge. |
| Launcher edits while game runs | Game has explicit reload paths, including successful add/refresh and checking an expired current account. | Immediate general add/remove/select notification or a reliable continuous list watcher was not established. |
| Game exit | Account manager close saves its in-memory account map. | A stale game snapshot can overwrite launcher changes if connected to the real store. |

Do not interpret eventual rereads as a verified bidirectional live-update
guarantee. Launcher Redux/UI updates accompany its own operations; a universal
reverse notification for direct game-file edits was not established. The
NotifySwitchAccount protobuf types exist, but the inspected launcher service
implements AddAccount and RefreshAccount; type existence proves no push channel.

## Exact current game evidence

Account manager:
`com.moonsworth.lunar.client.IHCHHIIIHROCIROHROCHRCIROHOICH.IHIRRHORRRRHICCOHOHORCHIHHHIOO.CRRHRIRIHOHRHRCCHRCOICHOOIHOIC`.

- IICIRHRIHCOOIORHIORHHHIHOHRCHR resolves the settings root plus accounts.json;
  absent-file handling attempts createNewFile then returns the Minecraft-directory
  launcher_accounts.json fallback for that invocation.
- util.IIICIHORHCRHRHIRCHRCRHRICCROOC static initialization resolves lunar.dataDir
  or user.home/.lunarclient, then settings/game. No independent account root was
  established in this path. Changing this shared settings field would affect
  more than accounts.
- init calls HCHRICCIHIRHORHHCHOICCHIHICOIH(true). Reload
  OCHORIIHRIOCOIRICROICRHIRCHHHC calls it with false; it clears the map and reads
  disk. load(JsonObject) reads activeAccountLocalId and selects the matching entry.
- Account UI add-success callback invokes reload at bytecode offset 23;
  refresh-success callback invokes reload at offset 45.
- CICOHIIRRHHIIHRROHOICOCRHHHHOO(account) removes the entry and calls save at
  offset 38. Selection with persistence enabled saves at offset 111. close saves.
- IHHHICHHRICRCIOHCIOHHOHIOIORRH(JsonObject) replaces accounts with a new object
  populated from the in-memory map, even if the generic saver read disk first.
- Generic interface IHCHHIIIHROCIROHROCHRCIROHOICH.ICCRRCCIOOIRICIHHCIOROHHROOHHR
  saves with FileWriter(File), offsets 223–228. No atomic replacement or shared
  cross-process lock was found in this path. Account writable checks attempt
  File.setWritable(true).

Launcher main.js: VR reads with r+ and initializes an empty map on invalid JSON;
HR uses writeFile(JSON.stringify(...), utf-8), without atomic replacement or a
shared lock. BR, zR, VTe and refreshAccountInternal use VR/HR. Same-UUID promise
deduplication in initAuth/refreshAccount is local operation deduplication, not a
cross-process transaction. A missing-entry race error during refresh protects
that case only; it does not prevent lost updates or partial-file reads.

## Decision and smallest next step

No safe production contract meeting all requested properties was found.
Keep isolated UI/resources/cache and the existing unsupported-account gate.
Do not connect real stores through a link, snapshot, whole-root substitution,
read-only attribute, or game-only mutex. Atomic writes alone would prevent torn
JSON but would still allow stale full-map replacement and lost removals/additions.

A path-only version-scoped adapter would be insufficient. A sufficient adapter
would need account-only path routing, fresh native reload/UI publication, and
cooperation of every launcher/game write path under one transaction owner (or
equivalent serialization and version-conflict handling). Authentication must
remain official; Moonrise must not parse/merge credentials. The existing IPC
does not expose the full remove/list/change/persistence contract needed to make
the launcher the sole writer. A game-only adapter cannot supply that missing
launcher cooperation, and an invented RPC or interception is not a solution.

Next implementation-sized step: add an offline, non-loading contract probe/test
using only program archives and synthetic account fixtures. Pin both launcher
and game hashes; enumerate account-path, reload, select/remove/close save sites
and auth RPC descriptors; reject unknown versions or missing transaction-owner
capability before any runtime adapter is enabled. This is a diagnostic gate,
not account integration. Enabling real integration remains blocked on a proven
Lunar-native coordination interface or an explicitly supported cooperating
writer mechanism. No such adapter was implemented in this investigation.

Validation: static installed-code inspection only. No build/tests were needed
for this documentation-only change. Real concurrent edits, live launcher/game
list synchronization, crash safety and gameplay were not tested.
