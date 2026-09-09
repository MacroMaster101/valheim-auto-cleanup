namespace ValheimAutoCleanup.Policy
{
    /// <summary>
    /// The subset of configuration the pure policy layer needs. A snapshot of this is
    /// taken at the start of every cleanup pass so that a config reload part-way through
    /// a pass cannot change the rules mid-flight.
    /// </summary>
    public sealed class PolicySettings
    {
        public double MinimumItemAgeSeconds = 600;

        public bool ProtectImportantItems = true;
        public NameSet ImportantItems = new NameSet(string.Empty);
        public NameSet Whitelist = new NameSet(string.Empty);

        public bool BlacklistOnly;
        public NameSet Blacklist = new NameSet(string.Empty);

        public bool ProtectEquipment = true;
        public bool ProtectUpgradedItems = true;
        public bool ProtectLargeStacks;
        public int LargeStackThreshold = 100;
    }
}
