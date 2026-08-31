using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>One reviewer's work order: the launch, and the one repair launch it is allowed.</summary>
public sealed record ReviewerWork(ReviewerInvocation Invocation, ReviewerInvocation? Repair = null);

/// <summary>
/// One reviewer crossing a line, reported the moment it happens.
/// </summary>
/// <remarks>
/// The fan-out is the slow part of the product — ten minutes is normal — and a round that showed
/// nothing until it finished left the person with no way to tell "working" from "hung". A callback
/// rather than a poll: the scheduler already knows, and nothing else has to guess.
/// </remarks>
/// <param name="Elapsed">How long the reviewer actually ran — zero until it finishes.</param>
public sealed record ReviewerProgress(
    string Provider,
    ReviewRole Role,
    string Status,
    ReviewerOutcome? Outcome = null,
    TimeSpan Elapsed = default);

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

    private int _running;

    /// <summary>
    /// The most reviewers ever in flight at once during the last <see cref="RunAllAsync"/>.
    /// </summary>
    /// <remarks>
    /// Instrumented rather than inferred. The first version of the cap test measured overlap from
    /// wall-clock ticks the child processes wrote, and on a loaded two-core CI runner it reported
    /// FIVE against a cap of three — a number the semaphore cannot produce, so the measurement was
    /// what was wrong. A counter incremented where the semaphore is actually held answers the
    /// question the test is asking, with no clocks and no files in the way.
    /// </remarks>
    public int PeakConcurrency { get; private set; }

    /// <summary>The same, per provider — a rate limit is per vendor.</summary>
    public IReadOnlyDictionary<string, int> PeakPerProvider => _peakPerProvider;

    private readonly Dictionary<string, int> _peakPerProvider = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="onProgress">
    /// Called as each reviewer is queued, starts and ends. Invoked from the fan-out's threads, so
    /// the handler must be thread-safe and quick — a slow one delays the reviewer it reports on.
    /// </param>
    public async Task<IReadOnlyList<(ReviewerInvocation Invocation, ReviewerOutcome Outcome)>> RunAllAsync(
        IReadOnlyList<ReviewerWork> work,
        ReviewerExecutor executor,
        CancellationToken ct = default,
        Action<ReviewerProgress>? onProgress = null)
    {
        PeakConcurrency = 0;
        _peakPerProvider.Clear();
        _running = 0;
        var runningPerProvider = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        using var global = new SemaphoreSlim(globalCap);
        var perProvider = work
            .Select(w => w.Invocation.Provider)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(p => p, _ => new SemaphoreSlim(perProviderCap), StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var w in work)
            {
                Report(onProgress, w.Invocation, "queued");
            }

            var tasks = work.Select(async w =>
            {
                var name = w.Invocation.Provider;
                var provider = perProvider[name];
                await provider.WaitAsync(ct);
                try
                {
                    await global.WaitAsync(ct);
                    Entered(name, runningPerProvider);
                    Report(onProgress, w.Invocation, "running");
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        var outcome = await RunWithOneRetryAsync(w, executor, ct);
                        Report(onProgress, w.Invocation, outcome is ReviewerOutcome.Ok ? "done" : "failed", outcome, watch.Elapsed);
                        return (w.Invocation, outcome);
                    }
                    finally
                    {
                        Left(name, runningPerProvider);
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

    /// <summary>A progress handler is a courtesy, never a dependency: its failure cannot fail a
    /// reviewer, because a panel that cannot repaint is not a review that did not happen.</summary>
    private static void Report(
        Action<ReviewerProgress>? onProgress,
        ReviewerInvocation invocation,
        string status,
        ReviewerOutcome? outcome = null,
        TimeSpan elapsed = default)
    {
        try
        {
            onProgress?.Invoke(new ReviewerProgress(invocation.Provider, invocation.Role, status, outcome, elapsed));
        }
        catch (Exception)
        {
            // Reporting is not the work.
        }
    }

    /// <summary>Counted where the slot is actually held; the lock covers the peaks, not the work.</summary>
    private void Entered(string provider, Dictionary<string, int> perProvider)
    {
        var running = Interlocked.Increment(ref _running);
        lock (_peakPerProvider)
        {
            PeakConcurrency = Math.Max(PeakConcurrency, running);
            var forProvider = perProvider.GetValueOrDefault(provider) + 1;
            perProvider[provider] = forProvider;
            _peakPerProvider[provider] = Math.Max(_peakPerProvider.GetValueOrDefault(provider), forProvider);
        }
    }

    private void Left(string provider, Dictionary<string, int> perProvider)
    {
        Interlocked.Decrement(ref _running);
        lock (_peakPerProvider)
        {
            perProvider[provider] = perProvider.GetValueOrDefault(provider) - 1;
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

    public static string Describe(ReviewerOutcome outcome) => outcome switch
    {
        ReviewerOutcome.TimedOut => "timeout",
        ReviewerOutcome.RateLimited => "rate limited (after one retry)",
        // The stderr tail travels WITH the exit code. "exit 1" alone was what made the same codex
        // failure undiagnosable twice at a real gate: the executor had captured the reason and
        // this sentence — the only place a person reads — threw it away.
        ReviewerOutcome.NonZeroExit e => $"exit {e.ExitCode}{Because(e.StdErrTail)}",
        ReviewerOutcome.NotStarted n => $"not started: {n.Reason}",
        ReviewerOutcome.Unparseable u => $"unparseable: {u.Reason}",
        _ => "unknown",
    };

    /// <summary>
    /// The CLI's last words, on one line and short enough to live inside a summary sentence.
    /// </summary>
    /// <remarks>
    /// The LAST non-empty line, not the first: vendors print a banner before they print what went
    /// wrong, so the top of the tail is reliably the least informative part of it.
    /// </remarks>
    private const int ReasonLength = 160;

    private static string Because(string stdErrTail)
    {
        var line = stdErrTail
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(l => l.Length > 0);
        if (line is null)
        {
            return " (the CLI said nothing on stderr)";
        }

        return $": {(line.Length <= ReasonLength ? line : $"{line[..ReasonLength]}…")}";
    }
}
