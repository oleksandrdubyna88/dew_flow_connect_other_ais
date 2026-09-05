using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>One reviewer's work order: the launch, and the one repair launch it is allowed.</summary>
/// <param name="Prompt">
/// Which catalog prompt this reviewer was given. Carried so the audit log can NAME it: a verifier
/// checking that rotation works had to infer the prompt from the byte count of the argv line and
/// the known lengths of the catalog files, which is not a thing anyone should have to do.
/// </param>
public sealed record ReviewerWork(
    ReviewerInvocation Invocation,
    ReviewerInvocation? Repair = null,
    string Prompt = "");

/// <summary>
/// One reviewer crossing a line, reported the moment it happens.
/// </summary>
/// <remarks>
/// The fan-out is the slow part of the product — ten minutes is normal — and a round that showed
/// nothing until it finished left the person with no way to tell "working" from "hung". A callback
/// rather than a poll: the scheduler already knows, and nothing else has to guess.
/// </remarks>
/// <param name="Elapsed">How long the reviewer actually ran — zero until it finishes.</param>
/// <param name="Note">
/// A sentence for the person watching, when the status alone does not say enough — today, what a
/// queued reviewer is waiting for and roughly how long.
/// </param>
public sealed record ReviewerProgress(
    string Provider,
    ReviewRole Role,
    string Status,
    ReviewerOutcome? Outcome = null,
    TimeSpan Elapsed = default,
    string Note = "");

