using ValheimAutoCleanup.Models;

namespace ValheimAutoCleanup.Policy
{
    /// <summary>
    /// The item-intrinsic half of the cleanup decision: age, name lists, category,
    /// quality and stack size. Deliberately contains no Unity, Valheim or world-state
    /// dependencies so it can be unit-tested directly (see tests/ValheimAutoCleanup.Tests).
    ///
    /// Spatial protections (players, bases, wards, world spawn) are evaluated separately
    /// in <c>ItemEvaluator</c> because they need live world state.
    ///
    /// Ordering note: the cheap, allocation-free checks run first so that a busy world
    /// short-circuits most candidates before any spatial query happens.
    /// </summary>
    public static class ItemCleanupPolicy
    {
        /// <summary>
        /// Evaluates the item-intrinsic rules.
        /// Returns <see cref="CleanupDecision.Delete"/> only when every intrinsic rule passes;
        /// the caller must still apply the spatial protections before destroying anything.
        /// </summary>
        public static CleanupDecision Evaluate(PolicyItem item, PolicySettings settings)
        {
            if (item == null || settings == null)
            {
                // Fail-safe: an evaluation we cannot perform is an evaluation that keeps the item.
                return CleanupDecision.Invalid;
            }

            // --- Age -------------------------------------------------------------------
            // A null age means "we could not determine how old this is". Fail safe: keep it.
            if (!item.AgeSeconds.HasValue)
            {
                return CleanupDecision.Invalid;
            }

            if (item.AgeSeconds.Value < settings.MinimumItemAgeSeconds)
            {
                return CleanupDecision.TooYoung;
            }

            // --- Name lists ------------------------------------------------------------
            // The general whitelist is an absolute veto and is checked before anything else.
            if (settings.Whitelist.Contains(item.PrefabName))
            {
                return CleanupDecision.Whitelisted;
            }

            if (settings.ProtectImportantItems && settings.ImportantItems.Contains(item.PrefabName))
            {
                return CleanupDecision.ImportantItem;
            }

            // --- Category / value ------------------------------------------------------
            if (settings.ProtectEquipment && IsEquipment(item.ItemType))
            {
                return CleanupDecision.Equipment;
            }

            if (settings.ProtectUpgradedItems && item.Quality > 1)
            {
                return CleanupDecision.Upgraded;
            }

            if (settings.ProtectLargeStacks && item.StackSize >= settings.LargeStackThreshold)
            {
                return CleanupDecision.LargeStack;
            }

            // --- Blacklist-only mode ---------------------------------------------------
            // In this mode nothing is removed unless it was explicitly named by the admin.
            if (settings.BlacklistOnly && !settings.Blacklist.Contains(item.PrefabName))
            {
                return CleanupDecision.NotBlacklisted;
            }

            return CleanupDecision.Delete;
        }

        /// <summary>
        /// True for categories a player would consider gear rather than bulk resources.
        /// Ammo is deliberately NOT equipment: arrows are the single most common form of
        /// battlefield litter, and treating them as gear would defeat the plugin's purpose.
        /// </summary>
        public static bool IsEquipment(PolicyItemType type)
        {
            switch (type)
            {
                case PolicyItemType.OneHandedWeapon:
                case PolicyItemType.TwoHandedWeapon:
                case PolicyItemType.Bow:
                case PolicyItemType.Shield:
                case PolicyItemType.Helmet:
                case PolicyItemType.ChestArmor:
                case PolicyItemType.LegArmor:
                case PolicyItemType.Hands:
                case PolicyItemType.Shoulder:
                case PolicyItemType.Utility:
                case PolicyItemType.Tool:
                case PolicyItemType.Torch:
                case PolicyItemType.Trinket:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>True when the decision means the item was (or would be) destroyed.</summary>
        public static bool IsRemoval(CleanupDecision decision)
        {
            return decision == CleanupDecision.Delete || decision == CleanupDecision.DryRunCandidate;
        }
    }
}
