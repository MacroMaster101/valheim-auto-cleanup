using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Logging;
using ValheimAutoCleanup.Server;

namespace ValheimAutoCleanup
{
    /// <summary>
    /// The single implementation of every admin command.
    ///
    /// There are three ways to reach it, and all three funnel into <see cref="Execute"/> so
    /// the behaviour is identical whichever one an admin uses:
    ///
    ///   1. A command file the server operator edits through their host's file manager.
    ///      This is the only channel that works on a stock headless server, because
    ///      valheim_server.exe never reads standard input - there is no server console to
    ///      type into.
    ///   2. In-game chat from a verified admin (vanilla client, no mod needed; off by default).
    ///   3. A Terminal console command, which is reachable on a listen host and through
    ///      mods that forward client console input to the server.
    /// </summary>
    internal sealed class Commands
    {
        internal const string RootCommand = "autocleanup";

        private readonly ManualLogSource _log;
        private readonly PluginConfig _config;
        private readonly CleanupManager _manager;
        private readonly Broadcaster _broadcaster;
        private readonly Action _onConfigReloaded;

        internal Commands(
            ManualLogSource log,
            PluginConfig config,
            CleanupManager manager,
            Broadcaster broadcaster,
            Action onConfigReloaded)
        {
            _log = log;
            _config = config;
            _manager = manager;
            _broadcaster = broadcaster;
            _onConfigReloaded = onConfigReloaded;
        }

        /// <summary>
        /// Runs a subcommand and returns the lines to show the caller. Never throws: a bad
        /// command produces an explanation, not an exception.
        /// </summary>
        internal IReadOnlyList<string> Execute(string arguments)
        {
            var output = new List<string>();

            try
            {
                var parts = (arguments ?? string.Empty)
                    .Trim()
                    .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                var subcommand = parts.Length > 0 ? parts[0].ToLowerInvariant() : "help";

                switch (subcommand)
                {
                    case "now":
                        RunNow(output);
                        break;

                    case "dryrun":
                        RunDryRun(output);
                        break;

                    case "preview":
                        RunPreview(output);
                        break;

                    case "status":
                        AppendStatus(output);
                        break;

                    case "stats":
                        AppendStats(output);
                        break;

                    case "history":
                        AppendHistory(output);
                        break;

                    case "announce":
                    case "testmessage":
                        output.Add(_broadcaster.SendTestAnnouncement(_config.AnnouncementPrefix));
                        break;

                    case "reload":
                        _config.Reload();
                        _onConfigReloaded?.Invoke();
                        output.Add("Configuration reloaded.");
                        output.Add(_config.DryRun
                            ? "Dry-run mode is ON. Nothing will be removed."
                            : "LIVE mode is ON. Eligible dropped items will be removed permanently.");
                        break;

                    case "enable":
                        _config.SetEnabled(true);
                        _onConfigReloaded?.Invoke();
                        output.Add("Automatic cleanup enabled.");
                        break;

                    case "disable":
                        _config.SetEnabled(false);
                        output.Add("Automatic cleanup disabled. Manual commands still work.");
                        break;

                    case "help":
                    case "?":
                        AppendHelp(output);
                        break;

                    default:
                        output.Add("Unknown subcommand '" + subcommand + "'.");
                        AppendHelp(output);
                        break;
                }
            }
            catch (Exception ex)
            {
                _log.LogError("Command failed: " + ex);
                output.Add("The command failed: " + ex.Message);
            }

            return output;
        }

        private void RunNow(List<string> output)
        {
            if (_manager.RequestManualCleanup(false, out var message))
            {
                output.Add(message);
                if (_config.DryRun)
                {
                    output.Add("DryRun is true in the config, so this pass will not remove anything.");
                }
            }
            else
            {
                output.Add(message);
            }
        }

        private void RunDryRun(List<string> output)
        {
            _manager.RequestManualCleanup(true, out var message);
            output.Add(message);
        }

        private void RunPreview(List<string> output)
        {
            _manager.RequestPreview(out var message);
            output.Add(message);
        }

