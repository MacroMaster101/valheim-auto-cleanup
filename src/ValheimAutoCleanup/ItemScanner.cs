using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using ValheimAutoCleanup.Models;
using ValheimAutoCleanup.Server;
using UnityEngine;

namespace ValheimAutoCleanup
{
    /// <summary>
    /// Finds the loose item drops in the world, together with the ward and build-piece
    /// geometry needed for the spatial protections, in a single pass.
    ///
    /// WHY THIS SCANS ZDOs RATHER THAN GameObjects
    /// -------------------------------------------
    /// Valheim's dedicated server only instantiates GameObjects around
    /// <c>ZNet.GetReferencePosition()</c>, and nothing on a headless server ever moves that
    /// reference position - only <c>Player.LateUpdate</c>, <c>Game.FindSpawnPoint</c> and a
    /// couple of other client-side paths call <c>SetReferencePosition</c>. In practice this
    /// means a dedicated server has GameObjects only in the zones around the world origin,
    /// while it holds the ZDO (the authoritative network record) for every object in the
    /// entire world.
    ///
    /// A scanner that only looked at loaded components - <c>ItemDrop.s_instances</c> or
    /// <c>FindObjectsOfType</c> - would therefore find almost nothing on the exact kind of
    /// server this plugin is written for. Scanning ZDOs is the only approach that actually
    /// works, and it is also far cheaper: one dictionary walk, no Unity object churn.
    ///
    /// Where a GameObject *does* happen to exist for a candidate, the destroyer routes
    /// through <c>ZNetScene.Destroy</c> so the local instance is torn down properly. See
    /// <c>NetworkDestroyer</c>.
    /// </summary>
    internal sealed class ItemScanner
    {
        /// <summary>
        /// Build pieces are bucketed at 16 m, comfortably above the default 15 m base radius,
        /// so a typical query touches a 3x3 block of cells.
        /// </summary>
        private const float PieceCellSize = 16f;

        private readonly ManualLogSource _log;
        private readonly PrefabCatalog _catalog;

        private readonly List<CleanupCandidate> _candidates = new List<CleanupCandidate>();
        private readonly Stack<CleanupCandidate> _pool = new Stack<CleanupCandidate>();
        private readonly List<WardArea> _wards = new List<WardArea>();
        private readonly SpatialIndex _basePieces = new SpatialIndex(PieceCellSize);
        private readonly List<ZDO> _zdoBuffer = new List<ZDO>();

        private FieldInfo _objectsByIdField;
        private bool _objectsByIdLookupAttempted;
        private bool _warnedAboutFallback;

        internal ItemScanner(ManualLogSource log, PrefabCatalog catalog)
        {
            _log = log;
            _catalog = catalog;
        }

        /// <summary>Loose item drops found by the most recent scan.</summary>
        internal IReadOnlyList<CleanupCandidate> Candidates => _candidates;

        /// <summary>Active wards found by the most recent scan. Empty unless ward protection is on.</summary>
        internal IReadOnlyList<WardArea> Wards => _wards;

        /// <summary>Player-built pieces found by the most recent scan. Empty unless base protection is on.</summary>
        internal SpatialIndex BasePieces => _basePieces;

        /// <summary>Total ZDOs examined by the most recent scan.</summary>
        internal int ObjectsScanned { get; private set; }

        /// <summary>
        /// Walks the world's ZDO table once, filling the candidate list and the protector
        /// geometry. Returns false when the world is not in a state that can be scanned, in
        /// which case nothing is touched.
        /// </summary>
        internal bool Scan(bool collectWards, bool collectBasePieces, bool protectFish, bool debugLog)
        {
            ReleaseCandidates();
            _wards.Clear();
            _basePieces.Clear();
            ObjectsScanned = 0;

            if (!ServerState.IsServerReady)
            {
                return false;
            }

            if (!_catalog.Build())
            {
                _log.LogWarning("Prefab catalogue is not available yet; skipping this pass.");
                return false;
            }

            if (!TryCollectZdos(_zdoBuffer))
            {
                return false;
            }

            var worldTime = ServerState.WorldTimeSeconds;

            for (var i = 0; i < _zdoBuffer.Count; i++)
            {
                var zdo = _zdoBuffer[i];
                ObjectsScanned++;

                try
                {
                    Examine(zdo, worldTime, collectWards, collectBasePieces, protectFish);
                }
                catch (Exception ex)
                {
                    // A single malformed ZDO must never abort the pass.
                    if (debugLog)
                    {
                        _log.LogWarning("Skipped an object that could not be examined: " + ex.Message);
                    }
                }
            }

            _zdoBuffer.Clear();

            if (debugLog)
            {
                _log.LogInfo(
                    "Scan finished: " + ObjectsScanned + " objects examined, " +
                    _candidates.Count + " loose drops, " +
                    _wards.Count + " active wards, " +
                    _basePieces.Count + " player-built pieces.");
            }

            return true;
        }

