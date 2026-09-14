using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using BepInEx.Logging;
using ValheimAutoCleanup.Models;
using ValheimAutoCleanup.Policy;
using ValheimAutoCleanup.Server;
using UnityEngine;

namespace ValheimAutoCleanup
{
    /// <summary>How a cleanup pass was started, for logging and history.</summary>
    internal enum CleanupTrigger
    {
        Scheduled,
        Startup,
        Manual,
        Preview
    }

    /// <summary>
    /// Owns the cleanup schedule and runs the passes.
    ///
    /// Scheduling is driven by a lightweight timer ticked from the host behaviour's Update;
    /// the only per-frame work is adding a delta to a float and comparing it. All real work
    /// happens inside a coroutine that yields between removal batches, so a pass that clears
    /// hundreds of objects never produces a frame spike.
    ///
    /// Everything here runs on Unity's main thread. Nothing in this plugin touches Unity or
    /// Valheim objects from a background thread.
    /// </summary>
    internal sealed class CleanupManager
    {
        private readonly ManualLogSource _log;
        private readonly PluginConfig _config;
        private readonly MonoBehaviour _host;

        private readonly PrefabCatalog _catalog;
        private readonly ItemScanner _scanner;
        private readonly PlayerProtection _playerProtection;
        private readonly ItemEvaluator _evaluator;
        private readonly NetworkDestroyer _destroyer;
        private readonly Broadcaster _broadcaster;

        private readonly List<CleanupCandidate> _removalQueue = new List<CleanupCandidate>();

        private float _timeUntilNextCleanup;
        private bool _warningSent;
        private bool _finalWarningSent;
        private bool _armed;
        private Coroutine _running;

        internal CleanupManager(ManualLogSource log, PluginConfig config, MonoBehaviour host, Broadcaster broadcaster)
        {
            _log = log;
            _config = config;
            _host = host;
            _broadcaster = broadcaster;

            _catalog = new PrefabCatalog(log);
            _scanner = new ItemScanner(log, _catalog);
            _playerProtection = new PlayerProtection();
            _evaluator = new ItemEvaluator(log, _playerProtection);
            _destroyer = new NetworkDestroyer(log);
        }

        internal CleanupHistory History { get; } = new CleanupHistory();

        /// <summary>True while a pass is in progress. Used to reject overlapping requests.</summary>
        internal bool IsRunning => _running != null;

        /// <summary>Seconds of real time until the next scheduled pass, or null when not armed.</summary>
        internal float? SecondsUntilNextCleanup => _armed ? _timeUntilNextCleanup : (float?)null;

        /// <summary>
        /// Starts the schedule. Called once the world is loaded and the server is confirmed.
        /// The first pass is delayed by <c>StartupCleanupDelaySeconds</c> so that world load,
        /// zone generation and the initial ZDO load are all finished first.
        /// </summary>
        internal void Arm()
        {
            _armed = true;

            var startupDelay = Math.Max(_config.StartupCleanupDelaySeconds, 0);

            if (_config.RunCleanupOnServerStart)
            {
                // The startup pass and the first scheduled pass would otherwise both fall due
                // at the same moment, and the concurrency guard would silently drop one of
                // them. Push the scheduled one out by a full interval instead.
                ResetTimer(startupDelay + _config.CleanupIntervalSeconds);

                _log.LogInfo(
                    "A startup cleanup is scheduled for " + startupDelay + " seconds from now; " +
                    "regular passes follow every " + FormatDuration(_config.CleanupIntervalSeconds) + ".");
                _host.StartCoroutine(StartupPass());
            }
            else
            {
                ResetTimer(startupDelay);
                _log.LogInfo(
                    "First scheduled cleanup in " + FormatDuration(_timeUntilNextCleanup) +
                    "; the interval after that is " + FormatDuration(_config.CleanupIntervalSeconds) + ".");
            }
        }

        /// <summary>Stops the schedule and releases everything the manager was holding.</summary>
        internal void Shutdown()
        {
            _armed = false;

            if (_running != null && _host != null)
            {
                _host.StopCoroutine(_running);
                _running = null;
            }

            _removalQueue.Clear();
            _scanner.Clear();
            _playerProtection.Clear();
            _catalog.Invalidate();
            ServerState.Reset();
        }

        /// <summary>
        /// Re-reads configuration and restarts the timer so a changed interval takes effect
        /// immediately. Never interrupts a pass that is already running.
        /// </summary>
        internal void ApplyConfigChange()
        {
            if (_armed)
            {
                ResetTimer(_config.CleanupIntervalSeconds);
            }
        }

