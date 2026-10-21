using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The arms of <see cref="AcpClient"/>'s clock that the two deadline tests (S12 in <c>AcpClientUsageTests</c>, C23 in <c>AcpClientTests</c>) do not
/// reach (<c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp_E4.md</c> §8 S2, risk consult §15 points 2 and 5): the caller's
/// cancellation against the deadline's, the timers a finished turn leaves behind, and a deadline of zero. Every one is on a manual clock that
/// nobody moves past what the test means.
/// </summary>
public sealed class AcpClientClockTests
{
    private static AcpClient ClientFor(FakeAcpAgent agent, ManualClock clock) =>
        new(agent.Profile(), new FolderReadBoundary(agent.Cwd, KimiAcpProfile.Exclusions), AcpClient.DefaultUsageBound, UnsupervisedTree.Instance, clock);

    /// <summary>
    /// Caller-first. The caller cancels while the prompt is outstanding and the deadline's timer is still armed: the end is the CALLER's
    /// (<c>Cancelled</c>, from <c>ct.IsCancellationRequested</c>), never <c>Deadline</c> — the clock did not move — and the agent still gets its
    /// <c>session/cancel</c> and is gone, on the system clock, with nobody advancing anything.
    /// </summary>
    [Fact]
    public async Task ACallerWhoCancelsFirst_EndsTheTurnAsCancelled_NeverAsTheDeadline_AndTheAgentIsStillEnded()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.AwaitCancel, FakeAcpAgent.Chunk("late")]);
        var clock = new ManualClock(ClockedAcpTurn.Start);

        var outcome = await ClockedAcpTurn.DrivenAsync(ClientFor(agent, clock), agent.Ask(deadline: TimeSpan.FromSeconds(5)), async caller =>
        {
            await clock.WhenTimersAsync(1);
            await agent.WhenReceivedAsync("session/prompt", TestContext.Current.CancellationToken);
            await caller.CancelAsync();
        });

        outcome.End.Should().Be(AcpEnd.Cancelled, outcome.Reason);
        clock.GetUtcNow().Should().Be(ClockedAcpTurn.Start, "the clock never moved, so nothing here is a deadline");
        agent.Received("session/cancel").Should().ContainSingle("the graceful step still reaches an agent that is listening");
        AcpClientTests.Gone(outcome.AgentPid).Should().BeTrue();
        clock.ActiveTimers.Should().Be(0, "the turn's deadline timer is disposed with the turn");
    }

    /// <summary>
    /// Dispose-before-deadline. A turn that ends by its own answer (<c>/usage</c> answered at once) leaves nothing armed: both timers —
    /// the deadline's and the usage bound's — are disposed by the turn, so a clock moved far past the deadline afterwards fires nothing.
    /// </summary>
    [Fact]
    public async Task ATurnThatEndsByItsOwnAnswer_LeavesNothingArmedOnTheClock()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("OK")], FakeAcpAgent.UsageReply("Context: 20004 / 262144 tokens (8%) Session total: 19,973 input, 31 output"));
        var clock = new ManualClock(ClockedAcpTurn.Start);

        var outcome = await ClockedAcpTurn.DrivenAsync(ClientFor(agent, clock), agent.Ask(deadline: TimeSpan.FromSeconds(2)), _ => Task.CompletedTask);

        outcome.End.Should().Be(AcpEnd.Answered, outcome.Reason);
        outcome.Tokens.Captured.Should().BeTrue();
        clock.ActiveTimers.Should().Be(0, "the deadline's and the usage bound's timers are both disposed with the turn");
        clock.Advance(TimeSpan.FromHours(1));
        clock.ActiveTimers.Should().Be(0, "nothing of the finished turn is left to fire");
    }

    /// <summary>
    /// Zero. A deadline of zero is refused before any start (<c>NotStarted</c>) — so the synchronous cancellation of a zero-delay timer source
    /// (plan §5.2) is never reached by the product: no agent was started and no timer was armed.
    /// </summary>
    [Fact]
    public async Task ADeadlineOfZero_IsRefusedBeforeAnythingStarts_AndArmsNoTimer()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("OK")]);
        var clock = new ManualClock(ClockedAcpTurn.Start);

        var outcome = await ClientFor(agent, clock).AskAsync(agent.Ask(deadline: TimeSpan.Zero), TestContext.Current.CancellationToken);

        outcome.End.Should().Be(AcpEnd.NotStarted);
        outcome.Reason.Should().Contain("outside 1..");
        File.Exists(agent.Log).Should().BeFalse("nothing was started");
        clock.ActiveTimers.Should().Be(0);
    }
}
