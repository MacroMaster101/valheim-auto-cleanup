using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using ValheimAutoCleanup.Policy;

namespace ValheimAutoCleanup
{
    /// <summary>
    /// Typed, validated access to the BepInEx configuration file.
    ///
    /// Every value is clamped when <see cref="Reload"/> runs, so the rest of the plugin can
    /// trust the properties on this class without re-validating. Nothing here touches Unity,
    /// so it is safe to construct before the game is ready.
    /// </summary>
    public sealed class PluginConfig
    {
        /// <summary>
        /// Boss progression items and boss trophies. Every prefab name below was verified
        /// against the shipped game data before being listed; nothing here is guessed.
        /// </summary>
        private const string DefaultImportantItems =
            "DragonEgg,Wishbone,CryptKey,DvergrKeyFragment,YagluthDrop,QueenDrop,FaderDrop," +
            "TrophyEikthyr,TrophyTheElder,TrophyBonemass,TrophyDragonQueen,TrophyGoblinKing," +
            "TrophySeekerQueen,TrophyFader,ShieldCore,BellFragment,MorgenHeart";

        private readonly ConfigFile _file;
        private readonly ManualLogSource _log;

        // [General]
        private readonly ConfigEntry<bool> _enabled;
        private readonly ConfigEntry<int> _cleanupIntervalSeconds;
        private readonly ConfigEntry<int> _minimumItemAgeSeconds;
        private readonly ConfigEntry<bool> _dryRun;
        private readonly ConfigEntry<bool> _runCleanupOnServerStart;
        private readonly ConfigEntry<int> _startupCleanupDelaySeconds;
        private readonly ConfigEntry<bool> _onlyCleanupWhenServerEmpty;

        // [PlayerProtection]
        private readonly ConfigEntry<bool> _protectNearPlayers;
        private readonly ConfigEntry<float> _playerProtectionRadius;
        private readonly ConfigEntry<bool> _protectRecentlyNearbyItems;
        private readonly ConfigEntry<int> _recentPlayerProtectionSeconds;

        // [BaseProtection]
        private readonly ConfigEntry<bool> _protectNearPlayerBase;
        private readonly ConfigEntry<float> _baseProtectionRadius;
        private readonly ConfigEntry<bool> _protectItemsInsideActiveWards;
        private readonly ConfigEntry<bool> _protectNearWorldSpawn;
        private readonly ConfigEntry<float> _worldSpawnProtectionRadius;

        // [ItemProtection]
        private readonly ConfigEntry<bool> _protectImportantItems;
        private readonly ConfigEntry<string> _importantItemWhitelist;
        private readonly ConfigEntry<string> _whitelist;
        private readonly ConfigEntry<bool> _blacklistOnly;
        private readonly ConfigEntry<string> _blacklist;
        private readonly ConfigEntry<bool> _protectEquipment;
        private readonly ConfigEntry<bool> _protectUpgradedItems;
        private readonly ConfigEntry<bool> _protectLargeStacks;
        private readonly ConfigEntry<int> _largeStackThreshold;
        private readonly ConfigEntry<bool> _protectFish;

        // [Performance]
        private readonly ConfigEntry<int> _maxDeletesPerCleanup;
        private readonly ConfigEntry<int> _deletesPerFrame;
        private readonly ConfigEntry<int> _delayBetweenDeleteBatchesMilliseconds;

        // [Warnings]
        private readonly ConfigEntry<bool> _enableCleanupWarnings;
        private readonly ConfigEntry<int> _warningSecondsBeforeCleanup;
        private readonly ConfigEntry<string> _warningMessage;
        private readonly ConfigEntry<int> _finalWarningSeconds;
        private readonly ConfigEntry<string> _finalWarningMessage;
        private readonly ConfigEntry<bool> _cleanupCompleteMessageEnabled;
        private readonly ConfigEntry<string> _announcementPrefix;
        private readonly ConfigEntry<string> _announcementStyle;

        // [Admin]
        private readonly ConfigEntry<bool> _enableChatCommands;
        private readonly ConfigEntry<string> _chatCommandPrefix;
        private readonly ConfigEntry<bool> _enableCommandFile;

