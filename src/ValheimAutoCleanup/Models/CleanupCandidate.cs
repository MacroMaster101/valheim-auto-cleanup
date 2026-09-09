using UnityEngine;

namespace ValheimAutoCleanup.Models
{
    /// <summary>
    /// One loose item drop the scanner found, resolved down to just the data the evaluator
    /// needs. Holding the ZDO reference (rather than a GameObject) is deliberate: on a real
    /// dedicated server most items have no instantiated GameObject at all.
    ///
    /// Instances are pooled and reused across passes; see <c>ItemScanner</c>.
    /// </summary>
    internal sealed class CleanupCandidate
    {
        internal ZDO Zdo;
        internal ZDOID Id;
        internal Vector3 Position;
        internal string PrefabName = string.Empty;
        internal int Stack = 1;
        internal int Quality = 1;

        /// <summary>Age in world-time seconds, or null when it could not be determined.</summary>
        internal double? AgeSeconds;

        internal Policy.PolicyItemType ItemType = Policy.PolicyItemType.Unknown;

        internal void Reset()
        {
            Zdo = null;
            Id = ZDOID.None;
            Position = Vector3.zero;
            PrefabName = string.Empty;
            Stack = 1;
            Quality = 1;
            AgeSeconds = null;
            ItemType = Policy.PolicyItemType.Unknown;
        }
    }

    /// <summary>Protector geometry gathered during the same scan pass as the candidates.</summary>
    internal sealed class WardArea
    {
        internal Vector3 Position;
        internal float Radius;
    }
}
