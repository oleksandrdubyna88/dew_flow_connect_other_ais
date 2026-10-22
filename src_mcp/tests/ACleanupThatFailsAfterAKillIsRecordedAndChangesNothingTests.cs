using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The executor's courtesy cleanup after a kill (<c>AbandonAsync</c>) stays best-effort — it must not change the round's outcome — but a cleanup that FAILED is
/// not silent: it is recorded through the executor's own note channel, the same <c>note</c> callback that reports an answer that could not be kept (E5 checkpoint 1,
/// finding F5; the repository rule is never to swallow an error silently). A cleanup that fails is exactly the case where work may still be running on the
/// company's subscription for an answer nobody will collect, so the person reading the round needs the sentence.
/// </summary>
public sealed class ACleanupThatFailsAfterAKillIsRecordedAndChangesNothingTests
{
    /// <summary>A bound on the WAIT of the test, never a decision: a correct run reaches none of it.</summary>
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);

    /// <summary>Ends as a launch killed on its own deadline, at once.</summary>
    private sealed class KilledOnItsDeadline : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true));
    }

    /// <summary>An adapter whose cleanup fails the way the test says.</summary>
    private sealed class CleanupThatFails(Func<CancellationToken, Task> cleanup) : CodexRuntime, IReviewerRuntime
    {
        public int Calls { get; private set; }

        async Task IReviewerRuntime.AbandonAsync(ReviewerInvocation invocation, CancellationToken ct)
        {
            Calls++;
            await cleanup(ct);
        }
    }

    private static ReviewerInvocation Invocation(CleanupThatFails adapter) =>
        FakeCliInvocations.Invoke("vendor", [], TimeSpan.FromSeconds(2)) with { Adapter = adapter };

    [Fact]
    public async Task ACleanupThatThrows_IsRecordedInTheNote_AndTheOutcomeIsStillTheTimeout()
    {
        var notes = new List<string>();
        var adapter = new CleanupThatFails(_ => throw new InvalidOperationException("the DELETE was refused"));
        var executor = new ReviewerExecutor(new KilledOnItsDeadline(), note: notes.Add);

        var outcome = await executor.RunAsync(Invocation(adapter), ct: TestContext.Current.CancellationToken).WaitAsync(Guard, TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.TimedOut>("the cleanup is best-effort: its failure never replaces the reviewer's own outcome");
        adapter.Calls.Should().Be(1);
        notes.Should().ContainSingle("the failed cleanup is recorded once, through the executor's one note channel")
            .Which.Should().Contain("cleanup").And.Contain("InvalidOperationException").And.Contain("the DELETE was refused");
    }

    [Fact]
    public async Task ACleanupThatRunsOutOfItsBudget_IsRecordedToo_AndTheOutcomeIsStillTheTimeout()
    {
        var notes = new List<string>();
        var adapter = new CleanupThatFails(ct => Task.Delay(Timeout.InfiniteTimeSpan, ct));
        var executor = new ReviewerExecutor(new KilledOnItsDeadline(), note: notes.Add) { AbandonBudget = TimeSpan.FromMilliseconds(100) };

        var outcome = await executor.RunAsync(Invocation(adapter), ct: TestContext.Current.CancellationToken).WaitAsync(Guard, TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.TimedOut>();
        notes.Should().ContainSingle().Which.Should().Contain("cleanup").And.Contain("100");
    }

    [Fact]
    public async Task ACleanupThatSucceeds_RecordsNothing()
    {
        var notes = new List<string>();
        var adapter = new CleanupThatFails(_ => Task.CompletedTask);
        var executor = new ReviewerExecutor(new KilledOnItsDeadline(), note: notes.Add);

        var outcome = await executor.RunAsync(Invocation(adapter), ct: TestContext.Current.CancellationToken).WaitAsync(Guard, TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.TimedOut>();
        notes.Should().BeEmpty("a cleanup that worked has nothing to say");
    }

    [Fact]
    public async Task ACleanupThatThrows_WithNoNoteChannel_StillLeavesTheOutcomeAlone()
    {
        var adapter = new CleanupThatFails(_ => throw new InvalidOperationException("boom"));
        var executor = new ReviewerExecutor(new KilledOnItsDeadline());

        var outcome = await executor.RunAsync(Invocation(adapter), ct: TestContext.Current.CancellationToken).WaitAsync(Guard, TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.TimedOut>();
    }
}