        // [Logging]
        private readonly ConfigEntry<bool> _logCleanupSummary;
        private readonly ConfigEntry<bool> _logDeletedItems;
        private readonly ConfigEntry<bool> _logProtectedItems;
        private readonly ConfigEntry<bool> _logDebugInformation;

        public PluginConfig(ConfigFile file, ManualLogSource log)
        {
            _file = file;
            _log = log;

            const string general = "General";
            _enabled = file.Bind(general, "Enabled", true,
                "Master switch. When false the plugin loads but never scans or removes anything.");
            _cleanupIntervalSeconds = file.Bind(general, "CleanupIntervalSeconds", 600,
                new ConfigDescription(
                    "How often, in real seconds, a cleanup pass runs.",
                    new AcceptableValueRange<int>(ConfigLimits.MinCleanupIntervalSeconds, ConfigLimits.MaxCleanupIntervalSeconds)));
            _minimumItemAgeSeconds = file.Bind(general, "MinimumItemAgeSeconds", 600,
                new ConfigDescription(
                    "An item must have existed for at least this many seconds of WORLD time before it can be removed. " +
                    "World time only advances while at least one player is connected, so items do not age on an empty " +
                    "server. This is what stops loot dropped moments before a scan from disappearing.",
                    new AcceptableValueRange<int>(ConfigLimits.MinItemAgeSeconds, ConfigLimits.MaxItemAgeSeconds)));
            _dryRun = file.Bind(general, "DryRun", true,
                "SAFETY DEFAULT. When true the plugin performs the full scan and reports what it WOULD remove but " +
                "removes nothing. Back up your world, watch the logs for at least one cycle, then set this to false.");
            _runCleanupOnServerStart = file.Bind(general, "RunCleanupOnServerStart", false,
                "Run one cleanup pass shortly after the world finishes loading.");
            _startupCleanupDelaySeconds = file.Bind(general, "StartupCleanupDelaySeconds", 120,
                new ConfigDescription(
                    "Seconds to wait after the world is ready before the first pass runs.",
                    new AcceptableValueRange<int>(ConfigLimits.MinStartupDelaySeconds, ConfigLimits.MaxStartupDelaySeconds)));
            _onlyCleanupWhenServerEmpty = file.Bind(general, "OnlyCleanupWhenServerEmpty", false,
                "Very conservative mode: scheduled passes only run when no players are connected. Manual admin " +
                "commands still work. Note that world time is frozen on an empty server, so items stop ageing " +
                "while nobody is online.");

            const string playerProtection = "PlayerProtection";
            _protectNearPlayers = file.Bind(playerProtection, "ProtectNearPlayers", true,
                "Never remove an item that a connected player is standing near.");
            _playerProtectionRadius = file.Bind(playerProtection, "PlayerProtectionRadius", 25f,
                new ConfigDescription(
                    "Radius in metres around every connected player inside which items are never removed.",
                    new AcceptableValueRange<float>(ConfigLimits.MinRadius, ConfigLimits.MaxRadius)));
            _protectRecentlyNearbyItems = file.Bind(playerProtection, "ProtectRecentlyNearbyItems", true,
                "Keep protecting an item for a while after the last player leaves its radius, so loot does not " +
                "vanish the instant someone walks away.");
            _recentPlayerProtectionSeconds = file.Bind(playerProtection, "RecentPlayerProtectionSeconds", 180,
                new ConfigDescription(
                    "How long, in real seconds, that after-the-fact protection lasts.",
                    new AcceptableValueRange<int>(0, 86400)));

            const string baseProtection = "BaseProtection";
            _protectNearPlayerBase = file.Bind(baseProtection, "ProtectNearPlayerBase", false,
                "Never remove items near player-built structures. Off by default because many public servers " +
                "specifically want litter cleared inside settlements.");
            _baseProtectionRadius = file.Bind(baseProtection, "BaseProtectionRadius", 15f,
                new ConfigDescription(
                    "Radius in metres around a player-built piece inside which items are protected.",
                    new AcceptableValueRange<float>(ConfigLimits.MinRadius, ConfigLimits.MaxRadius)));
            _protectItemsInsideActiveWards = file.Bind(baseProtection, "ProtectItemsInsideActiveWards", false,
                "Never remove items inside an active PrivateArea (ward).");
            _protectNearWorldSpawn = file.Bind(baseProtection, "ProtectNearWorldSpawn", false,
                "Never remove items near the world spawn / starting stones. Useful for public meeting areas.");
            _worldSpawnProtectionRadius = file.Bind(baseProtection, "WorldSpawnProtectionRadius", 50f,
                new ConfigDescription(
                    "Radius in metres around the world spawn point.",
                    new AcceptableValueRange<float>(ConfigLimits.MinRadius, ConfigLimits.MaxRadius)));

            const string itemProtection = "ItemProtection";
            _protectImportantItems = file.Bind(itemProtection, "ProtectImportantItems", true,
                "Apply the ImportantItemWhitelist below.");
            _importantItemWhitelist = file.Bind(itemProtection, "ImportantItemWhitelist", DefaultImportantItems,
                "Comma-separated prefab names that must never be removed. Case-insensitive, whitespace trimmed. " +
                "The defaults are boss progression items and boss trophies.");
            _whitelist = file.Bind(itemProtection, "Whitelist", "",
                "Comma-separated prefab names that must never be removed, for example Iron,BlackMetal. " +
                "This list is an absolute veto and is checked before every other rule.");
            _blacklistOnly = file.Bind(itemProtection, "BlacklistOnly", false,
                "Conservative mode. When true, ONLY prefabs named in Blacklist can ever be removed; everything " +
                "else is left alone regardless of age.");
            _blacklist = file.Bind(itemProtection, "Blacklist", "Wood,Stone,Resin,BoneFragments",
                "Comma-separated prefab names. Only meaningful when BlacklistOnly is true.");
            _protectEquipment = file.Bind(itemProtection, "ProtectEquipment", true,
                "Never remove weapons, shields, armour, tools, torches, trinkets or utility gear. Ammo is " +
                "deliberately NOT treated as equipment, since spent arrows are the most common form of litter.");
            _protectUpgradedItems = file.Bind(itemProtection, "ProtectUpgradedItems", true,
                "Never remove an item whose quality (upgrade level) is above 1.");
            _protectLargeStacks = file.Bind(itemProtection, "ProtectLargeStacks", false,
                "Never remove a drop whose stack size is at or above LargeStackThreshold. Guards against wiping " +
                "a large resource transfer somebody left on the ground.");
            _largeStackThreshold = file.Bind(itemProtection, "LargeStackThreshold", 100,
                new ConfigDescription("Stack size at which ProtectLargeStacks takes effect.",
                    new AcceptableValueRange<int>(1, 100000)));
            _protectFish = file.Bind(itemProtection, "ProtectFish", true,
                "Never remove live fish. Fish prefabs carry an ItemDrop component, so without this they would " +
                "look like ordinary loose drops. Strongly recommended to leave on.");

            const string performance = "Performance";
            _maxDeletesPerCleanup = file.Bind(performance, "MaxDeletesPerCleanup", 250,
                new ConfigDescription("Hard ceiling on removals in a single pass.",
                    new AcceptableValueRange<int>(ConfigLimits.MinMaxDeletesPerCleanup, ConfigLimits.MaxMaxDeletesPerCleanup)));
            _deletesPerFrame = file.Bind(performance, "DeletesPerFrame", 25,
                new ConfigDescription("Removals performed before the coroutine yields back to the game loop.",
                    new AcceptableValueRange<int>(ConfigLimits.MinDeletesPerFrame, ConfigLimits.MaxDeletesPerFrame)));
            _delayBetweenDeleteBatchesMilliseconds = file.Bind(performance, "DelayBetweenDeleteBatchesMilliseconds", 50,
                new ConfigDescription("Pause between removal batches, in milliseconds.",
                    new AcceptableValueRange<int>(ConfigLimits.MinBatchDelayMs, ConfigLimits.MaxBatchDelayMs)));

            const string warnings = "Warnings";
            _enableCleanupWarnings = file.Bind(warnings, "EnableCleanupWarnings", true,
                "Announce upcoming cleanups. Messages are shown through Valheim's own on-screen message system, " +
                "so vanilla clients see them with no mod installed. If broadcasting is unavailable the warnings " +
                "still appear in the server log.");
            _warningSecondsBeforeCleanup = file.Bind(warnings, "WarningSecondsBeforeCleanup", 60,
                new ConfigDescription("Lead time for the first warning. Must be less than CleanupIntervalSeconds.",
                    new AcceptableValueRange<int>(0, ConfigLimits.MaxCleanupIntervalSeconds)));
            _warningMessage = file.Bind(warnings, "WarningMessage",
                "Server cleanup in {seconds} seconds. Pick up dropped items you want to keep.",
                "{seconds} is replaced with the remaining lead time.");
            _finalWarningSeconds = file.Bind(warnings, "FinalWarningSeconds", 10,
                new ConfigDescription("Lead time for the final warning. Must be less than WarningSecondsBeforeCleanup.",
                    new AcceptableValueRange<int>(0, ConfigLimits.MaxCleanupIntervalSeconds)));
            _finalWarningMessage = file.Bind(warnings, "FinalWarningMessage",
                "Server cleanup in {seconds} seconds!",
                "{seconds} is replaced with the remaining lead time.");
            _cleanupCompleteMessageEnabled = file.Bind(warnings, "CleanupCompleteMessageEnabled", false,
                "Announce the result to players after a pass finishes.");
            _announcementPrefix = file.Bind(warnings, "AnnouncementPrefix", "[Server]",
                "Text placed in front of every announcement, so players can see it came from the server. " +
                "Leave empty for no prefix.");
            _announcementStyle = file.Bind(warnings, "AnnouncementStyle", "TopLeft",
                new ConfigDescription(
                    "Where announcements appear on screen. TopLeft is the small corner notice used for pickups; " +
                    "it is also written to the player's in-game message log, so it can be read after the fact. " +
                    "Center is the large banner Valheim uses for messages like \"You are cold\"; it is louder " +
                    "but vanishes after a few seconds and leaves no record.",
                    new AcceptableValueList<string>("TopLeft", "Center")));

            const string admin = "Admin";
            _enableChatCommands = file.Bind(admin, "EnableChatCommands", true,
                "Let server admins run cleanup commands by typing them in in-game chat. Only accounts listed in " +
                "adminlist.txt are obeyed, and the check uses the connection's authenticated platform ID rather " +
                "than the display name carried in the message. Vanilla clients need no mod for this.");
            _chatCommandPrefix = file.Bind(admin, "ChatCommandPrefix", "!autocleanup",
                "Text an admin types in chat to run a command, for example: !autocleanup status");
            _enableCommandFile = file.Bind(admin, "EnableCommandFile", true,
                "Watch a plain text file for commands. Write a command into it with your host's file manager " +
                "(DatHost, FTP, ...) and the plugin runs it, then clears the file. This is the most reliable " +
                "control channel on a rented dedicated server.");

            const string logging = "Logging";
            _logCleanupSummary = file.Bind(logging, "LogCleanupSummary", true,
                "Log a one-line summary after each pass.");
            _logDeletedItems = file.Bind(logging, "LogDeletedItems", false,
                "Log every individual removal with prefab name, stack size and position. Verbose.");
            _logProtectedItems = file.Bind(logging, "LogProtectedItems", false,
                "Log every individual item that was kept, and why. Very verbose; use for tuning only.");
            _logDebugInformation = file.Bind(logging, "LogDebugInformation", false,
                "Extra diagnostics about scanning, prefab classification and network state.");

            Reload();
        }

