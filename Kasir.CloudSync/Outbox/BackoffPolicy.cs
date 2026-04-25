using System;

namespace Kasir.CloudSync.Outbox
{
    // Exponential backoff for OutboxRouter ticks after a sink failure.
    // Worker calls Delay(consecutiveFailures) to compute the next sleep.
    // 0 failures -> base interval; each subsequent failure doubles the
    // sleep up to MaxBackoffSeconds. Keeps a connection storm during a
    // Supabase outage from spamming the Postgres connection budget.
    //
    // Sequence (base 30s, max 600s):
    //   failures=0 ->  30s
    //   failures=1 ->  60s
    //   failures=2 -> 120s
    //   failures=3 -> 240s
    //   failures=4 -> 480s
    //   failures=5+-> 600s (capped)
    public static class BackoffPolicy
    {
        public const int MaxBackoffSeconds = 600;
        public const int DefaultBaseIntervalSeconds = 30;

        public static TimeSpan Delay(int consecutiveFailures, int baseIntervalSeconds = DefaultBaseIntervalSeconds)
        {
            if (consecutiveFailures < 0)
                consecutiveFailures = 0;

            // Use long arithmetic so 1 << 30 doesn't overflow into negatives
            // even though we cap well before that.
            long multiplier = 1L << Math.Min(consecutiveFailures, 30);
            long seconds = baseIntervalSeconds * multiplier;
            if (seconds > MaxBackoffSeconds) seconds = MaxBackoffSeconds;
            return TimeSpan.FromSeconds(seconds);
        }
    }
}
