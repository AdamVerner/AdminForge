using System.Diagnostics;

namespace AdminForge.UnitTests.LiveUpdates;

/// <summary>
/// Tiny polling helper for async-state convergence in the LiveUpdates tests.
/// FakeTimeProvider drives the producer clock; the test still needs to wait
/// for the consumer-side task to drain a channel item.
/// </summary>
internal static class Wait
{
    public static async Task Until(
        Func<bool> condition,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null
    )
    {
        var deadline =
            Stopwatch.GetTimestamp()
            + (long)((timeout ?? TimeSpan.FromSeconds(2)).TotalSeconds * Stopwatch.Frequency);
        var poll = pollInterval ?? TimeSpan.FromMilliseconds(5);
        while (!condition())
        {
            if (Stopwatch.GetTimestamp() > deadline)
                throw new TimeoutException(
                    "Wait.Until predicate did not become true within the timeout."
                );
            await Task.Delay(poll);
        }
    }
}
