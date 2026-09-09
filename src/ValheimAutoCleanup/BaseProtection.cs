using System.Collections.Generic;
using ValheimAutoCleanup.Models;
using ValheimAutoCleanup.Server;
using UnityEngine;

namespace ValheimAutoCleanup
{
    /// <summary>
    /// The place-based protections: player bases, active wards, and the world spawn area.
    ///
    /// All three work off geometry gathered during the same ZDO scan that produced the
    /// candidates, so no extra world queries happen here.
    /// </summary>
    internal static class BaseProtection
    {
        /// <summary>
        /// Applies base, ward and spawn protection in that order and returns the first rule
        /// that keeps the item, or <see cref="CleanupDecision.Delete"/> when none of them do.
        /// </summary>
        internal static CleanupDecision Evaluate(
            CleanupCandidate candidate,
            PluginConfig config,
            SpatialIndex basePieces,
            IReadOnlyList<WardArea> wards)
        {
            if (config.ProtectNearPlayerBase && basePieces.AnyWithin(candidate.Position, config.BaseProtectionRadius))
            {
                return CleanupDecision.BaseProtected;
            }

            if (config.ProtectItemsInsideActiveWards && IsInsideWard(candidate.Position, wards))
            {
                return CleanupDecision.WardProtected;
            }

            if (config.ProtectNearWorldSpawn && IsNearWorldSpawn(candidate.Position, config.WorldSpawnProtectionRadius))
            {
                return CleanupDecision.WorldSpawnProtected;
            }

            return CleanupDecision.Delete;
        }

        /// <summary>
        /// True when the point is inside an active ward's sphere of influence.
        /// Only wards whose ZDO reports them as enabled were collected by the scanner, so a
        /// switched-off ward protects nothing - matching what the ward does in-game.
        /// </summary>
        private static bool IsInsideWard(Vector3 point, IReadOnlyList<WardArea> wards)
        {
            for (var i = 0; i < wards.Count; i++)
            {
                var ward = wards[i];
                if (ward.Radius <= 0f)
                {
                    continue;
                }

                if (ServerState.DistanceSquared(ward.Position, point) <= ward.Radius * ward.Radius)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when the point is inside the protected radius around the starting stones.
        ///
        /// Fail-safe: if the spawn location has not been resolved yet (world generation not
        /// finished, or the location genuinely missing), this returns true so that enabling
        /// the option never silently does nothing. An admin who turned spawn protection on
        /// gets protection, not a guess.
        /// </summary>
        private static bool IsNearWorldSpawn(Vector3 point, float radius)
        {
            if (!ServerState.TryGetWorldSpawn(out var spawn))
            {
                return true;
            }

            return ServerState.DistanceSquared(spawn, point) <= radius * radius;
        }
    }
}
