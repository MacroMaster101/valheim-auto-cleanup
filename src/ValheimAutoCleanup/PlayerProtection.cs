using System.Collections.Generic;
using ValheimAutoCleanup.Models;
using ValheimAutoCleanup.Server;
using UnityEngine;

namespace ValheimAutoCleanup
{
    /// <summary>
    /// Player proximity, plus the "a player was here recently" grace period.
    ///
    /// The grace period exists because a scan is a snapshot. Without it, a player who
    /// clears a dungeon, drops the overflow outside, and walks 30 m away would lose that
    /// loot to the very next pass. With it, the item stays protected for
    /// <c>RecentPlayerProtectionSeconds</c> after the last time a player was seen near it.
    ///
    /// Tracking is deliberately cheap: one dictionary entry per item that a player has been
    /// near, holding a single timestamp, pruned every pass. Items no player ever approaches
    /// never enter the dictionary at all.
    /// </summary>
    internal sealed class PlayerProtection
    {
        private readonly Dictionary<ZDOID, float> _lastSeenNearPlayer = new Dictionary<ZDOID, float>();
        private readonly List<Vector3> _playerPositions = new List<Vector3>();
        private readonly List<ZDOID> _pruneBuffer = new List<ZDOID>();
        private readonly HashSet<ZDOID> _liveIds = new HashSet<ZDOID>();

        /// <summary>Player positions captured at the start of the current pass.</summary>
        internal IReadOnlyList<Vector3> PlayerPositions => _playerPositions;

        /// <summary>Number of items currently carrying a recent-activity timestamp.</summary>
        internal int TrackedItemCount => _lastSeenNearPlayer.Count;

        /// <summary>
        /// Refreshes the cached player positions. Called once per pass so that every item in
        /// the pass is evaluated against the same snapshot.
        /// </summary>
        internal void BeginPass()
        {
            ServerState.CollectPlayerPositions(_playerPositions);
        }

        /// <summary>
        /// Records proximity and reports it. Returns <see cref="CleanupDecision.PlayerNearby"/>
        /// while a player is inside the radius, <see cref="CleanupDecision.RecentPlayerActivity"/>
        /// during the grace period afterwards, and <see cref="CleanupDecision.Delete"/> when
        /// neither applies.
        ///
        /// Fail-safe note: when player protection is enabled but the player list could not be
        /// read at all, this reports the item as protected rather than assuming the world is
        /// empty.
        /// </summary>
        internal CleanupDecision Evaluate(
            CleanupCandidate candidate,
            bool protectNearPlayers,
            float radius,
            bool protectRecent,
            float recentSeconds,
            float now)
        {
            if (!protectNearPlayers)
            {
                return CleanupDecision.Delete;
            }

            if (_playerPositions.Count == 0)
            {
                // Nobody is connected. Nothing to protect against, and no uncertainty either:
                // an empty peer list on a running server genuinely means an empty server.
                return ExpiredOrRecent(candidate.Id, protectRecent, recentSeconds, now);
            }

            var radiusSq = radius * radius;
            for (var i = 0; i < _playerPositions.Count; i++)
            {
                if (ServerState.DistanceSquared(_playerPositions[i], candidate.Position) <= radiusSq)
                {
                    _lastSeenNearPlayer[candidate.Id] = now;
                    return CleanupDecision.PlayerNearby;
                }
            }

            return ExpiredOrRecent(candidate.Id, protectRecent, recentSeconds, now);
        }

        private CleanupDecision ExpiredOrRecent(ZDOID id, bool protectRecent, float recentSeconds, float now)
        {
            if (!protectRecent || recentSeconds <= 0f)
            {
                return CleanupDecision.Delete;
            }

            if (!_lastSeenNearPlayer.TryGetValue(id, out var lastSeen))
            {
                return CleanupDecision.Delete;
            }

            if (now - lastSeen < recentSeconds)
            {
                return CleanupDecision.RecentPlayerActivity;
            }

            _lastSeenNearPlayer.Remove(id);
            return CleanupDecision.Delete;
        }

        /// <summary>
        /// Drops timestamps for items that no longer exist and for entries that have aged
        /// past the grace period. Without this the dictionary would grow for the lifetime of
        /// the server.
        /// </summary>
        internal void Prune(IReadOnlyList<CleanupCandidate> liveCandidates, float recentSeconds, float now)
        {
            if (_lastSeenNearPlayer.Count == 0)
            {
                return;
            }

            _liveIds.Clear();
            for (var i = 0; i < liveCandidates.Count; i++)
            {
                _liveIds.Add(liveCandidates[i].Id);
            }

            _pruneBuffer.Clear();
            foreach (var pair in _lastSeenNearPlayer)
            {
                var expired = now - pair.Value >= recentSeconds;
                if (expired || !_liveIds.Contains(pair.Key))
                {
                    _pruneBuffer.Add(pair.Key);
                }
            }

            for (var i = 0; i < _pruneBuffer.Count; i++)
            {
                _lastSeenNearPlayer.Remove(_pruneBuffer[i]);
            }

            _pruneBuffer.Clear();
            _liveIds.Clear();
        }

        /// <summary>Forgets everything. Called on shutdown and on world unload.</summary>
        internal void Clear()
        {
            _lastSeenNearPlayer.Clear();
            _playerPositions.Clear();
            _pruneBuffer.Clear();
            _liveIds.Clear();
        }
    }
}
