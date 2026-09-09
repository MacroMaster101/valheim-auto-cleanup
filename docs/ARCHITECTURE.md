# Architecture

How Valheim Auto Cleanup is put together, and the reasoning behind the parts that are not
obvious.

---

## The finding that shapes everything

**A Valheim dedicated server barely instantiates any GameObjects.**

`ZNetScene.CreateDestroyObjects` builds the set of objects to instantiate from the zones
around a single point, `ZNet.GetReferencePosition()`. The only things that ever move that
point are client-side: `Player.SetLocalPlayer`, `Player.LateUpdate`, `Game.FindSpawnPoint`,
`Tracker`, and `Valkyrie.SyncPlayer`. A headless server runs none of them, so its reference
position stays where `ZNet`'s constructor left it — the world origin — for the entire
session.

The consequence: a dedicated server holds the **ZDO** (the authoritative network record)
for every object in the world, but instantiates GameObjects only in the couple of zones
around origin. Players 3 km away are simulated on their own clients.

So a cleanup plugin that scanned loaded components — `ItemDrop.s_instances`, or
`FindObjectsOfType<ItemDrop>()` — would find almost nothing on exactly the kind of server
it was written for. **This plugin scans ZDOs instead.** That is also cheaper: one
dictionary walk per pass, no Unity object churn, no allocation storm.

Where a GameObject *does* happen to exist for a candidate (listen host, or an item near
origin), the destroyer notices and routes through `ZNetScene.Destroy` so the local
component graph is torn down properly too.

---

## Component map

```
Plugin                    BepInEx entry point. Detects server mode, owns the
                          per-frame tick, wires everything together.
  |
  +-- PluginConfig        Binds and validates the BepInEx config. Clamps every
  |                       value, never refuses to start. Rebuilt on reload.
  |
  +-- CleanupManager      Owns the schedule and runs passes as coroutines.
  |     |                 Concurrency guard, warning timers, batching, history.
  |     |
  |     +-- ItemScanner       One ZDO walk -> candidates + ward/base geometry.
  |     |     +-- PrefabCatalog   Classifies every prefab once at world load.
  |     |     +-- SpatialIndex    Uniform grid over player-built pieces.
  |     |
  |     +-- ItemEvaluator      The full per-item gate chain.
  |     |     +-- ItemCleanupPolicy   Pure, Unity-free, unit-tested.
  |     |     +-- PlayerProtection    Proximity + recent-activity grace period.
  |     |     +-- BaseProtection      Bases, wards, world spawn.
  |     |
  |     +-- NetworkDestroyer   Ownership claim + network-safe removal.
  |
  +-- Commands            One implementation of every admin command.
  |     +-- ChatCommandListener   Admin chat, vanilla clients.
  |     +-- CommandFileWatcher    Watched text file, works on any host.
  |     +-- (Terminal.ConsoleCommand registered from Plugin)
  |
  +-- Broadcaster         Server -> vanilla client on-screen messages.
  +-- ServerState         Defensive facade over ZNet / ZNetScene / ZDOMan / ZoneSystem.
  +-- Reflect             Two cached private-field reads, with fallbacks.
```

---

## Harmony: exactly one patch

Valheim updates often; patched methods are where mods break. Almost everything here needs
no patch:

| What it needs | How it gets it, without a patch |
|---|---|
| Know when the world is ready | Poll `ServerState.IsServerReady` every 2 s from a coroutine |
| A per-frame tick | `BaseUnityPlugin` is a `MonoBehaviour`; `Update` is ours already |
| Enumerate world objects | Read `ZDOMan`'s table |
| Item age | Read the game's existing `s_spawnTime` ZDO field |
| Remove an object | `ZNetScene.Destroy` / `ZDOMan.DestroyZDO`, both public |
| Player positions | `ZNet.GetConnectedPeers()`, public |
| Announce to players | Invoke the vanilla `"Message"` routed RPC on the player's character |
| Receive admin chat | **The one Harmony patch** — a prefix on `Chat.RPC_ChatMessage` |
| Admin check | `ZNet.IsAdmin(hostName)`, public |

