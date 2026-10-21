using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// D2 of <c>todo/PLAN_a_cancelled_review_stops_its_round.md</c> and §2.1 / §10.2-3 of its S2 spec: the scheduler MARKS every turn the round's
/// own token ended — at the four sites where a cut turn ends — and never one that ended on its own terms. The engine decides whether a round
/// was withdrawn from these marks; report timing and the cause alone decide nothing.
/// </summary>
/// <remarks>
/// Driven through the real <see cref="BoundedScheduler"/> and <see cref="ReviewerExecutor"/> over a launcher the test plays, so "killed while
/// running", "still queued" and "waiting on the ladder" are each reached by the product's own path and not by a hand-built outcome.
/// </remarks>
public sealed class TheSchedulerMarksWhatTheRoundTokenEndedTests
{
    private const string Clean = """{"findings": []}""";

    /// <summary>A launcher the test plays.</summary>
    private sealed class Launcher(Func<ProcessRequest, CancellationToken, Task<ProcessResult>> run) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) => run(request, ct);
    }

    /// <summary>An adapter whose abandon courtesy the test can hold — the seam between an exception being thrown and being caught upstream.</summary>
    private sealed class Adapter(Func<Task> onAbandon) : IReviewerRuntime
    {
        public string Provider => "fake";

        public ReviewerInvocation Build(string role, string prompt, string worktreePath, string schemaFilePath, string outputDir, ReviewerSettings settings) =>
            throw new NotSupportedException("the test builds its own work");

        public Task AbandonAsync(ReviewerInvocation invocation, CancellationToken ct = default) => onAbandon();
    }

    private static ReviewerWork Work(string provider = "fake", IReviewerRuntime? adapter = null, string role = "Architecture") =>
        new(new ReviewerInvocation(provider, role, new ProcessRequest("vendor", [], Path.GetTempPath()) { Timeout = TimeSpan.FromSeconds(30) }, Adapter: adapter));

    private static ReviewerExecutor Executor(Func<ProcessRequest, CancellationToken, Task<ProcessResult>> run) =>
        new(new Launcher(run));

    private static ProcessResult Killed() => new(-1, string.Empty, string.Empty, TimedOut: true, Cancelled: true);

    private static async Task<ProcessResult> BlockedAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            // The launcher's own answer for a kill the round's token ordered.
        }

        return Killed();
    }

    private static Task<T> Within<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

    // ---------- the four sites ----------

    [Fact]
    public async Task ATurnKilledWhileRunning_AndReportedCancelled_IsMarked()
    {
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var round = new CancellationTokenSource();
        var running = new BoundedScheduler().RunAllAsync(
            [Work()], Executor((_, ct) => { arrived.TrySetResult(); return BlockedAsync(ct); }), round.Token);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        await round.CancelAsync();
        var outcome = (await Within(running)).Single().Outcome;

        outcome.Should().BeOfType<ReviewerOutcome.Cancelled>();
        outcome.EndedByRoundToken.Should().BeTrue("the launcher reported the kill the round's token ordered");
    }

    [Fact]
    public async Task ATurnKilledWhileRunning_WhoseLaunchThrows_IsMarked()
    {
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var round = new CancellationTokenSource();
        var running = new BoundedScheduler().RunAllAsync(
            [Work()],
            Executor(async (_, ct) =>
            {
                arrived.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);

                return Killed();
            }),
            round.Token);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        await round.CancelAsync();
        var outcome = (await Within(running)).Single().Outcome;

        outcome.Should().BeOfType<ReviewerOutcome.NotStarted>().Which.Reason.Should().Contain("cancelled while it was running");
        outcome.EndedByRoundToken.Should().BeTrue("the round's token ended the launch that threw");
    }

    [Fact]
    public async Task ATurnStillQueuedForASlot_IsMarked()
    {
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var round = new CancellationTokenSource();
        var running = new BoundedScheduler(globalCap: 1).RunAllAsync(
            [Work(role: "First"), Work(role: "Second")], Executor((_, ct) => { arrived.TrySetResult(); return BlockedAsync(ct); }), round.Token);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        await round.CancelAsync();
        var results = await Within(running);

        results.Select(r => r.Outcome).Should().OnlyContain(o => o.EndedByRoundToken, "the running one was killed and the queued one never got a slot");
        results.Select(r => r.Outcome).OfType<ReviewerOutcome.NotStarted>().Single().Reason.Should().Contain("still queued");
    }

    [Fact]
    public async Task ATurnWaitingOnTheLadder_IsMarked_AndKeepsTheRefusalItLastSaw()
    {
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var round = new CancellationTokenSource();
        var running = new BoundedScheduler(retryLadder: [TimeSpan.FromSeconds(30)]).RunAllAsync(
            [Work()],
            Executor((_, _) => Task.FromResult(new ProcessResult(1, string.Empty, "429 Too Many Requests", TimedOut: false))),
            round.Token,
            progress =>
            {
                if (progress.Note.StartsWith("rate limited on attempt 1", StringComparison.Ordinal))
                {
                    waiting.TrySetResult();
                }
            });
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        await round.CancelAsync();
        var outcome = (await Within(running)).Single().Outcome;

        outcome.Should().BeOfType<ReviewerOutcome.RateLimited>("no launch was killed: the turn ends as the refusal it last saw");
        outcome.EndedByRoundToken.Should().BeTrue("the round's token ended the wait");
    }

    // ---------- what is NOT a cut ----------

    [Fact]
    public async Task ACancelledOutcomeNobodyFired_IsNotMarked()
    {
        using var round = new CancellationTokenSource();

        var outcome = (await Within(new BoundedScheduler().RunAllAsync(
            [Work()], Executor((_, _) => Task.FromResult(Killed())), round.Token))).Single().Outcome;

        outcome.Should().BeOfType<ReviewerOutcome.Cancelled>();
        outcome.EndedByRoundToken.Should().BeFalse("a remote row the server ended: the round's token never fired");
    }

    [Fact]
    public async Task ATurnThatAnsweredWhileTheCancelFired_IsNotMarked()
    {
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var round = new CancellationTokenSource();
        var running = new BoundedScheduler().RunAllAsync(
            [Work()],
            Executor(async (_, _) =>
            {
                arrived.TrySetResult();
                await release.Task;

                return new ProcessResult(0, Clean, string.Empty, TimedOut: false);
            }),
            round.Token);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        await round.CancelAsync();
        release.TrySetResult();
        var outcome = (await Within(running)).Single().Outcome;

        outcome.Should().BeOfType<ReviewerOutcome.Ok>("a paid, finished answer is kept whatever fired after it");
        outcome.EndedByRoundToken.Should().BeFalse();
    }

    [Fact]
    public async Task AReportedTurn_IsMarkedBeforeItIsReported()
    {
        // The ledger line, the live view and the stand-down read the outcome the progress report carries: it must already be marked there.
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reported = new List<ReviewerOutcome>();
        using var round = new CancellationTokenSource();
        var running = new BoundedScheduler().RunAllAsync(
            [Work()],
            Executor((_, ct) => { arrived.TrySetResult(); return BlockedAsync(ct); }),
            round.Token,
            progress =>
            {
                if (progress.Outcome is { } outcome)
                {
                    lock (reported)
                    {
                        reported.Add(outcome);
                    }
                }
            });
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        await round.CancelAsync();
        await Within(running);

        reported.Should().ContainSingle().Which.EndedByRoundToken.Should().BeTrue();
    }

    // ---------- provenance (§10.2): an adapter's own cancellation is not a cut ----------

    [Theory]
    [InlineData(true, null, 10L, true)]
    [InlineData(false, null, 10L, false)]
    [InlineData(false, 5L, 10L, true)]
    [InlineData(false, 10L, 10L, true)]
    [InlineData(false, 11L, 10L, false)]
    public void TheMarkRule_ForAThrownCancellation(bool thrownWithTheRoundsToken, long? firedAt, long observedAt, bool expected)
    {
        // `round` is cancelled in every row but the second, whose token has not fired: the rule reads the token first.
        using var roundSource = new CancellationTokenSource();
        using var own = new CancellationTokenSource();
        var roundHasFired = firedAt is not null || thrownWithTheRoundsToken;
        if (roundHasFired)
        {
            roundSource.Cancel();
        }

        var thrownWith = thrownWithTheRoundsToken ? roundSource.Token : own.Token;

        RoundCut.Ended(thrownWith, roundSource.Token, firedAt, observedAt).Should().Be(expected);
    }

    [Fact]
    public async Task AnAdaptersOwnCancellation_BeforeTheCallerCancels_IsNotACut()
    {
        // The adapter throws (a Team-server client's own timeout), the executor is held in its abandon courtesy, the caller cancels, and the
        // courtesy returns: the exception reaches the scheduler AFTER the round was cancelled, but it was thrown BEFORE.
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var caller = new CancellationTokenSource();
        using var cancellation = new RoundCancellation(CancellationToken.None, caller.Token, TimeSpan.FromMinutes(5));
        using var own = new CancellationTokenSource();
        await own.CancelAsync();
        var adapter = new Adapter(async () =>
        {
            holding.TrySetResult();
            await release.Task;
        });
        var running = new BoundedScheduler().RunAllStampedAsync(
            [Work(adapter: adapter)],
            Executor((_, _) => throw new OperationCanceledException(own.Token)),
            cancellation.Token,
            roundFiredAt: () => cancellation.FiredAt);
        await holding.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        await caller.CancelAsync();
        release.TrySetResult();
        var outcome = (await Within(running)).Results.Single().Outcome;

        outcome.Should().BeOfType<ReviewerOutcome.NotStarted>();
        outcome.EndedByRoundToken.Should().BeFalse("the adapter's cancellation was thrown before any round source fired: it is not a withdrawal");
    }

    [Fact]
    public async Task AnAdaptersCancellation_ThrownAfterTheCallerCancelled_IsACut()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var caller = new CancellationTokenSource();
        using var cancellation = new RoundCancellation(CancellationToken.None, caller.Token, TimeSpan.FromMinutes(5));
        using var own = new CancellationTokenSource();
        var running = new BoundedScheduler().RunAllStampedAsync(
            [Work()],
            Executor(async (_, _) =>
            {
                arrived.TrySetResult();
                await release.Task;
                throw new OperationCanceledException(own.Token);
            }),
            cancellation.Token,
            roundFiredAt: () => cancellation.FiredAt);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        await caller.CancelAsync();
        release.TrySetResult();
        var outcome = (await Within(running)).Results.Single().Outcome;

        outcome.EndedByRoundToken.Should().BeTrue("the round was already cancelled when the exception was first seen");
    }

    // ---------- the return stamp (§10.3) ----------

    [Fact]
    public async Task TheStamp_IsTakenAfterEveryReportAndBeforeTheCallerContinues()
    {
        var lastReport = 0L;
        var before = System.Diagnostics.Stopwatch.GetTimestamp();
        var run = await Within(new BoundedScheduler().RunAllStampedAsync(
            [Work()],
            Executor((_, _) => Task.FromResult(new ProcessResult(0, Clean, string.Empty, TimedOut: false))),
            onProgress: progress => Interlocked.Exchange(ref lastReport, Math.Max(Interlocked.Read(ref lastReport), progress.LandedAt))));
        var after = System.Diagnostics.Stopwatch.GetTimestamp();

        run.ReturnedAt.Should().BeGreaterThanOrEqualTo(lastReport, "stamped as the scheduler's last act, after the last report was built");
        run.ReturnedAt.Should().BeGreaterThanOrEqualTo(before).And.BeLessThanOrEqualTo(after);
    }

    [Fact]
    public async Task AnEmptyRun_IsStampedToo()
    {
        var before = System.Diagnostics.Stopwatch.GetTimestamp();

        var run = await Within(new BoundedScheduler().RunAllStampedAsync([], Executor((_, _) => throw new InvalidOperationException("no work"))));

        run.Results.Should().BeEmpty();
        run.ReturnedAt.Should().BeGreaterThanOrEqualTo(before, "a lane-only round launches nobody and still returns a moment the cause is read against");
    }
}
