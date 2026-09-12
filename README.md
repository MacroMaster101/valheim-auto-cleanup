# Valheim Auto Cleanup

**Safe, server-side automatic cleanup of old dropped items for Valheim dedicated servers.**

[![build](https://github.com/MacroMaster101/valheim-auto-cleanup/actions/workflows/build.yml/badge.svg)](https://github.com/MacroMaster101/valheim-auto-cleanup/actions/workflows/build.yml)
[![licence: MIT](https://img.shields.io/badge/licence-MIT-blue.svg)](LICENSE)
[![latest release](https://img.shields.io/github/v/release/MacroMaster101/valheim-auto-cleanup?display_name=tag&sort=semver)](https://github.com/MacroMaster101/valheim-auto-cleanup/releases/latest)

> ## SERVER-SIDE ONLY
> ## PLAYERS DO NOT INSTALL THIS MOD
> ## VANILLA CLIENTS ARE FULLY SUPPORTED
>
> Install it on the machine hosting the world and nowhere else. Your players connect with
> a completely unmodified copy of Valheim. They do not need BepInEx, they do not need this
> plugin, and nothing about their client changes.

| | |
|---|---|
| **Name** | Valheim Auto Cleanup |
| **GUID** | `io.github.macromaster101.valheimautocleanup` |
| **Author** | MacroMaster101 |
| **Version** | 1.0.2 |
| **Licence** | MIT |
| **Requires** | BepInEx 5 (`denikson-BepInExPack_Valheim`) on the **server only** |

---

## What it does

Dropped items pile up on a long-running server: spent arrows from every fight, wood and
stone dumped outside a build site, loot that overflowed an inventory in a crypt. Each one
is a live network object the server has to track and hand out to clients. Enough of them
and everyone feels it.

This plugin removes **old, loose, unattended item drops** on a schedule, and is built so
that removing something a player still wanted is very hard to do by accident.

**It never touches** player inventories, chests or any other container, tombstones,
building pieces, workbenches, portals, beds, fires, ships, carts, wards, terrain,
vegetation, creatures, bosses, or live fish. The only thing it can ever remove is a loose
`ItemDrop` lying in the world.

---

## Safety design

The plugin ships in **dry-run mode**. Out of the box it scans, reports exactly what it
would remove, and removes nothing at all. You have to deliberately turn that off.

Its guiding rule is **if in doubt, keep the item**. Every one of these results in the item
being kept:

| Situation | Result |
|---|---|
| Age cannot be determined | Kept |
| Network state invalid or object already gone | Kept |
| Prefab not recognised | Kept |
| Server cannot take ownership of the object | Kept |
| Evaluation threw an exception | Kept |
| World spawn location not resolved yet, with spawn protection on | Kept |

On top of that, a candidate has to survive every one of these gates before it is removed:

1. **Age.** Younger than `MinimumItemAgeSeconds` of world time → kept.
2. **Whitelist.** An absolute veto, checked before anything else.
3. **Important items.** Boss progression items and boss trophies, on by default.
4. **Equipment.** Weapons, shields, armour, tools, torches, trinkets and utility gear.
5. **Upgraded items.** Anything with quality above 1.
6. **Large stacks.** Optional guard against wiping a big resource transfer.
7. **Blacklist-only mode.** Optional: remove *only* the prefabs you name, nothing else.
8. **Player proximity.** Any connected player within `PlayerProtectionRadius`.
9. **Recent player activity.** A grace period after the last player walked away.
10. **Player base.** Optional radius around player-built structures.
11. **Wards.** Optional, for items inside an active PrivateArea.
12. **World spawn.** Optional radius around the starting stones.
13. **Removal budget.** `MaxDeletesPerCleanup` caps how much one pass can do.

Hard-coded and **not configurable**: tombstones are never removed. Neither is anything
that is a container, a creature, a ship, a cart, a ward, a build piece or a destructible
structure, even if it also happens to carry an item component.

---

## How the safety mechanisms actually work

**Tombstone protection.** Every networked prefab is classified once at world load. A
prefab carrying a `TombStone` component is marked and can never become a candidate, before
any config is consulted. There is no setting that turns this off.

**Fish protection.** Fish prefabs carry an `ItemDrop` component — that is how you pick a
fish up off the ground. Without special handling, a fish swimming past would look exactly
like litter. Prefabs carrying a `Fish` component are excluded while `ProtectFish` is on
(the default).

**Container and structure protection.** The plugin never reads or writes an inventory. A
prefab with a `Container`, `Character`, `BaseAI`, `Ship`, `Vagon`, `Plant` or `Pickable`
component is excluded from the candidate set outright, as is any prefab that is a build
piece in its own right. It also skips any individual item a player has converted into a
placed build piece, which is recorded per object rather than per prefab and so is checked
per instance.

On a live server running Valheim l-0.221.12 the classifier reports 3458 networked prefabs,
821 of them cleanable item drops, 438 build pieces, 3 wards, and 0 item-carrying prefabs
disqualified by the component rules.

**Item age.** Valheim already stamps every item drop with its creation time:
`ItemDrop.Awake` writes the current world time into the item's own network record, and the
game's own despawn logic reads it back. This plugin reuses that same field. That means it
adds **no new data of its own** to any object and changes nothing about your save format.
An item with no stamp — one saved before the field existed, say — reports an unknown age
and is kept.

Note that "world time" only advances while at least one player is connected. Items do not
age on an empty server. That is Valheim's behaviour, not this plugin's, and it is usually
what you want: an item's age reflects time the world was actually being played.

**Player proximity.** Player positions come from the connected peers' reported reference
positions, which the server always has, plus any locally loaded `Player` components for the
listen-host case.

**Recent player activity.** Without it, a player who clears a crypt, drops the overflow
outside and walks 30 m away could lose that loot to the very next pass. Each item a player
has been near gets a single timestamp, and stays protected for
`RecentPlayerProtectionSeconds` after the last sighting. The table is pruned every pass, so
it cannot grow without bound.

**Two small Harmony patches, and why.** Admin chat commands need the server to observe chat.
Current Valheim sends chat to each player *individually* rather than broadcasting it
(`Chat.CheckPermissionsAndSendChatMessageRPCsAsync` walks `ZNet.GetPlayerList()`), so a
dedicated server is never an addressee and `Chat.RPC_ChatMessage` never runs there. The one
place a server does see chat is when it forwards it, so one patch is a postfix on
`ZRoutedRpc.RouteRPC`. It runs after routing completes, parses a copy of the payload, and
swallows all failures, so it cannot affect anyone's chat. The sender ID inside a routed
message is written by the client and Valheim never checks it, so a second patch — a prefix on
`ZRoutedRpc.RPC_RoutedRPC` — records which connection each message really arrived on, and a
command is obeyed only when the two match. Both are applied only when `EnableChatCommands`
is on, and it is off by default.

**Chat commands need another player online.** A player's own message is handled locally and
never routed off their client, so when the admin is alone the server sees nothing to act on.
**Use the command file if you administer the server alone** - it always works.

**Announcements to vanilla clients.** Warnings are delivered through the `Message` routed
RPC that every `Player` registers on its own character in `Player.Awake`, which lands in
`MessageHud` — the same call the game uses for "You are cold". Sending Valheim's chat RPC
from the server does *not* work: the client runs the sender's platform ID through
`RelationsManager.CheckPermissionAsync`, a server has no valid ID to present, and the
resulting error makes the client discard the message before displaying it. The MessageHud
route has no such check, and does not require impersonating a player.

**Network-safe removal.** Removal goes through Valheim's own networking layer, never
`UnityEngine.Object.Destroy`. Both correct paths refuse to act unless the caller owns the
object, so the server claims ownership and removes it in the same step. Clients holding a
copy are told to drop it through the game's normal destroy broadcast. No custom RPC, no
client mod.

**Race conditions.** A player picking an item up between the scan and the removal is
normal, not an error. Liveness is re-checked immediately before each removal and a
vanished item is skipped silently.

**Per-item error isolation.** Every candidate is evaluated and removed inside its own
try/catch, and so is the pass as a whole. One corrupt object cannot stop a cleanup, and
nothing in this plugin can bring the server down.

---

## Installation

**[Download the latest release](https://github.com/MacroMaster101/valheim-auto-cleanup/releases/latest)** — you do not need to build anything.

### Any dedicated server

1. Install BepInEx (the `denikson-BepInExPack_Valheim` pack) on the **server**.
2. **Stop the server.**
3. Copy `ValheimAutoCleanup.dll` into:
   ```
   BepInEx/plugins/ValheimAutoCleanup/
   ```
4. Start the server once. Look for these lines in the log:
   ```
   [Info : Valheim Auto Cleanup] Dedicated server detected.
   [Info : Valheim Auto Cleanup] Server-side cleanup active.
   [Info : Valheim Auto Cleanup] Dry-run mode is active. No items will be removed.
   ```
5. The config file appears at:
   ```
   BepInEx/config/io.github.macromaster101.valheimautocleanup.cfg
   ```
6. **Leave `DryRun = true`.**
7. Let at least one cleanup cycle run and read the log. You are looking for lines like:
   ```
   DRY RUN: 37 items would have been removed. Set DryRun = false in the config to make cleanup live.
   ```
   The same breakdown is logged after every pass (`LogCleanupReport`), and `preview`
   (see Commands) prints it on demand.
8. **Make a world backup.**
9. Set `DryRun = false` when you are happy with what it reports.

### DatHost

DatHost is a common host for this, so here is the exact click path.

1. **Mods & Plugins** → enable **BepInEx** → **Save and Reboot**.
2. **Stop** the server.
3. **File Manager** → navigate to `valheim/BepInEx/plugins/` → create a folder
   `ValheimAutoCleanup` → **Upload** `ValheimAutoCleanup.dll` into it.
   (Uploading the Thunderstore `.zip` through **Mods & Plugins** works too.)
4. **Save and Reboot.**
5. **File Manager** → `valheim/BepInEx/config/` → open
   `io.github.macromaster101.valheimautocleanup.cfg` and edit it there.
6. Reboot, or apply the change live with the command file (below).
7. Use **Console** to watch the log for the startup lines and cleanup summaries.

Your players still install nothing.

---

## Commands

There are three ways to reach the same set of commands.

### The command file — recommended on a rented server

`valheim_server.exe` never reads standard input, so a headless server has no console you
can type into. The command file is the reliable substitute, and it works from any file
manager or FTP client.

1. Open `BepInEx/config/autocleanup.command.txt` (the plugin creates it).
2. Write one command on a line of its own, below the `#` header. For example: `status`
3. Save. Within about five seconds the plugin runs it, writes the result to the server log,
   and restores the header so the command does not repeat.

### In-game chat, for admins

Any player whose account is in `adminlist.txt` can type commands straight into chat on a
**vanilla client**. Note the limitation below: this needs at least one *other* player online.

```
!autocleanup status
```

The reply appears as an on-screen notice, and the full output goes to the server log.
Nothing inside a chat message is trusted to say who sent it: each message is matched against
the network connection it arrived on, and the admin check then uses that connection's
platform ID, so a modified client cannot pose as an admin. Non-admins typing the prefix are
ignored.

This channel is **off by default**. Turn it on with `EnableChatCommands = true` and change
the prefix with `ChatCommandPrefix`; left off, no Harmony patches are applied at all.

> **Limitation.** Valheim sends chat to each player individually, and a player's own message
> is never routed off their client. A dedicated server therefore only observes chat while at
> least one *other* player is connected. If you administer the server on your own, use the
> command file — it always works.

### Console command

`autocleanup` is also registered as a normal Valheim console command. That is reachable on
a listen host (a player hosting the world) and through mods that forward client console
input to the server. It is registered on a dedicated server too, but nothing there can type
into it — use one of the other two channels.

### The commands

| Command | What it does |
|---|---|
| `autocleanup now` | Run a pass now, honouring the current `DryRun` setting |
| `autocleanup dryrun` | Run one report-only pass, whatever `DryRun` says |
| `autocleanup preview` | Scan and report what would be removed, grouped by prefab, with a protection breakdown |
| `autocleanup status` | Version, enabled state, dry-run state, intervals, radii, next and last cleanup, session totals |
| `autocleanup stats` | Session totals |
| `autocleanup history` | The last 10 passes |
| `autocleanup announce` | Send a test on-screen message to every player, in both styles, and report how many received it |
| `autocleanup reload` | Re-read the config and restart the timer — no reboot needed |
| `autocleanup enable` | Turn automatic cleanup on (written to the config) |
| `autocleanup disable` | Turn automatic cleanup off |
| `autocleanup help` | The list above |

`preview` output looks like this:

```
Cleanup preview (nothing was removed):
  Objects examined : 48213
  Loose drops found: 182
  Would remove     : 131
  Top prefabs:
    Wood: 58
    Stone: 42
    Resin: 19
    BoneFragments: 9
    Iron: 3
  Kept:
    players nearby     : 17
    not old enough     : 22
    whitelisted        : 3
    equipment          : 4
```

---

## Configuration

Full annotated defaults: [`docs/example-config/`](docs/example-config/). The highlights:

### `[General]`

| Setting | Default | Notes |
|---|---|---|
| `Enabled` | `true` | Master switch |
| `CleanupIntervalSeconds` | `600` | Real seconds between passes; minimum 60 |
| `MinimumItemAgeSeconds` | `600` | **World**-time seconds an item must survive first |
| `DryRun` | `true` | Report only. Turn off deliberately |
| `RunCleanupOnServerStart` | `false` | |
| `StartupCleanupDelaySeconds` | `120` | Wait for the world to settle |
| `OnlyCleanupWhenServerEmpty` | `false` | Very conservative mode |

The interval and the minimum age are both 600 by default, and they are not the same thing.
A pass runs every 10 minutes, but an item has to have existed for 10 minutes of world time
before that pass can touch it. Loot dropped 30 seconds before a scan is safe.

### `[PlayerProtection]`

| Setting | Default |
|---|---|
| `ProtectNearPlayers` | `true` |
| `PlayerProtectionRadius` | `25` |
| `ProtectRecentlyNearbyItems` | `true` |
| `RecentPlayerProtectionSeconds` | `180` |

### `[BaseProtection]`

| Setting | Default | Notes |
|---|---|---|
| `ProtectNearPlayerBase` | `false` | Off because many public servers *want* settlements tidied |
| `BaseProtectionRadius` | `15` | |
| `ProtectItemsInsideActiveWards` | `false` | Only wards that are switched on count |
| `ProtectNearWorldSpawn` | `false` | Handy if spawn is your meeting point |
| `WorldSpawnProtectionRadius` | `50` | |

### `[ItemProtection]`

| Setting | Default |
|---|---|
| `ProtectImportantItems` | `true` |
| `ImportantItemWhitelist` | boss progression items and trophies |
| `Whitelist` | *(empty)* |
| `BlacklistOnly` | `false` |
| `Blacklist` | `Wood,Stone,Resin,BoneFragments` |
| `ProtectEquipment` | `true` |
| `ProtectUpgradedItems` | `true` |
| `ProtectLargeStacks` | `false` |
| `LargeStackThreshold` | `100` |
| `ProtectFish` | `true` |

Name lists are comma-separated prefab names, matched case-insensitively with surrounding
whitespace trimmed; empty entries are ignored. `Whitelist = Iron, blackmetal ,,` is three
tokens, two of which are real, and `IRON` matches.

Every default in `ImportantItemWhitelist` was checked against the shipped game data,
most recently Valheim 1.0.12:

- **Boss drops and trophies:** `DragonEgg`, `Wishbone`, `YagluthDrop`, `QueenDrop`,
  `FaderDrop`, `FrozenKingDrop`, `TrophyEikthyr`, `TrophyTheElder`, `TrophyBonemass`,
  `TrophyDragonQueen`, `TrophyGoblinKing`, `TrophySeekerQueen`, `TrophyFader`, `ShieldCore`,
  `BellFragment`, `MorgenHeart`
- **Keys:** `CryptKey`, `DvergrKeyFragment`, `DvergrKey`, `BloodGoldKey`,
  `HildirKey_forestcrypt`, `HildirKey_mountaincave`, `HildirKey_plainsfortress`
- **Quest fragments:** `DyrnwynBladeFragment`, `DyrnwynHiltFragment`, `DyrnwynTipFragment`

`FrozenKingDrop` is Sacrificial Blood, dropped by Kall Fimbulbringer, the boss added in 1.0;
1.0.12 ships no trophy for it. The swamp key is `CryptKey` — there is no prefab called
`SwampKey`.

**Upgrading keeps your old list.** BepInEx never overwrites a value already saved in your
config, so an existing install keeps whatever `ImportantItemWhitelist` it was first created
with. To pick up new defaults, add the names yourself, or delete the config file and let the
plugin write a fresh one.

**Ammo is deliberately not equipment.** Spent arrows are the single most common form of
litter, so `ProtectEquipment` does not cover them. Add specific arrows to `Whitelist` if
you want them kept.

### `[Performance]`

| Setting | Default |
|---|---|
| `MaxDeletesPerCleanup` | `250` |
| `DeletesPerFrame` | `25` |
| `DelayBetweenDeleteBatchesMilliseconds` | `50` |

### `[Warnings]`

Announcements appear on screen through Valheim's own message system, so **vanilla clients
see them** with nothing installed. If the network layer cannot deliver, warnings still go to
the server log and nothing breaks.

| Setting | Default | Notes |
|---|---|---|
| `EnableCleanupWarnings` | `true` | |
| `WarningSecondsBeforeCleanup` | `60` | Must be shorter than the interval |
| `WarningMessage` | see config | `{seconds}` is replaced with the lead time |
| `FinalWarningSeconds` | `10` | Must be shorter than the first warning |
| `FinalWarningMessage` | see config | |
| `CleanupCompleteMessageEnabled` | `false` | |
| `AnnouncementPrefix` | `[Server]` | Prefixed to every announcement |
| `AnnouncementStyle` | `TopLeft` | `TopLeft` (corner notice, also written to the player's message log) or `Center` (large banner, louder but leaves no record) |

Out-of-range lead times are clamped with a log line rather than rejected.

### `[Admin]` and `[Logging]`

See the annotated example config.

---

## Recommended settings

**Cautious public server** — clear obvious litter, touch nothing else:

```ini
[General]
DryRun = false
CleanupIntervalSeconds = 900
MinimumItemAgeSeconds = 1800

[ItemProtection]
BlacklistOnly = true
Blacklist = Wood,Stone,Resin,BoneFragments,ArrowWood,ArrowFlint,ArrowBronze

[BaseProtection]
ProtectNearPlayerBase = true
ProtectItemsInsideActiveWards = true
```

**Busy server with a real litter problem:**

```ini
[General]
DryRun = false
CleanupIntervalSeconds = 600
MinimumItemAgeSeconds = 600

[PlayerProtection]
PlayerProtectionRadius = 30
RecentPlayerProtectionSeconds = 300

[Performance]
MaxDeletesPerCleanup = 500
```

**Small private server:** the defaults with `DryRun = false` are fine.

---

## Backups

**Always make a world backup before enabling live cleanup.** Removal is permanent; there
is no undo.

The plugin reminds you which mode it is in on every startup:

```
[Info : Valheim Auto Cleanup] Dry-run mode is active. No items will be removed.
```
```
[Warning : Valheim Auto Cleanup] LIVE CLEANUP MODE ENABLED. Eligible dropped items may be permanently removed. Make sure you have a world backup.
```

---

## Troubleshooting

**"Client/non-server instance detected. Cleanup disabled."**
Working as intended — you installed it on a client that is not hosting. Only the host needs
it.

**Nothing is ever removed, and the log says `TooYoung` for everything.**
World time only advances while a player is connected, so items barely age on a quiet
server. Lower `MinimumItemAgeSeconds`, or accept that it is measuring played time.

**Nothing is removed and the summary shows `PlayerProtected` for everything.**
Someone is standing near the items, or was recently. Check
`RecentPlayerProtectionSeconds`.

**`Would remove` is always 0 but the ground is covered in stuff.**
Run `preview` and read the "Kept" breakdown — it names the rule responsible. The usual
culprits are `BlacklistOnly = true` with a short blacklist, or `ProtectEquipment`.

**"Could not enable admin chat commands: ..."**
The chat patch could not be applied — most often another mod patching the same method in an
incompatible way. The command file and console command are unaffected; use those.

**"Falling back to scanning only currently loaded item instances."**
A Valheim update changed something internal. The plugin degrades to a safe but much
narrower scan. Please open an issue.

**Turn on `LogDebugInformation = true`** for scan counts and prefab classification, and
`LogProtectedItems = true` to see the reason for every single kept item. Both are verbose.

---

## Compatibility

- **Clients:** vanilla, unmodified. Nothing to install.
- **Valheim:** verified against **1.0.12 (network version 40)**. Every game API the plugin
  uses, and every behaviour it relies on, is re-checked against the shipped assembly rather
  than assumed.
- **Other server mods:** two small Harmony patches — a postfix on `ZRoutedRpc.RouteRPC` and a
  prefix on `ZRoutedRpc.RPC_RoutedRPC` — and only when `EnableChatCommands` is on, which is
  off by default. Neither changes what the game does, so they coexist with other chat mods.
  With chat commands off, no patches are applied at all.
- **Dependencies:** BepInEx 5 only. No Jötunn, no ServerSync.

---

## Known limitations

- **Item age is world time, not wall-clock time.** It advances only while somebody is
  online. This is Valheim's own clock; the plugin does not invent a second one.
- **Items with no spawn stamp are never removed.** Very old saves may hold drops that
  predate the field. They are kept, on purpose.
- **Base detection is proximity to player-built pieces**, not a true building-footprint
  test. It errs toward protecting too much.
- **`OnlyCleanupWhenServerEmpty` interacts with frozen world time.** Nothing ages while the
  server is empty, so eligible items are only the ones that already aged enough before the
  last player left.
- **No persistent statistics.** History is in memory and resets with the server, by design
  — the plugin writes nothing to your world.
- **Unloaded-ZDO-only cleanup, Discord logging, per-biome rules and similar** are
  deliberately not in v1.

---

## Building from source

You need your own legal Valheim installation with BepInEx installed into it. **No game
files are included in this repository and none are ever redistributed.**

```bash
git clone https://github.com/MacroMaster101/valheim-auto-cleanup.git
cd valheim-auto-cleanup
cp Environment.props.example Environment.props   # then edit the path inside
dotnet build -c Release
```

The build finds Valheim in this order:

1. `Environment.props` in the repo root (git-ignored)
2. the `VALHEIM_INSTALL` environment variable
3. auto-detection of common Steam locations

If none work you get a clear error naming the path it looked in.

Output: `src/ValheimAutoCleanup/bin/Release/ValheimAutoCleanup.dll` — one file, no dependencies.
Game and BepInEx assemblies are referenced with `Private=false` and are never copied to the
output.

Run the tests (these need no Valheim installation):

```bash
dotnet test
```

Build a Thunderstore package:

```bash
./build/package.sh          # or: pwsh ./build/package.ps1
```

which produces `dist/ValheimAutoCleanup-1.0.2.zip` containing exactly
`ValheimAutoCleanup.dll`, `README.md`, `CHANGELOG.md`, `manifest.json` and `icon.png`.

---

## Further reading

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — how it works, and why it scans the way it does
- [`docs/TEST-PLAN.md`](docs/TEST-PLAN.md) — the manual test checklist
- [`docs/DATHOST.md`](docs/DATHOST.md) — DatHost walkthrough
- [`CHANGELOG.md`](CHANGELOG.md)

---

## Contributing

Issues and pull requests are welcome — see [`CONTRIBUTING.md`](CONTRIBUTING.md). The short
version: no game binaries in the repo, don't add Harmony patches without exhausting the
alternatives, and any new way to remove an item needs a matching way to keep one.

When reporting a problem, the cleanup summary line is usually the whole answer on its own —
it names the rule that kept every item.

## Licence

MIT — see [`LICENSE`](LICENSE) and [`NOTICE`](NOTICE).

Independent and unofficial; not affiliated with, endorsed by or sponsored by Iron Gate AB or
Coffee Stain Publishing. No Valheim game files, decompiled game source, Unity assemblies or
BepInEx binaries are included in this repository or in any release artifact — building it
requires your own legal installation of Valheim.