The single patch lives in `Patches/ChatMessagePatch.cs` and is applied only on the server,
and only when `EnableChatCommands` is on. It is a **prefix** that returns void, never
reports the call as handled, and swallows every failure, so the original method always runs
exactly as it would have.

It exists because the patch-free approach genuinely does not work. That approach was
implemented first: register a handler for the `"ChatMessage"` routed RPC on the server. It
fails because **a dedicated server runs its own `Chat` component** — the headless server log
prints the `/w [text] - Whisper` and `/s [text] - Shout` help lines that only `Chat.Awake`
emits — so `Chat.Awake` has already registered `RPC_ChatMessage` before any plugin loads.
`ZRoutedRpc.Register` keeps one delegate per name and overwrites silently, so registering
ours would *displace* the game's handler rather than run beside it. Valheim exposes no way
to add a second listener and no event to subscribe to.

A prefix is used rather than a postfix because `Chat.OnNewChatMessage` reaches
`RelationsManager.CheckPermissionAsync`, which dereferences
`PlatformManager.DistributionPlatform.LocalUser`; whether that is initialised on a headless
server is not something this plugin should have to depend on. Running first makes command
handling independent of it.

One private field is read through cached reflection, because Valheim exposes no public
accessor for it:

- `ZDOMan.m_objectsByID` — the ZDO table. If the lookup ever fails, the scanner falls back
  to the ZDOs behind loaded `ItemDrop` instances and logs a loud warning.

---

## Item age

`ItemDrop.Awake` already does this:

```csharp
if (m_nview.IsOwner() && new DateTime(m_nview.GetZDO().GetLong(ZDOVars.s_spawnTime, 0)).Ticks == 0)
{
    m_nview.GetZDO().Set(ZDOVars.s_spawnTime, ZNet.instance.GetTime().Ticks);
}
```

and `ItemDrop.GetTimeSinceSpawned` reads it back the same way, for the vanilla
one-hour auto-despawn. The plugin uses the same field, so:

- it adds **no new data** to any ZDO;
- the world save format is untouched;
- the value is already networked and already persisted;
- it survives restarts.

A stamp of `0` means unknown — an item saved before the field existed, or one whose owner
never wrote it. That yields a null age, and the policy layer keeps null-age items. A
negative age (world time reset) is treated the same way. No local first-seen tracker is
needed, and inventing one would be strictly worse: it would forget everything on restart
and make every item look brand new.

**World time is not wall-clock time.** `ZNet.UpdateNetTime` advances `m_netTime` only when
`GetNrOfPlayers() > 0`. Item ages therefore freeze while the server is empty. This is
Valheim's clock, not the plugin's, and it is generally the more sensible measure: an item's
age reflects time the world was actually being played. The scheduler uses real time
(`Time.deltaTime`), not world time, so passes keep running on a quiet server.

---

## Prefab classification

Built once from `ZNetScene.m_prefabs` when the world comes up. For each prefab:

- `PrivateArea` → a ward; its radius is cached for ward protection.
- `TombStone` → marked and permanently excluded.
- `Piece` **and no `ItemDrop`** → a build piece, used for base detection.
- `ItemDrop`, and **none** of `TombStone`, `PrivateArea`, `Container`, `Character`,
  `BaseAI`, `Ship`, `Vagon`, `Plant`, `Pickable` → a cleanable loose drop.
- `Fish` → flagged, excluded while `ProtectFish` is on. Fish carry an `ItemDrop` because
  you pick them up off the ground.

