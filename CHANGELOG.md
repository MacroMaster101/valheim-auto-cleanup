# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- **The "cleanup finished" announcement ignored `AnnouncementStyle`.** It was hardcoded to the
  corner notice, so a server set to `Center` showed its warnings as the large banner and then
  delivered the result somewhere else entirely. It now uses the configured style, like the
  warnings already did.

### Changed

- `PlayerProtectionRadius` now defaults to **50** metres (was 25). 25 m covered little more
  than the ground a player was standing on; 50 m covers a build site or the area a fight
  ranges over. The proximity test is one squared-distance comparison per item per player, so
  a larger radius costs nothing — it only means fewer items are eligible.
- `AnnouncementStyle` now defaults to **`Center`** (was `TopLeft`). The corner notice is easy
  to miss mid-fight, which defeats the point of a warning that exists to make people pick
  their loot up. `TopLeft` is still the choice when you want the message kept in the player's
  in-game message log — `MessageHud` only writes a log entry for that style.

Existing installs keep the values already saved in their config file; these defaults apply to
fresh installs.

## [1.0.2] - 2026-09-12

### Security

- **Fixed: a modified client could run admin chat commands by posing as an admin.** Valheim
  reads a routed RPC's sender ID from the packet the client wrote
  (`RoutedRPCData.Deserialize`), and `ZRoutedRpc.RPC_RoutedRPC` never compares it with the
  connection the packet arrived on. The admin check trusted that ID, so a player with a
  modified client could put an online admin's ID on a chat message and run cleanup commands
  such as `disable` (which is saved to the config) or `now`. It could not switch `DryRun`
  off, change other settings, or reach anything outside this plugin. A new prefix on
  `ZRoutedRpc.RPC_RoutedRPC` now records the connection each message arrived on, a chat
  command runs only when its claimed sender is that connection (`ChatSenderCheck`), and the
  admin check uses the verified connection. Affects 1.0.0 and 1.0.1 with
  `EnableChatCommands = true`, which was the default.
- `EnableChatCommands` is now **off by default**. The command file is the recommended
  channel on a rented server. Existing configs keep their saved value.

### Added

- **A cleanup report after every pass**, on by default (`LogCleanupReport`). It is the same
  breakdown `preview` prints — what was removed (or, in a dry run, what would be), grouped by
  item, and why the rest was kept — so you can see what the cleanup does without running a
  command.

### Fixed

- `reload` now logs `LIVE CLEANUP MODE ENABLED` (or the dry-run notice) when it changes
  `DryRun`. Previously that line only appeared at startup, although the DatHost guide said
  to look for it after a reload.

## [1.0.1] - 2026-09-12

### Changed

- Extended the `ImportantItemWhitelist` defaults for Valheim 1.0. Added `FrozenKingDrop`
  (Sacrificial Blood, from Kall Fimbulbringer, the boss 1.0 introduced), progression and quest
  keys (`DvergrKey`, `BloodGoldKey`, `HildirKey_forestcrypt`, `HildirKey_mountaincave`,
  `HildirKey_plainsfortress`), and the three Dyrnwyn sword fragments. Every name was verified
  against the 1.0.12 game data and localization. Existing configs keep their saved list, since
  BepInEx does not overwrite saved values; add the names manually or regenerate the config.
- Pinned the Thunderstore dependency to `denikson-BepInExPack_Valheim-5.4.2350`, released
  alongside Valheim 1.0.
- The build now locates BepInEx independently of the game folder, and honours a
  `BepInExCore` property or `BEPINEX_PATH` environment variable. Steam can move Valheim to
  another library without taking BepInEx with it, and mod managers keep BepInEx in their own
  profile directory; the previous build assumed the two always sat together.

### Verified

- Compatible with **Valheim 1.0.12 (network version 40)**, via 1.0.7 (network 39), up from
  0.221.12 (network 36). All 113 API symbols the plugin depends on are present, it builds
  clean against the 1.0.12 assembly, and the behaviours it relies on were re-checked in the
  shipped IL: the `s_spawnTime` stamp in `ItemDrop.Awake`, the ownership gate in
  `ZDOMan.DestroyZDO`, the `ZNetScene.Destroy` path, world time freezing on an empty server,
  per-player chat routing, the `m_server` guard around `RouteRPC`, and the chat RPC's
  parameter signature.
- 1.0.12 replaced the world save format with a chunked loader (`ZDOMan.LoadChunks`). It still
  loads every ZDO into `ZDOMan.m_objectsByID` at startup, so the scanner is unaffected.

## [1.0.0] - 2026-09-09

First release.

### Added

- **Scheduled cleanup of old, loose dropped items.** Runs every
  `CleanupIntervalSeconds` (600 by default) and only considers items that have already
  existed for `MinimumItemAgeSeconds` (600 by default) of world time.
