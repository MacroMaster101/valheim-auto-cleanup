using System.Collections.Generic;
using UnityEngine;

namespace ValheimAutoCleanup.Server
{
    /// <summary>
    /// A thin, defensive facade over the pieces of Valheim's networking layer the plugin
    /// depends on. Every accessor tolerates a half-initialised or shutting-down game and
    /// returns a conservative answer rather than throwing.
    /// </summary>
    internal static class ServerState
    {
        /// <summary>
        /// The location name of the world's starting stones. Used for
        /// <c>ProtectNearWorldSpawn</c>.
        /// </summary>
        private const string StartTempleLocation = "StartTemple";

        private static bool _spawnPointResolved;
        private static Vector3 _spawnPoint = Vector3.zero;

        /// <summary>
        /// True only when this process is acting as the Valheim server, the network scene
        /// exists, and the ZDO manager is up. Every destructive path checks this first.
        /// </summary>
        internal static bool IsServerReady
        {
            get
            {
                var znet = ZNet.instance;
                return znet != null
                       && znet.IsServer()
                       && ZNetScene.instance != null
                       && ZDOMan.instance != null
                       && ZoneSystem.instance != null;
            }
        }

        /// <summary>True when this process is a headless dedicated server rather than a listen host.</summary>
        internal static bool IsDedicated
        {
            get
            {
                var znet = ZNet.instance;
                return znet != null && znet.IsDedicated();
            }
        }

        /// <summary>Number of connected players, or 0 when the network layer is not up.</summary>
        internal static int ConnectedPlayerCount
        {
            get
            {
                var znet = ZNet.instance;
                if (znet == null)
                {
                    return 0;
                }

                var peers = znet.GetConnectedPeers();
                return peers?.Count ?? 0;
            }
        }

        /// <summary>
        /// World time in seconds. Valheim advances this only while at least one player is
        /// connected, which is why item ages freeze on an empty server.
        /// </summary>
        internal static double WorldTimeSeconds
        {
            get
            {
                var znet = ZNet.instance;
                return znet == null ? 0d : znet.GetTimeSeconds();
            }
        }

        /// <summary>
        /// Fills <paramref name="into"/> with the world position of every connected player.
        ///
        /// This deliberately uses the peers' reference positions rather than
        /// <c>Player.GetAllPlayers()</c>. A dedicated server only instantiates GameObjects
        /// around its own (never-updated) reference position, so on a real dedicated server
        /// there are usually no Player components at all - but every peer still reports its
        /// position to the server continuously. Loaded Player components are merged in as
        /// well so that a listen host (client hosting a world) is also covered.
        /// </summary>
        internal static void CollectPlayerPositions(List<Vector3> into)
        {
            into.Clear();

            var znet = ZNet.instance;
            if (znet != null)
            {
                var peers = znet.GetConnectedPeers();
                if (peers != null)
                {
                    for (var i = 0; i < peers.Count; i++)
                    {
                        var peer = peers[i];
                        if (peer != null && peer.IsReady())
                        {
                            into.Add(peer.GetRefPos());
                        }
                    }
                }
            }

            // Listen host: the hosting player is not a peer of itself.
            var players = Player.GetAllPlayers();
            if (players != null)
            {
                for (var i = 0; i < players.Count; i++)
                {
                    var player = players[i];
                    if (player != null)
                    {
                        into.Add(player.transform.position);
                    }
                }
            }
        }

        /// <summary>
        /// The world's starting-stones position, cached after the first successful lookup.
        /// Returns false while world generation has not produced the location yet, in which
        /// case spawn protection is skipped rather than guessed.
        /// </summary>
        internal static bool TryGetWorldSpawn(out Vector3 position)
        {
            if (_spawnPointResolved)
            {
                position = _spawnPoint;
                return true;
            }

            position = Vector3.zero;

            var zoneSystem = ZoneSystem.instance;
            if (zoneSystem == null)
            {
                return false;
            }

            if (!zoneSystem.GetLocationIcon(StartTempleLocation, out var found))
            {
                return false;
            }

            _spawnPoint = found;
            _spawnPointResolved = true;
            position = found;
            return true;
        }

        /// <summary>Clears cached world state. Called on shutdown so a re-hosted world re-resolves.</summary>
        internal static void Reset()
        {
            _spawnPointResolved = false;
            _spawnPoint = Vector3.zero;
        }

        /// <summary>
        /// Squared horizontal-plus-vertical distance. Kept as a helper so every proximity
        /// test in the plugin compares squared magnitudes and avoids a square root.
        /// </summary>
        internal static float DistanceSquared(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dy = a.y - b.y;
            var dz = a.z - b.z;
            return (dx * dx) + (dy * dy) + (dz * dz);
        }
    }
}