Note the subtlety in the `Piece` rule. `ItemDrop.MakePiece` turns a dropped item into a
placed build piece at runtime by enabling a `Piece` and a `WearNTear` component on it, so
"has a Piece component" is not by itself proof that a prefab is a structure. A prefab that
also carries an `ItemDrop` is treated as an item; whether a *particular instance* has been
placed is a per-object fact recorded in its ZDO under `ZDOVars.s_piece`, which the scanner
checks separately.

Measured on a live server running Valheim l-0.221.12: 3458 networked prefabs, of which 821
are cleanable item drops, 438 are build pieces, 3 are wards, and **0** item-carrying
prefabs are disqualified by any of the component rules. So on the current game data the
`Piece` refinement changes nothing; it is defensive, not a fix for an observed
misclassification.

The item category (`ItemDrop.ItemData.ItemType`) is mapped to a Unity-free enum here, once,
so the policy layer stays testable.

Per-item cost at scan time is then one dictionary lookup instead of a dozen
`GetComponent` calls.

---

## Scan and evaluate

One pass over the ZDO table produces, in a single sweep:

- the candidate list (loose drops, with position, prefab, stack, quality, age)
- the active-ward list, if ward protection is on
- a spatial grid of player-built pieces, if base protection is on

"Player-built" means `ZDOVars.s_creator != 0`. Ruins and dungeon furniture generated by the
world have no creator and do not count as a base.

The grid is a flat `Dictionary<long, List<Vector3>>` bucketed at 16 m, comfortably above
the default 15 m base radius, so a query touches a 3×3 block of cells. Without it, a world
with 30 000 build pieces and 200 candidates would be six million distance checks per pass.

Scanning and evaluation do not yield. They are a single dictionary walk plus cheap per-item
tests, and splitting them across frames would mean evaluating half the world against one
snapshot of player positions and half against another.

---

## Removal

Both correct removal paths refuse to act unless the caller owns the ZDO:

```
ZNetScene.Destroy(go)   -> calls ZDOMan.DestroyZDO only when zdo.IsOwner()
ZDOMan.DestroyZDO(zdo)  -> returns immediately unless zdo.IsOwner()
```

So the destroyer claims ownership first — `ZNetView.ClaimOwnership()` is exactly
`m_zdo.SetOwner(ZDOMan.GetSessionID())` — and removes in the same call, leaving no window
for ownership to move. Two paths:

- **A GameObject exists locally.** `ClaimOwnership()`, verify `IsOwner()`, then
  `ZNetScene.Destroy(gameObject)`. Local components torn down and network record removed.
- **No local instance** — the usual case on a dedicated server. `SetOwner(sessionId)`,
  verify, then `ZDOMan.DestroyZDO(zdo)`.

Either way `ZDOMan` queues the id into its destroy-send list and broadcasts the removal to
every peer on its next update. Clients holding a copy drop it through the game's normal
path. No custom RPC; vanilla clients need nothing.

`UnityEngine.Object.Destroy` is never used: it would tear down the local copy while leaving
the ZDO alive, so the object would come back on the next zone refresh and every client
would keep its own. That is a desync, not a cleanup.

Removal is batched (`DeletesPerFrame`) with a yield and an optional delay between batches,
and re-checks that the world is still up between batches so a shutdown mid-pass stops
cleanly.

---

## Admin commands

`valheim_server.exe` never calls `Console.ReadLine` — verified by scanning the whole game
assembly for references. A headless server has **no console to type into**, so a
`Terminal.ConsoleCommand` alone is unreachable there. Hence three channels:

1. **Command file** — a watched text file in the config folder. Works from any file
   manager or FTP client, needs nobody logged in. Polled every 5 s rather than watched with
   a `FileSystemWatcher`, because hosted file managers and FTP uploads often replace files
   in ways that raise no reliable change notification.
2. **Admin chat** — described below.
3. **Console command** — registered anyway; reachable on a listen host and through mods
   that forward client console input to the server.

All three call the same `Commands.Execute`.

### How admin chat works without a client mod

