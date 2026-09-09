using ValheimAutoCleanup.Policy;
using Xunit;

namespace ValheimAutoCleanup.Tests
{
    public class ConfigLimitsTests
    {
        [Theory]
        [InlineData(0, 60)]
        [InlineData(59, 60)]
        [InlineData(60, 60)]
        [InlineData(600, 600)]
        [InlineData(999999, 86400)]
        public void CleanupIntervalIsClampedIntoRange(int input, int expected)
        {
            var clamped = ConfigLimits.ClampInt(
                input, ConfigLimits.MinCleanupIntervalSeconds, ConfigLimits.MaxCleanupIntervalSeconds);

            Assert.Equal(expected, clamped);
        }

        [Theory]
        [InlineData(-10f, 0f)]
        [InlineData(0f, 0f)]
        [InlineData(25f, 25f)]
        [InlineData(99999f, 10000f)]
        public void RadiusIsClampedIntoRange(float input, float expected)
        {
            Assert.Equal(expected, ConfigLimits.ClampFloat(input, ConfigLimits.MinRadius, ConfigLimits.MaxRadius));
        }

        [Fact]
        public void NegativeCountsAreRaisedToTheirMinimum()
        {
            Assert.Equal(1, ConfigLimits.ClampInt(-5, ConfigLimits.MinMaxDeletesPerCleanup, ConfigLimits.MaxMaxDeletesPerCleanup));
            Assert.Equal(1, ConfigLimits.ClampInt(0, ConfigLimits.MinDeletesPerFrame, ConfigLimits.MaxDeletesPerFrame));
        }

        // --- Warning lead times ----------------------------------------------------------

        [Fact]
        public void WarningLeadIsPulledInsideTheInterval()
        {
            // A 60 s warning inside a 600 s cycle is fine.
            Assert.Equal(60, ConfigLimits.ClampWarningLead(60, 600));

            // A warning as long as the cycle would never fire; it is pulled to interval - 1.
            Assert.Equal(599, ConfigLimits.ClampWarningLead(600, 600));
            Assert.Equal(599, ConfigLimits.ClampWarningLead(5000, 600));
        }

        [Fact]
        public void NegativeWarningLeadBecomesZero()
        {
            Assert.Equal(0, ConfigLimits.ClampWarningLead(-30, 600));
        }

        [Fact]
        public void FinalWarningMustLandAfterTheFirstWarning()
        {
            Assert.Equal(10, ConfigLimits.ClampFinalWarningLead(10, 60, 600));

            // Equal to the first warning: pulled in by one second so the ordering holds.
            Assert.Equal(59, ConfigLimits.ClampFinalWarningLead(60, 60, 600));

            // Further out than the first warning: pulled in as well.
            Assert.Equal(59, ConfigLimits.ClampFinalWarningLead(120, 60, 600));
        }

        [Fact]
        public void FinalWarningIsAlsoBoundedByTheInterval()
        {
            // No first warning at all, so only the interval bounds it.
            Assert.Equal(59, ConfigLimits.ClampFinalWarningLead(500, 0, 60));
        }
    }
}
