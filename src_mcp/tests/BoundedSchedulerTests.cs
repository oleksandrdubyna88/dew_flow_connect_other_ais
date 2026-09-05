using Xunit;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;

namespace CoaiMcp.Tests;

[Collection("fakecli-env")]
public sealed class BoundedSchedulerTests
{
    private readonly ReviewerExecutor _executor = new(new ProcessLauncher());
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-sched-").FullName;


    /// <summary>
    /// The cap, measured where the slot is held rather than inferred from the clock.
    /// </summary>
    /// <remarks>
    /// The first version counted overlapping [start,end] ticks written by the child processes.
    /// On a loaded two-core CI runner it reported FIVE against a cap of three — a number the
    /// semaphore cannot produce, so the measurement was what was wrong, not the scheduler. The
    /// counter answers exactly the question being asked, with no clocks and no files in between.
    /// </remarks>
    [Fact]
    public async Task ConcurrentReviewers_NeverExceedTheGlobalCap()
    {
        var busyDir = Directory.CreateDirectory(Path.Combine(_dir, "global")).FullName;
        var work = Enumerable.Range(0, 6)
            .Select(i => new ReviewerWork(
                FakeCliInvocations.Invoke($"vendor{i % 3}", ["busy", busyDir, "300"])))
            .ToList();
        var scheduler = new BoundedScheduler(globalCap: 3, perProviderCap: 2);

        await scheduler.RunAllAsync(work, _executor, TestContext.Current.CancellationToken);

        scheduler.PeakConcurrency.Should().BeLessThanOrEqualTo(3, "the global semaphore is the machine's cap")
            .And.BeGreaterThan(1, "the fan-out must still be a fan-out, not a serial queue");
        Directory.GetFiles(busyDir, "*.end").Should().HaveCount(6, "every reviewer really ran");
    }

    [Fact]
    public async Task OneVendor_NeverHoldsMoreThanItsPerProviderCap()
    {
        var slowDir = Directory.CreateDirectory(Path.Combine(_dir, "slow")).FullName;
        var work = Enumerable.Range(0, 4)
            .Select(_ => new ReviewerWork(FakeCliInvocations.Invoke("slowvendor", ["busy", slowDir, "300"])))
            .ToList();
        var scheduler = new BoundedScheduler(globalCap: 4, perProviderCap: 2);

        await scheduler.RunAllAsync(work, _executor, TestContext.Current.CancellationToken);

        scheduler.PeakPerProvider["slowvendor"].Should().BeLessThanOrEqualTo(2,
            "a rate limit is per vendor — a global cap alone would put every slot on one provider");
        scheduler.PeakConcurrency.Should().BeLessThanOrEqualTo(2,
            "with one vendor, its own cap is the binding one");
    }

    /// <summary>
    /// A configured single step still means a single retry — the behaviour
    /// <c>COAI_RATE_LIMIT_BACKOFF_SECONDS</c> has always bought, pinned so the ladder cannot take
    /// it away from a deployment that asked for it by name.
    /// </summary>
    [Fact]
    public async Task RateLimited_RetriesExactlyOnce_ThenSucceeds()
    {
        var counter = Path.Combine(_dir, "rl-count.txt");
        var flag = Path.Combine(_dir, "rl-flag");
        var work = new ReviewerWork(FakeCliInvocations.Invoke(
            "codex",
            ["count", counter, "flip", flag, "429 Too Many Requests", "1", FakeCliInvocations.CleanReview]));

        var results = await new BoundedScheduler(rateLimitBackoff: TimeSpan.FromMilliseconds(1))
            .RunAllAsync([work], _executor, TestContext.Current.CancellationToken);

        results.Single().Outcome.Should().BeOfType<ReviewerOutcome.Ok>();
        (await File.ReadAllLinesAsync(counter, TestContext.Current.CancellationToken))
            .Should().HaveCount(2, "one hit, one retry after backoff — and no third");
    }

    [Fact]
    public async Task RateLimitedTwice_IsNamed_AfterExactlyTwoLaunches()
    {
        var counter = Path.Combine(_dir, "rl2-count.txt");
        var work = new ReviewerWork(FakeCliInvocations.Invoke(
            "codex",
            ["count", counter, "stderr-exit", "429 Too Many Requests", "1"]));

        var results = await new BoundedScheduler(rateLimitBackoff: TimeSpan.FromMilliseconds(1))
            .RunAllAsync([work], _executor, TestContext.Current.CancellationToken);

        results.Single().Outcome.Should().BeOfType<ReviewerOutcome.RateLimited>(
            "a quota is a distinct outcome, never a timeout in disguise");
        (await File.ReadAllLinesAsync(counter, TestContext.Current.CancellationToken)).Should().HaveCount(2);
    }

