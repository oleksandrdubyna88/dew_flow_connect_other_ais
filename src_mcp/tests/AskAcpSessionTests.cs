using System.Text.Json;
using CoaiMcp.Acp;
using CoaiMcp.Runners.Acp;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The shim's load path (E5.2 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>): <c>coai-mcp --ask-acp … --session &lt;id&gt;</c> continues a
/// session with <c>session/load</c> on the same working folder, checks the model against what the LOAD announced, keeps the replayed history
/// out of the answer, and ends <c>session_lost</c> (exit 70) when the agent no longer holds the session — in-process, over the scripted agent.
/// </summary>
/// <remarks>
/// What the agent does on a load is what M-K16 measured on Kimi 2.1.1 (<c>research/RESULTS_kimi_confinement.md</c> §33): the load's result
/// announces the <c>model</c> option, the history is replayed as <c>session/update</c> BEFORE the load is answered, and an unknown id is a
/// JSON-RPC error. Nothing here starts a real <c>kimi</c>.
/// </remarks>
public sealed class AskAcpSessionTests
{
    private readonly List<string> _stderr = [];
    private readonly StringWriter _stdout = new();

    private string Stderr => string.Join("\n", _stderr);

    private JsonElement Report() => JsonDocument.Parse(_stdout.ToString().Trim()).RootElement.Clone();

    private Task<int> RunAsync(FakeAcpAgent agent, IReadOnlyList<string> extra, string model = "m-1", IReadOnlyList<AcpKnownFailure>? failures = null) =>
        AskAcpMode.RunAsync(
            ["--ask-acp", "--profile", "fake", "--model", model, "--cwd", agent.Cwd, "--deadline-seconds", "30", .. extra],
            new StringReader("say hello"), _stderr.Add, _stdout, [agent.Profile(failures: failures)]);

    private Task<int> LoadAsync(FakeAcpAgent agent, string session = "s-1", string model = "m-1", IReadOnlyList<AcpKnownFailure>? failures = null) =>
        RunAsync(agent, ["--session", session], model, failures);

    // ---------- S5.2.1 ----------

