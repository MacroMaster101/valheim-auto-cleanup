using System.Collections.Generic;
using BepInEx.Logging;
using ValheimAutoCleanup.Policy;
using UnityEngine;

namespace ValheimAutoCleanup.Server
{
    /// <summary>
    /// Static, immutable facts about one prefab, derived once from the prefab asset.
    /// </summary>
    internal sealed class PrefabInfo
    {
        internal int Hash;
        internal string Name = string.Empty;

        /// <summary>True when this prefab is a loose, collectable item drop and nothing else.</summary>
        internal bool IsCleanableItem;

        /// <summary>True when the prefab is a live fish (fish carry an ItemDrop component).</summary>
        internal bool IsFish;

        /// <summary>True when the prefab is a player grave.</summary>
        internal bool IsTombStone;

        /// <summary>True when the prefab is a build piece (used for base detection).</summary>
        internal bool IsPiece;

        /// <summary>True when the prefab is a PrivateArea ward.</summary>
        internal bool IsWard;

        /// <summary>Ward radius in metres; 0 for non-wards.</summary>
        internal float WardRadius;

        /// <summary>Item category, for equipment protection.</summary>
        internal PolicyItemType ItemType = PolicyItemType.Unknown;
    }

    /// <summary>
    /// Classifies every networked prefab once, at world load, so that the per-item hot path
    /// is a single dictionary lookup instead of repeated <c>GetComponent</c> calls.
    ///
    /// This is where the "only loose ItemDrop objects are ever candidates" rule is actually
    /// enforced. A prefab qualifies only if it has an ItemDrop component AND carries none of
    /// the components that would make it something else: a grave, a container, a creature,
    /// a ship, a cart, a ward, a build piece, or a destructible structure.
    /// </summary>
    internal sealed class PrefabCatalog
    {
        private readonly ManualLogSource _log;
        private readonly Dictionary<int, PrefabInfo> _byHash = new Dictionary<int, PrefabInfo>();

        internal PrefabCatalog(ManualLogSource log)
        {
            _log = log;
        }

        internal bool IsBuilt { get; private set; }

        internal int CleanablePrefabCount { get; private set; }

        internal int WardPrefabCount { get; private set; }

        internal int PiecePrefabCount { get; private set; }

        internal PrefabInfo Lookup(int prefabHash)
        {
            return _byHash.TryGetValue(prefabHash, out var info) ? info : null;
        }

        /// <summary>
        /// Builds the catalogue from <c>ZNetScene.m_prefabs</c>, which is a public field
        /// holding every networked prefab the game registered. Safe to call repeatedly; the
        /// second and later calls are no-ops unless <see cref="Invalidate"/> ran first.
        /// </summary>
        internal bool Build()
        {
            if (IsBuilt)
            {
                return true;
            }

            var scene = ZNetScene.instance;
            if (scene == null || scene.m_prefabs == null || scene.m_prefabs.Count == 0)
            {
                return false;
            }

            _byHash.Clear();
            CleanablePrefabCount = 0;
            WardPrefabCount = 0;
            PiecePrefabCount = 0;

            var skippedNonItem = 0;

            for (var i = 0; i < scene.m_prefabs.Count; i++)
            {
                var prefab = scene.m_prefabs[i];
                if (prefab == null)
                {
                    continue;
                }

                int hash;
                try
                {
                    hash = prefab.name.GetStableHashCode();
                }
                catch
                {
                    continue;
                }

                if (_byHash.ContainsKey(hash))
                {
                    continue;
                }

                var info = Classify(prefab, hash);
                _byHash[hash] = info;

                if (info.IsCleanableItem)
                {
                    CleanablePrefabCount++;
                }
                else if (info.ItemType != PolicyItemType.Unknown)
                {
                    skippedNonItem++;
                }

                if (info.IsWard)
                {
                    WardPrefabCount++;
                }

                if (info.IsPiece)
                {
                    PiecePrefabCount++;
                }
            }

            IsBuilt = true;
            _log.LogInfo(
                "Prefab catalogue built: " + _byHash.Count + " networked prefabs, " +
                CleanablePrefabCount + " cleanable item drops, " +
                skippedNonItem + " item-like prefabs excluded as graves/containers/creatures/pieces, " +
                WardPrefabCount + " wards, " + PiecePrefabCount + " build pieces.");

            return true;
        }

        /// <summary>Forces the next <see cref="Build"/> to re-classify. Used on world unload.</summary>
        internal void Invalidate()
        {
            IsBuilt = false;
            _byHash.Clear();
            CleanablePrefabCount = 0;
            WardPrefabCount = 0;
            PiecePrefabCount = 0;
        }

