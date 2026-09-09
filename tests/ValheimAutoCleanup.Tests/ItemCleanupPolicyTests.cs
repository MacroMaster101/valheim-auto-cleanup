using ValheimAutoCleanup.Models;
using ValheimAutoCleanup.Policy;
using Xunit;

namespace ValheimAutoCleanup.Tests
{
    public class ItemCleanupPolicyTests
    {
        private static PolicySettings Settings(
            double minimumAge = 600,
            string whitelist = "",
            string important = "",
            bool protectImportant = true,
            bool blacklistOnly = false,
            string blacklist = "",
            bool protectEquipment = true,
            bool protectUpgraded = true,
            bool protectLargeStacks = false,
            int largeStackThreshold = 100)
        {
            return new PolicySettings
            {
                MinimumItemAgeSeconds = minimumAge,
                Whitelist = new NameSet(whitelist),
                ImportantItems = new NameSet(important),
                ProtectImportantItems = protectImportant,
                BlacklistOnly = blacklistOnly,
                Blacklist = new NameSet(blacklist),
                ProtectEquipment = protectEquipment,
                ProtectUpgradedItems = protectUpgraded,
                ProtectLargeStacks = protectLargeStacks,
                LargeStackThreshold = largeStackThreshold
            };
        }

        private static PolicyItem Item(
            string name = "Wood",
            double? age = 1200,
            int stack = 1,
            int quality = 1,
            PolicyItemType type = PolicyItemType.Material)
        {
            return new PolicyItem
            {
                PrefabName = name,
                AgeSeconds = age,
                StackSize = stack,
                Quality = quality,
                ItemType = type
            };
        }

        // --- Age -------------------------------------------------------------------------

        [Fact]
        public void OldEnoughOrdinaryMaterialIsRemoved()
        {
            Assert.Equal(CleanupDecision.Delete, ItemCleanupPolicy.Evaluate(Item(age: 601), Settings()));
        }

        [Fact]
        public void ItemYoungerThanTheMinimumIsKept()
        {
            Assert.Equal(CleanupDecision.TooYoung, ItemCleanupPolicy.Evaluate(Item(age: 599), Settings()));
        }

        [Fact]
        public void ItemExactlyAtTheMinimumIsRemoved()
        {
            Assert.Equal(CleanupDecision.Delete, ItemCleanupPolicy.Evaluate(Item(age: 600), Settings()));
        }

        [Fact]
        public void UnknownAgeIsKept()
        {
            // Fail-safe: an item whose spawn stamp is missing must never be removed.
            Assert.Equal(CleanupDecision.Invalid, ItemCleanupPolicy.Evaluate(Item(age: null), Settings()));
        }

        [Fact]
        public void NullInputsAreKept()
        {
            Assert.Equal(CleanupDecision.Invalid, ItemCleanupPolicy.Evaluate(null, Settings()));
            Assert.Equal(CleanupDecision.Invalid, ItemCleanupPolicy.Evaluate(Item(), null));
        }

        // --- Name lists ------------------------------------------------------------------

        [Fact]
        public void WhitelistedPrefabIsKept()
        {
            var settings = Settings(whitelist: "Iron, BlackMetal ,DragonEgg");
            Assert.Equal(CleanupDecision.Whitelisted, ItemCleanupPolicy.Evaluate(Item("Iron"), settings));
            Assert.Equal(CleanupDecision.Whitelisted, ItemCleanupPolicy.Evaluate(Item("BlackMetal"), settings));
        }

        [Fact]
        public void WhitelistMatchingIgnoresCaseAndSurroundingSpace()
        {
            var settings = Settings(whitelist: "  iron ,, ");
            Assert.Equal(CleanupDecision.Whitelisted, ItemCleanupPolicy.Evaluate(Item("IRON"), settings));
            Assert.Equal(1, settings.Whitelist.Count);
        }

        [Fact]
        public void EmptyWhitelistMatchesNothing()
        {
            var settings = Settings(whitelist: " , , ");
            Assert.True(settings.Whitelist.IsEmpty);
            Assert.Equal(CleanupDecision.Delete, ItemCleanupPolicy.Evaluate(Item("Iron"), settings));
        }

        [Fact]
        public void ImportantItemIsKeptOnlyWhileTheOptionIsOn()
        {
            var on = Settings(important: "DragonEgg", protectImportant: true);
            var off = Settings(important: "DragonEgg", protectImportant: false);

            Assert.Equal(CleanupDecision.ImportantItem, ItemCleanupPolicy.Evaluate(Item("DragonEgg"), on));
            Assert.Equal(CleanupDecision.Delete, ItemCleanupPolicy.Evaluate(Item("DragonEgg"), off));
        }

        [Fact]
        public void WhitelistOutranksBlacklistOnlyMode()
        {
            var settings = Settings(whitelist: "Wood", blacklistOnly: true, blacklist: "Wood");
            Assert.Equal(CleanupDecision.Whitelisted, ItemCleanupPolicy.Evaluate(Item("Wood"), settings));
        }

        // --- Blacklist-only mode ---------------------------------------------------------

