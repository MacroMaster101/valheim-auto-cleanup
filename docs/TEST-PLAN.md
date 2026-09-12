# Manual test plan

Run this against a **test world you do not care about**, with a backup, before pointing the
plugin at anything real.

## Setup

- Server: Valheim dedicated + BepInEx + `ValheimAutoCleanup.dll`
- Client: **completely vanilla** Valheim, no BepInEx, no mods
- Config: start from defaults, then change only what each test names
- Add your account to `adminlist.txt` and set `EnableChatCommands = true` so the chat
  commands are available (they are off by default)
- Keep the server log open (DatHost: **Console**)

Two settings make testing much faster:

```ini
[General]
CleanupIntervalSeconds = 60
MinimumItemAgeSeconds = 60

[Logging]
LogDeletedItems = true
LogProtectedItems = true
```

Remember that **world time only advances while a player is connected**, so stay logged in
while waiting for items to age.

---

### TEST 1 — Vanilla client compatibility

1. Start the server with the plugin installed.
2. Join from a vanilla client.

**Expect:** connects normally, no version-mismatch error, no visible difference. Log shows
`Dedicated server detected.` and `Server-side cleanup active.`

---

### TEST 2 — Dry run reports without removing

1. `DryRun = true`.
2. Drop a stack of Wood. Walk more than 25 m away.
3. Wait for the item to pass `MinimumItemAgeSeconds`, then for a pass.

**Expect:** log shows `Would remove Wood x… at (…)` and
`DRY RUN: N items would have been removed.` **The wood is still on the ground.**

---

### TEST 3 — Live removal

1. Set `DryRun = false`. Apply it: write `reload` into
   `BepInEx/config/autocleanup.command.txt`.
2. Repeat TEST 2.

**Expect:** startup/reload logs the `LIVE CLEANUP MODE ENABLED` warning; the wood
disappears; the summary shows `Deleted=1` or more.

---

### TEST 4 — Player proximity protection

1. `DryRun = false`, `ProtectNearPlayers = true`, `PlayerProtectionRadius = 25`.
2. Drop Wood and **stand within 25 m** of it.
3. Wait through at least two passes.

**Expect:** item remains. Summary shows `PlayerProtected=1`. With `LogProtectedItems = true`
you see `Keeping Wood x… (a player is nearby)`.

---

### TEST 5 — Leaving the area, and the grace period

1. Continue from TEST 4. Walk more than 25 m away.
2. Wait for a pass **within** `RecentPlayerProtectionSeconds` (180 s by default).

**Expect:** still kept, reason `a player was nearby recently`.

3. Keep waiting past the grace period.

**Expect:** removed on the next pass.

---

### TEST 6 — Young items are safe

1. Drop Wood, immediately move more than 25 m away.
2. Let the very next pass run.

**Expect:** kept, reason `not old enough yet`, `TooYoung` in the summary.

---

### TEST 7 — Tombstones are never removed

1. Die (`/die` in chat, or jump off something).
2. Leave the tombstone alone and move well away.
3. Let several passes run — even with `MinimumItemAgeSeconds = 0`.

**Expect:** the tombstone is still there and never appears in any removal log. It is not
a candidate at all, so it does not show up as "protected" either.

---

### TEST 8 — Containers are untouched

1. Place a chest, put Wood, Stone and a weapon in it.
2. Move away and let several passes run.

**Expect:** the chest is intact with the same contents. The plugin never reads or writes an
inventory.

---

### TEST 9 — Equipment and upgraded items

1. `ProtectEquipment = true`, `ProtectUpgradedItems = true`.
2. Drop a weapon (any), a shield, and a **level-2 or higher** weapon. Move away.

**Expect:** all kept. Summary shows `Equipment=…` (and `Upgraded=…` if the upgraded item is
in a category `ProtectEquipment` does not already cover).

3. Set both to `false`, `reload`, repeat.

**Expect:** now removed.

---

### TEST 10 — Whitelist

1. `Whitelist = Iron`.
2. Drop Iron and Wood, move away.

**Expect:** Iron kept (`Whitelisted=1`), Wood removed.

3. Try `whitelist = iron` and `Whitelist =  IRON ,,` — both must behave identically.

---

### TEST 11 — Blacklist-only mode

1. `BlacklistOnly = true`, `Blacklist = Wood`.
2. Drop Wood and Iron, move away.

**Expect:** Wood removed; Iron kept with `NotBlacklisted=1`.

---

### TEST 12 — Removal budget

1. `MaxDeletesPerCleanup = 5`, `DeletesPerFrame = 2`.
2. Spawn a lot of disposable drops (`devcommands`, then `spawn Wood 1 0` many times), age
   them, move away.

**Expect:** at most 5 removed per pass; summary shows `BudgetExhausted=…`; no visible
stutter; the rest go on subsequent passes.