        private static PrefabInfo Classify(GameObject prefab, int hash)
        {
            var info = new PrefabInfo
            {
                Hash = hash,
                Name = prefab.name
            };

            var privateArea = prefab.GetComponent<PrivateArea>();
            if (privateArea != null)
            {
                info.IsWard = true;
                info.WardRadius = privateArea.m_radius;
            }

            var tombStone = prefab.GetComponent<TombStone>();
            if (tombStone != null)
            {
                // Hard-coded, non-configurable exclusion. Graves are never touched.
                info.IsTombStone = true;
                return info;
            }

            var itemDrop = prefab.GetComponent<ItemDrop>();

            // A build piece, for base detection.
            //
            // Careful: carrying a Piece component does NOT make a prefab a structure. Item
            // prefabs that support being placed as a stack (Wood, Stone and friends) ship
            // with a disabled Piece AND a disabled WearNTear, which ItemDrop.MakePiece turns
            // on at runtime. Treating those components as disqualifiers would exclude
            // precisely the most common litter from cleanup.
            //
            // So: a prefab that also has an ItemDrop is an item, not a structure. Whether a
            // particular instance has actually been placed as a piece is a per-object fact,
            // recorded in its ZDO under ZDOVars.s_piece, and the scanner checks that.
            info.IsPiece = itemDrop == null && prefab.GetComponent<Piece>() != null;

            if (itemDrop == null)
            {
                return info;
            }

            info.ItemType = MapItemType(itemDrop);
            info.IsFish = prefab.GetComponent<Fish>() != null;

            // Anything that is also a grave, a ward, a container, a creature or a vehicle is
            // not a loose drop, whatever else it may be.
            var disqualified =
                info.IsTombStone
                || info.IsWard
                || prefab.GetComponent<Container>() != null
                || prefab.GetComponent<Character>() != null
                || prefab.GetComponent<BaseAI>() != null
                || prefab.GetComponent<Ship>() != null
                || prefab.GetComponent<Vagon>() != null
                || prefab.GetComponent<Plant>() != null
                || prefab.GetComponent<Pickable>() != null;

            info.IsCleanableItem = !disqualified;
            return info;
        }

        /// <summary>
        /// Maps Valheim's item category onto the Unity-free enum the policy layer uses.
        /// Unrecognised values (for example a category added by a future update) map to
        /// <see cref="PolicyItemType.Unknown"/>, which the equipment rule treats as "not
        /// equipment" but which is otherwise harmless.
        /// </summary>
        private static PolicyItemType MapItemType(ItemDrop itemDrop)
        {
            var shared = itemDrop.m_itemData?.m_shared;
            if (shared == null)
            {
                return PolicyItemType.Unknown;
            }

            switch (shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.Material: return PolicyItemType.Material;
                case ItemDrop.ItemData.ItemType.Consumable: return PolicyItemType.Consumable;
                case ItemDrop.ItemData.ItemType.Ammo: return PolicyItemType.Ammo;
                case ItemDrop.ItemData.ItemType.AmmoNonEquipable: return PolicyItemType.Ammo;
                case ItemDrop.ItemData.ItemType.Trophy: return PolicyItemType.Trophy;
                case ItemDrop.ItemData.ItemType.Fish: return PolicyItemType.Fish;
                case ItemDrop.ItemData.ItemType.Misc: return PolicyItemType.Misc;
                case ItemDrop.ItemData.ItemType.Customization: return PolicyItemType.Customization;

                case ItemDrop.ItemData.ItemType.OneHandedWeapon: return PolicyItemType.OneHandedWeapon;
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon: return PolicyItemType.TwoHandedWeapon;
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft: return PolicyItemType.TwoHandedWeapon;
                case ItemDrop.ItemData.ItemType.Bow: return PolicyItemType.Bow;
                case ItemDrop.ItemData.ItemType.Shield: return PolicyItemType.Shield;
                case ItemDrop.ItemData.ItemType.Helmet: return PolicyItemType.Helmet;
                case ItemDrop.ItemData.ItemType.Chest: return PolicyItemType.ChestArmor;
                case ItemDrop.ItemData.ItemType.Legs: return PolicyItemType.LegArmor;
                case ItemDrop.ItemData.ItemType.Hands: return PolicyItemType.Hands;
                case ItemDrop.ItemData.ItemType.Shoulder: return PolicyItemType.Shoulder;
                case ItemDrop.ItemData.ItemType.Utility: return PolicyItemType.Utility;
                case ItemDrop.ItemData.ItemType.Tool: return PolicyItemType.Tool;
                case ItemDrop.ItemData.ItemType.Torch: return PolicyItemType.Torch;
                case ItemDrop.ItemData.ItemType.Trinket: return PolicyItemType.Trinket;

                default: return PolicyItemType.Unknown;
            }
        }
    }
}
