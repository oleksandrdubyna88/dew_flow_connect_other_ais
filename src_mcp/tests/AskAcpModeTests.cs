using System.Text.Json;
using CoaiMcp.Acp;
using CoaiMcp.Runners.Acp;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>coai-mcp --ask-acp</c>, in-process, over the scripted agent: the exit-code contract — never 64 — and the one
/// JSON object on stdout the future <c>KimiRuntime</c> reads.
/// </summary>
public sealed class AskAcpModeTests
{
    private readonly List<string> _stderr = [];
    private readonly StringWriter _stdout = new();

    private string Stderr => string.Join("\n", _stderr);

    private JsonElement Report() => JsonDocument.Parse(_stdout.ToString().Trim()).RootElement.Clone();

    private Task<int> RunAsync(FakeAcpAgent agent, string prompt = "say hello", int deadlineSeconds = 30, IReadOnlyList<AcpKnownFailure>? failures = null, string profile = "fake") =>
        AskAcpMode.RunAsync(
            ["--ask-acp", "--profile", profile, "--model", "m-1", "--cwd", agent.Cwd, "--deadline-seconds", deadlineSeconds.ToString()],
            new StringReader(prompt), _stderr.Add, _stdout, [agent.Profile(failures: failures)]);

    [Fact]
    public void AskAcp_IsAMode_NotAnUnknownArgument()
    {
        Program.Classify(["--ask-acp"]).Should().NotBe(Program.Startup.Usage,
            "an unknown args[0] exits 64, which the executor would read as a vendor that could not start");
        Program.Classify(["--ask-acp"]).Should().NotBe(Program.Classify(["--ask-api"]));
    }

    [Fact]
    public async Task WithNoArguments_ItExits65_Never64()
    {
        var code = await AskAcpMode.RunAsync(["--ask-acp"], new StringReader("x"), _stderr.Add, _stdout);

        code.Should().Be(65, "64 means 'never heard of that mode' and would send the executor down a fallback");
        Stderr.Should().Contain("no profile").And.Contain("kimi", "the refusal names what this build knows");
    }

    [Fact]
    public async Task AnUnknownProfile_IsRefused_NamingTheOnesThisBuildKnows()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")]);

        var code = await RunAsync(agent, profile: "vibe");

