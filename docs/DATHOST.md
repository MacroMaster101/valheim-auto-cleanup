# Installing on DatHost

Your players install nothing. This all happens on the server.

---

## 1. Enable BepInEx

1. Open your server in the DatHost panel.
2. **Mods & Plugins** → enable **BepInEx**.
3. **Save and Reboot.**
4. Wait for the server to come back, then **Stop** it.

Confirm it worked: **File Manager** should now show a `valheim/BepInEx/` folder containing
`core`, `config`, `plugins` and `patchers`.

---

## 2. Upload the plugin

**Either** — through Mods & Plugins:

1. **Mods & Plugins** → upload `ValheimAutoCleanup-1.0.1.zip`.
2. **Save and Reboot.**

**Or** — through the File Manager, which is the more predictable route:

1. **File Manager** → open `valheim/BepInEx/plugins/`.
2. Create a folder called `ValheimAutoCleanup`.
3. Open it and **Upload** `ValheimAutoCleanup.dll` into it.
4. **Save and Reboot.**

Final layout:

```
valheim/
  BepInEx/
    plugins/
      ValheimAutoCleanup/
        ValheimAutoCleanup.dll
```

---

## 3. Check that it loaded

Open **Console** and look for:

```
[Info   :   BepInEx] Loading [Valheim Auto Cleanup 1.0.1]
[Info   :Valheim Auto Cleanup] Valheim Auto Cleanup 1.0.1 loaded. Waiting for the world to start.
[Info   :Valheim Auto Cleanup] Dedicated server detected.
[Info   :Valheim Auto Cleanup] Server-side cleanup active.
[Info   :Valheim Auto Cleanup] Dry-run mode is active. No items will be removed.
[Info   :Valheim Auto Cleanup] Prefab catalogue built: … cleanable item drops, …
[Info   :Valheim Auto Cleanup] First scheduled cleanup in 2m 0s; the interval after that is 10m 0s.
```

If you see `Client/non-server instance detected` on a dedicated server, the world had not
finished loading — reboot and check again.

---

## 4. Edit the configuration

1. **File Manager** → `valheim/BepInEx/config/`.
2. Open `io.github.macromaster101.valheimautocleanup.cfg` and edit it in place.
3. Save.

To apply changes **without rebooting**, see the next section — `reload` does it live.

---

## 5. Run commands without a console

DatHost's **Console** tab shows the log; it is not a Valheim command prompt. The Valheim
dedicated server has no command prompt at all — it never reads standard input. So use one
of these two.

### The command file (works with nobody logged in)

1. **File Manager** → `valheim/BepInEx/config/`.
2. Open `autocleanup.command.txt`.
3. Add one command on its own line below the `#` header:

   ```
   # Valheim Auto Cleanup - admin command file
   # ...
   status
   ```

4. Save. Within about five seconds the plugin runs it and writes the result to the
   **Console** log, then restores the header.

Useful ones: `status`, `preview`, `dryrun`, `now`, `reload`, `stats`, `history`,
`enable`, `disable`.

### In-game admin chat (vanilla client)

1. Add your platform ID to `valheim/BepInEx/config/../adminlist.txt` — DatHost also has a
   field for this under **Settings**.
2. Join the server with your normal, unmodified Valheim client.
3. Type in chat:

   ```
   !autocleanup status
   ```

The reply appears as an on-screen notice; the full output is in the Console log. Only
admins are obeyed.

---

## 6. Before going live

1. Leave `DryRun = true` for at least one full cleanup cycle.
2. Read the Console log. Run `preview` to see exactly which prefabs would go.
3. **Backups** → take a manual backup, or confirm automatic backups are on.
4. Set `DryRun = false` in the config.
5. Write `reload` into the command file.
6. Confirm the Console shows:

   ```
   LIVE CLEANUP MODE ENABLED. Eligible dropped items may be permanently removed.
   ```

---

## Troubleshooting on DatHost

**The plugin does not appear in the log.** Check the DLL is at
`valheim/BepInEx/plugins/ValheimAutoCleanup/ValheimAutoCleanup.dll` and not nested one folder
deeper — unzipping a Thunderstore package sometimes creates an extra level.

**The config file is missing.** It is written on the first successful start. Reboot once
and look again.

**The command file does not exist.** It is created at startup when
`EnableCommandFile = true`. Create it yourself if needed:
`valheim/BepInEx/config/autocleanup.command.txt`, containing just the word `status`.

**A command ran twice.** The plugin clears the file after running. If the file manager
restored your text, delete the command line manually.

**Chat commands do nothing.** Check the account is genuinely in the admin list (the log
says when it ignores a non-admin), and check `EnableChatCommands = true`. If the log shows
"Could not enable admin chat commands", use the command file instead.

---

## Security note

Your DatHost panel shows FTP credentials in plain text. If you ever share a screenshot of
that page, click **Generate new password** first.
