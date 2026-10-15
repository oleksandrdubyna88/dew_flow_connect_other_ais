using System.Diagnostics;
using System.Text.Json;
using CoaiMcp.Runners.Acp;
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

    private static AcpClient ClientFor(FakeAcpAgent agent, TimeSpan? usageBound = null) =>
        new(agent.Profile(), new FolderReadBoundary(agent.Cwd, KimiAcpProfile.Exclusions), usageBound ?? AcpClient.DefaultUsageBound);

    [Fact]
    public async Task TheTokens_AreTheUsageRepliesTotals()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("OK")], FakeAcpAgent.UsageReply(UsageText));

        var outcome = await ClientFor(agent).AskAsync(agent.Ask(), TestContext.Current.CancellationToken);

        outcome.End.Should().Be(AcpEnd.Answered, outcome.Reason);
        outcome.Answer.Should().Be("OK");
        outcome.Tokens.Should().Be(new AcpTokens(19973, 31, true));
    }

    [Fact]
    public async Task AUsagePromptThatNeverAnswers_IsGivenUpOnAtItsBound_AndTheAnswerStands()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("OK")], ""","usageHang":true""");
        var asked = Stopwatch.StartNew();

        var outcome = await ClientFor(agent, TimeSpan.FromSeconds(1)).AskAsync(agent.Ask(), TestContext.Current.CancellationToken);

        outcome.End.Should().Be(AcpEnd.Answered, "the answer was given before /usage was asked: " + outcome.Reason);
        outcome.Answer.Should().Be("OK");
        outcome.Tokens.Should().Be(AcpTokens.Unknown);
        asked.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "the bound plus the teardown’s grace, not the turn’s 30 seconds");
    }

    [Fact]
    public async Task AUsagePromptThatOutlivesTheTurnsDeadline_ReturnsTheAnswerAtTheDeadline_NotAfterTheBound()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("OK")], ""","usageHang":true""");
        var asked = Stopwatch.StartNew();

        var outcome = await ClientFor(agent, TimeSpan.FromSeconds(60)).AskAsync(agent.Ask(deadline: TimeSpan.FromSeconds(2)), TestContext.Current.CancellationToken);

        outcome.End.Should().Be(AcpEnd.Answered, "the deadline struck while /usage was asked, and /usage never changes the end: " + outcome.Reason);
        outcome.Tokens.Captured.Should().BeFalse();
        asked.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(12), "the usage bound (60 s) is never longer than what is left of the turn");
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
