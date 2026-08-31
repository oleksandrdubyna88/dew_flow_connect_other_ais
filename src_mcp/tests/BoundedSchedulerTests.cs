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
}
