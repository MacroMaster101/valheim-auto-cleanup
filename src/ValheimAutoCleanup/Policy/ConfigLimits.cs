using System;

namespace ValheimAutoCleanup.Policy
{
    /// <summary>
    /// Configuration bounds and clamping. Pure and unit-testable.
    /// The plugin never refuses to start because of a bad value: it clamps, logs, and runs.
    /// </summary>
    public static class ConfigLimits
    {
        public const int MinCleanupIntervalSeconds = 60;
        public const int MaxCleanupIntervalSeconds = 86400;

        public const int MinItemAgeSeconds = 0;
        public const int MaxItemAgeSeconds = 604800;   // 7 days

        public const float MinRadius = 0f;
        public const float MaxRadius = 10000f;

        public const int MinMaxDeletesPerCleanup = 1;
        public const int MaxMaxDeletesPerCleanup = 100000;

        public const int MinDeletesPerFrame = 1;
        public const int MaxDeletesPerFrame = 5000;

        public const int MinBatchDelayMs = 0;
        public const int MaxBatchDelayMs = 5000;

        public const int MinStartupDelaySeconds = 0;
        public const int MaxStartupDelaySeconds = 86400;

        public static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static float ClampFloat(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>
        /// A warning must fire strictly before the cleanup it is warning about, otherwise
        /// it would either never fire or fire during the previous cycle. Returns the
        /// largest legal lead time.
        /// </summary>
        public static int ClampWarningLead(int warningSeconds, int cleanupIntervalSeconds)
        {
            if (warningSeconds < 0)
            {
                return 0;
            }

            var ceiling = Math.Max(0, cleanupIntervalSeconds - 1);
            return warningSeconds > ceiling ? ceiling : warningSeconds;
        }

        /// <summary>
        /// The final warning must land at or after the main warning (i.e. closer to the
        /// cleanup) and must still be inside the cycle.
        /// </summary>
        public static int ClampFinalWarningLead(int finalSeconds, int warningSeconds, int cleanupIntervalSeconds)
        {
            var clamped = ClampWarningLead(finalSeconds, cleanupIntervalSeconds);

            // If the admin set the final warning further out than the first warning, the two
            // are swapped in meaning; pull the final warning in so ordering always holds.
            if (warningSeconds > 0 && clamped >= warningSeconds)
            {
                clamped = Math.Max(0, warningSeconds - 1);
            }

            return clamped;
        }
    }
}