        private void AppendStatus(List<string> output)
        {
            output.Add("Valheim Auto Cleanup " + PluginInfo.Version);
            output.Add("  Server ready      : " + (ServerState.IsServerReady ? "yes" : "no") +
                       (ServerState.IsDedicated ? " (dedicated)" : " (listen host)"));
            output.Add("  Enabled           : " + _config.Enabled);
            output.Add("  Dry run           : " + _config.DryRun +
                       (_config.DryRun ? "  <- nothing is being removed" : "  <- LIVE, items are removed"));
            output.Add("  Cleanup interval  : " + CleanupManager.FormatDuration(_config.CleanupIntervalSeconds));
            output.Add("  Minimum item age  : " + CleanupManager.FormatDuration(_config.MinimumItemAgeSeconds) +
                       " of world time");
            output.Add("  Player protection : " + (_config.ProtectNearPlayers
                           ? _config.PlayerProtectionRadius + " m"
                           : "off"));
            output.Add("  Recent-player     : " + (_config.ProtectRecentlyNearbyItems
                           ? CleanupManager.FormatDuration(_config.RecentPlayerProtectionSeconds)
                           : "off"));
            output.Add("  Base protection   : " + (_config.ProtectNearPlayerBase
                           ? _config.BaseProtectionRadius + " m"
                           : "off"));
            output.Add("  Ward protection   : " + (_config.ProtectItemsInsideActiveWards ? "on" : "off"));
            output.Add("  Spawn protection  : " + (_config.ProtectNearWorldSpawn
                           ? _config.WorldSpawnProtectionRadius + " m"
                           : "off"));
            output.Add("  Blacklist only    : " + _config.Policy.BlacklistOnly +
                       (_config.Policy.BlacklistOnly ? " (" + _config.Policy.Blacklist.Count + " entries)" : ""));
            output.Add("  Equipment kept    : " + _config.Policy.ProtectEquipment);
            output.Add("  Upgraded kept     : " + _config.Policy.ProtectUpgradedItems);
            output.Add("  Fish kept         : " + _config.ProtectFish);
            output.Add("  Whitelist         : " + _config.Policy.Whitelist.Count + " entries");
            output.Add("  Important items   : " + _config.Policy.ImportantItems.Count + " entries");
            output.Add("  Players online    : " + ServerState.ConnectedPlayerCount);
            output.Add("  Prefab catalogue  : " + _manager.DescribeCatalog());

            var next = _manager.SecondsUntilNextCleanup;
            output.Add("  Next cleanup      : " + (_manager.IsRunning
                           ? "running now"
                           : next.HasValue
                               ? "in " + CleanupManager.FormatDuration(next.Value)
                               : "not scheduled"));

            var history = _manager.History;
            output.Add("  Last cleanup      : " + (history.LastCleanupUtc.HasValue
                           ? history.LastCleanupUtc.Value.ToString("yyyy-MM-dd HH:mm:ss") + "Z, " +
                             history.LastDeletedCount + " removed"
                           : "none yet"));
            output.Add("  Session removed   : " + history.TotalDeleted);
        }

        private void AppendStats(List<string> output)
        {
            var history = _manager.History;
            output.Add("Session statistics:");
            output.Add("  Cleanup passes        : " + history.TotalCleanups);
            output.Add("  Items removed         : " + history.TotalDeleted);
            output.Add("  Dry-run candidates    : " + history.TotalDryRunCandidates);
            output.Add("  Errors                : " + history.TotalErrors);
            output.Add("  Proximity entries held: " + _manager.TrackedProximityEntries);
        }

        private void AppendHistory(List<string> output)
        {
            var any = false;
            output.Add("Recent cleanup passes (most recent first):");

            foreach (var entry in _manager.History.Recent())
            {
                output.Add("  " + entry);
                any = true;
            }

            if (!any)
            {
                output.Add("  (none yet)");
            }
        }

        private static void AppendHelp(List<string> output)
        {
            output.Add("Valheim Auto Cleanup commands:");
            output.Add("  " + RootCommand + " now      - run a cleanup now, honouring the DryRun setting");
            output.Add("  " + RootCommand + " dryrun   - run one report-only pass, never removes anything");
            output.Add("  " + RootCommand + " preview  - scan and report what would be removed, grouped by prefab");
            output.Add("  " + RootCommand + " status   - show configuration and schedule");
            output.Add("  " + RootCommand + " stats    - show session totals");
            output.Add("  " + RootCommand + " history  - show the last 10 passes");
            output.Add("  " + RootCommand + " announce - send a test on-screen message to every player");
            output.Add("  " + RootCommand + " reload   - re-read the config file and restart the timer");
            output.Add("  " + RootCommand + " enable   - turn automatic cleanup on");
            output.Add("  " + RootCommand + " disable  - turn automatic cleanup off");
            output.Add("  " + RootCommand + " help     - this list");
        }

        /// <summary>Joins command output for a single-line channel such as in-game chat.</summary>
        internal static string Flatten(IReadOnlyList<string> lines, int maxLines)
        {
            var sb = new StringBuilder();
            var limit = Math.Min(lines.Count, maxLines);

            for (var i = 0; i < limit; i++)
            {
                if (sb.Length > 0)
                {
                    sb.Append(" | ");
                }

                sb.Append(lines[i].Trim());
            }

            if (lines.Count > limit)
            {
                sb.Append(" | (").Append(lines.Count - limit).Append(" more lines in the server log)");
            }

            return sb.ToString();
        }
    }
}