        /// <summary>
        /// The per-frame tick. Deliberately trivial: a float add, a couple of comparisons and
        /// nothing else until the interval actually elapses.
        /// </summary>
        internal void Tick(float deltaTime)
        {
            if (!_armed || !_config.Enabled || IsRunning)
            {
                return;
            }

            if (!ServerState.IsServerReady)
            {
                return;
            }

            _timeUntilNextCleanup -= deltaTime;

            // Do not announce a pass that is about to be skipped anyway.
            if (_config.EnableCleanupWarnings && !WillSkipNextPass())
            {
                MaybeWarn();
            }

            if (_timeUntilNextCleanup > 0f)
            {
                return;
            }

            ResetTimer(_config.CleanupIntervalSeconds);

            if (_config.OnlyCleanupWhenServerEmpty && ServerState.ConnectedPlayerCount > 0)
            {
                if (_config.LogDebugInformation)
                {
                    _log.LogInfo("Skipping this pass: OnlyCleanupWhenServerEmpty is on and players are connected.");
                }

                return;
            }

            Begin(CleanupTrigger.Scheduled, _config.DryRun);
        }

        /// <summary>
        /// Requests a pass outside the schedule.
        /// <paramref name="forceDryRun"/> makes the pass report-only regardless of config.
        /// Returns false and explains why when a pass cannot start right now.
        /// </summary>
        internal bool RequestManualCleanup(bool forceDryRun, out string message)
        {
            if (IsRunning)
            {
                message = "A cleanup is already in progress.";
                return false;
            }

            if (!ServerState.IsServerReady)
            {
                message = "The world is not ready yet.";
                return false;
            }

            var dryRun = forceDryRun || _config.DryRun;
            Begin(CleanupTrigger.Manual, dryRun);
            message = dryRun
                ? "Dry-run cleanup started; nothing will be removed."
                : "Cleanup started.";
            return true;
        }

        /// <summary>
        /// Runs a full scan and reports the outcome without removing anything or touching the
        /// schedule. Backs the <c>preview</c> command.
        /// </summary>
        internal bool RequestPreview(out string message)
        {
            if (IsRunning)
            {
                message = "A cleanup is already in progress.";
                return false;
            }

            if (!ServerState.IsServerReady)
            {
                message = "The world is not ready yet.";
                return false;
            }

            Begin(CleanupTrigger.Preview, true);
            message = "Preview started; results will follow in the log.";
            return true;
        }

        private void Begin(CleanupTrigger trigger, bool dryRun)
        {
            if (IsRunning)
            {
                _log.LogInfo("Cleanup already in progress; ignoring the new request.");
                return;
            }

            _running = _host.StartCoroutine(RunPass(trigger, dryRun));
        }

        private IEnumerator StartupPass()
        {
            yield return new WaitForSeconds(Math.Max(_config.StartupCleanupDelaySeconds, 0));

            if (!_armed || !_config.Enabled)
            {
                yield break;
            }

            if (!ServerState.IsServerReady)
            {
                _log.LogWarning("Skipping the startup cleanup: the world is still not ready.");
                yield break;
            }

            Begin(CleanupTrigger.Startup, _config.DryRun);
        }

