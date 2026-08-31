using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>One reviewer's work order: the launch, and the one repair launch it is allowed.</summary>
public sealed record ReviewerWork(ReviewerInvocation Invocation, ReviewerInvocation? Repair = null);

/// <summary>
/// The fan-out stays logically parallel; the queue is what keeps it survivable. Two providers ×
/// three roles is six CLIs wanting to start at the same instant — unbounded, that is where local
/// process limits, the CLIs' own lock files and the vendors' 429s all arrive at once, each
/// looking like a timeout unless handled by name.
/// </summary>
/// <remarks>
/// A global cap bounds the machine; a per-provider cap bounds each vendor, because a rate limit
/// is per vendor and a global cap alone would happily put all of its slots on one provider. A
/// rate-limited reviewer is retried exactly once after a backoff, and only then reported.
/// </remarks>
public sealed class BoundedScheduler(int globalCap = 3, int perProviderCap = 2, TimeSpan? rateLimitBackoff = null)
{
    private readonly TimeSpan _backoff = rateLimitBackoff ?? TimeSpan.FromSeconds(15);

    public async Task<IReadOnlyList<(ReviewerInvocation Invocation, ReviewerOutcome Outcome)>> RunAllAsync(
        IReadOnlyList<ReviewerWork> work,
        ReviewerExecutor executor,
        CancellationToken ct = default)
    {
        using var global = new SemaphoreSlim(globalCap);
        var perProvider = work
            .Select(w => w.Invocation.Provider)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(p => p, _ => new SemaphoreSlim(perProviderCap), StringComparer.OrdinalIgnoreCase);
        try
        {
            var tasks = work.Select(async w =>
            {
                var provider = perProvider[w.Invocation.Provider];
                await provider.WaitAsync(ct);
                try
                {
                    await global.WaitAsync(ct);
                    try
                    {
                        return (w.Invocation, await RunWithOneRetryAsync(w, executor, ct));
                    }
                    finally
                    {
                        global.Release();
                    }
                }
                finally
                {
                    provider.Release();
                }
            });
            return await Task.WhenAll(tasks);
        }
        finally
        {
            foreach (var semaphore in perProvider.Values)
            {
                semaphore.Dispose();
            }
        }
    }

    private async Task<ReviewerOutcome> RunWithOneRetryAsync(ReviewerWork w, ReviewerExecutor executor, CancellationToken ct)
    {
        var outcome = await executor.RunAsync(w.Invocation, w.Repair, ct);
        if (outcome is not ReviewerOutcome.RateLimited)
        {
            return outcome;
        }

        await Task.Delay(_backoff, ct);
        return await executor.RunAsync(w.Invocation, w.Repair, ct);
    }
}

/// <summary>Folds a fan-out's outcomes into the core's honest per-round summary.</summary>
public static class ReviewerSummaryFactory
{
    public static ReviewerSummary From(IReadOnlyList<(ReviewerInvocation Invocation, ReviewerOutcome Outcome)> results) =>
        new(
            results.Count,
            results.Count(r => r.Outcome is ReviewerOutcome.Ok),
            [.. results
                .Where(r => r.Outcome is not ReviewerOutcome.Ok)
                .Select(r => $"{r.Invocation.Provider}/{r.Invocation.Role}: {Describe(r.Outcome)}")]);

    private static string Describe(ReviewerOutcome outcome) => outcome switch
    {
        ReviewerOutcome.TimedOut => "timeout",
        ReviewerOutcome.RateLimited => "rate limited (after one retry)",
        ReviewerOutcome.NonZeroExit e => $"exit {e.ExitCode}",
        ReviewerOutcome.Unparseable u => $"unparseable: {u.Reason}",
        _ => "unknown",
    };
}
