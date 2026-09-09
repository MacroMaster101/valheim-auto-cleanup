# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

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
  one cached private-field read, with a documented fallback. The one patch is a prefix
  on `Chat.RPC_ChatMessage`, applied only when `EnableChatCommands` is on; it never blocks
  the original method. It is unavoidable because a dedicated server registers the
  "ChatMessage" RPC itself and Valheim keeps only one handler per RPC name.
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
- Statistics and history are in memory and reset when the server restarts.
- Cleanup of world objects that exist only as unloaded ZDOs is handled, but there is no
  region-scoped or biome-scoped rule set in this version.

[1.0.0]: https://github.com/MacroMaster101/valheim-auto-cleanup/releases/tag/v1.0.0