        // ---------------------------------------------------------------------------
        // Validated snapshot. These are the values the rest of the plugin reads.
        // ---------------------------------------------------------------------------

        public bool Enabled { get; private set; }
        public int CleanupIntervalSeconds { get; private set; }
        public int MinimumItemAgeSeconds { get; private set; }
        public bool DryRun { get; private set; }
        public bool RunCleanupOnServerStart { get; private set; }
        public int StartupCleanupDelaySeconds { get; private set; }
        public bool OnlyCleanupWhenServerEmpty { get; private set; }

        public bool ProtectNearPlayers { get; private set; }
        public float PlayerProtectionRadius { get; private set; }
        public bool ProtectRecentlyNearbyItems { get; private set; }
        public int RecentPlayerProtectionSeconds { get; private set; }

        public bool ProtectNearPlayerBase { get; private set; }
        public float BaseProtectionRadius { get; private set; }
        public bool ProtectItemsInsideActiveWards { get; private set; }
        public bool ProtectNearWorldSpawn { get; private set; }
        public float WorldSpawnProtectionRadius { get; private set; }

        public bool ProtectFish { get; private set; }

        public int MaxDeletesPerCleanup { get; private set; }
        public int DeletesPerFrame { get; private set; }
        public int DelayBetweenDeleteBatchesMilliseconds { get; private set; }