---

### TEST 13 — Manual cleanup

1. Write `now` into `BepInEx/config/autocleanup.command.txt`, save.

**Expect:** within ~5 s the log shows `Running command from file: autocleanup now` and a
pass runs. The file returns to its `#` header.

2. As an admin on a **vanilla client**, type `!autocleanup status` in chat.

**Expect:** a short reply as an on-screen notice; the full output in the server log.

3. Type `!autocleanup preview`.

**Expect:** a prefab-by-prefab breakdown in the log, nothing removed.

4. Have a **non-admin** type `!autocleanup now`.

**Expect:** ignored. Log notes a command from a non-admin connection. No pass runs.

5. Let a scheduled pass run with drops on the ground, without running any command.

**Expect:** after the `Cleanup complete` line, a `Cleanup report` block listing the top
prefabs and why the rest were kept.

The sender-spoofing fix from 1.0.2 cannot be exercised from a vanilla client, which always
sends its own ID; `ChatSenderCheckTests` covers the rule. A rejected spoof appears in the log
as `Ignoring a cleanup command that claims to come from peer ...`.

---

### TEST 14 — Concurrent cleanup is rejected

1. Set `MaxDeletesPerCleanup = 5000` and `DelayBetweenDeleteBatchesMilliseconds = 500` so a
   pass takes a while.
2. Start one pass, then immediately request another (file, then chat).

**Expect:** the second is refused with `A cleanup is already in progress.` Only one pass
runs.

---

### TEST 15 — Restart

1. Restart the server.

**Expect:** clean startup, no exceptions, the mode banner, and
`First scheduled cleanup in …`. Items keep the ages they had — the spawn stamp is
persisted in the world, not held in memory.

---

## Optional

### TEST 16 — Fish are safe

1. `ProtectFish = true` (default), `MinimumItemAgeSeconds = 0`.
2. Stand well away from a shore with fish and let passes run.

**Expect:** fish still swimming. They never appear in removal logs.

### TEST 17 — Ward protection

1. `ProtectItemsInsideActiveWards = true`.
2. Place a ward, **activate it**, drop Wood inside its radius, move away.

**Expect:** kept, `WardProtected=1`. Deactivate the ward, `reload`, and it becomes eligible.

### TEST 18 — Base protection

1. `ProtectNearPlayerBase = true`, `BaseProtectionRadius = 15`.
2. Drop Wood within 15 m of something you built, move away.

**Expect:** kept, `BaseProtected=1`. Drop another far from any structure — that one is
removed.

### TEST 19 — Cleanup warnings on a vanilla client

1. `EnableCleanupWarnings = true`, `WarningSecondsBeforeCleanup = 60`,
   `FinalWarningSeconds = 10`, `AnnouncementStyle = Center`.
2. Stay logged in on a vanilla client and watch the screen.

**Expect:** both warnings appear as on-screen messages (the large centre banner), prefixed
with `AnnouncementPrefix`. Set `AnnouncementStyle = TopLeft`, `reload`, and they should move
to the small corner notice instead. If the server cannot broadcast, they appear in the log
only and nothing breaks.

### TEST 20 — Configuration validation

1. Set deliberately silly values:
   ```ini
   CleanupIntervalSeconds = 5
   PlayerProtectionRadius = -10
   MaxDeletesPerCleanup = 0
   WarningSecondsBeforeCleanup = 9999
   ```
2. `reload`.

**Expect:** the server does not crash. The log names each clamped setting and the value it
used (60, 0, 1, and interval-1 respectively).

### TEST 21 — Empty-server mode

1. `OnlyCleanupWhenServerEmpty = true`.
2. Stay online through a scheduled pass.

**Expect:** skipped (visible with `LogDebugInformation = true`).

3. Log out and let a pass run.

**Expect:** it runs. Remember nothing ages while the server is empty.

---

## Sign-off

| # | Test | Result |
|---|---|---|
| 1 | Vanilla client compatibility | |
| 2 | Dry run reports without removing | |
| 3 | Live removal | |
| 4 | Player proximity protection | |
| 5 | Leaving the area / grace period | |
| 6 | Young items are safe | |
| 7 | Tombstones never removed | |
| 8 | Containers untouched | |
| 9 | Equipment / upgraded items | |
| 10 | Whitelist | |
| 11 | Blacklist-only mode | |
| 12 | Removal budget | |
| 13 | Manual cleanup + admin check | |
| 14 | Concurrent cleanup rejected | |
| 15 | Restart | |
| 16 | Fish safe | |
| 17 | Ward protection | |
| 18 | Base protection | |
| 19 | Warnings on vanilla client | |
| 20 | Configuration validation | |
| 21 | Empty-server mode | |
