using System;
using System.Collections.Generic;
using ValheimAutoCleanup.Models;

namespace ValheimAutoCleanup
{
    /// <summary>
    /// A bounded, in-memory ring of recent cleanup passes plus lifetime session totals.
    /// Nothing here is persisted: the plugin never writes to world saves or player files.
    /// </summary>
    public sealed class CleanupHistory
    {
        private const int Capacity = 10;

        private readonly LinkedList<CleanupHistoryEntry> _entries = new LinkedList<CleanupHistoryEntry>();

        public int TotalCleanups { get; private set; }
        public int TotalDeleted { get; private set; }
        public int TotalDryRunCandidates { get; private set; }
        public int TotalErrors { get; private set; }

        public DateTime? LastCleanupUtc { get; private set; }
        public int LastDeletedCount { get; private set; }

        public void Add(CleanupStatistics stats, bool dryRun, string trigger)
        {
            var entry = new CleanupHistoryEntry
            {
                TimestampUtc = DateTime.UtcNow,
                DryRun = dryRun,
                Deleted = dryRun ? stats.DryRunCandidates : stats.Deleted,
                Protected = stats.TotalProtected,
                Errors = stats.Errors + stats.DeletionFailures + stats.OwnershipFailures,
                DurationMilliseconds = stats.ExecutionMilliseconds,
                Trigger = trigger ?? "scheduled"
            };

            _entries.AddLast(entry);
            while (_entries.Count > Capacity)
            {
                _entries.RemoveFirst();
            }

            TotalCleanups++;
            TotalDeleted += stats.Deleted;
            TotalDryRunCandidates += stats.DryRunCandidates;
            TotalErrors += entry.Errors;
            LastCleanupUtc = entry.TimestampUtc;
            LastDeletedCount = entry.Deleted;
        }

        /// <summary>Most recent pass first.</summary>
        public IEnumerable<CleanupHistoryEntry> Recent()
        {
            for (var node = _entries.Last; node != null; node = node.Previous)
            {
                yield return node.Value;
            }
        }

        public void Clear()
        {
            _entries.Clear();
        }
    }
}
