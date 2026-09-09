# Contributing

Thanks for taking a look. This is a small, deliberately conservative plugin — the guiding
rule is **if in doubt, keep the item**, and changes are weighed against that first.

## Ground rules

1. **No game files in the repository, ever.** `assembly_valheim.dll`, Unity assemblies and
   BepInEx binaries are copyrighted. They are referenced from your own installation with
   `Private=false` and are never copied or committed. `.gitignore` blocks them; please don't
   work around it.
2. **Don't add a Harmony patch without exhausting the alternatives.** There is exactly one,
   and [`Patches/ChatMessagePatch.cs`](src/ValheimAutoCleanup/Patches/ChatMessagePatch.cs)
   documents at length why no patch-free approach works for it. Every patch is a thing that
   can break on the next Valheim update.
3. **A new way for an item to be removed needs a new way for it to be kept.** Protections
   are cheap; a wrongly deleted item is not recoverable.
4. **Verify against the game, don't assume.** Several obvious-looking assumptions about
   Valheim turned out to be wrong while building this — see
   [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the list. If you are relying on a
   behaviour, check the current assembly or a real server log and say so in the PR.

## Setting up

You need your own legal Valheim installation with BepInEx installed into it.

```bash
git clone https://github.com/MacroMaster101/valheim-auto-cleanup.git
cd valheim-auto-cleanup
cp Environment.props.example Environment.props   # then edit the path inside
dotnet build -c Release
```

`Environment.props` is git-ignored. You can instead set the `VALHEIM_INSTALL` environment
variable, or rely on auto-detection of the common Steam locations. If none of the three
work you get an error naming the path it looked in.

## Tests

```bash
dotnet test
```

The tests need **no** Valheim installation. That is deliberate: everything in
`src/ValheimAutoCleanup/Policy/` is free of Unity and Valheim types precisely so the
decision logic can be tested on a plain .NET runtime, and CI can run it.

If you change how an item is judged, add a case to
[`ItemCleanupPolicyTests`](tests/ValheimAutoCleanup.Tests/ItemCleanupPolicyTests.cs). If your
change needs live world state to test, it belongs behind the policy boundary, and the
manual [`docs/TEST-PLAN.md`](docs/TEST-PLAN.md) is where it gets covered instead.

## Before opening a PR

- `dotnet build -c Release` is clean, with no new warnings.
- `dotnet test` passes.
- Nothing from `bin/`, `obj/`, `dist/` or `Environment.props` is staged.
- If behaviour changed, `CHANGELOG.md` says so.
- If you touched anything network-facing, run the relevant tests from
  [`docs/TEST-PLAN.md`](docs/TEST-PLAN.md) on a throwaway world and paste the log lines.

## Reporting a bug

Please include the plugin version, the Valheim version, whether it is a dedicated server or
a listen host, the relevant `[Valheim Auto Cleanup]` log lines, and your config. A cleanup
summary line is usually the single most useful thing:

```
Cleanup complete (DRY RUN): Scanned=20243 Drops=16 TooYoung=16 WouldDelete=0 Duration=1ms
```

It names the rule that kept every item, which is normally the whole answer.

## Licence

By contributing you agree your work is licensed under the [MIT Licence](LICENSE).