    [Fact]
    public async Task ALoadTurn_SendsSessionLoad_WithTheSameCwd_AndNoMcpServer()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("NEW")]);

        var code = await LoadAsync(agent);

        agent.ReceivedMethods().Should().Contain("session/load", "a --session turn continues that session — it never starts a new one");
        agent.ReceivedMethods().Should().NotContain("session/new");
        var load = agent.Received("session/load").Single().GetProperty("params");
        load.GetProperty("sessionId").GetString().Should().Be("s-1");
        load.GetProperty("cwd").GetString().Should().Be(Path.GetFullPath(agent.Cwd), "a loaded session resolves paths against the folder it was made in (M-K5)");
        load.GetProperty("mcpServers").GetArrayLength().Should().Be(0);
        agent.ReceivedMethods().SkipWhile(method => method != "session/load").Skip(1).Take(2)
            .Should().Equal(["session/set_config_option", "session/prompt"], "the model is set before the prompt, as on a new session");
        code.Should().Be(0, Stderr);
        Report().GetProperty("sessionId").GetString().Should().Be("s-1", "the report names the session the turn continued");
    }

    // ---------- S5.2.2 ----------

    [Fact]
    public async Task AReplayedHistory_IsNeverTheAnswer()
    {
        using var agent = new FakeAcpAgent(
            [FakeAcpAgent.Chunk("NEW")],
            $$""","replay":[{{FakeAcpAgent.Chunk("OLD ANSWER")}},{{FakeAcpAgent.ToolCall("t-old", "Read old", "read", "completed")}}]""");

        var code = await LoadAsync(agent);

        code.Should().Be(0, Stderr);
        Report().GetProperty("answer").GetString().Should().Be("NEW", "Kimi replays the history before the load answers (RESULTS §33) — the answer is only what follows the prompt");
        Report().GetProperty("toolCalls").EnumerateArray().Select(call => call.GetProperty("id").GetString())
            .Should().NotContain("t-old", "a replayed tool call is history, not this turn's trace");
    }

    // ---------- S5.2.3 ----------

    [Fact]
    public async Task AnUnknownSession_IsSessionLost_Exit70_NothingPrompted()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("NEW")], ""","loadable":false""");

        var code = await LoadAsync(agent, "s-9");

        Report().GetProperty("end").GetString().Should().Be("session_lost");
        code.Should().Be(70, "the agent refused in its own words — never 69, which reads as 'ended before an answer': " + Stderr);
        Report().GetProperty("reason").GetString().Should().Contain("s-9");
        agent.ReceivedMethods().Should().NotContain("session/set_config_option").And.NotContain("session/prompt", "nothing is spent on a session the agent does not hold");
    }

    /// <summary>Kimi's own refusal quotes the id back (<c>Invalid params: Unknown sessionId: &lt;id&gt;</c>, RESULTS §33): the reason names it once, in coai's words, and the agent's echo of it is not repeated.</summary>
    [Fact]
    public async Task ASessionLostReason_NamesTheIdInItsOwnWords_NotTheAgentsEcho()
    {
        using var agent = new FakeAcpAgent(
            [FakeAcpAgent.Chunk("NEW")], ""","loadable":false,"loadError":{"code":-32602,"message":"Invalid params: Unknown sessionId: s-9"}""");

        var code = await LoadAsync(agent, "s-9");

        code.Should().Be(70, Stderr);
        var reason = Report().GetProperty("reason").GetString()!;
        reason.Should().Contain("s-9").And.Contain("-32602").And.NotContain("Unknown sessionId: s-9", "the agent's quoting of the id is data, not a sentence to repeat");
    }

    // ---------- S5.2.4 ----------

    [Fact]
    public async Task ALoadRefusedForTheSignIn_IsStillNotSignedIn()
    {
        using var agent = new FakeAcpAgent(
            [FakeAcpAgent.Chunk("NEW")], ""","loadable":false,"loadError":{"code":-32000,"message":"Authentication required"}""");

        var code = await LoadAsync(agent, failures: new KimiAcpProfile().KnownFailures);

        code.Should().Be(77, "a known failure names the refused load, and it wins: " + Stderr);
        Report().GetProperty("failure").GetString().Should().Be("not_signed_in");
        Report().GetProperty("end").GetString().Should().NotBe("session_lost", "a sign-in failure does not drop the conversation — the person signs in and the session is still there");
    }

    // ---------- S5.2.5 ----------

    [Theory]
    [InlineData("a b")]
    [InlineData("../x")]
    [InlineData("a\nb")]
    [InlineData("abc\n")]
    [InlineData("TOO-LONG")]
    [InlineData("")]
    public async Task AMalformedSessionId_Is65_BeforeAnyProcess(string id)
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("NEW")]);
        string[] session = id switch
        {
            "TOO-LONG" => ["--session", new string('a', 101)],
            "" => ["--session"], // the flag with no value: a continuation that lost its id must not become a new session
            _ => ["--session", id],
        };

        var code = await RunAsync(agent, session);

        code.Should().Be(65, Stderr);
        Stderr.Should().Contain("--session");
        File.Exists(agent.Log).Should().BeFalse("nothing was started for an id the shim will not put on a wire");
    }

    // ---------- the review of E5.2: the asked id on EVERY exit ----------

    /// <summary>
    /// A <c>--session</c> turn names its session on every exit the host can read a report from — the hold busy past the deadline, an agent that cannot be
    /// started, an agent that fails before the load — so the host never takes an empty handle for "the conversation is gone".
    /// </summary>
    [Theory]
    [InlineData("busy")]
    [InlineData("no-executable")]
    [InlineData("initialize")]
    public async Task EveryExitOfALoadTurn_NamesTheAskedSession(string how)
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("NEW")], how == "initialize" ? ""","initializeError":"boom" """ : string.Empty);
        IAcpProfile profile = how == "busy" ? new BusyProfile(agent.Profile()) : agent.Profile();
        string[] executable = how == "no-executable" ? ["--executable", agent.Inside("no-such-cli")] : [];

        var code = await AskAcpMode.RunAsync(
            ["--ask-acp", "--profile", profile.Name, "--model", "m-1", "--cwd", agent.Cwd, "--deadline-seconds", "30", "--session", "s-1", .. executable],
            new StringReader("say hello"), _stderr.Add, _stdout, [profile]);

        code.Should().NotBe(0, Stderr);
        Report().GetProperty("sessionId").GetString().Should().Be("s-1", $"the {how} exit of a --session turn still names the session it was asked to continue");
    }

    /// <summary>The fake profile with a sign-in held elsewhere past the deadline.</summary>
    private sealed class BusyProfile(IAcpProfile inner) : IAcpProfile
    {
        public string Name => "busy";

        public string DefaultExecutable => inner.DefaultExecutable;

        public IReadOnlyList<string> Arguments => inner.Arguments;

        public string ModelOptionId => inner.ModelOptionId;

        public IReadOnlyList<string> ExcludedNames => inner.ExcludedNames;

        public IReadOnlyList<AcpKnownFailure> KnownFailures => inner.KnownFailures;

        public AcpHolding Hold(AcpAsk ask, TimeSpan within) => new AcpHolding.Busy("another Kimi started on this sign-in is still running");
    }

    // ---------- the review of E5.2: a replayed tool call stays history (M1) ----------

    [Fact]
    public async Task ALateUpdateOfAReplayedToolCall_AddsNoTraceEntry()
    {
        using var agent = new FakeAcpAgent(
            [FakeAcpAgent.ToolCallUpdate("t-old", "completed"), FakeAcpAgent.Chunk("NEW")],
            $$""","replay":[{{FakeAcpAgent.ToolCall("t-old", "Read old", "read", "pending")}}]""");

        var code = await LoadAsync(agent);

        code.Should().Be(0, Stderr);
        Report().GetProperty("toolCalls").GetArrayLength().Should().Be(0, "an update of a tool call the replay announced belongs to the history, not to this turn's trace");
    }

    // ---------- the review of E5.2: the id cannot change the diagnosis (M2) ----------

    [Fact]
    public async Task AShortSessionId_DoesNotGarbleTheAgentsWords_SoTheKnownFailureStillWins()
    {
        using var agent = new FakeAcpAgent(
            [FakeAcpAgent.Chunk("NEW")], ""","loadable":false,"loadError":{"code":-32000,"message":"Authentication required"}""");

        var code = await LoadAsync(agent, session: "a", failures: new KimiAcpProfile().KnownFailures);

        code.Should().Be(77, "a one-letter id must not be written over the letters of the agent's sentence: " + Stderr);
        Report().GetProperty("failure").GetString().Should().Be("not_signed_in");
    }

    [Fact]
    public async Task ASessionIdThatSpellsAKnownFailure_IsStillSessionLost()
    {
        using var agent = new FakeAcpAgent(
            [FakeAcpAgent.Chunk("NEW")], ""","loadable":false,"loadError":{"code":-32602,"message":"Invalid params: Unknown sessionId: quota-gone"}""");
        AcpKnownFailure[] failures = [new("quota-gone", "quota_gone", "wait", AcpFailureKind.Spent)];

        var code = await LoadAsync(agent, session: "quota-gone", failures: failures);

        code.Should().Be(70, "the id is coai's own word for the session, never the agent's failure: " + Stderr);
        Report().GetProperty("end").GetString().Should().Be("session_lost");
        Report().TryGetProperty("failure", out _).Should().BeFalse();
    }

    // ---------- S5.2.6 (E5.1 (a) took the first branch: the load announces the model, RESULTS §33) ----------

    [Fact]
    public async Task TheModelOnALoad_FollowsWhatTheLoadAnnounced()
    {
        using var agent = new FakeAcpAgent(
            [FakeAcpAgent.Chunk("NEW")], FakeAcpAgent.Models("kimi-code/k3") + ""","loadModels":["kimi-code/k3-256k"]""");

        var code = await LoadAsync(agent, model: "kimi-code/k3");

        code.Should().Be(65, "the LOAD's announcement is the one checked, and it does not offer the asked model: " + Stderr);
        Report().GetProperty("end").GetString().Should().Be("model_not_offered");
        agent.ReceivedMethods().Should().NotContain("session/set_config_option").And.NotContain("session/prompt");
    }

    // ---------- S5.2.7 ----------

    [Fact]
    public async Task ALoadedTurnsUsage_IsTheSessionTotal_AsKimiSaidIt()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("NEW")], FakeAcpAgent.UsageReply("Session total: 40004 input, 72 output"));

        var code = await LoadAsync(agent);

        code.Should().Be(0, Stderr);
        var usage = Report().GetProperty("usage");
        usage.GetProperty("tokensIn").GetInt64().Should().Be(40004, "the shim reports the session's total; the per-turn share is its parent's (E5.4)");
        usage.GetProperty("tokensOut").GetInt64().Should().Be(72);
        usage.GetProperty("notCaptured").GetBoolean().Should().BeFalse();
        Report().GetProperty("answer").GetString().Should().Be("NEW", "the /usage reply is not the answer on a load either");
    }

    // ---------- S5.2.8 ----------

    /// <summary>
    /// Decision 6, "nothing else moves": the lines the client sends on a turn WITHOUT <c>--session</c>, recorded before E5.2 was written
    /// (the working folder spelled <c>&lt;cwd&gt;</c>) — literal on purpose, so a change of the new-session path is a red here.
    /// </summary>
    private const string WithoutSessionGolden = """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":1,"clientCapabilities":{"fs":{"readTextFile":true,"writeTextFile":false},"terminal":false}}}
        {"jsonrpc":"2.0","id":2,"method":"session/new","params":{"cwd":"<cwd>","mcpServers":[]}}
        {"jsonrpc":"2.0","id":3,"method":"session/set_config_option","params":{"sessionId":"fake-session","configId":"model","value":"m-1"}}
        {"jsonrpc":"2.0","id":4,"method":"session/prompt","params":{"sessionId":"fake-session","prompt":[{"type":"text","text":"say hello"}]}}
        {"jsonrpc":"2.0","id":5,"method":"session/prompt","params":{"sessionId":"fake-session","prompt":[{"type":"text","text":"/usage"}]}}
        {"jsonrpc":"2.0","method":"session/cancel","params":{"sessionId":"fake-session"}}
        """;

    [Fact]
    public async Task WithoutSession_TheTurnIsByteForByteToday()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")]);
        var cwd = JsonSerializer.Serialize(Path.GetFullPath(agent.Cwd))[1..^1];

        var code = await RunAsync(agent, []);

        code.Should().Be(0, Stderr);
        var sent = agent.Entries().Where(entry => entry.Dir == "in").Select(entry => entry.Msg.GetRawText().Replace(cwd, "<cwd>", StringComparison.Ordinal));
        string.Join("\n", sent).Should().Be(WithoutSessionGolden.ReplaceLineEndings("\n"));
    }

    // ---------- S5.2.9 ----------

    [Fact]
    public async Task ALoadedTurn_RefusesEveryPermission_AndServesOnlyItsCwd()
    {
        const string outsideCanary = "OUTSIDE-CANARY-5f1c";
        const string agentsCanary = "AGENTS-CANARY-9a2e";
        using var agent = new FakeAcpAgent(
            string.Empty,
            _ => FakeAcpAgent.Permission("Write pwned.txt", "edit", ("a", "allow_once"), ("r", "reject_once")),
            a => FakeAcpAgent.ReadEchoed(a.Outside("outside.txt")),
            a => FakeAcpAgent.ReadEchoed(a.Inside("AGENTS.md")),
            _ => FakeAcpAgent.Chunk("NEW"));
        File.WriteAllText(agent.Outside("outside.txt"), outsideCanary);
        File.WriteAllText(agent.Inside("AGENTS.md"), agentsCanary);

        var code = await LoadAsync(agent);

        code.Should().Be(0, Stderr);
        var report = Report();
        report.GetProperty("refusals")[0].GetProperty("option").GetString().Should().Be("reject_once", "every permission is refused on a loaded session too (M-K5)");
        report.GetProperty("reads").EnumerateArray().Select(read => read.GetProperty("served").GetBoolean()).Should().Equal([false, false]);
        report.GetRawText().Should().NotContain(outsideCanary).And.NotContain(agentsCanary, "a refused read is not quoted anywhere");
        agent.ReceivedMethods().Should().Contain("session/load");
    }

    // ---------- S5.2.11 ----------

    [Fact]
    public void TheHelpNamesSession() =>
        Program.HelpText.Should().Contain("[--session <id>]");

    // ---------- S5.2.12 (decision 9) ----------

    [Fact]
    public void EveryEnd_HasItsOwnWordAndExit_NoneFallsToADefaultArm()
    {
        var ends = Enum.GetValues<AcpEnd>();

        AskAcpMode.EndWord(AcpEnd.SessionLost).Should().Be(AcpWords.SessionLostWord, "an end with no arm of its own is printed 'cancelled'");
        AskAcpMode.EndExit(AcpEnd.SessionLost).Should().Be(70, "an end with no arm of its own exits 69 — 'ended before an answer' — and the host would retry a session that is gone");
        ends.Select(AskAcpMode.EndWord).Should().OnlyHaveUniqueItems("each end has its own word");
        ends.Where(end => AskAcpMode.EndWord(end) == "cancelled").Should().Equal([AcpEnd.Cancelled]);
        ends.Where(end => AskAcpMode.EndExit(end) == AskAcpMode.Unavailable).Should().BeEquivalentTo([AcpEnd.NotStarted, AcpEnd.Deadline, AcpEnd.Cancelled]);
    }
}