        /// <summary>
        /// One complete pass. Wrapped so that no failure here can take the server down; the
        /// coroutine always clears the running flag on the way out.
        /// </summary>
        private IEnumerator RunPass(CleanupTrigger trigger, bool dryRun)
        {
            var stats = new CleanupStatistics();
            var stopwatch = Stopwatch.StartNew();

            // The scan and evaluation phase does no yielding: it is a single dictionary walk
            // plus cheap per-item tests, and splitting it would risk evaluating half the world
            // against one snapshot and half against another.
            var scanned = false;
            try
            {
                scanned = ScanAndEvaluate(stats, trigger);
            }
            catch (Exception ex)
            {
                stats.Errors++;
                _log.LogError("Cleanup scan failed: " + ex);
            }

            if (!scanned)
            {
                stopwatch.Stop();
                _running = null;
                yield break;
            }

            if (trigger == CleanupTrigger.Preview)
            {
                for (var i = 0; i < _removalQueue.Count; i++)
                {
                    stats.Record(CleanupDecision.DryRunCandidate);
                    stats.CountRemoval(_removalQueue[i].PrefabName, _removalQueue[i].Stack);
                }

                stopwatch.Stop();
                stats.ExecutionMilliseconds = stopwatch.ElapsedMilliseconds;
                _log.LogInfo(stats.BuildReport("Cleanup preview (nothing was removed):", dryRun: true));
                Cleanup(stats, dryRun: true, trigger, stopwatch);
                yield break;
            }

            // Removal phase. Batched and yielded so a large pass costs a little time across
            // several frames instead of one long stall.
            var perFrame = Math.Max(1, _config.DeletesPerFrame);
            var batchDelay = Math.Max(0, _config.DelayBetweenDeleteBatchesMilliseconds) / 1000f;
            var inBatch = 0;

            for (var i = 0; i < _removalQueue.Count; i++)
            {
                var candidate = _removalQueue[i];

                if (dryRun)
                {
                    stats.Record(CleanupDecision.DryRunCandidate);
                    stats.CountRemoval(candidate.PrefabName, candidate.Stack);

                    if (_config.LogDeletedItems)
                    {
                        _log.LogInfo(
                            "Would remove " + candidate.PrefabName + " x" + candidate.Stack +
                            " at " + FormatPosition(candidate.Position));
                    }

                    continue;
                }

                CleanupDecision outcome;
                try
                {
                    outcome = _destroyer.Destroy(candidate, _config.LogDebugInformation);
                }
                catch (Exception ex)
                {
                    stats.Errors++;
                    _log.LogWarning("Unexpected failure removing an item: " + ex.Message);
                    outcome = CleanupDecision.DeletionFailure;
                }

                stats.Record(outcome);

                if (outcome == CleanupDecision.Delete)
                {
                    stats.CountRemoval(candidate.PrefabName, candidate.Stack);

                    if (_config.LogDeletedItems)
                    {
                        _log.LogInfo(
                            "Removed " + candidate.PrefabName + " x" + candidate.Stack +
                            " at " + FormatPosition(candidate.Position));
                    }
                }

                inBatch++;
                if (inBatch >= perFrame)
                {
                    inBatch = 0;

                    // Bail out early if the world went away mid-pass (server shutting down).
                    if (!ServerState.IsServerReady)
                    {
                        _log.LogWarning("The world became unavailable during cleanup; stopping this pass early.");
                        break;
                    }

                    if (batchDelay > 0f)
                    {
                        yield return new WaitForSeconds(batchDelay);
                    }
                    else
                    {
                        yield return null;
                    }
                }
            }

            stopwatch.Stop();
            stats.ExecutionMilliseconds = stopwatch.ElapsedMilliseconds;

            if (_config.LogCleanupSummary)
            {
                _log.LogInfo(stats.BuildSummary(dryRun));
            }

            // The same breakdown the preview command prints, so an operator can see what a
            // pass did without having to ask. Skipped when there was nothing on the ground.
            if (_config.LogCleanupReport && stats.ItemDropsFound > 0)
            {
                _log.LogInfo(stats.BuildReport(
                    dryRun ? "Cleanup report (dry run, nothing was removed):" : "Cleanup report:",
                    dryRun));
            }

            if (dryRun && stats.DryRunCandidates > 0)
            {
                _log.LogWarning(
                    "DRY RUN: " + stats.DryRunCandidates + " items would have been removed. " +
                    "Set DryRun = false in the config to make cleanup live.");
            }

            if (_config.CleanupCompleteMessageEnabled)
            {
                var removed = dryRun ? stats.DryRunCandidates : stats.Deleted;
                if (removed > 0)
                {
                    _broadcaster.Announce(
                        dryRun
                            ? "Cleanup finished (dry run): " + removed + " old dropped items would have been removed."
                            : "Cleanup finished: " + removed + " old dropped items removed.",
                        _config.AnnouncementPrefix,
                        _config.AnnouncementStyle);
                }
            }

            Cleanup(stats, dryRun, trigger, stopwatch);
        }

