using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The executor's courtesy cleanup after a kill is REAL I/O, so its bound is the system clock's even when the executor reads a manual one
/// (E5 plan §3.14, rule 3 — the teardown exception of E4's clock contract, extended from "a real pipe" to "real cleanup I/O").
/// </summary>
/// <remarks>
/// <para>Why it must: <c>AbandonAsync</c> runs AFTER the outcome is decided, around the adapter's cancel of work that outlives the process
/// (for the remote runtime an HTTP DELETE on a client whose own timeout is ten seconds). A bound on a manual clock nobody advances would
/// never fire, and the reviewer's <c>TimedOut</c> would be parked behind a cleanup that waits for ever.</para>
/// <para>The clock here is FROZEN: nothing advances it. The adapter awaits its cleanup token and does nothing else, so the only thing that can end
/// the cleanup is the executor's own system-time bound — shortened to 200 ms through the internal <c>AbandonBudget</c> so the test does not wait
/// the production ten seconds. If the bound moved onto the executor's clock this test would not finish; it fails under its guard, with a
/// sentence that names what it waited for. The scan has the matching allow-list entry
/// (<c>EveryBudgetInAClockedClassReadsItsClockTests</c>).</para>
/// </remarks>
public sealed class TheCleanupAfterAKillStaysOnTheSystemClockTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A bound on the WAIT of the test, never a decision: a correct run reaches none of it.</summary>
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);

    /// <summary>Ends as a launch killed on its own deadline, at once.</summary>
    private sealed class KilledOnItsDeadline : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true));
    }

    /// <summary>An adapter whose cleanup waits on the token it is given, and says what ended the wait.</summary>
    private sealed class CleanupThatWaits : CodexRuntime, IReviewerRuntime
    {
        private readonly TaskCompletionSource<bool> _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes with <c>true</c> when the wait was ended by the token being cancelled.</summary>
        public Task<bool> Cancelled => _ended.Task;

        async Task IReviewerRuntime.AbandonAsync(ReviewerInvocation invocation, CancellationToken ct)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                _ended.TrySetResult(ct.IsCancellationRequested);
                throw;
            }
        }
    }

    [Fact]
    public async Task WithTheManualClockFrozen_ACleanupAwaitingItsToken_IsStillCancelled_AndTheTimeoutSurvives()
    {
        var clock = new ManualClock(Epoch);
        var adapter = new CleanupThatWaits();
        var invocation = FakeCliInvocations.Invoke("vendor", [], TimeSpan.FromSeconds(2)) with { Adapter = adapter };
        var executor = new ReviewerExecutor(new KilledOnItsDeadline(), clock: clock) { AbandonBudget = TimeSpan.FromMilliseconds(200) };

        var outcome = await ThatEndsAsync(executor.RunAsync(invocation, ct: TestContext.Current.CancellationToken), "the launch to end behind its cleanup");

        (await ThatEndsAsync(adapter.Cancelled, "the adapter's cleanup to be cancelled")).Should().BeTrue(
            "the cleanup was ended by its token, which the executor's system-time bound cancelled while the manual clock stood still");
        outcome.Should().BeOfType<ReviewerOutcome.TimedOut>("the launch's own outcome survives the cleanup that was cancelled");
        (clock.GetUtcNow() - Epoch).Should().Be(TimeSpan.Zero, "nothing advanced the clock: only the system bound can have ended the wait");
    }

    /// <summary>A bounded wait that says WHAT it waited for when it gives up: the guard is a bound on the test, never a decision.</summary>
    private static async Task<T> ThatEndsAsync<T>(Task<T> task, string what)
    {
        try
        {
            return await task.WaitAsync(Guard, TestContext.Current.CancellationToken);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"gave up after {Guard.TotalSeconds:0} s waiting for {what}: the cleanup's bound is not on the system clock");
        }
    }
}
