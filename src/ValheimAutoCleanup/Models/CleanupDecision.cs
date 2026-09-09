namespace ValheimAutoCleanup.Models
{
    /// <summary>
    /// The outcome of evaluating a single cleanup candidate.
    /// Every value except <see cref="Delete"/> and <see cref="DryRunCandidate"/> means
    /// "the item was kept", and carries the reason it was kept.
    /// </summary>
    public enum CleanupDecision
    {
        /// <summary>Item passed every safety gate and may be destroyed.</summary>
        Delete = 0,

        /// <summary>Item passed every safety gate but dry-run mode is active, so nothing was destroyed.</summary>
        DryRunCandidate,

        /// <summary>Item has not existed for <c>MinimumItemAgeSeconds</c> of world time yet.</summary>
        TooYoung,

        /// <summary>A connected player is inside <c>PlayerProtectionRadius</c>.</summary>
        PlayerNearby,

        /// <summary>A player was inside the protection radius recently (see <c>RecentPlayerProtectionSeconds</c>).</summary>
        RecentPlayerActivity,

        /// <summary>Item sits inside a player-built base area.</summary>
        BaseProtected,

        /// <summary>Item sits inside an active PrivateArea (ward).</summary>
        WardProtected,

        /// <summary>Item sits inside the protected world-spawn radius.</summary>
        WorldSpawnProtected,

        /// <summary>Prefab name appears in the general <c>Whitelist</c>.</summary>
        Whitelisted,

        /// <summary>Prefab name appears in <c>ImportantItemWhitelist</c>.</summary>
        ImportantItem,

        /// <summary>Item is a weapon / armour / tool and <c>ProtectEquipment</c> is on.</summary>
        Equipment,

        /// <summary>Item quality (star / upgrade level) is above 1 and <c>ProtectUpgradedItems</c> is on.</summary>
        Upgraded,

        /// <summary>Stack size is at or above <c>LargeStackThreshold</c>.</summary>
        LargeStack,

        /// <summary><c>BlacklistOnly</c> is on and the prefab is not in the blacklist.</summary>
        NotBlacklisted,

        /// <summary>Item is a live fish, a tombstone, a placed build piece, or otherwise not a loose drop.</summary>
        NotALooseDrop,

        /// <summary>Object vanished, its network data was invalid, or its age could not be determined.</summary>
        Invalid,

        /// <summary>The server could not take ownership of the object, so it was left alone.</summary>
        OwnershipFailure,

        /// <summary>Destruction was attempted and threw.</summary>
        DeletionFailure,

        /// <summary><c>MaxDeletesPerCleanup</c> was reached before this item was reached.</summary>
        BudgetExhausted
    }
}