        /// <summary>
        /// Scans the world and sorts every candidate into "remove" or "keep, because...".
        /// Returns false when the world could not be scanned.
        /// </summary>
        private bool ScanAndEvaluate(CleanupStatistics stats, CleanupTrigger trigger)
        {
            _removalQueue.Clear();

            var collectWards = _config.ProtectItemsInsideActiveWards;
            var collectPieces = _config.ProtectNearPlayerBase;

            if (!_scanner.Scan(collectWards, collectPieces, _config.ProtectFish, _config.LogDebugInformation))
            {
                return false;
            }

            stats.TotalObjectsScanned = _scanner.ObjectsScanned;
            stats.ItemDropsFound = _scanner.Candidates.Count;

            _playerProtection.BeginPass();

            var now = Time.realtimeSinceStartup;
            var budget = _config.MaxDeletesPerCleanup;
            var candidates = _scanner.Candidates;

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                CleanupDecision decision;

                try
                {
                    decision = _evaluator.Evaluate(
                        candidate, _config, _scanner.BasePieces, _scanner.Wards, now);
                }
                catch (Exception ex)
                {
                    // Belt and braces: the evaluator already swallows its own failures.
                    stats.Errors++;
                    _log.LogWarning("Keeping an item that could not be evaluated: " + ex.Message);
                    decision = CleanupDecision.Invalid;
                }

                if (decision != CleanupDecision.Delete)
                {
                    stats.Record(decision);

                    if (_config.LogProtectedItems)
                    {
                        _log.LogInfo(
                            "Keeping " + candidate.PrefabName + " x" + candidate.Stack +
                            " at " + FormatPosition(candidate.Position) +
                            " (" + ItemEvaluator.Describe(decision) + ")");
                    }

                    continue;
                }

                // A preview reports the whole eligible set; a real pass respects the budget.
                if (trigger != CleanupTrigger.Preview && _removalQueue.Count >= budget)
                {
                    stats.Record(CleanupDecision.BudgetExhausted);
                    continue;
                }

                _removalQueue.Add(candidate);
            }

            _playerProtection.Prune(candidates, _config.RecentPlayerProtectionSeconds, now);
            return true;
        }

        private void Cleanup(CleanupStatistics stats, bool dryRun, CleanupTrigger trigger, Stopwatch stopwatch)
        {
            if (stopwatch.IsRunning)
            {
                stopwatch.Stop();
            }

            if (stats.ExecutionMilliseconds == 0)
            {
                stats.ExecutionMilliseconds = stopwatch.ElapsedMilliseconds;
            }

            History.Add(stats, dryRun, trigger.ToString().ToLowerInvariant());

            // Release the ZDO references the pass was holding. Keeping them alive between
            // passes would pin objects the game wants to release.
            _removalQueue.Clear();
            _scanner.ReleaseCandidates();
            _running = null;
        }

        /// <summary>
        /// True when the next scheduled pass will be skipped, so that warnings are not sent
        /// for a cleanup that never happens.
        /// </summary>
        private bool WillSkipNextPass()
        {
            return _config.OnlyCleanupWhenServerEmpty && ServerState.ConnectedPlayerCount > 0;
        }

        private void MaybeWarn()
        {
            var lead = _config.WarningSecondsBeforeCleanup;
            var finalLead = _config.FinalWarningSeconds;

            if (!_warningSent && lead > 0 && _timeUntilNextCleanup <= lead)
            {
                _warningSent = true;
                _broadcaster.Announce(
                    Broadcaster.Format(_config.WarningMessage, lead),
                    _config.AnnouncementPrefix,
                    _config.AnnouncementStyle);
            }

            if (!_finalWarningSent && finalLead > 0 && _timeUntilNextCleanup <= finalLead)
            {
                _finalWarningSent = true;
                _broadcaster.Announce(
                    Broadcaster.Format(_config.FinalWarningMessage, finalLead),
                    _config.AnnouncementPrefix,
                    _config.AnnouncementStyle);
            }
        }

        private void ResetTimer(float seconds)
        {
            _timeUntilNextCleanup = seconds;
            _warningSent = false;
            _finalWarningSent = false;
        }

        internal static string FormatDuration(double seconds)
        {
            if (seconds < 60d)
            {
                return Math.Round(seconds) + "s";
            }

            var span = TimeSpan.FromSeconds(seconds);
            if (span.TotalHours >= 1d)
            {
                return span.Hours + "h " + span.Minutes + "m";
            }

            return span.Minutes + "m " + span.Seconds + "s";
        }

        private static string FormatPosition(Vector3 position)
        {
            return "(" + Mathf.RoundToInt(position.x) + ", " +
                   Mathf.RoundToInt(position.y) + ", " +
                   Mathf.RoundToInt(position.z) + ")";
        }

        /// <summary>Diagnostics for the <c>status</c> command.</summary>
        internal string DescribeCatalog()
        {
            if (!_catalog.IsBuilt)
            {
                return "prefab catalogue not built yet";
            }

            return _catalog.CleanablePrefabCount + " cleanable item prefabs, " +
                   _catalog.WardPrefabCount + " ward prefabs, " +
                   _catalog.PiecePrefabCount + " build-piece prefabs";
        }

        internal int TrackedProximityEntries => _playerProtection.TrackedItemCount;
    }
}