        private void Examine(ZDO zdo, double worldTime, bool collectWards, bool collectBasePieces, bool protectFish)
        {
            if (zdo == null || !zdo.IsValid())
            {
                return;
            }

            var info = _catalog.Lookup(zdo.GetPrefab());
            if (info == null)
            {
                // A prefab the catalogue does not know about. Unknown means untouched.
                return;
            }

            if (info.IsWard)
            {
                if (collectWards && zdo.GetBool(ZDOVars.s_enabled, false))
                {
                    _wards.Add(new WardArea { Position = zdo.GetPosition(), Radius = info.WardRadius });
                }

                return;
            }

            if (info.IsPiece)
            {
                // Only pieces a player actually built count as "base". Ruins and dungeon
                // furniture generated by the world have no creator.
                if (collectBasePieces && zdo.GetLong(ZDOVars.s_creator, 0L) != 0L)
                {
                    _basePieces.Add(zdo.GetPosition());
                }

                return;
            }

            if (!info.IsCleanableItem)
            {
                return;
            }

            if (protectFish && info.IsFish)
            {
                return;
            }

            // An item drop can be converted into a placed build piece by a player (item
            // stacks). Those are structures, not litter.
            if (zdo.GetBool(ZDOVars.s_piece, false))
            {
                return;
            }

            var candidate = Rent();
            candidate.Zdo = zdo;
            candidate.Id = zdo.m_uid;
            candidate.Position = zdo.GetPosition();
            candidate.PrefabName = info.Name;
            candidate.ItemType = info.ItemType;
            candidate.Stack = Math.Max(1, zdo.GetInt(ZDOVars.s_stack, 1));
            candidate.Quality = Math.Max(1, zdo.GetInt(ZDOVars.s_quality, 1));
            candidate.AgeSeconds = ResolveAge(zdo, worldTime);

            _candidates.Add(candidate);
        }

        /// <summary>
        /// Age of an item in world-time seconds.
        ///
        /// Valheim already stamps every ItemDrop with its creation time: <c>ItemDrop.Awake</c>
        /// writes <c>ZNet.GetTime().Ticks</c> into the ZDO under <c>ZDOVars.s_spawnTime</c>
        /// when it is not already set, and <c>ItemDrop.GetTimeSinceSpawned</c> reads it back
        /// the same way. Reusing the game's own networked field means the plugin adds no new
        /// ZDO data of its own and changes nothing about the save format.
        ///
        /// A zero stamp means "unknown" - an item saved before the field existed, or one
        /// whose owner never got to write it. Returning null there makes the policy layer
        /// keep the item, which is the fail-safe outcome. A negative age (clock skew after a
        /// world time reset) is treated the same way.
        /// </summary>
        private static double? ResolveAge(ZDO zdo, double worldTimeSeconds)
        {
            var spawnTicks = zdo.GetLong(ZDOVars.s_spawnTime, 0L);
            if (spawnTicks <= 0L)
            {
                return null;
            }

            var spawnSeconds = spawnTicks / (double)TimeSpan.TicksPerSecond;
            var age = worldTimeSeconds - spawnSeconds;
            return age < 0d ? (double?)null : age;
        }

        /// <summary>
        /// Copies the server's ZDO table into <paramref name="into"/>.
        ///
        /// The table itself (<c>ZDOMan.m_objectsByID</c>) is private and Valheim exposes no
        /// enumerator for it, so it is read once through cached reflection. If that ever
        /// fails - say a future update renames the field - the scanner falls back to the
        /// ZDOs behind currently loaded <c>ItemDrop</c> components. That fallback is correct
        /// but sees far less of the world, so it logs a clear warning once.
        /// </summary>
        private bool TryCollectZdos(List<ZDO> into)
        {
            into.Clear();

            var manager = ZDOMan.instance;
            if (manager == null)
            {
                return false;
            }

            if (!_objectsByIdLookupAttempted)
            {
                _objectsByIdLookupAttempted = true;
                _objectsByIdField = Reflect.InstanceField(typeof(ZDOMan), "m_objectsByID", _log);
            }

            var table = Reflect.Read<Dictionary<ZDOID, ZDO>>(_objectsByIdField, manager);
            if (table != null)
            {
                if (into.Capacity < table.Count)
                {
                    into.Capacity = table.Count;
                }

                // Copy first: destroying ZDOs later must not mutate a collection we are
                // still iterating.
                foreach (var pair in table)
                {
                    into.Add(pair.Value);
                }

                return true;
            }

            return CollectFromLoadedInstances(into);
        }

        private bool CollectFromLoadedInstances(List<ZDO> into)
        {
            if (!_warnedAboutFallback)
            {
                _warnedAboutFallback = true;
                _log.LogWarning(
                    "Falling back to scanning only currently loaded item instances. On a dedicated server " +
                    "this sees very little of the world, so few items will be found. This usually means a " +
                    "Valheim update changed ZDOMan internally - please report it.");
            }

            var instancesField = Reflect.StaticField(typeof(ItemDrop), "s_instances", _log);
            var instances = Reflect.Read<List<ItemDrop>>(instancesField, null);
            if (instances == null)
            {
                _log.LogError("No way to enumerate item drops is available. Cleanup is disabled for this pass.");
                return false;
            }

            for (var i = 0; i < instances.Count; i++)
            {
                var drop = instances[i];
                if (drop == null)
                {
                    continue;
                }

                var view = drop.GetComponent<ZNetView>();
                if (view == null || !view.IsValid())
                {
                    continue;
                }

                var zdo = view.GetZDO();
                if (zdo != null && zdo.IsValid())
                {
                    into.Add(zdo);
                }
            }

            return true;
        }

        private CleanupCandidate Rent()
        {
            return _pool.Count > 0 ? _pool.Pop() : new CleanupCandidate();
        }

        /// <summary>
        /// Returns every candidate to the pool. Called at the start of each scan and on
        /// shutdown so no ZDO references are held between passes.
        /// </summary>
        internal void ReleaseCandidates()
        {
            for (var i = 0; i < _candidates.Count; i++)
            {
                var candidate = _candidates[i];
                candidate.Reset();
                _pool.Push(candidate);
            }

            _candidates.Clear();
        }

        /// <summary>Drops every retained buffer. Called on plugin or world shutdown.</summary>
        internal void Clear()
        {
            ReleaseCandidates();
            _pool.Clear();
            _wards.Clear();
            _basePieces.Clear();
            _zdoBuffer.Clear();
            ObjectsScanned = 0;
        }
    }
}