Chat is a routed RPC named `"ChatMessage"`. `ZRoutedRpc.RPC_RoutedRPC` invokes the local
handler for any message addressed to everybody *before* forwarding it on, and a dedicated
server registers `Chat.RPC_ChatMessage` for that name like any client would. So the server
already receives a copy of every broadcast chat message; the patch above simply observes it.
Forwarding to the other players is a separate code path and is untouched.

**Security.** The `UserInfo` inside a chat message is supplied by the sending client and is
not trusted. The admin check resolves the sender's peer and reads the platform ID from its
authenticated socket (`peer.m_socket.GetHostName()`), then calls `ZNet.IsAdmin` — the same
value Valheim uses for `adminlist.txt`. Anything unresolvable is "not an admin".

### Announcements

Announcements do **not** use chat. Every `Player` registers a routed RPC on its own
character in `Player.Awake`:

```csharp
m_nview.Register<int, string, int>("Message", RPC_Message)
```

and `RPC_Message` forwards straight to `MessageHud.ShowMessage`. The server invokes that
RPC addressed at each peer's `m_characterID`, which the server already knows. A peer that
has not finished spawning has no character yet and is skipped.

Sending the chat RPC from the server was tried first and does not work in the current
build. Two independent gates stop it:

1. `Chat.OnNewChatMessage` passes the sender's `PlatformUserID` to
   `RelationsManager.CheckPermissionAsync`. A server has no valid platform ID; an invalid
   one returns `RelationsManagerPermissionResult.Error`, which fails `IsGranted()`, and the
   handler returns before displaying anything.
2. Even past that, `Terminal.AddString(PlatformUserID, ...)` resolves the display name via
   `ZNet.TryGetPlayerByPlatformUserID` and logs an error and returns when the sender is not
   a connected player — which a server never is.

The only way to force chat to work would be to send a connected player's own ID, which
would attribute server notices to a person. The MessageHud route avoids both gates and
misattributes nothing. `Chat.SendText` is separately unusable on a server because it reads
`Player.m_localPlayer`.

---

## Threading

Everything runs on Unity's main thread. There is no `async`, no `Task`, no background
thread anywhere in the plugin. Batching is done with coroutines, which are main-thread
constructs.

---

## Failure handling

- Every candidate is evaluated inside its own try/catch → the item is kept.
- Every removal is inside its own try/catch → counted as a failure, pass continues.
- The scan is inside a try/catch → the pass aborts, the server does not.
- The per-frame `Update` is inside a try/catch → on failure the plugin disables itself and
  logs once, rather than throwing every frame.
- `Shutdown` is idempotent and runs from both `OnDestroy` and `OnApplicationQuit`.

Shutdown stops coroutines, clears the candidate pool, the proximity table, the spatial
index and the prefab catalogue, and resets cached world state. No ZDO references are held
between passes — holding them would pin objects the game wants to release.

---

## Testability

`src/ValheimAutoCleanup/Policy/` contains no Unity or Valheim types at all. It holds the
item-intrinsic decision — age, name lists, category, quality, stack size — plus config
clamping. The test project compiles those files directly and runs on plain .NET, so
`dotnet test` needs no Valheim installation.

Spatial protections are not unit-tested: they need live world state, and mocking Unity
internals would test the mock rather than the plugin. They are covered by the manual test
plan instead.

---

## Room for later versions

The seams are already in place, but none of this is in v1:

- **Unloaded-ZDO-only cleanup** — the scanner already works at ZDO level.
- **Per-biome or per-item-age rules** — `PolicySettings` is a plain object.
- **Discord/webhook logging, Prometheus metrics** — `CleanupStatistics` and
  `CleanupHistory` are already the aggregation point.
- **Load-triggered cleanup** (item count or TPS thresholds) — `CleanupManager.Tick` is the
  hook.
- **Persistent statistics** — would go in a separate plugin data file, never in the world
  save.
