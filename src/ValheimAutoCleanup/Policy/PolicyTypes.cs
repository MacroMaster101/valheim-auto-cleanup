namespace ValheimAutoCleanup.Policy
{
    /// <summary>
    /// A Unity-free mirror of <c>ItemDrop.ItemData.ItemType</c>.
    /// The policy layer is deliberately free of Valheim and Unity types so that it can be
    /// unit-tested on a normal .NET runtime. The mapping lives in <c>ItemEvaluator</c>.
    /// </summary>
    public enum PolicyItemType
    {
        Unknown = 0,
        Material,
        Consumable,
        Ammo,
        Trophy,
        Fish,
        Misc,
        Customization,

        // Everything below counts as "equipment" for ProtectEquipment.
        OneHandedWeapon,
        TwoHandedWeapon,
        Bow,
        Shield,
        Helmet,
        ChestArmor,
        LegArmor,
        Hands,
        Shoulder,
        Utility,
        Tool,
        Torch,
        Trinket
    }

    /// <summary>
    /// Everything the pure policy needs to know about one candidate item.
    /// Populated by <c>ItemEvaluator</c> from live ZDO / prefab data.
    /// </summary>
    public sealed class PolicyItem
    {
        /// <summary>Prefab name, e.g. "Wood". Never null (use string.Empty when unknown).</summary>
        public string PrefabName = string.Empty;

        /// <summary>Age in world-time seconds, or <c>null</c> when the age could not be determined.</summary>
        public double? AgeSeconds;

        /// <summary>Stack size; 1 when unknown.</summary>
        public int StackSize = 1;

        /// <summary>Quality / upgrade level; 1 for a base item.</summary>
        public int Quality = 1;

        /// <summary>Item category.</summary>
        public PolicyItemType ItemType = PolicyItemType.Unknown;
    }
}