        code.Should().Be(65);
        Stderr.Should().Contain("'vibe'").And.Contain("fake");
        agent.Entries().Should().BeEmpty("nothing was launched");
    }

    [Fact]
    public async Task WithoutACwd_OrAModel_ItIsRefusedBeforeTheAgentStarts()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")]);

        var noCwd = await AskAcpMode.RunAsync(["--ask-acp", "--profile", "fake", "--model", "m"], new StringReader("x"), _stderr.Add, _stdout, [agent.Profile()]);
        var badCwd = await AskAcpMode.RunAsync(["--ask-acp", "--profile", "fake", "--model", "m", "--cwd", agent.Outside("nowhere")], new StringReader("x"), _stderr.Add, _stdout, [agent.Profile()]);
        var noModel = await AskAcpMode.RunAsync(["--ask-acp", "--profile", "fake", "--cwd", agent.Cwd], new StringReader("x"), _stderr.Add, _stdout, [agent.Profile()]);

        new[] { noCwd, badCwd, noModel }.Should().AllBeEquivalentTo(65);
        Stderr.Should().Contain("--cwd").And.Contain("--model");
        agent.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task AnEmptyPromptIsRefused()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")]);

        var code = await RunAsync(agent, prompt: "  \n");

        code.Should().Be(65);
        Stderr.Should().Contain("empty");
    }

    [Fact]
    public async Task AnAnsweredTurn_Exits0_AndPrintsTheAnswerUsageAndTrace()
    {
        using var agent = new FakeAcpAgent(
        [
            FakeAcpAgent.ToolCall("t1", "Write x", "edit", "pending"),
            FakeAcpAgent.Permission("Write x", "edit", ("a", "allow_once"), ("r", "reject_once")),
            FakeAcpAgent.Chunk("Hello, "), FakeAcpAgent.Chunk("world"),
            FakeAcpAgent.Usage(20553, 262144),
        ]);

        var code = await RunAsync(agent);

        code.Should().Be(0, Stderr);
        var report = Report();
        report.GetProperty("answer").GetString().Should().Be("Hello, world");
        report.GetProperty("end").GetString().Should().Be("answered");
        report.GetProperty("stopReason").GetString().Should().Be("end_turn");
        report.GetProperty("sessionId").GetString().Should().Be("fake-session");
        var usage = report.GetProperty("usage");
        usage.GetProperty("tokensIn").GetInt64().Should().Be(0);
        usage.GetProperty("notCaptured").GetBoolean().Should().BeTrue("ACP reports context tokens with no input/output split (D15)");
        usage.GetProperty("contextUsed").GetInt64().Should().Be(20553);
        usage.GetProperty("contextSize").GetInt64().Should().Be(262144);
        report.GetProperty("refusals")[0].GetRawText().Should().Be("""{"tool":"Write x","kind":"edit","option":"reject_once"}""");
        report.GetProperty("toolCalls")[0].GetProperty("title").GetString().Should().Be("Write x");
        report.GetProperty("reads").GetArrayLength().Should().Be(0);
        report.TryGetProperty("failure", out _).Should().BeFalse("nothing was diagnosed");
    }

    [Fact]
    public async Task WithoutAUsageUpdate_TheReportCarriesNoContextNumbers()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")]);

        await RunAsync(agent);

        Report().GetProperty("usage").TryGetProperty("contextUsed", out _).Should().BeFalse("unknown is said by absence, never by a zero");
    }

    [Fact]
    public async Task ThePromptOnStdinReachesTheAgentWhole_ToItsEof()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")]);
        var prompt = "line one \"quoted\"\nline two with a backslash \\ and a tab\t\n# Turn 2 of 3\n";

        await RunAsync(agent, prompt: prompt);

        agent.Received("session/prompt").Single().GetProperty("params").GetProperty("prompt")[0].GetProperty("text").GetString()
            .Should().Be(prompt, "stdin EOF means the prompt is complete — never cancel");
    }

    [Fact]
    public async Task AKnownRefusalSentence_Exits77_AndNamesTheFailure()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")],
            ""","refuseModel":"Authentication required: 401 Your current subscription does not have access to kimi-for-coding-highspeed. Upgrade to higher-tier Kimi Code plans." """);

        var code = await RunAsync(agent, failures: new KimiAcpProfile().KnownFailures);

        code.Should().Be(77, Stderr);
        Report().GetProperty("failure").GetString().Should().Be("subscription_model");
        Report().GetProperty("end").GetString().Should().Be("agent_failed");
        Stderr.Should().Contain("subscription_model").And.Contain("kimi-code/k3-256k", "the cure is in the sentence");
    }

    [Fact]
    public async Task AKnownSpentSentence_Exits75()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")], ""","promptError":"429 insufficient balance" """);

        var code = await RunAsync(agent, failures: new KimiAcpProfile().KnownFailures);

        code.Should().Be(75, Stderr);
        Report().GetProperty("failure").GetString().Should().Be("balance_spent");
    }

    [Fact]
    public async Task TheAgentsOwnError_Exits70_WithItsWords()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")], ""","promptError":"model exploded" """);

        var code = await RunAsync(agent);

        code.Should().Be(70, Stderr);
        Stderr.Should().Contain("model exploded");
        Report().GetProperty("end").GetString().Should().Be("agent_failed");
    }

    [Fact]
    public async Task TheDeadline_Exits69_AndTheReportSaysDeadline()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Hang]);

        var code = await RunAsync(agent, deadlineSeconds: 2);

        code.Should().Be(69, Stderr);
        Stderr.Should().Contain("2s");
        Report().GetProperty("end").GetString().Should().Be("deadline");
    }

    [Fact]
    public async Task APrematureEof_Exits76_AndKeepsWhatArrived()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("half"), FakeAcpAgent.Exit(0)]);

        var code = await RunAsync(agent);

        code.Should().Be(76, Stderr);
        Stderr.Should().Contain("before answering");
        Report().GetProperty("end").GetString().Should().Be("protocol");
        Report().GetProperty("answer").GetString().Should().Be("half");
    }

    [Fact]
    public async Task AnExecutableThatCannotStart_Exits69()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")]);

        var code = await AskAcpMode.RunAsync(
            ["--ask-acp", "--profile", "fake", "--model", "m", "--cwd", agent.Cwd, "--executable", agent.Inside("no-such-cli")],
            new StringReader("x"), _stderr.Add, _stdout, [agent.Profile()]);

        code.Should().Be(69, Stderr);
        Report().GetProperty("end").GetString().Should().Be("not_started");
    }

    [Fact]
    public void EveryExitCodeIsDistinct_AndNoneIs64()
    {
        int[] codes = [AskAcpMode.Ok, AskAcpMode.BadRequest, AskAcpMode.Unavailable, AskAcpMode.Failed, AskAcpMode.Spent, AskAcpMode.Protocol, AskAcpMode.Refused];

        codes.Should().OnlyHaveUniqueItems().And.NotContain(64, "64 means 'this binary has never heard of that mode' (.agents/PROJECT.md)");
    }
}
