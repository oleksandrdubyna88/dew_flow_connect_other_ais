using System.Text.Json;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The client’s two E4.3 duties against a scripted agent in a real second process (S3.4, S3.9 and S3.13 of
/// <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>): the model found in the session’s own announcement, and <c>/usage</c> asked once, bounded,
/// after an answer — whose hang or failure never reaches the turn’s end.
/// </summary>
public sealed class AcpClientUsageTests
{
    private const string UsageText = "Context: 20004 / 262144 tokens (8%) Session total: 19,973 input, 31 output";

    private static AcpClient ClientFor(FakeAcpAgent agent, TimeSpan? usageBound = null, TimeProvider? clock = null) =>
        new(agent.Profile(), new FolderReadBoundary(agent.Cwd, KimiAcpProfile.Exclusions), usageBound ?? AcpClient.DefaultUsageBound, UnsupervisedTree.Instance, clock);

    [Fact]
    public async Task TheTokens_AreTheUsageRepliesTotals()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("OK")], FakeAcpAgent.UsageReply(UsageText));

        var outcome = await ClientFor(agent).AskAsync(agent.Ask(), TestContext.Current.CancellationToken);

        outcome.End.Should().Be(AcpEnd.Answered, outcome.Reason);
        outcome.Answer.Should().Be("OK");
        outcome.Tokens.Should().Be(new AcpTokens(19973, 31, true));
    }

    /// <summary>
    /// The bound is the CLOCK's: the test waits for both timers (the turn's 30 s deadline and the 1 s usage bound, armed once the answer is in)
    /// and moves the clock exactly the bound. The wall-time bound this test used to carry (<c>asked.Elapsed &lt; 10 s</c>) is replaced by the
    /// clock-side proof: the turn ended when the clock had moved the usage bound and no further, so the turn's own deadline was never reached.
    /// </summary>
    [Fact]
    public async Task AUsagePromptThatNeverAnswers_IsGivenUpOnAtItsBound_AndTheAnswerStands()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("OK")], ""","usageHang":true""");
        var clock = new ManualClock(ClockedAcpTurn.Start);
        var bound = TimeSpan.FromSeconds(1);

        var outcome = await ClockedAcpTurn.DrivenAsync(ClientFor(agent, bound, clock), agent.Ask(), ClockedAcpTurn.AdvanceOnceArmed(clock, 2, bound));

        outcome.End.Should().Be(AcpEnd.Answered, "the answer was given before /usage was asked: " + outcome.Reason);
        outcome.Answer.Should().Be("OK");
        outcome.Tokens.Should().Be(AcpTokens.Unknown);
        (clock.GetUtcNow() - ClockedAcpTurn.Start).Should().Be(bound, "the turn ended when the clock had moved the usage bound, not the turn's 30 seconds");
        AcpClientTests.Gone(outcome.AgentPid).Should().BeTrue("the agent is ended on the way out, on the system clock, with nobody advancing anything");
    }

    /// <summary>
    /// C5 / S12. The agent starts SLOWER than the turn's whole deadline (2.5 s against 2 s) — as a loaded CI machine does — and it no longer
    /// matters: the deadline is the clock's, and the clock moves only when the test moves it, after the answer is in and <c>/usage</c> is pending.
    /// The wall-time bound this test used to carry (<c>asked.Elapsed &lt; 12 s</c>) is replaced by the clock-side proof below.
    /// </summary>
    [Fact]
    public async Task AUsagePromptThatOutlivesTheTurnsDeadline_ReturnsTheAnswerAtTheDeadline_NotAfterTheBound()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("OK")], FakeAcpAgent.StartDelay(2500) + ""","usageHang":true""");
        var clock = new ManualClock(ClockedAcpTurn.Start);
        var deadline = TimeSpan.FromSeconds(2);

        var outcome = await ClockedAcpTurn.DrivenAsync(ClientFor(agent, TimeSpan.FromSeconds(60), clock), agent.Ask(deadline: deadline), ClockedAcpTurn.AdvanceOnceArmed(clock, 2, deadline));

        outcome.End.Should().Be(AcpEnd.Answered, "the deadline struck while /usage was asked, and /usage never changes the end: " + outcome.Reason);
        outcome.Answer.Should().Be("OK", "the answer received before the deadline is kept");
        outcome.Tokens.Captured.Should().BeFalse();
        (clock.GetUtcNow() - ClockedAcpTurn.Start).Should().Be(deadline, "the turn ended when the clock had moved exactly the deadline; the usage bound (60 s) was never reached");
        AcpClientTests.Gone(outcome.AgentPid).Should().BeTrue("the teardown runs on the system clock after the last advance, and the agent is gone");
    }

    [Fact]
    public async Task AModelTheSessionDoesNotOffer_EndsBeforeAnythingIsSet()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")], FakeAcpAgent.Models("a", "b"));

        var outcome = await ClientFor(agent).AskAsync(agent.Ask(model: "c"), TestContext.Current.CancellationToken);

        outcome.End.Should().Be(AcpEnd.ModelNotOffered);
        outcome.Reason.Should().Contain("a, b").And.Contain("'c'").And.Contain("'model'");
        agent.Received("session/set_config_option").Should().BeEmpty();
        agent.Received("session/prompt").Should().BeEmpty();
        outcome.AgentPid.Should().BeGreaterThan(0, "the agent did start — it announced its models — and was ended like any other");
    }

    [Fact]
    public async Task ARequestTheProfileRefuses_NeverStartsTheAgent()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")]);
        var client = new AcpClient(agent.Profile(agentRefusal: "this request is not coai’s"), new FolderReadBoundary(agent.Cwd, []));

        var outcome = await client.AskAsync(agent.Ask(), TestContext.Current.CancellationToken);

        outcome.End.Should().Be(AcpEnd.NotStarted);
        outcome.Reason.Should().Be("this request is not coai’s");
        File.Exists(agent.Log).Should().BeFalse();
    }

    // ---------- AnnouncedValues ----------

    [Theory]
    [InlineData("""{"configOptions":[{"id":"model","options":[{"value":"a","name":"A"},{"value":"b","name":"B"}]}]}""", "a,b")]
    [InlineData("""{"configOptions":[{"id":"mode","options":[{"value":"plan"}]},{"id":"model","options":[{"value":"a"}]}]}""", "a")]
    [InlineData("""{"configOptions":[{"id":"model","options":[{"name":"no value"},{"value":""},{"value":"a"}]}]}""", "a")]
    [InlineData("""{"configOptions":[{"id":"model"}]}""", "")]
    [InlineData("""{"configOptions":[]}""", "")]
    [InlineData("""{"configOptions":"nope"}""", "")]
    [InlineData("""{}""", "")]
    public void TheAnnouncedValues_AreTheModelOptionsValues_NothingElse(string sessionResult, string expected)
    {
        var values = AcpMessages.AnnouncedValues(JsonDocument.Parse(sessionResult).RootElement, "model");

        string.Join(",", values).Should().Be(expected);
    }
}