- **Dry-run mode, on by default.** A fresh install scans and reports but removes nothing
  until an admin sets `DryRun = false`.
- **Player proximity protection** with a configurable radius (25 m by default), plus a
  grace period after the last player leaves (180 s by default).
- **Hard-coded tombstone protection.** Player graves are excluded before any configuration
  is consulted and cannot be re-enabled as targets.
- **Fish protection.** Fish prefabs carry an `ItemDrop` component; they are excluded while
  `ProtectFish` is on.
- **Container, structure, creature and vehicle exclusion.** Anything carrying a
  `Container`, `Character`, `BaseAI`, `Ship`, `Vagon`, `Plant` or `Pickable` component can
  never be a candidate, and neither can any item a player has placed as a build piece.
  Inventories are never read or written.
- **Item protection rules:** general whitelist, important-item whitelist (boss progression
  items and trophies, verified against the shipped game data), equipment protection,
  upgraded-item protection, large-stack protection, and a conservative blacklist-only mode.
- **Optional place-based protection:** player bases, active wards, and the world spawn area.
- **Optional empty-server mode** (`OnlyCleanupWhenServerEmpty`).
- **Batched, coroutine-driven removal** with `MaxDeletesPerCleanup`, `DeletesPerFrame` and
  a configurable inter-batch delay, so a large pass never causes a frame spike.
- **Cleanup warnings** delivered to vanilla clients through Valheim's own on-screen message
  RPC (`MessageHud`), configurable between the centre banner and the corner notice, with a
  server-log fallback. The chat RPC is deliberately not used: the receiving client rejects
  messages whose sender is not a connected player, so server-authored chat is silently
  discarded.
- **Admin commands** — `now`, `dryrun`, `preview`, `status`, `stats`, `history`, `reload`,
  `enable`, `disable`, `help` — reachable three ways: a watched command file, in-game chat
  from an authenticated admin, and a registered console command.
- **Live config reload** via `reload`, including restarting the schedule when the interval
  changes. No server reboot needed for ordinary changes.
- **Per-pass statistics and a rolling 10-entry history**, kept in memory only.
- **Configuration validation** that clamps out-of-range values and logs what it did, rather
  than refusing to start.
- Unit tests for the policy layer (52 tests), a manual test plan, an architecture document
  and a DatHost walkthrough.

### Design notes

- **Exactly one Harmony patch.** Everything except admin chat uses public game APIs plus
  one cached private-field read, with a documented fallback. The one patch is a postfix on
  `ZRoutedRpc.RouteRPC`, applied only when `EnableChatCommands` is on; it runs after routing
  completes and parses a copy of the payload, so it cannot affect anyone's chat. Hooking
  `Chat.RPC_ChatMessage` does not work: current Valheim sends chat to each player
  individually rather than broadcasting it, so a dedicated server is never an addressee and
  that method never runs there.
- **No new persistent data.** Item age reuses Valheim's own `s_spawnTime` ZDO field, the
  one `ItemDrop.Awake` already writes and `ItemDrop.GetTimeSinceSpawned` already reads. The
  world save format is untouched, and no player or character files are modified.
- **ZDO-level scanning.** A Valheim dedicated server only instantiates GameObjects around
  its own never-updated reference position, so a component-based scan would see almost
  nothing of the world. Scanning the ZDO table is both correct on a dedicated server and
  cheaper.
- **Network-safe removal.** Ownership is claimed and the object removed through
  `ZNetScene.Destroy` or `ZDOMan.DestroyZDO` in the same step;
  `UnityEngine.Object.Destroy` is never used.
- **Server-side only.** No custom prefabs, items, assets, status effects, ZDO types or
  RPCs, and no client-side behaviour. Vanilla clients connect normally.

### Known limitations

- Item age is measured in world time, which Valheim advances only while at least one player
  is connected.
- Items whose spawn stamp is missing (very old saves) report an unknown age and are never
  removed.
- Base detection is proximity to player-built pieces, not a true building-footprint test.
- **In-game admin chat commands require at least one other player online.** A player's own
  chat message is handled locally and never routed off their client, so when the admin is
  alone the server never receives it. The command file works in every case and is the
  recommended channel for solo administration.
- Statistics and history are in memory and reset when the server restarts.
- Cleanup of world objects that exist only as unloaded ZDOs is handled, but there is no
  region-scoped or biome-scoped rule set in this version.

[1.0.2]: https://github.com/MacroMaster101/valheim-auto-cleanup/releases/tag/v1.0.2
[1.0.1]: https://github.com/MacroMaster101/valheim-auto-cleanup/releases/tag/v1.0.1
[1.0.0]: https://github.com/MacroMaster101/valheim-auto-cleanup/releases/tag/v1.0.0