    /// <summary>
    /// The ladder: a limit that clears on the second wait is a reviewer that ANSWERS, where the
    /// single retry reported it as failed for the round.
    /// </summary>
    [Fact]
    public async Task RateLimitedTwice_ClimbsTheLadder_ThenAnswers()
    {
        var counter = Path.Combine(_dir, "ladder-count.txt");
        var work = new ReviewerWork(FakeCliInvocations.Invoke(
            "codex",
            ["count", counter, "fail-until", counter, "2", "429 Too Many Requests", "1", FakeCliInvocations.CleanReview]));

        var results = await new BoundedScheduler(retryLadder: Tiny(3))
            .RunAllAsync([work], _executor, TestContext.Current.CancellationToken);

        results.Single().Outcome.Should().BeOfType<ReviewerOutcome.Ok>(
            "the third attempt was not rate limited, and the ladder had a step left for it");
        (await File.ReadAllLinesAsync(counter, TestContext.Current.CancellationToken))
            .Should().HaveCount(3, "two limits, two waits, three launches");
    }

    /// <summary>
    /// A ladder that is spent is the answer, and the count of attempts travels with it — the
    /// summary used to say "after one retry" whatever had happened.
    /// </summary>
    [Fact]
    public async Task RateLimitedThroughout_IsNamed_WithTheAttemptsItActuallyTook()
    {
        var counter = Path.Combine(_dir, "ladder-spent.txt");
        var work = new ReviewerWork(FakeCliInvocations.Invoke(
            "codex",
            ["count", counter, "stderr-exit", "429 Too Many Requests", "1"]));

        var results = await new BoundedScheduler(retryLadder: Tiny(2))
            .RunAllAsync([work], _executor, TestContext.Current.CancellationToken);

        results.Single().Outcome.Should().BeOfType<ReviewerOutcome.RateLimited>()
            .Which.Attempts.Should().Be(3, "one launch plus a step each for the two waits");
        (await File.ReadAllLinesAsync(counter, TestContext.Current.CancellationToken)).Should().HaveCount(3);
        ReviewerSummaryFactory.From(results).Failures.Single()
            .Should().Contain("after 3 attempts", "a person reading the round gets the real number");
    }

    /// <summary>
    /// The one limit no ladder may climb. Measured before this existed: gemini answered "you have
    /// exhausted your daily quota", the scheduler waited and launched a second doomed reviewer, and
    /// the round took 157 seconds instead of 19. A four-step ladder would have made it worse.
    /// </summary>
    [Fact]
    public async Task AHopelessLimit_IsNotRetriedAtAll_HoweverLongTheLadder()
    {
        var counter = Path.Combine(_dir, "hopeless-count.txt");
        var work = new ReviewerWork(FakeCliInvocations.Invoke(
            "gemini",
            ["count", counter, "stderr-exit", "You have exhausted your daily quota on this model", "1"]));

        var results = await new BoundedScheduler(retryLadder: Tiny(4))
            .RunAllAsync([work], _executor, TestContext.Current.CancellationToken);

        results.Single().Outcome.Should().BeOfType<ReviewerOutcome.RateLimited>()
            .Which.Attempts.Should().Be(1);
        (await File.ReadAllLinesAsync(counter, TestContext.Current.CancellationToken))
            .Should().HaveCount(1, "a daily allowance clears at midnight, not after a wait");
    }

    /// <summary>
    /// A reviewer whose own deadline is shorter than the ladder stops when the deadline does,
    /// rather than waiting past it to fail once more.
    /// </summary>
    [Fact]
    public async Task ALadderLongerThanTheReviewersDeadline_StopsAtTheDeadline()
    {
        var counter = Path.Combine(_dir, "budget-count.txt");
        var work = new ReviewerWork(FakeCliInvocations.Invoke(
            "codex",
            ["count", counter, "stderr-exit", "429 Too Many Requests", "1"],
            timeout: TimeSpan.FromMilliseconds(250)));

        var results = await new BoundedScheduler(
                retryLadder: [TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(30)])
            .RunAllAsync([work], _executor, TestContext.Current.CancellationToken);

        results.Single().Outcome.Should().BeOfType<ReviewerOutcome.RateLimited>();
        (await File.ReadAllLinesAsync(counter, TestContext.Current.CancellationToken))
            .Should().HaveCount(2, "the 1ms step fits a 250ms deadline and the 30s step cannot");
    }

