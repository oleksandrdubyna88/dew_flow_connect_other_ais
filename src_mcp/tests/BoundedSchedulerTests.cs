using Xunit;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;

namespace CoaiMcp.Tests;

public sealed class BoundedSchedulerTests
{
    private readonly ReviewerExecutor _executor = new(new ProcessLauncher());
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-sched-").FullName;

    /// <summary>Max simultaneous fake-CLI bodies, from the ticks each wrote at start and end.</summary>
    private static int MaxOverlap(string dir)
    {
        var intervals = Directory.GetFiles(dir, "*.start")
            .Select(start => (
                Start: long.Parse(File.ReadAllText(start)),
                End: long.Parse(File.ReadAllText(Path.ChangeExtension(start, ".end")))))
            .ToList();
        return intervals.Count == 0
            ? 0
            : intervals.Max(i => intervals.Count(o => o.Start < i.End && i.Start < o.End));
    }

    /// <summary>
    /// The cap is the invariant and is asserted hard. The "it is still a fan-out" half is a
    /// TIMING claim, and its body is long enough that process start-up cannot explain the result:
    /// at 800ms it failed once on a loaded CI runner where six `dotnet` launches serialised
    /// themselves, which measured the runner rather than the scheduler.
    /// </summary>
    [Fact]
    public async Task ConcurrentProcesses_NeverExceedTheGlobalCap()
    {
        var busyDir = Directory.CreateDirectory(Path.Combine(_dir, "global")).FullName;
        var work = Enumerable.Range(0, 6)
            .Select(i => new ReviewerWork(
                FakeCliInvocations.Invoke($"vendor{i % 3}", ["busy", busyDir, "3000"])))
            .ToList();

        await new BoundedScheduler(globalCap: 3, perProviderCap: 2)
            .RunAllAsync(work, _executor, TestContext.Current.CancellationToken);

        MaxOverlap(busyDir).Should().BeLessThanOrEqualTo(3, "the global semaphore is the machine's cap")
            .And.BeGreaterThanOrEqualTo(2, "the fan-out must still be a fan-out, not a serial queue");
    }

    [Fact]
    public async Task OneVendor_NeverHoldsMoreThanItsPerProviderCap()
    {
        var slowDir = Directory.CreateDirectory(Path.Combine(_dir, "slow")).FullName;
        var work = Enumerable.Range(0, 4)
            .Select(_ => new ReviewerWork(FakeCliInvocations.Invoke("slowvendor", ["busy", slowDir, "2000"])))
            .ToList();

        await new BoundedScheduler(globalCap: 4, perProviderCap: 2)
            .RunAllAsync(work, _executor, TestContext.Current.CancellationToken);

        MaxOverlap(slowDir).Should().BeLessThanOrEqualTo(2,
            "a rate limit is per vendor — a global cap alone would put every slot on one provider");
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