        [Fact]
        public void BlacklistOnlyRemovesOnlyListedPrefabs()
        {
            var settings = Settings(blacklistOnly: true, blacklist: "Wood,Stone");

            Assert.Equal(CleanupDecision.Delete, ItemCleanupPolicy.Evaluate(Item("Wood"), settings));
            Assert.Equal(CleanupDecision.NotBlacklisted, ItemCleanupPolicy.Evaluate(Item("Iron"), settings));
        }

        [Fact]
        public void BlacklistIsIgnoredWhenBlacklistOnlyIsOff()
        {
            var settings = Settings(blacklistOnly: false, blacklist: "Wood");
            Assert.Equal(CleanupDecision.Delete, ItemCleanupPolicy.Evaluate(Item("Iron"), settings));
        }

        // --- Category, quality, stack ----------------------------------------------------

        [Theory]
        [InlineData(PolicyItemType.OneHandedWeapon)]
        [InlineData(PolicyItemType.TwoHandedWeapon)]
        [InlineData(PolicyItemType.Bow)]
        [InlineData(PolicyItemType.Shield)]
        [InlineData(PolicyItemType.Helmet)]
        [InlineData(PolicyItemType.ChestArmor)]
        [InlineData(PolicyItemType.LegArmor)]
        [InlineData(PolicyItemType.Hands)]
        [InlineData(PolicyItemType.Shoulder)]
        [InlineData(PolicyItemType.Utility)]
        [InlineData(PolicyItemType.Tool)]
        [InlineData(PolicyItemType.Torch)]
        [InlineData(PolicyItemType.Trinket)]
        public void EquipmentIsKeptWhenTheOptionIsOn(PolicyItemType type)
        {
            Assert.True(ItemCleanupPolicy.IsEquipment(type));
            Assert.Equal(CleanupDecision.Equipment, ItemCleanupPolicy.Evaluate(Item(type: type), Settings()));
        }

        [Theory]
        [InlineData(PolicyItemType.Material)]
        [InlineData(PolicyItemType.Consumable)]
        [InlineData(PolicyItemType.Ammo)]
        [InlineData(PolicyItemType.Trophy)]
        [InlineData(PolicyItemType.Misc)]
        [InlineData(PolicyItemType.Unknown)]
        public void NonEquipmentCategoriesAreNotTreatedAsGear(PolicyItemType type)
        {
            Assert.False(ItemCleanupPolicy.IsEquipment(type));
        }

        [Fact]
        public void AmmoIsDeliberatelyNotEquipment()
        {
            // Spent arrows are the most common litter, so they must stay removable even with
            // equipment protection on.
            Assert.Equal(
                CleanupDecision.Delete,
                ItemCleanupPolicy.Evaluate(Item("ArrowWood", type: PolicyItemType.Ammo), Settings()));
        }

        [Fact]
        public void EquipmentIsRemovableWhenTheOptionIsOff()
        {
            var settings = Settings(protectEquipment: false);
            Assert.Equal(
                CleanupDecision.Delete,
                ItemCleanupPolicy.Evaluate(Item(type: PolicyItemType.Bow), settings));
        }

        [Fact]
        public void UpgradedItemIsKept()
        {
            Assert.Equal(CleanupDecision.Upgraded, ItemCleanupPolicy.Evaluate(Item(quality: 2), Settings()));
        }

        [Fact]
        public void BaseQualityItemIsNotTreatedAsUpgraded()
        {
            Assert.Equal(CleanupDecision.Delete, ItemCleanupPolicy.Evaluate(Item(quality: 1), Settings()));
        }

        [Fact]
        public void LargeStackIsKeptOnlyWhenTheOptionIsOn()
        {
            var on = Settings(protectLargeStacks: true, largeStackThreshold: 100);
            var off = Settings(protectLargeStacks: false, largeStackThreshold: 100);

            Assert.Equal(CleanupDecision.LargeStack, ItemCleanupPolicy.Evaluate(Item(stack: 100), on));
            Assert.Equal(CleanupDecision.Delete, ItemCleanupPolicy.Evaluate(Item(stack: 99), on));
            Assert.Equal(CleanupDecision.Delete, ItemCleanupPolicy.Evaluate(Item(stack: 500), off));
        }

        // --- Ordering --------------------------------------------------------------------

        [Fact]
        public void AgeIsCheckedBeforeEveryOtherRule()
        {
            // A young whitelisted item reports TooYoung, not Whitelisted: age is the cheapest
            // gate and short-circuits first. Either answer keeps the item, so this is about
            // the reported reason being honest, not about safety.
            var settings = Settings(whitelist: "Wood");
            Assert.Equal(CleanupDecision.TooYoung, ItemCleanupPolicy.Evaluate(Item("Wood", age: 10), settings));
        }

        [Fact]
        public void IsRemovalCoversBothRemovalOutcomes()
        {
            Assert.True(ItemCleanupPolicy.IsRemoval(CleanupDecision.Delete));
            Assert.True(ItemCleanupPolicy.IsRemoval(CleanupDecision.DryRunCandidate));
            Assert.False(ItemCleanupPolicy.IsRemoval(CleanupDecision.PlayerNearby));
            Assert.False(ItemCleanupPolicy.IsRemoval(CleanupDecision.Invalid));
        }
    }
}
