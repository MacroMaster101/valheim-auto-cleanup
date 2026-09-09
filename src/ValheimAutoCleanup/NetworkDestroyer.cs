using System;
using BepInEx.Logging;
using ValheimAutoCleanup.Models;
using ValheimAutoCleanup.Server;

namespace ValheimAutoCleanup
{
    /// <summary>
    /// Removes an item through Valheim's own networking layer.
    ///
    /// WHY NOT UnityEngine.Object.Destroy
    /// ----------------------------------
    /// Destroying the GameObject directly would tear down the local copy while leaving the
    /// ZDO alive in <c>ZDOMan</c>. The object would be recreated on the next zone refresh,
    /// and every connected client would keep its own copy. The result is a desync, not a
    /// cleanup.
    ///
    /// HOW OWNERSHIP WORKS HERE
    /// ------------------------
    /// Both correct removal paths refuse to do anything unless the caller owns the ZDO:
    ///
    ///   ZNetScene.Destroy(go)  ->  only calls ZDOMan.DestroyZDO when zdo.IsOwner()
    ///   ZDOMan.DestroyZDO(zdo) ->  returns immediately unless zdo.IsOwner()
    ///
    /// So the server must claim ownership first. <c>ZNetView.ClaimOwnership()</c> is exactly
    /// <c>m_zdo.SetOwner(ZDOMan.GetSessionID())</c>, which is what this class does directly
    /// for the (common) case where no GameObject exists. Ownership is claimed and the object
    /// destroyed within the same call so nothing can take ownership back in between.
    ///
    /// Once the ZDO is destroyed, <c>ZDOMan</c> queues it into its destroy-send list and
    /// broadcasts the removal to every peer on its next update, which is how clients that
    /// currently have the item loaded get rid of their copies. No custom RPC is involved and
    /// vanilla clients need no mod.
    /// </summary>
    internal sealed class NetworkDestroyer
    {
        private readonly ManualLogSource _log;

        internal NetworkDestroyer(ManualLogSource log)
        {
            _log = log;
        }

        /// <summary>
        /// Attempts to remove one candidate.
        /// Returns <see cref="CleanupDecision.Delete"/> on success,
        /// <see cref="CleanupDecision.Invalid"/> when the object vanished on its own (someone
        /// picked it up - not an error), <see cref="CleanupDecision.OwnershipFailure"/> when
        /// the server could not take ownership, and
        /// <see cref="CleanupDecision.DeletionFailure"/> when the removal itself threw.
        /// </summary>
        internal CleanupDecision Destroy(CleanupCandidate candidate, bool debugLog)
        {
            try
            {
                if (!ServerState.IsServerReady)
                {
                    return CleanupDecision.OwnershipFailure;
                }

                var zdo = candidate.Zdo;

                // Re-check liveness immediately before destroying. The scan and the delete
                // batches are separated by frames, and a player can pick the item up in
                // between. That must be a silent skip, never an exception.
                if (zdo == null || !zdo.IsValid())
                {
                    return CleanupDecision.Invalid;
                }

                // Confirm the ZDO manager still knows about it. A ZDO that has already been
                // destroyed this frame is no longer in the table.
                var manager = ZDOMan.instance;
                if (manager == null || manager.GetZDO(zdo.m_uid) == null)
                {
                    return CleanupDecision.Invalid;
                }

                var scene = ZNetScene.instance;

                // Path A: this process has the object instantiated. Go through ZNetScene so
                // the local component graph is torn down as well as the network record.
                var instance = scene.FindInstance(zdo);
                if (instance != null)
                {
                    if (!instance.IsValid())
                    {
                        return CleanupDecision.Invalid;
                    }

                    instance.ClaimOwnership();

                    if (!zdo.IsOwner())
                    {
                        return CleanupDecision.OwnershipFailure;
                    }

                    scene.Destroy(instance.gameObject);
                    return CleanupDecision.Delete;
                }

                // Path B: the usual case on a dedicated server - the ZDO exists but nothing
                // is instantiated locally. Claim ownership and destroy the network record;
                // ZDOMan broadcasts the removal to every peer.
                zdo.SetOwner(ZDOMan.GetSessionID());

                if (!zdo.IsOwner())
                {
                    if (debugLog)
                    {
                        _log.LogWarning(
                            "Could not take ownership of '" + candidate.PrefabName + "'; leaving it alone.");
                    }

                    return CleanupDecision.OwnershipFailure;
                }

                manager.DestroyZDO(zdo);
                return CleanupDecision.Delete;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Failed to remove '" + candidate.PrefabName + "': " + ex.Message);
                return CleanupDecision.DeletionFailure;
            }
        }
    }
}
