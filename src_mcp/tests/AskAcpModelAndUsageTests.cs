using System.Text.Json;
using CoaiMcp.Acp;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The shim's two E4.3 duties about the SESSION (S3.4–S3.10 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>): the asked model is checked against
/// what the session announced before any prompt is spent (D10), and after an answered turn <c>/usage</c> is asked and its totals reported (D15,
/// M-K2) — without the second prompt ever changing the first one's end, answer or exit.
/// </summary>
public sealed class AskAcpModelAndUsageTests
{
    private const string UsageText = "Context: 20004 / 262144 tokens (8%) Session total: 19973 input, 31 output";

    private readonly List<string> _stderr = [];
    private readonly StringWriter _stdout = new();

    private string Stderr => string.Join("\n", _stderr);

    private JsonElement Report() => JsonDocument.Parse(_stdout.ToString().Trim()).RootElement.Clone();

    private Task<int> RunAsync(FakeAcpAgent agent, string model = "m-1") =>
        AskAcpMode.RunAsync(
            ["--ask-acp", "--profile", "fake", "--model", model, "--cwd", agent.Cwd, "--deadline-seconds", "30"],
            new StringReader("say hello"), _stderr.Add, _stdout, [agent.Profile()]);

    private static IEnumerable<string> PromptTexts(FakeAcpAgent agent) =>
        agent.Received("session/prompt").Select(prompt => prompt.GetProperty("params").GetProperty("prompt")[0].GetProperty("text").GetString() ?? string.Empty);

    // ---------- the model, checked in the session (D10) ----------

    [Fact]
    public async Task AModelTheSessionDoesNotOffer_Exits65WithTheList()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")], FakeAcpAgent.Models("kimi-code/k3-256k", "kimi-code/k3"));

        var code = await RunAsync(agent, "kimi-code/kimi-for-coding");

        code.Should().Be(65, "the request named a value the agent will not take — 65, never 64, never 70: " + Stderr);
        Stderr.Should().Contain("kimi-code/k3-256k").And.Contain("kimi-code/k3").And.Contain("kimi-code/kimi-for-coding");
        agent.Received("session/set_config_option").Should().BeEmpty("a model the session does not offer is found out before it is set");
        agent.Received("session/prompt").Should().BeEmpty("and before any prompt is spent");
        Report().GetProperty("end").GetString().Should().Be("model_not_offered");
    }

    [Fact]
    public async Task AnOfferedModel_IsSetAndAsked()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")], FakeAcpAgent.Models("kimi-code/k3-256k", "kimi-code/k3"));

        var code = await RunAsync(agent, "kimi-code/k3");

        code.Should().Be(0, Stderr);
        agent.ReceivedMethods().Should().ContainInOrder("session/new", "session/set_config_option", "session/prompt");
        agent.Received("session/set_config_option").Single().GetProperty("params").GetProperty("value").GetString().Should().Be("kimi-code/k3");
    }

    [Fact]
    public async Task ASessionThatOffersNoModelOption_Exits65()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")], FakeAcpAgent.Models());

        var code = await RunAsync(agent, "kimi-code/k3");

        code.Should().Be(65, Stderr);
        Stderr.Should().Contain("offers no 'model' option");
        agent.Received("session/prompt").Should().BeEmpty();
    }

    // ---------- /usage after an answer (D15, M-K2) ----------

    [Fact]
    public async Task AfterAnAnswer_UsageIsTheSessionTotal()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("OK")], FakeAcpAgent.UsageReply(UsageText));

        var code = await RunAsync(agent);

        code.Should().Be(0, Stderr);
        var report = Report();
        var usage = report.GetProperty("usage");
        usage.GetProperty("tokensIn").GetInt64().Should().Be(19973);
        usage.GetProperty("tokensOut").GetInt64().Should().Be(31);
        usage.GetProperty("notCaptured").GetBoolean().Should().BeFalse();
        report.GetProperty("answer").GetString().Should().Be("OK", "the /usage reply is the agent’s own command output, never part of the answer");
        PromptTexts(agent).Should().Equal("say hello", "/usage");
    }

    [Fact]
    public async Task AnUnparseableUsageReply_IsNotCaptured_NeverZeroAsANumber()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("OK")], FakeAcpAgent.UsageReply("Context: 0 / 262144 tokens (0%) Session total: no LLM calls yet"));

        var code = await RunAsync(agent);

        code.Should().Be(0, Stderr);
        var usage = Report().GetProperty("usage");
        usage.GetProperty("notCaptured").GetBoolean().Should().BeTrue();
        usage.GetProperty("tokensIn").GetInt64().Should().Be(0);
        usage.GetProperty("tokensOut").GetInt64().Should().Be(0);
        Report().GetProperty("answer").GetString().Should().Be("OK");
    }

    [Fact]
    public async Task AUsagePromptThatFails_LeavesTheAnswerAlone()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("OK")], ""","promptErrorOnUsage":"usage is not available" """);

        var code = await RunAsync(agent);

        code.Should().Be(0, "the second prompt can never change the first one’s exit: " + Stderr);
        var report = Report();
        report.GetProperty("end").GetString().Should().Be("answered");
        report.GetProperty("answer").GetString().Should().Be("OK");
        report.GetProperty("usage").GetProperty("notCaptured").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task NoUsageIsAskedAfterANonAnswer()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("partial")], ""","stopReason":"max_tokens" """ + FakeAcpAgent.UsageReply(UsageText));

        var code = await RunAsync(agent);

        code.Should().Be(70, Stderr);
        PromptTexts(agent).Should().Equal(["say hello"], "a turn that did not end in an answer sends nothing more");
        Report().GetProperty("usage").GetProperty("notCaptured").GetBoolean().Should().BeTrue();
    }
}
