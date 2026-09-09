## What does this change?

<!-- One or two sentences. If it changes when an item can be removed, say so plainly. -->

## Why?

<!-- What problem does it solve? Link an issue if there is one. -->

## Checklist

- [ ] `dotnet build -c Release` is clean, with no new warnings
- [ ] `dotnet test` passes
- [ ] No game or BepInEx binaries, `bin/`, `obj/`, `dist/` or `Environment.props` are staged
- [ ] `CHANGELOG.md` updated if behaviour changed
- [ ] If this changes item-removal logic, a test was added to `ItemCleanupPolicyTests`

## Tested on a real server?

<!--
Anything touching scanning, removal or networking should be exercised against a throwaway
world. Paste the relevant log lines - a cleanup summary is ideal:

Cleanup complete (DRY RUN): Scanned=20243 Drops=16 TooYoung=16 WouldDelete=0 Duration=1ms
-->
