using ValheimAutoCleanup.Models;
using Xunit;

namespace ValheimAutoCleanup.Tests
{
    public class CleanupReportTests
    {
        [Fact]
        public void DryRunReportShowsWhatWouldBeRemovedAndWhy()
        {
            var stats = new CleanupStatistics { TotalObjectsScanned = 1000, ItemDropsFound = 6, TooYoung = 1, EquipmentProtected = 1 };
            for (var i = 0; i < 4; i++)
            {
                stats.Record(CleanupDecision.DryRunCandidate);
            }

            stats.CountRemoval("Wood", 3);
            stats.CountRemoval("Stone", 1);
            stats.CountRemoval("wood", 1);
            stats.CountRemoval("Resin", 2);

            var report = stats.BuildReport("Cleanup report (dry run, nothing was removed):", dryRun: true);

            Assert.StartsWith("Cleanup report (dry run, nothing was removed):", report);
            Assert.Contains("Objects examined : 1000", report);
            Assert.Contains("Would remove     : 4", report);
            Assert.Contains("Wood: 4", report);
            Assert.Contains("not old enough     : 1", report);
            Assert.Contains("equipment          : 1", report);
            Assert.DoesNotContain("players nearby", report);

            // Largest first.
            Assert.True(report.IndexOf("Wood: 4") < report.IndexOf("Resin: 2"));
            Assert.True(report.IndexOf("Resin: 2") < report.IndexOf("Stone: 1"));
        }

        [Fact]
        public void LiveReportShowsRemovedCountAndOmitsAnEmptyKeptSection()
        {
            var stats = new CleanupStatistics { ItemDropsFound = 2 };
            stats.Record(CleanupDecision.Delete);
            stats.Record(CleanupDecision.Delete);
            stats.CountRemoval("Wood", 2);

            var report = stats.BuildReport("Cleanup report:", dryRun: false);

            Assert.Contains("Removed          : 2", report);
            Assert.DoesNotContain("Would remove", report);
            Assert.DoesNotContain("Kept:", report);
        }

        [Fact]
        public void LongListsAreCutAndTheRestSummarised()
        {
            var stats = new CleanupStatistics();
            for (var i = 0; i < 12; i++)
            {
                stats.CountRemoval("P" + i.ToString("00"), i + 1);
            }

            var report = stats.BuildReport("Cleanup report:", dryRun: false, maxPrefabs: 10);

            Assert.Contains("P11: 12", report);
            Assert.Contains("P02: 3", report);
            Assert.DoesNotContain("P01:", report);
            Assert.DoesNotContain("P00:", report);
            Assert.Contains("... and 2 more", report);
        }

        [Fact]
        public void TiesAreOrderedByName()
        {
            var stats = new CleanupStatistics();
            stats.CountRemoval("Stone", 1);
            stats.CountRemoval("Resin", 1);

            var report = stats.BuildReport("Cleanup report:", dryRun: false);

            Assert.True(report.IndexOf("Resin: 1") < report.IndexOf("Stone: 1"));
        }

        [Fact]
        public void FailuresAndThePerPassLimitAreReported()
        {
            var stats = new CleanupStatistics();
            stats.Record(CleanupDecision.OwnershipFailure);
            stats.Record(CleanupDecision.BudgetExhausted);

            var report = stats.BuildReport("Cleanup report:", dryRun: false);

            Assert.Contains("Failed to remove : 1", report);
            Assert.Contains("per-pass limit     : 1", report);
        }
    }
}