        public bool EnableCleanupWarnings { get; private set; }
        public int WarningSecondsBeforeCleanup { get; private set; }
        public string WarningMessage { get; private set; }
        public int FinalWarningSeconds { get; private set; }
        public string FinalWarningMessage { get; private set; }
        public bool CleanupCompleteMessageEnabled { get; private set; }
        public string AnnouncementPrefix { get; private set; }
        public Server.AnnouncementStyle AnnouncementStyle { get; private set; }

        public bool EnableChatCommands { get; private set; }
        public string ChatCommandPrefix { get; private set; }
        public bool EnableCommandFile { get; private set; }

        public bool LogCleanupSummary { get; private set; }
        public bool LogDeletedItems { get; private set; }
        public bool LogProtectedItems { get; private set; }
        public bool LogDebugInformation { get; private set; }

        /// <summary>The item-intrinsic rules, rebuilt on every reload.</summary>
        public PolicySettings Policy { get; private set; } = new PolicySettings();

        /// <summary>
        /// Turns automatic cleanup on or off at runtime and writes the change to disk so it
        /// survives a restart. Used by the enable / disable commands.
        /// </summary>
        public void SetEnabled(bool value)
        {
            _enabled.Value = value;
            Enabled = value;

            try
            {
                _file.Save();
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not write the config file: " + ex.Message);
            }
        }