/// <summary>
/// The fan-out stays logically parallel; the queue is what keeps it survivable. Two providers ×
/// three roles is six CLIs wanting to start at the same instant — unbounded, that is where local
/// process limits, the CLIs' own lock files and the vendors' 429s all arrive at once, each
/// looking like a timeout unless handled by name.
/// </summary>
/// <remarks>
/// A global cap bounds the machine; a per-provider cap bounds each vendor, because a rate limit
/// is per vendor and a global cap alone would happily put all of its slots on one provider. A
/// rate-limited reviewer climbs a ladder of waits — 5 s, 30 s, 60 s, 120 s by default, each jittered
/// — and is reported only when the ladder is spent, the limit is hopeless, or the next wait would
/// outrun the reviewer's own deadline.
/// </remarks>
public sealed class BoundedScheduler(
    int globalCap = 3,
    int perProviderCap = 2,
    TimeSpan? rateLimitBackoff = null,
    int sharedResourceCap = 1,
    IReadOnlyList<TimeSpan>? retryLadder = null,
    Func<double>? jitterRoll = null)
{
    /// <summary>
    /// The waits a rate-limited reviewer climbs, widest source first.
    /// </summary>
    /// <remarks>
    /// A ladder if one was given; otherwise the single backoff if THAT was given, because passing
    /// it is how a caller says "one retry, at this interval" and a deployment that set
    /// <c>COAI_RATE_LIMIT_BACKOFF_SECONDS</c> must keep meaning what it meant; otherwise the
    /// shipped ladder. The distinction is possible only because the parameter is nullable — a
    /// defaulted fifteen seconds and a configured fifteen seconds would be the same value.
    /// </remarks>
    private readonly IReadOnlyList<TimeSpan> _ladder =
        retryLadder is { Count: > 0 } ladder ? ladder
        : rateLimitBackoff is { } backoff ? [backoff]
        : RetryLadder.Default;

    /// <summary>
    /// Where the jitter comes from — injected, so a test can pin a wait instead of sleeping.
    /// </summary>
    private readonly Func<double> _roll = jitterRoll ?? Random.Shared.NextDouble;

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

    /// <summary>
    /// The same, per SHARED RESOURCE — a local engine, keyed by the endpoint it answers on.
    /// </summary>
    /// <remarks>
    /// <b>Not reset per run</b>, unlike the two above: the whole point of this cap is that it holds
    /// across rounds, so a high-water mark that started again with every round could not show it.
    /// </remarks>
    public IReadOnlyDictionary<string, int> PeakPerResource => _peakPerResource;

    private readonly Dictionary<string, int> _peakPerResource = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _runningPerResource = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The caps, held for the life of the SCHEDULER.
    /// </summary>
    /// <remarks>
    /// They used to be created inside <see cref="RunAllAsync"/>, which made every one of them a cap
    /// per ROUND: two rounds in one server built two sets, so a cap of three allowed six on the
    /// machine the docstring says it bounds. Found by codex reviewing the plan for the engine cap —
    /// the same defect one layer above the one being fixed.
    /// </remarks>
    private readonly SemaphoreSlim _global = new(Math.Max(1, globalCap));
    private readonly Dictionary<string, SemaphoreSlim> _perProvider = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SemaphoreSlim> _perResource = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _limiters = new();

    /// <summary>
    /// The limiter for one key, created once and shared by every run afterwards.
    /// </summary>
    private SemaphoreSlim Limiter(Dictionary<string, SemaphoreSlim> map, string key, int cap)
    {
        lock (_limiters)
        {
            if (!map.TryGetValue(key, out var limiter))
            {
                // A cap below one would make every holder wait for ever, so a configured zero is a
                // configured mistake and not a way to switch reviewers off.
                limiter = new SemaphoreSlim(Math.Max(1, cap));
                map[key] = limiter;
            }

            return limiter;
        }
    }

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

        // These live as long as the SCHEDULER, not as long as one run. They were built inside this
        // method until 2026-09-03, which made every cap per ROUND: two rounds in one server each got
        // their own set, so a cap of three allowed six. Found by codex reviewing the plan for the
        // engine cap below — the same defect one layer up from the one being fixed.
        var global = _global;
        var perProvider = work
            .Select(w => w.Invocation.Provider)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(p => p, p => Limiter(_perProvider, p, perProviderCap), StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var w in work)
            {
                // A queued reviewer on a shared engine says what it is waiting for. "queued" alone
                // cannot tell ten seconds from ten minutes, and the machine knows: the lease counts
                // who is on the card — other processes included — and the history says how long
                // that model's runs take.
                Report(onProgress, w.Invocation, "queued", note: QueueNote(w.Invocation));
            }

            var tasks = work.Select(async w =>
            {
                var name = w.Invocation.Provider;
                var provider = perProvider[name];
                var engine = w.Invocation.SharedResource;
                var queued = System.Diagnostics.Stopwatch.StartNew();
                // Widest first, narrowest last: the machine, then the vendor, then the ENGINE.
                // The first version took the engine first, and gemini's reviewer named the cost —
                // a local reviewer would hold the card while still blocked on a machine slot filled
                // by hosted vendors, so the GPU sat idle and locked and every other local reviewer
                // waited on a card nobody was using. Same order for every reviewer, so nothing can
                // deadlock; released in reverse.
                try
                {
                    await global.WaitAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    // A round that ends while a reviewer is still queued is that reviewer's
                    // failure, not the round's exception: `Task.WhenAll` would otherwise fault and
                    // every sibling's result would be lost with it.
                    return Abandoned(onProgress, w.Invocation, queued.Elapsed);
                }
                try
                {
                    try
                    {
                        await provider.WaitAsync(ct);
                    }
                    catch (OperationCanceledException)
                    {
                        return Abandoned(onProgress, w.Invocation, queued.Elapsed);
                    }
                    try
                    {
                        var resource = engine.Length == 0
                            ? null
                            : Limiter(_perResource, engine, sharedResourceCap);
                        if (resource is not null)
                        {
                            // The note is computed HERE and not when the round was laid out. At
                            // lay-out nothing held the card yet, so the queue was always empty and
                            // the sentence was always blank — the estimate would have shipped
                            // never saying anything. (gemini, this change's code round.)
                            Report(onProgress, w.Invocation, "queued", note: QueueNote(w.Invocation));
                            try
                            {
                                await resource.WaitAsync(ct);
                            }
                            catch (OperationCanceledException)
                            {
                                return Abandoned(onProgress, w.Invocation, queued.Elapsed);
                            }

                            EnteredResource(engine);
                        }
                        try
                        {
                            Entered(name, runningPerProvider);
                            Report(onProgress, w.Invocation, "running");
                            var watch = System.Diagnostics.Stopwatch.StartNew();
                            try
                            {
                                var outcome = await RunWithLadderAsync(w, executor, ct);
                                Report(onProgress, w.Invocation, outcome is ReviewerOutcome.Ok ? "done" : "failed", outcome, watch.Elapsed);
                                return (w.Invocation, outcome);
                            }
                            catch (OperationCanceledException)
                            {
                                // The same defect as the queued case, one step further in — and it
                                // was the test for THAT which found this: a reviewer cancelled
                                // while RUNNING threw out of the fan-out, so `Task.WhenAll` faulted
                                // and the round reported none of its finished reviewers either.
                                return Abandoned(
                                    onProgress,
                                    w.Invocation,
                                    watch.Elapsed,
                                    "was cancelled while it was running");
                            }
                            finally
                            {
                                Left(name, runningPerProvider);
                            }
                        }
                        finally
                        {
                            if (resource is not null)
                            {
                                LeftResource(engine);
                                resource.Release();
                            }
                        }
                    }
                    finally
                    {
                        provider.Release();
                    }
                }
                finally
                {
                    global.Release();
                }
            });
            return await Task.WhenAll(tasks);
        }
        finally
        {
            // Nothing is disposed here any more: the limiters outlive the run on purpose, and a
            // `Dispose` in this block is what made them per-round in the first place.
        }
    }

    /// <summary>
    /// A reviewer the round ended under while it was still waiting for a slot.
    /// </summary>
    /// <remarks>
    /// It is REPORTED rather than thrown. A cancellation escaping one of these tasks faults
    /// <c>Task.WhenAll</c>, which discards every sibling's result along with it — so a round
    /// cancelled with five reviewers finished would have reported none of them. Raised twice in this
    /// change's code round, against two of the three waits.
    /// </remarks>
    private static (ReviewerInvocation, ReviewerOutcome) Abandoned(
        Action<ReviewerProgress>? onProgress,
        ReviewerInvocation invocation,
        TimeSpan elapsed,
        string what = "was still queued")
    {
        var outcome = new ReviewerOutcome.NotStarted(
            $"the round ended while this reviewer {what}, after {elapsed.TotalSeconds:F0}s");
        Report(onProgress, invocation, "failed", outcome, elapsed);

        return (invocation, outcome);
    }

    private void EnteredResource(string resource)
    {
        lock (_limiters)
        {
            var now = _runningPerResource.GetValueOrDefault(resource) + 1;
            _runningPerResource[resource] = now;
            _peakPerResource[resource] = Math.Max(_peakPerResource.GetValueOrDefault(resource), now);
        }
    }

    private void LeftResource(string resource)
    {
        lock (_limiters)
        {
            _runningPerResource[resource] = Math.Max(0, _runningPerResource.GetValueOrDefault(resource) - 1);
        }
    }

    /// <summary>A progress handler is a courtesy, never a dependency: its failure cannot fail a
    /// reviewer, because a panel that cannot repaint is not a review that did not happen.</summary>
    /// <summary>
    /// What a reviewer queued on a shared engine is waiting for, or nothing when it waits for
    /// nothing.
    /// </summary>
    /// <remarks>
    /// Read from the LEASE rather than from this scheduler's own queue, because the card is shared
    /// with every other server on the machine and "two ahead" that counted only our own round would
    /// be the comfortable half of the truth.
    /// </remarks>
    private static string QueueNote(ReviewerInvocation invocation) =>
        invocation.SharedResource.Length == 0
            ? string.Empty
            : EngineLease.WaitNote(invocation.SharedResource, invocation.Model);

    private static void Report(
        Action<ReviewerProgress>? onProgress,
        ReviewerInvocation invocation,
        string status,
        ReviewerOutcome? outcome = null,
        TimeSpan elapsed = default,
        string note = "")
    {
        try
        {
            onProgress?.Invoke(
                new ReviewerProgress(invocation.Provider, invocation.Role, status, outcome, elapsed, note));
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

    /// <summary>
    /// Launch, and while the answer is a rate limit worth waiting out, climb the ladder.
    /// </summary>
    /// <remarks>
    /// <para>The budget is the reviewer's OWN deadline and the clock starts at the first launch, so
    /// what is measured is wall time — the failed launches as well as the waits. A reviewer that has
    /// already spent its deadline being refused does not then wait two more minutes to be refused
    /// once more.</para>
    /// <para>A daily allowance is still not retried at all, ladder or no ladder: it clears at
    /// midnight in somebody else's timezone, and the one measured case cost a round 157 seconds
    /// instead of 19 for a second doomed launch.</para>
    /// </remarks>
    private async Task<ReviewerOutcome> RunWithLadderAsync(ReviewerWork w, ReviewerExecutor executor, CancellationToken ct)
    {
        var budget = w.Invocation.Request.Timeout;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var attempts = 1;
        var outcome = await executor.RunAsync(w.Invocation, w.Repair, ct);

        while (outcome is ReviewerOutcome.RateLimited limited && !RateLimit.Hopeless(limited.Reason))
        {
            var wait = RetryLadder.NextWait(attempts - 1, _ladder, _roll(), watch.Elapsed, budget);
            if (wait is null)
            {
                return limited with { Attempts = attempts };
            }

            await Task.Delay(wait.Value, ct);
            attempts += 1;
            outcome = await executor.RunAsync(w.Invocation, w.Repair, ct);
        }

        return outcome is ReviewerOutcome.RateLimited hopeless
            ? hopeless with { Attempts = attempts }
            : outcome;
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
        // The real number, because after the ladder it is not always one — and a round that says
        // "one retry" over four launches is a sentence a person would plan around.
        ReviewerOutcome.RateLimited r =>
            $"rate limited (after {r.Attempts} attempt{(r.Attempts == 1 ? "" : "s")}){Because(r.Reason)}",
        // The stderr tail travels WITH the exit code. "exit 1" alone was what made the same codex
        // failure undiagnosable twice at a real gate: the executor had captured the reason and
        // this sentence — the only place a person reads — threw it away.
        ReviewerOutcome.NonZeroExit e => $"exit {e.ExitCode}{Because(e.StdErrTail)}",
        ReviewerOutcome.NotStarted n => $"not started: {n.Reason}",
        ReviewerOutcome.Unparseable u => $"unparseable: {u.Reason}",
        _ => "unknown",
    };

    /// <summary>
    /// The CLI's last words: the most informative line of its stderr, short enough to live inside
    /// a summary sentence.
    /// </summary>
    /// <remarks>
    /// <para>The first version of this took the LAST non-empty line, reasoning that vendors print
    /// a banner before they print what went wrong. Measured against the exact failure class the
    /// feature exists for — a node CLI exiting 1 — that is the worst possible pick: node's last
    /// line is its own version. The gate reported <c>exit 1: Node.js v20.20.2</c> while
    /// <c>Error: Missing optional dependency @openai/codex-linux-x64</c> sat eight lines earlier,
    /// INSIDE the captured tail.</para>
    /// <para>So the rule is by CONTENT, not position: the first line that announces an error,
    /// skipping the stack frames and source echoes that surround it. Everything falls back to the
    /// first line that is not scaffolding, and only then to the last.</para>
    /// </remarks>
    private const int ReasonLength = 160;

    private static readonly string[] Announcements =
        ["error:", "error ", "exception", "fatal", "refused", "denied", "unauthorized", "quota", "not found", "missing"];

    private static string Because(string stdErrTail)
    {
        // A vendor that named a closed door gets its cure quoted instead of its stack trace. The
        // Gemini retirement was mistaken for three different things because the sentence a reader
        // saw was `exit 1` and the sentence that mattered was eight lines inside a node stack.
        if (VendorDiagnosis.For(stdErrTail) is { } cure)
        {
            return $": {cure}";
        }

        var lines = stdErrTail
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Length > 0)
            .ToList();
        if (lines.Count == 0)
        {
            return " (the CLI said nothing on stderr)";
        }

        var meaningful = lines.Where(l => !IsScaffolding(l)).ToList();
        var line = meaningful.FirstOrDefault(Announces) ?? meaningful.FirstOrDefault() ?? lines[^1];
        return $": {(line.Length <= ReasonLength ? line : $"{line[..ReasonLength]}…")}";
    }

    private static bool Announces(string line) =>
        Announcements.Any(a => line.Contains(a, StringComparison.OrdinalIgnoreCase));

    /// <summary>Stack frames, source echoes and version banners — the noise around the message.</summary>
    private static bool IsScaffolding(string line) =>
        line.StartsWith("at ", StringComparison.Ordinal) ||
        line.StartsWith('^') ||
        line.StartsWith("throw ", StringComparison.Ordinal) ||
        line.StartsWith("Node.js v", StringComparison.Ordinal) ||
        line.StartsWith("file:///", StringComparison.Ordinal);
}