    /// <summary>
    /// A reviewer that is waiting out a rate limit SAYS so, with the attempt and the wait.
    /// </summary>
    /// <remarks>
    /// The ladder can hold a reviewer for over three minutes, and the panel's only report until now
    /// was "running" — indistinguishable from a model that is thinking. Raised on this change's code
    /// round, and it is the same lesson the queued-reviewer note already learned.
    /// </remarks>
    [Fact]
    public async Task AReviewerWaitingOutALimit_SaysWhatItIsWaitingFor()
    {
        var counter = Path.Combine(_dir, "note-count.txt");
        var work = new ReviewerWork(FakeCliInvocations.Invoke(
            "codex",
            ["count", counter, "fail-until", counter, "1", "429 Too Many Requests", "1", FakeCliInvocations.CleanReview]));
        var notes = new List<string>();

        await new BoundedScheduler(retryLadder: Tiny(2))
            .RunAllAsync(
                [work],
                _executor,
                TestContext.Current.CancellationToken,
                p => { lock (notes) { notes.Add($"{p.Status}|{p.Note}"); } });

        notes.Should().ContainMatch("*rate limited*", "the status alone reads as a model thinking");
    }

    /// <summary>A ladder of n steps, each too short to slow a test down.</summary>
    private static IReadOnlyList<TimeSpan> Tiny(int steps) =>
        [.. Enumerable.Repeat(TimeSpan.FromMilliseconds(1), steps)];

    [Fact]
    public async Task FourOfSixAnswer_TheSummaryNamesWhoFailedAndWhy()
    {
        var work = new List<ReviewerWork>
        {
            new(FakeCliInvocations.Invoke("codex", ["emit", FakeCliInvocations.CleanReview])),
            new(FakeCliInvocations.Invoke("codex", ["emit", FakeCliInvocations.CleanReview])),
            new(FakeCliInvocations.Invoke("gemini", ["emit", FakeCliInvocations.CleanReview])),
            new(FakeCliInvocations.Invoke("gemini", ["emit", FakeCliInvocations.CleanReview])),
            new(FakeCliInvocations.Invoke("codex", ["stderr-exit", "boom", "3"])),
            new(FakeCliInvocations.Invoke("gemini", ["sleep", "20000"], timeout: TimeSpan.FromMilliseconds(700))),
        };

        var results = await new BoundedScheduler().RunAllAsync(work, _executor, TestContext.Current.CancellationToken);
        var summary = ReviewerSummaryFactory.From(results);

        summary.Sentence.Should().Contain("4 of 6");
        summary.Sentence.Should().Contain("codex/Architecture: exit 3");
        summary.Sentence.Should().Contain("gemini/Architecture: timeout");
    }

    [Fact]
    public async Task AFailedReviewer_CarriesTheClisOwnLastWords_NotJustItsExitCode()
    {
        // Twice at a real gate a reviewer failed as "codex/PlanCritique: exit 1" and the reason
        // was unrecoverable: the executor captured stderr and the summary — the only place a
        // person reads — dropped it. An exit code alone names no cure.
        var work = new List<ReviewerWork>
        {
            new(FakeCliInvocations.Invoke("codex", ["stderr-exit", "stream error: the frobnicator is out of widgets", "1"])),
        };

        var results = await new BoundedScheduler().RunAllAsync(work, _executor, TestContext.Current.CancellationToken);

        ReviewerSummaryFactory.From(results).Sentence
            .Should().Contain("exit 1").And.Contain("frobnicator is out of widgets");
    }

    [Fact]
    public async Task AFailedReviewerThatSaidNothing_SaysSoRatherThanLookingTruncated()
    {
        var work = new List<ReviewerWork> { new(FakeCliInvocations.Invoke("codex", ["stderr-exit", "", "9"])) };

        var results = await new BoundedScheduler().RunAllAsync(work, _executor, TestContext.Current.CancellationToken);

        ReviewerSummaryFactory.From(results).Sentence.Should().Contain("said nothing on stderr");
    }

    [Fact]
    public async Task AFinishedReviewer_ReportsHowLongItRan()
    {
        // The audit trail's timings come from here; a zero would make every line say "0.0s".
        var seen = new List<ReviewerProgress>();
        var work = new List<ReviewerWork> { new(FakeCliInvocations.Invoke("gemini", ["sleep", "300"])) };

        await new BoundedScheduler().RunAllAsync(
            work, _executor, TestContext.Current.CancellationToken, p => { lock (seen) { seen.Add(p); } });

        seen.Should().Contain(p => p.Status == "running" && p.Elapsed == TimeSpan.Zero);
        seen.Single(p => p.Status is "done" or "failed").Elapsed
            .Should().BeGreaterThan(TimeSpan.FromMilliseconds(200));
    }
}
