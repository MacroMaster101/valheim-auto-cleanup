using System;
using System.Collections.Generic;
using BepInEx.Logging;
using ValheimAutoCleanup.Models;
using ValheimAutoCleanup.Policy;
using ValheimAutoCleanup.Server;

namespace ValheimAutoCleanup
{
    /// <summary>
    /// The full evaluation pipeline for one candidate: intrinsic rules first (cheap,
    /// allocation-free), then the spatial protections (which need world state).
    ///
    /// Every gate here is a veto. A candidate reaches <see cref="CleanupDecision.Delete"/>
    /// only by passing all of them, and any exception anywhere in the chain results in the
    /// item being kept.
    /// </summary>
    internal sealed class ItemEvaluator
    {
        private readonly ManualLogSource _log;
        private readonly PlayerProtection _playerProtection;
        private readonly PolicyItem _scratch = new PolicyItem();

        internal ItemEvaluator(ManualLogSource log, PlayerProtection playerProtection)
        {
            _log = log;
            _playerProtection = playerProtection;
        }

        /// <summary>
        /// Evaluates one candidate. <paramref name="now"/> is real time in seconds, used only
        /// for the recent-player grace period; item ages use world time and come from the
        /// scanner.
        /// </summary>
        internal CleanupDecision Evaluate(
            CleanupCandidate candidate,
            PluginConfig config,
            SpatialIndex basePieces,
            IReadOnlyList<WardArea> wards,
            float now)
        {
            try
            {
                // 1. The object must still exist and still be a live network object. Items get
                //    picked up between the scan and the evaluation all the time; that is a
                //    normal race, not an error.
                if (candidate.Zdo == null || !candidate.Zdo.IsValid())
                {
                    return CleanupDecision.Invalid;
                }

                // 2. Item-intrinsic rules: age, name lists, category, quality, stack size.
                _scratch.PrefabName = candidate.PrefabName;
                _scratch.AgeSeconds = candidate.AgeSeconds;
                _scratch.StackSize = candidate.Stack;
                _scratch.Quality = candidate.Quality;
                _scratch.ItemType = candidate.ItemType;

                var intrinsic = ItemCleanupPolicy.Evaluate(_scratch, config.Policy);
                if (intrinsic != CleanupDecision.Delete)
                {
                    return intrinsic;
                }

                // 3. Player proximity, including the post-departure grace period.
                var proximity = _playerProtection.Evaluate(
                    candidate,
                    config.ProtectNearPlayers,
                    config.PlayerProtectionRadius,
                    config.ProtectRecentlyNearbyItems,
                    config.RecentPlayerProtectionSeconds,
                    now);

                if (proximity != CleanupDecision.Delete)
                {
                    return proximity;
                }

                // 4. Bases, wards and the world spawn area.
                var place = BaseProtection.Evaluate(candidate, config, basePieces, wards);
                if (place != CleanupDecision.Delete)
                {
                    return place;
                }

                return CleanupDecision.Delete;
            }
            catch (Exception ex)
            {
                // Fail-safe: an item we could not finish evaluating is an item we keep.
                if (config.LogDebugInformation)
                {
                    _log.LogWarning(
                        "Keeping '" + candidate.PrefabName + "' because it could not be evaluated: " + ex.Message);
                }

                return CleanupDecision.Invalid;
            }
        }

        /// <summary>Human-readable reason text used by the verbose protected-item log.</summary>
        internal static string Describe(CleanupDecision decision)
        {
            switch (decision)
            {
                case CleanupDecision.TooYoung: return "not old enough yet";
                case CleanupDecision.PlayerNearby: return "a player is nearby";
                case CleanupDecision.RecentPlayerActivity: return "a player was nearby recently";
                case CleanupDecision.BaseProtected: return "inside a player base";
                case CleanupDecision.WardProtected: return "inside an active ward";
                case CleanupDecision.WorldSpawnProtected: return "inside the world spawn area";
                case CleanupDecision.Whitelisted: return "whitelisted";
                case CleanupDecision.ImportantItem: return "on the important-item list";
                case CleanupDecision.Equipment: return "equipment";
                case CleanupDecision.Upgraded: return "upgraded (quality above 1)";
                case CleanupDecision.LargeStack: return "large stack";
                case CleanupDecision.NotBlacklisted: return "not on the blacklist";
                case CleanupDecision.NotALooseDrop: return "not a loose drop";
                case CleanupDecision.Invalid: return "invalid or unknown state";
                case CleanupDecision.OwnershipFailure: return "the server could not take ownership";
                case CleanupDecision.DeletionFailure: return "removal failed";
                case CleanupDecision.BudgetExhausted: return "the per-pass removal limit was reached";
                default: return decision.ToString();
            }
        }
    }
}
