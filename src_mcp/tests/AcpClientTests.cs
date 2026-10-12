using System.Text.Json;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The ACP client against a scripted agent in a real second process (S4 of <c>PLAN_vibe_and_kimi_runtimes.md</c>,
/// the <c>AcpClientTests</c> bullet of §5): what it offers, the order it asks in, what it refuses, what it serves,
/// what it reads back — and that its child's tree is gone on every way out.
/// </summary>
/// <remarks>
/// Every assertion about the wire reads the agent's LOG of the lines that crossed the pipe (<see cref="FakeAcpAgent"/>),
/// never the client's own record of what it meant to send. The liveness checks ask the operating system by pid
/// (<see cref="ProcessTracking.StartedAt"/>): a pid that answers no start time is gone.
/// </remarks>
public sealed class AcpClientTests
{
    private static Task<AcpOutcome> RunAsync(FakeAcpAgent agent, AcpAsk? ask = null, CancellationToken ct = default) =>
        new AcpClient(agent.Profile(), new FolderReadBoundary(agent.Cwd, KimiAcpProfile.Exclusions)).AskAsync(ask ?? agent.Ask(), ct);

    /// <summary>Whether the pid names no live process — polled briefly, since a terminated process can still be listed for a moment.</summary>
    private static bool Gone(int pid)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (ProcessTracking.StartedAt(pid) is not null && DateTime.UtcNow < until)
        {
            Thread.Sleep(50);
        }

        return ProcessTracking.StartedAt(pid) is null;
    }

    [Fact]
    public async Task TheAnswerIsTheChunksJoined_AndTheUsageIsTheLastUpdate()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("Hel"), FakeAcpAgent.Usage(10, 100), FakeAcpAgent.Chunk("lo"), FakeAcpAgent.Usage(20553, 262144)]);

        var outcome = await RunAsync(agent);

        outcome.End.Should().Be(AcpEnd.Answered, outcome.Reason);
        outcome.Answer.Should().Be("Hello");
        outcome.StopReason.Should().Be("end_turn");
        outcome.SessionId.Should().Be("fake-session");
        outcome.Usage.Should().Be(new AcpUsage(20553, 262144, true), "the last usage_update is the session's context, no split (D15)");
    }

    [Fact]
    public async Task WithoutAUsageUpdate_TheUsageIsUnknown_NeverZero()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")]);

        var outcome = await RunAsync(agent);

        outcome.Usage.Captured.Should().BeFalse();
        outcome.Usage.Should().Be(AcpUsage.Unknown);
    }

    [Fact]
    public async Task InitializeOffersRead_AndNeverWriteOrTerminal()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")]);

        await RunAsync(agent);

        var initialize = agent.Received("initialize").Should().ContainSingle().Subject.GetProperty("params");
        initialize.GetProperty("protocolVersion").GetInt32().Should().Be(1);
        initialize.GetProperty("clientCapabilities").GetRawText().Should().Be(
            """{"fs":{"readTextFile":true,"writeTextFile":false},"terminal":false}""",
            "the read offer is mandatory (without it Kimi read an outside file itself, RESULTS §2.2) and nothing else is offered");
    }

    [Fact]
    public async Task TheModelIsSetBeforeThePrompt_AndNewSendsTheCwdAndNoMcpServer()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")]);

        await RunAsync(agent, agent.Ask(model: "kimi-code/k3-256k"));

        agent.ReceivedMethods().Should().StartWith(["initialize", "session/new", "session/set_config_option", "session/prompt"],
            "a session comes up on the vendor's default model, which failed 401 on this plan — the model is set first (RESULTS §2)");
        agent.ReceivedMethods().Should().EndWith("session/cancel", "the teardown's graceful step runs on the normal path too");
        var created = agent.Received("session/new").Single().GetProperty("params");
        created.GetProperty("cwd").GetString().Should().Be(agent.Cwd);
        created.GetProperty("mcpServers").GetArrayLength().Should().Be(0, "no MCP server starts (the #514 rule, D8b)");
        var option = agent.Received("session/set_config_option").Single().GetProperty("params");
        option.GetProperty("sessionId").GetString().Should().Be("fake-session");
        option.GetProperty("configId").GetString().Should().Be("model");
        option.GetProperty("value").GetString().Should().Be("kimi-code/k3-256k");
        var prompt = agent.Received("session/prompt").Single().GetProperty("params").GetProperty("prompt");
        prompt[0].GetProperty("type").GetString().Should().Be("text");
        prompt[0].GetProperty("text").GetString().Should().Be("say hello");
    }

    [Fact]
    public async Task EveryPermissionRequestIsRefused_WhateverTheOptions_AndWhicheverToolAsked()
    {
        using var agent = new FakeAcpAgent(
        [
            FakeAcpAgent.Permission("Write pwned.txt", "edit", ("a", "allow_always"), ("b", "allow_once"), ("c", "reject_once"), ("d", "reject_always")),
            FakeAcpAgent.Permission("Bash echo hi > shell.txt", "execute", ("a", "allow_once"), ("d", "reject_always")),
            FakeAcpAgent.Permission("AskUserQuestion", "other", ("a", "allow_once")),
            FakeAcpAgent.Chunk("done"),
        ]);

        var outcome = await RunAsync(agent);

        outcome.End.Should().Be(AcpEnd.Answered, outcome.Reason);
        var replies = agent.RepliesTo("session/request_permission");
        replies.Should().HaveCount(3);
        replies[0].GetProperty("result").GetProperty("outcome").GetRawText().Should().Be("""{"outcome":"selected","optionId":"c"}""",
            "reject_once is chosen over every allow, wherever it sits in the list");
        replies[1].GetProperty("result").GetProperty("outcome").GetRawText().Should().Be("""{"outcome":"selected","optionId":"d"}""",
            "any reject_* option when there is no reject_once");
        replies[2].GetProperty("result").GetProperty("outcome").GetRawText().Should().Be("""{"outcome":"cancelled"}""",
            "with no reject option at all the request is cancelled — never an allow");
        outcome.Refusals.Should().Equal(
            new AcpRefusal("Write pwned.txt", "edit", "reject_once"),
            new AcpRefusal("Bash echo hi > shell.txt", "execute", "reject_always"),
            new AcpRefusal("AskUserQuestion", "other", "cancelled"));
    }

    [Fact]
    public async Task AReadInsideTheFolderIsServed_OutsideAndExcludedNamesAreRefused()
    {
        using var agent = new FakeAcpAgent([]);
        File.WriteAllText(agent.Inside("marker.txt"), "EGRET-5");
        Directory.CreateDirectory(agent.Inside("deep", ".kimi-code"));
        File.WriteAllText(agent.Inside("deep", ".kimi-code", "config.toml"), "secret config");
        File.WriteAllText(agent.Inside("AGENTS.md"), "instructions");
        File.WriteAllText(agent.Outside("secret.txt"), "LYNX-404");
        using var scripted = new FakeAcpAgent(
        [
            FakeAcpAgent.Read(agent.Inside("marker.txt")),
            FakeAcpAgent.Read(agent.Outside("secret.txt")),
            FakeAcpAgent.Read(agent.Inside("AGENTS.md")),
            FakeAcpAgent.Read(agent.Inside("deep", ".kimi-code", "config.toml")),
            FakeAcpAgent.Chunk("read"),
        ]);

        var outcome = await new AcpClient(scripted.Profile(), new FolderReadBoundary(agent.Cwd, KimiAcpProfile.Exclusions))
            .AskAsync(scripted.Ask() with { Cwd = agent.Cwd });

        outcome.End.Should().Be(AcpEnd.Answered, outcome.Reason);
        var replies = scripted.RepliesTo("fs/read_text_file");
        replies.Should().HaveCount(4);
        replies[0].GetProperty("result").GetProperty("content").GetString().Should().Be("EGRET-5");
        replies[1].GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32602);
        replies[1].GetProperty("error").GetProperty("message").GetString().Should().Contain("outside the working folder").And.NotContain("LYNX-404");
        replies[2].GetProperty("error").GetProperty("message").GetString().Should().Contain("AGENTS.md");
        replies[3].GetProperty("error").GetProperty("message").GetString().Should().Contain(".kimi-code", "an excluded name is refused at any depth");
        outcome.Reads.Select(r => r.Served).Should().Equal(true, false, false, false);
    }

    [Fact]
    public async Task AReadWindowIsServedByLineAndLimit()
    {
        using var agent = new FakeAcpAgent([]);
        File.WriteAllText(agent.Inside("lines.txt"), "a\nb\nc\nd");
        using var scripted = new FakeAcpAgent([FakeAcpAgent.Read(agent.Inside("lines.txt"), 2, 2), FakeAcpAgent.Chunk("x")]);

        await new AcpClient(scripted.Profile(), new FolderReadBoundary(agent.Cwd, [])).AskAsync(scripted.Ask() with { Cwd = agent.Cwd });

        scripted.RepliesTo("fs/read_text_file").Single().GetProperty("result").GetProperty("content").GetString().Should().Be("b\nc");
    }

    [Fact]
    public async Task AnyOtherClientRequestIsAnsweredWithAnError_AndNothingIsWritten()
    {
        using var agent = new FakeAcpAgent(
        [
            FakeAcpAgent.Request("fs/write_text_file", $$"""{"sessionId":"fake-session","path":{{FakeAcpAgent.Quote(Path.Combine(Path.GetTempPath(), "coai-acp-should-not-exist.txt"))}},"content":"x"}"""),
            FakeAcpAgent.Request("terminal/create", """{"sessionId":"fake-session","command":"echo"}"""),
            FakeAcpAgent.Chunk("x"),
        ]);

        var outcome = await RunAsync(agent);

        outcome.End.Should().Be(AcpEnd.Answered, outcome.Reason);
        foreach (var method in new[] { "fs/write_text_file", "terminal/create" })
        {
            var reply = agent.RepliesTo(method).Should().ContainSingle().Subject;
            reply.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32601, $"{method} was not offered in initialize");
        }

        File.Exists(Path.Combine(Path.GetTempPath(), "coai-acp-should-not-exist.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task ToolCallsAreTraced_WithTheirLastStatus()
    {
        using var agent = new FakeAcpAgent(
        [
            FakeAcpAgent.ToolCall("t1", "Read marker.txt", "read", "pending"),
            FakeAcpAgent.ToolCallUpdate("t1", "completed"),
            FakeAcpAgent.ToolCall("t2", "Grep", "search", "in_progress"),
            FakeAcpAgent.Chunk("x"),
        ]);

        var outcome = await RunAsync(agent);

        outcome.ToolCalls.Should().Equal(new AcpToolCall("t1", "Read marker.txt", "read", "completed"), new AcpToolCall("t2", "Grep", "search", "in_progress"));
    }

    [Fact]
    public async Task AStrayLineOnStdoutIsIgnored()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Stray("Kimi Code CLI starting…"), FakeAcpAgent.Chunk("x")]);

        var outcome = await RunAsync(agent);

        outcome.End.Should().Be(AcpEnd.Answered, outcome.Reason);
    }

    [Fact]
    public async Task AnErrorToARequestOfOurs_IsTheAgentsFailure_AndThePromptIsNeverSent()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")],
            ""","refuseModel":"Authentication required: 401 Your current subscription does not have access to kimi-for-coding-highspeed. Upgrade to higher-tier Kimi Code plans." """);

        var outcome = await RunAsync(agent);

        outcome.End.Should().Be(AcpEnd.AgentFailed);
        outcome.Reason.Should().Contain("does not have access to").And.Contain("-32000");
        agent.Received("session/prompt").Should().BeEmpty("a model the plan lacks is found out before any prompt is spent");
        Gone(outcome.AgentPid).Should().BeTrue();
    }

    [Theory]
    [InlineData("refusal")]
    [InlineData("max_tokens")]
    [InlineData("")]
    public async Task AStopReasonOtherThanEndTurn_IsNotAnAnswer(string stopReason)
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("partial")],
            stopReason.Length > 0 ? $$""","stopReason":{{FakeAcpAgent.Quote(stopReason)}}""" : ""","omitStopReason":true""");

        var outcome = await RunAsync(agent);

        outcome.End.Should().Be(AcpEnd.AgentFailed);
        outcome.Answer.Should().Be("partial", "what arrived is kept for the record even though it is not the answer");
        outcome.Reason.Should().Contain(stopReason.Length > 0 ? stopReason : "no stopReason");
    }

    [Fact]
    public async Task AnExecutableThatIsNotThere_IsNotStarted()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")]);

        var outcome = await RunAsync(agent, agent.Ask(executable: Path.Combine(agent.Cwd, "no-such-cli")));

        outcome.End.Should().Be(AcpEnd.NotStarted);
        outcome.Reason.Should().Contain("no-such-cli");
        outcome.AgentPid.Should().Be(0);
    }

    [Fact]
    public async Task ANormalEndLeavesNoChild()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")]);

        var outcome = await RunAsync(agent);

        outcome.End.Should().Be(AcpEnd.Answered, outcome.Reason);
        outcome.AgentPid.Should().BePositive();
        Gone(outcome.AgentPid).Should().BeTrue("the agent is ended on the way out of an answered turn too");
    }

    [Fact]
    public async Task TheDeadlineEndsAHungAgent_AndItsGrandchild()
    {
        using var agent = new FakeAcpAgent(string.Empty, a => a.Grandchild(), _ => FakeAcpAgent.Hang);

        var outcome = await RunAsync(agent, agent.Ask(deadline: TimeSpan.FromSeconds(3)));
        var grandchild = agent.GrandchildPid();

        outcome.End.Should().Be(AcpEnd.Deadline, outcome.Reason);
        outcome.Reason.Should().Contain("3s");
        Gone(outcome.AgentPid).Should().BeTrue("the agent was killed by its pid");
        Gone(grandchild).Should().BeTrue("the tree kill reaches the heartbeat the agent spawned");
    }

    [Fact]
    public async Task APrematureEofIsAProtocolEnd_AndTheGrandchildIsGoneToo()
    {
        using var agent = new FakeAcpAgent(string.Empty, _ => FakeAcpAgent.Chunk("half"), a => a.Grandchild(), _ => FakeAcpAgent.Exit(0));

        var outcome = await RunAsync(agent);
        var grandchild = agent.GrandchildPid();

        outcome.End.Should().Be(AcpEnd.Protocol, outcome.Reason);
        // The grandchild inherited the stdout pipe, so no EOF ever comes: the exit itself is what ends the turn.
        outcome.Reason.Should().Contain("before answering").And.Contain("exited with code 0");
        outcome.Answer.Should().Be("half");
        Gone(outcome.AgentPid).Should().BeTrue();
        Gone(grandchild).Should().BeTrue("a grandchild orphaned by the agent's own exit is still the client's to end");
    }

    [Fact]
    public async Task TheCallersCancellationEndsTheTurn_AndLeavesNoChild()
    {
        using var agent = new FakeAcpAgent(string.Empty, a => a.Grandchild(), _ => FakeAcpAgent.Hang);
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));

        var outcome = await RunAsync(agent, agent.Ask(deadline: TimeSpan.FromMinutes(5)), caller.Token);
        var grandchild = agent.GrandchildPid();

        outcome.End.Should().Be(AcpEnd.Cancelled, "the caller's token fired, not the deadline — the two lead to opposite conclusions above");
        Gone(outcome.AgentPid).Should().BeTrue();
        Gone(grandchild).Should().BeTrue();
    }

    [Theory]
    [InlineData("""[{"optionId":"a","kind":"allow_once"},{"optionId":"r","kind":"reject_once"}]""", "r")]
    [InlineData("""[{"optionId":"ra","kind":"reject_always"},{"optionId":"r","kind":"reject_once"}]""", "r")]
    [InlineData("""[{"optionId":"a","kind":"allow_once"},{"optionId":"ra","kind":"reject_always"}]""", "ra")]
    [InlineData("""[{"optionId":"a","kind":"allow_once"},{"optionId":"aa","kind":"allow_always"}]""", "")]
    [InlineData("""[]""", "")]
    public void ThePermissionChoice_IsRejectOnce_ThenAnyReject_ThenNone(string options, string chosen)
    {
        using var parsed = JsonDocument.Parse(options);

        AcpSession.Permission(parsed.RootElement).Should().Be(chosen);
    }
}
