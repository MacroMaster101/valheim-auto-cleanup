using System;
using System.Collections.Generic;
using System.Text;

namespace ValheimAutoCleanup.Models
{
    /// <summary>Per-pass counters. One instance is created per cleanup pass.</summary>
    public sealed class CleanupStatistics
    {
        public int TotalObjectsScanned;
        public int ItemDropsFound;

        public int TooYoung;
        public int PlayerProtected;
        public int RecentPlayerProtected;
        public int BaseProtected;
        public int WardProtected;
        public int WorldSpawnProtected;
        public int Whitelisted;
        public int ImportantItemProtected;
        public int EquipmentProtected;
        public int UpgradedItemProtected;
        public int LargeStackProtected;
        public int BlacklistFiltered;
        public int NotALooseDrop;

        public int InvalidObjects;
        public int OwnershipFailures;
        public int DeletionFailures;
        public int BudgetExhausted;
        public int Errors;

        public int Deleted;
        public int DryRunCandidates;

        public long ExecutionMilliseconds;

        /// <summary>Prefab name -&gt; number of units (stack-aware) that were or would be removed.</summary>
        public readonly Dictionary<string, int> RemovalsByPrefab =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Total number of items kept for any protective reason.</summary>
        public int TotalProtected =>
            TooYoung + PlayerProtected + RecentPlayerProtected + BaseProtected + WardProtected +
            WorldSpawnProtected + Whitelisted + ImportantItemProtected + EquipmentProtected +
            UpgradedItemProtected + LargeStackProtected + BlacklistFiltered + NotALooseDrop;

        public void Record(CleanupDecision decision)
        {
            switch (decision)
            {
                case CleanupDecision.Delete: Deleted++; break;
                case CleanupDecision.DryRunCandidate: DryRunCandidates++; break;
                case CleanupDecision.TooYoung: TooYoung++; break;
                case CleanupDecision.PlayerNearby: PlayerProtected++; break;
                case CleanupDecision.RecentPlayerActivity: RecentPlayerProtected++; break;
                case CleanupDecision.BaseProtected: BaseProtected++; break;
                case CleanupDecision.WardProtected: WardProtected++; break;
                case CleanupDecision.WorldSpawnProtected: WorldSpawnProtected++; break;
                case CleanupDecision.Whitelisted: Whitelisted++; break;
                case CleanupDecision.ImportantItem: ImportantItemProtected++; break;
                case CleanupDecision.Equipment: EquipmentProtected++; break;
                case CleanupDecision.Upgraded: UpgradedItemProtected++; break;
                case CleanupDecision.LargeStack: LargeStackProtected++; break;
                case CleanupDecision.NotBlacklisted: BlacklistFiltered++; break;
                case CleanupDecision.NotALooseDrop: NotALooseDrop++; break;
                case CleanupDecision.Invalid: InvalidObjects++; break;
                case CleanupDecision.OwnershipFailure: OwnershipFailures++; break;
                case CleanupDecision.DeletionFailure: DeletionFailures++; break;
                case CleanupDecision.BudgetExhausted: BudgetExhausted++; break;
            }
        }

        public void CountRemoval(string prefabName, int stack)
        {
            if (string.IsNullOrEmpty(prefabName))
            {
                prefabName = "<unknown>";
            }

            RemovalsByPrefab.TryGetValue(prefabName, out var current);
            RemovalsByPrefab[prefabName] = current + (stack > 0 ? stack : 1);
        }

        /// <summary>
        /// A single-line-per-field summary. Zero-valued protection counters are omitted so
        /// the log stays readable on a quiet server.
        /// </summary>
        public string BuildSummary(bool dryRun)
        {
            var sb = new StringBuilder();
            sb.Append("Cleanup complete (").Append(dryRun ? "DRY RUN" : "LIVE").Append("): ");
            sb.Append("Scanned=").Append(TotalObjectsScanned);
            sb.Append(" Drops=").Append(ItemDropsFound);

            Append(sb, "TooYoung", TooYoung);
            Append(sb, "PlayerProtected", PlayerProtected);
            Append(sb, "RecentPlayer", RecentPlayerProtected);
            Append(sb, "BaseProtected", BaseProtected);
            Append(sb, "WardProtected", WardProtected);
            Append(sb, "SpawnProtected", WorldSpawnProtected);
            Append(sb, "Whitelisted", Whitelisted);
            Append(sb, "Important", ImportantItemProtected);
            Append(sb, "Equipment", EquipmentProtected);
            Append(sb, "Upgraded", UpgradedItemProtected);
            Append(sb, "LargeStack", LargeStackProtected);
            Append(sb, "NotBlacklisted", BlacklistFiltered);
            Append(sb, "NotALooseDrop", NotALooseDrop);
            Append(sb, "Invalid", InvalidObjects);
            Append(sb, "OwnershipFailed", OwnershipFailures);
            Append(sb, "DeleteFailed", DeletionFailures);
            Append(sb, "BudgetExhausted", BudgetExhausted);
            Append(sb, "Errors", Errors);

            sb.Append(dryRun ? " WouldDelete=" : " Deleted=")
              .Append(dryRun ? DryRunCandidates : Deleted);
            sb.Append(" Duration=").Append(ExecutionMilliseconds).Append("ms");
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, string label, int value)
        {
            if (value > 0)
            {
                sb.Append(' ').Append(label).Append('=').Append(value);
            }
        }
    }

    /// <summary>One immutable entry in the rolling in-memory cleanup history.</summary>
    public sealed class CleanupHistoryEntry
    {
        public DateTime TimestampUtc;
        public bool DryRun;
        public int Deleted;
        public int Protected;
        public int Errors;
        public long DurationMilliseconds;
        public string Trigger = "scheduled";

        public override string ToString()
        {
            return string.Format(
                "{0:yyyy-MM-dd HH:mm:ss}Z  {1,-9} {2,-9} removed={3,-5} protected={4,-6} errors={5,-3} {6}ms",
                TimestampUtc,
                Trigger,
                DryRun ? "[dry-run]" : "[live]",
                Deleted,
                Protected,
                Errors,
                DurationMilliseconds);
        }
    }
}