        /// <summary>
        /// Re-reads the file from disk and rebuilds the validated snapshot. Safe to call at
        /// any time; the cleanup manager restarts its timer afterwards.
        /// </summary>
        public void Reload()
        {
            try
            {
                _file.Reload();
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not re-read the config file, keeping the values already in memory: " + ex.Message);
            }

            Enabled = _enabled.Value;

            CleanupIntervalSeconds = Clamp(_cleanupIntervalSeconds.Value,
                ConfigLimits.MinCleanupIntervalSeconds, ConfigLimits.MaxCleanupIntervalSeconds, "CleanupIntervalSeconds");
            MinimumItemAgeSeconds = Clamp(_minimumItemAgeSeconds.Value,
                ConfigLimits.MinItemAgeSeconds, ConfigLimits.MaxItemAgeSeconds, "MinimumItemAgeSeconds");
            DryRun = _dryRun.Value;
            RunCleanupOnServerStart = _runCleanupOnServerStart.Value;
            StartupCleanupDelaySeconds = Clamp(_startupCleanupDelaySeconds.Value,
                ConfigLimits.MinStartupDelaySeconds, ConfigLimits.MaxStartupDelaySeconds, "StartupCleanupDelaySeconds");
            OnlyCleanupWhenServerEmpty = _onlyCleanupWhenServerEmpty.Value;

            ProtectNearPlayers = _protectNearPlayers.Value;
            PlayerProtectionRadius = Clamp(_playerProtectionRadius.Value,
                ConfigLimits.MinRadius, ConfigLimits.MaxRadius, "PlayerProtectionRadius");
            ProtectRecentlyNearbyItems = _protectRecentlyNearbyItems.Value;
            RecentPlayerProtectionSeconds = Clamp(_recentPlayerProtectionSeconds.Value, 0, 86400,
                "RecentPlayerProtectionSeconds");

            ProtectNearPlayerBase = _protectNearPlayerBase.Value;
            BaseProtectionRadius = Clamp(_baseProtectionRadius.Value,
                ConfigLimits.MinRadius, ConfigLimits.MaxRadius, "BaseProtectionRadius");
            ProtectItemsInsideActiveWards = _protectItemsInsideActiveWards.Value;
            ProtectNearWorldSpawn = _protectNearWorldSpawn.Value;
            WorldSpawnProtectionRadius = Clamp(_worldSpawnProtectionRadius.Value,
                ConfigLimits.MinRadius, ConfigLimits.MaxRadius, "WorldSpawnProtectionRadius");

            ProtectFish = _protectFish.Value;

            MaxDeletesPerCleanup = Clamp(_maxDeletesPerCleanup.Value,
                ConfigLimits.MinMaxDeletesPerCleanup, ConfigLimits.MaxMaxDeletesPerCleanup, "MaxDeletesPerCleanup");
            DeletesPerFrame = Clamp(_deletesPerFrame.Value,
                ConfigLimits.MinDeletesPerFrame, ConfigLimits.MaxDeletesPerFrame, "DeletesPerFrame");
            DelayBetweenDeleteBatchesMilliseconds = Clamp(_delayBetweenDeleteBatchesMilliseconds.Value,
                ConfigLimits.MinBatchDelayMs, ConfigLimits.MaxBatchDelayMs, "DelayBetweenDeleteBatchesMilliseconds");

            EnableCleanupWarnings = _enableCleanupWarnings.Value;

            var warningLead = ConfigLimits.ClampWarningLead(_warningSecondsBeforeCleanup.Value, CleanupIntervalSeconds);
            if (warningLead != _warningSecondsBeforeCleanup.Value)
            {
                _log.LogWarning(
                    "WarningSecondsBeforeCleanup (" + _warningSecondsBeforeCleanup.Value +
                    ") must be less than CleanupIntervalSeconds (" + CleanupIntervalSeconds + "); using " + warningLead + ".");
            }

            WarningSecondsBeforeCleanup = warningLead;

            var finalLead = ConfigLimits.ClampFinalWarningLead(_finalWarningSeconds.Value, warningLead, CleanupIntervalSeconds);
            if (finalLead != _finalWarningSeconds.Value)
            {
                _log.LogWarning(
                    "FinalWarningSeconds (" + _finalWarningSeconds.Value +
                    ") must be less than WarningSecondsBeforeCleanup (" + warningLead + "); using " + finalLead + ".");
            }

            FinalWarningSeconds = finalLead;
            WarningMessage = _warningMessage.Value ?? string.Empty;
            FinalWarningMessage = _finalWarningMessage.Value ?? string.Empty;
            CleanupCompleteMessageEnabled = _cleanupCompleteMessageEnabled.Value;
            AnnouncementPrefix = _announcementPrefix.Value ?? string.Empty;
            AnnouncementStyle = Server.Broadcaster.ParseStyle(_announcementStyle.Value);

            EnableChatCommands = _enableChatCommands.Value;
            ChatCommandPrefix = string.IsNullOrEmpty(_chatCommandPrefix.Value)
                ? "!autocleanup"
                : _chatCommandPrefix.Value.Trim();
            EnableCommandFile = _enableCommandFile.Value;

            LogCleanupSummary = _logCleanupSummary.Value;
            LogDeletedItems = _logDeletedItems.Value;
            LogProtectedItems = _logProtectedItems.Value;
            LogDebugInformation = _logDebugInformation.Value;

            Policy = new PolicySettings
            {
                MinimumItemAgeSeconds = MinimumItemAgeSeconds,
                ProtectImportantItems = _protectImportantItems.Value,
                ImportantItems = new NameSet(_importantItemWhitelist.Value),
                Whitelist = new NameSet(_whitelist.Value),
                BlacklistOnly = _blacklistOnly.Value,
                Blacklist = new NameSet(_blacklist.Value),
                ProtectEquipment = _protectEquipment.Value,
                ProtectUpgradedItems = _protectUpgradedItems.Value,
                ProtectLargeStacks = _protectLargeStacks.Value,
                LargeStackThreshold = Clamp(_largeStackThreshold.Value, 1, 100000, "LargeStackThreshold")
            };
        }

        private int Clamp(int value, int min, int max, string name)
        {
            var clamped = ConfigLimits.ClampInt(value, min, max);
            if (clamped != value)
            {
                _log.LogWarning(name + " was " + value + ", outside the allowed range " + min + "-" + max + ". Using " + clamped + ".");
            }

            return clamped;
        }

        private float Clamp(float value, float min, float max, string name)
        {
            var clamped = ConfigLimits.ClampFloat(value, min, max);
            if (Math.Abs(clamped - value) > float.Epsilon)
            {
                _log.LogWarning(name + " was " + value + ", outside the allowed range " + min + "-" + max + ". Using " + clamped + ".");
            }

            return clamped;
        }
    }
}
