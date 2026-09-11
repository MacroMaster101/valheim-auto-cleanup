using System;
using System.Collections;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using ValheimAutoCleanup.Patches;
using ValheimAutoCleanup.Server;
using UnityEngine;

namespace ValheimAutoCleanup
{
    /// <summary>Compile-time plugin identity, referenced by the manifest and the status command.</summary>
    public static class PluginInfo
    {
        public const string Guid = "io.github.macromaster101.valheimautocleanup";
        public const string Name = "Valheim Auto Cleanup";
        public const string Version = "1.0.1";
    }

    /// <summary>
    /// Plugin entry point.
    ///
    /// SERVER-SIDE ONLY
    /// ----------------
    /// This plugin is installed on the host and nowhere else. It creates no custom prefabs,
    /// items, assets, status effects, ZDO types or RPCs, and registers no client-side
    /// behaviour. A completely unmodified Valheim client connects normally and cannot tell
    /// the plugin is present, apart from the optional cleanup announcements - which are
    /// delivered through Valheim's own on-screen message RPC.
    ///
    /// It applies exactly one Harmony patch, only when admin chat commands are enabled, and
    /// only on the server. See <see cref="ChatMessagePatch"/> for why no patch-free
    /// alternative exists.
    ///
    /// If the plugin finds itself loaded in a client that is not hosting, it says so once and
    /// then does nothing at all for the rest of the session.
    /// </summary>
    [BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private const float ServerDetectionPollSeconds = 2f;
        private const float ServerDetectionGiveUpSeconds = 300f;

        private ManualLogSource _log;
        private PluginConfig _config;
        private Broadcaster _broadcaster;
        private CleanupManager _manager;
        private Commands _commands;
        private ChatCommandListener _chatCommands;
        private CommandFileWatcher _commandFile;

        private Harmony _harmony;
        private bool _active;
        private bool _shuttingDown;
        private bool _consoleCommandRegistered;

        private void Awake()
        {
            _log = Logger;

            try
            {
                _config = new PluginConfig(Config, _log);
                _broadcaster = new Broadcaster(_log);
                _manager = new CleanupManager(_log, _config, this, _broadcaster);
                _commands = new Commands(_log, _config, _manager, _broadcaster, OnConfigReloaded);
                _chatCommands = new ChatCommandListener(_log, _config, _commands, _broadcaster);
                _commandFile = new CommandFileWatcher(_log, _config, _commands, Paths.ConfigPath);

                _log.LogInfo(PluginInfo.Name + " " + PluginInfo.Version + " loaded. Waiting for the world to start.");

                RegisterConsoleCommand();
                StartCoroutine(WaitForServer(firstAttempt: true));
            }
            catch (Exception ex)
            {
                _log.LogError("Startup failed; the plugin will stay inactive. " + ex);
            }
        }

        /// <summary>
        /// Waits until the process is confirmed to be acting as the Valheim server and the
        /// world is loaded, then arms the cleanup schedule.
        ///
        /// The check is polled rather than patched: no Harmony hook is needed to notice that
        /// the world came up, and polling twice a second for a few minutes costs nothing.
        /// </summary>
        private IEnumerator WaitForServer(bool firstAttempt)
        {
            var waited = 0f;

            while (!_shuttingDown)
            {
                if (ServerState.IsServerReady)
                {
                    Activate();
                    yield break;
                }

                // A client that is not hosting will never satisfy the check. Say so once and
                // stop burning cycles on it. When we are re-watching after a world unload,
                // keep waiting quietly instead - the operator may simply be between worlds.
                if (firstAttempt && waited >= ServerDetectionGiveUpSeconds)
                {
                    _log.LogInfo("Client/non-server instance detected. Cleanup disabled.");
                    _log.LogInfo(
                        "This plugin only does anything on the machine hosting the world. " +
                        "Players do not need to install it.");
                    yield break;
                }

                waited += ServerDetectionPollSeconds;
                yield return new WaitForSeconds(ServerDetectionPollSeconds);
            }
        }

        private void Activate()
        {
            _active = true;

            _log.LogInfo(ServerState.IsDedicated
                ? "Dedicated server detected."
                : "Host (listen server) detected.");
            _log.LogInfo("Server-side cleanup active.");

            if (_config.DryRun)
            {
                _log.LogInfo("Dry-run mode is active. No items will be removed.");
            }
            else
            {
                _log.LogWarning(
                    "LIVE CLEANUP MODE ENABLED. Eligible dropped items may be permanently removed. " +
                    "Make sure you have a world backup.");
            }

            if (!_config.Enabled)
            {
                _log.LogWarning("Enabled = false in the config, so no automatic cleanup will run.");
            }

            _commandFile.EnsureExists();
            EnableChatCommands();
            _manager.Arm();
        }

        private void Update()
        {
            if (!_active || _shuttingDown)
            {
                return;
            }

            try
            {
                // The world can go away underneath us (server shutdown, or a listen host
                // returning to the menu). Stand down cleanly rather than operating on a
                // half-torn-down scene, and start watching for a new world so that hosting
                // again in the same process re-arms the plugin.
                if (!ServerState.IsServerReady)
                {
                    _log.LogInfo("The world is no longer available; cleanup is standing by.");
                    _active = false;
                    _manager.Shutdown();

                    if (!_shuttingDown)
                    {
                        StartCoroutine(WaitForServer(firstAttempt: false));
                    }

                    return;
                }

                var dt = Time.deltaTime;
                _commandFile.Tick(dt);
                _manager.Tick(dt);
            }
            catch (Exception ex)
            {
                // A per-frame failure must never take the server down or spam every frame.
                _active = false;
                _log.LogError("Cleanup has been disabled after an unexpected failure: " + ex);
            }
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        private void Shutdown()
        {
            if (_shuttingDown)
            {
                return;
            }

            _shuttingDown = true;
            _active = false;

            try
            {
                StopAllCoroutines();
                _manager?.Shutdown();
                DisableChatCommands();
            }
            catch (Exception ex)
            {
                _log?.LogWarning("Error while shutting down: " + ex.Message);
            }
        }

        private void OnConfigReloaded()
        {
            _manager?.ApplyConfigChange();
            _commandFile?.EnsureExists();

            if (_active)
            {
                // Turning EnableChatCommands on or off takes effect without a restart.
                if (_config.EnableChatCommands)
                {
                    EnableChatCommands();
                }
                else
                {
                    DisableChatCommands();
                }
            }
        }

        /// <summary>
        /// Applies the single chat patch, if admin chat commands are switched on. Safe to
        /// call repeatedly. A failure here disables the chat channel and says so; the command
        /// file and console command are unaffected.
        /// </summary>
        private void EnableChatCommands()
        {
            if (!_config.EnableChatCommands || _harmony != null)
            {
                return;
            }

            try
            {
                ChatMessagePatch.Listener = _chatCommands;
                _harmony = new Harmony(PluginInfo.Guid);
                _harmony.PatchAll(typeof(ChatMessagePatch));

                _log.LogInfo(
                    "Admin chat commands are active. An admin can type '" + _config.ChatCommandPrefix +
                    " help' in game.");
                _log.LogInfo(
                    "Note: Valheim sends chat to each player individually, and a player's own " +
                    "message is never routed off their client. A dedicated server therefore only " +
                    "observes chat while at least one OTHER player is connected. The command file " +
                    "is the channel that always works.");
            }
            catch (Exception ex)
            {
                ChatMessagePatch.Listener = null;
                _harmony = null;
                _log.LogWarning(
                    "Could not enable admin chat commands: " + ex.Message +
                    ". Use the command file instead - see the Admin section of the config.");
            }
        }

        /// <summary>Removes the chat patch and detaches the listener.</summary>
        private void DisableChatCommands()
        {
            ChatMessagePatch.Listener = null;

            if (_harmony == null)
            {
                return;
            }

            try
            {
                _harmony.UnpatchSelf();
            }
            catch (Exception ex)
            {
                _log?.LogWarning("Could not remove the chat patch: " + ex.Message);
            }
            finally
            {
                _harmony = null;
            }
        }

        /// <summary>
        /// Registers the console command.
        ///
        /// On a headless dedicated server this is unreachable on its own, because
        /// valheim_server.exe has no console input - use the command file or in-game admin
        /// chat there. It is registered anyway because it IS reachable on a listen host and
        /// through mods that forward client console input to the server.
        /// </summary>
        private void RegisterConsoleCommand()
        {
            if (_consoleCommandRegistered)
            {
                return;
            }

            try
            {
                // The two-overload ConsoleCommand constructor needs an explicit delegate type;
                // a bare lambda would be ambiguous between ConsoleEvent and ConsoleEventFailable.
                var handler = new Terminal.ConsoleEvent(OnConsoleCommand);

                new Terminal.ConsoleCommand(
                    Commands.RootCommand,
                    "Valheim Auto Cleanup admin commands. Try: " + Commands.RootCommand + " help",
                    handler,
                    isCheat: false,
                    isNetwork: false,
                    onlyServer: true,
                    isSecret: false,
                    allowInDevBuild: true,
                    optionsFetcher: null,
                    alwaysRefreshTabOptions: false,
                    remoteCommand: false,
                    onlyAdmin: true);

                _consoleCommandRegistered = true;
            }
            catch (Exception ex)
            {
                _log.LogWarning(
                    "Could not register the '" + Commands.RootCommand + "' console command: " + ex.Message +
                    ". The command file and admin chat commands still work.");
            }
        }

        /// <summary>Console-command handler. Output goes to the caller's terminal and the log.</summary>
        private void OnConsoleCommand(Terminal.ConsoleEventArgs args)
        {
            var arguments = string.Empty;
            if (args?.Args != null && args.Args.Length > 1)
            {
                arguments = string.Join(" ", args.Args, 1, args.Args.Length - 1);
            }

            foreach (var line in _commands.Execute(arguments))
            {
                args?.Context?.AddString(line);
                _log.LogInfo(line);
            }
        }
    }
}
