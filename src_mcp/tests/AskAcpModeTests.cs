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

    /// <summary>Finding 4 of the security review: a deadline the clock cannot hold is a bad argument (65), never a throw past the child.</summary>
    [Fact]
    public async Task ADeadlineBeyondADay_IsRefused65_BeforeAnyLaunch()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")]);

        var code = await RunAsync(agent, deadlineSeconds: 2_000_000_000);

        code.Should().Be(65, Stderr);
        Stderr.Should().Contain("86400");
        agent.Entries().Should().BeEmpty("nothing was launched");
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

        agent.Received("session/prompt").First().GetProperty("params").GetProperty("prompt")[0].GetProperty("text").GetString()
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
    public async Task AQuotaBehindTheSignInPrefix_Exits75_NamedKimiQuotaSpent()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")],
            ""","promptError":"Authentication required: 429 You've reached your 5-hour usage limit. Your quota will reset …" """);

        var code = await RunAsync(agent, failures: new KimiAcpProfile().KnownFailures);

        code.Should().Be(75, Stderr);
        Report().GetProperty("failure").GetString().Should().Be("kimi_quota_spent");
        Stderr.Should().Contain("quota is used up").And.NotContain("kimi login", "signing in again does not clear a spent quota");
    }

    [Fact]
    public async Task ADetailKeptInTheErrorsData_IsPartOfTheReason_AndReadsAsAQuota()
    {
        // K5 of the code gate: only error.code and error.message were kept, so a bare "Authentication required" carrying the 429 in error.data read as S9's outcome
        // (a lost sign-in) for a spent quota. Object data is kept as compact JSON, string data as it is; both redacted and capped.
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")],
            ""","promptError":"Authentication required","promptErrorData":{"status":429,"detail":"You've reached your 5-hour usage limit. Your quota will reset at 14:00 UTC. Account org-0a1b2c3d4e5f6a7b."} """);

        var code = await RunAsync(agent, failures: new KimiAcpProfile().KnownFailures);

        code.Should().Be(75, Stderr);
        Report().GetProperty("failure").GetString().Should().Be("kimi_quota_spent");
        Report().GetProperty("reason").GetString().Should().Contain("Your quota will reset at 14:00 UTC.").And.NotContain("0a1b2c3d4e5f6a7b", "the id is redacted before the data is kept");
        Stderr.Should().NotContain("0a1b2c3d4e5f6a7b");
    }

    [Fact]
    public async Task AStringInTheErrorsData_IsKeptAsItIs()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")], ""","promptError":"model exploded","promptErrorData":"the gateway said: out of widgets" """);
        await RunAsync(agent);

        var reason = Report().GetProperty("reason").GetString();

        reason.Should().Contain("model exploded").And.Contain("the gateway said: out of widgets");
        reason.Should().NotContain("\"the gateway", "a string is kept as it is, not as JSON");
    }

    [Fact]
    public void TheReasonIsMatchedFirst_AndStderrOnlyWhenTheReasonMatchesNoRow()
    {
        // K4 of the code gate: reason and stderr used to be matched in ONE pass, so a bare sign-in reason plus any stderr line holding "Authentication required:"
        // landed in provider_error — no cure — whatever the agent had said about the turn itself.
        var kimi = new KimiAcpProfile();
        AcpOutcome Outcome(string reason, string stderr) =>
            new(AcpEnd.AgentFailed, string.Empty, reason, string.Empty, "s", AcpUsage.Unknown, [], [], [], stderr, 0);

        AskAcpMode.Diagnose(kimi, Outcome("Authentication required", "Authentication required: 503 from the gateway")).Name.Should().Be("not_signed_in", "the reason names a row; stderr is not asked");
        AskAcpMode.Diagnose(kimi, Outcome("the agent stopped", "Authentication required: 429 You've reached your 5-hour usage limit.")).Name
            .Should().Be("kimi_quota_spent", "a reason that matches no row falls through to stderr, where a CLI writes its own diagnosis");
        AskAcpMode.Diagnose(kimi, Outcome("the agent stopped", "nothing known")).Known.Should().BeFalse();
    }

    [Fact]
    public async Task AProviderErrorBehindTheSignInPrefix_Exits70_InTheVendorsWords_WithNoCure()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")],
            ""","promptError":"Authentication required: 503 Service Unavailable" """);

        var code = await RunAsync(agent, failures: new KimiAcpProfile().KnownFailures);

        code.Should().Be(70, Stderr);
        Report().GetProperty("failure").GetString().Should().Be("provider_error");
        Stderr.Should().Contain("provider_error: ").And.Contain("503 Service Unavailable").And.NotContain("kimi login");
        Stderr.Should().NotContain(" — ", "no cure follows the name: a known shape with nothing to do about it");
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

    // ---------- E4.3: the home, the refusal of the agent's request, the lock folder ----------

    /// <summary>The real `kimi` profile with its flags — and an executable that does not exist, so a test that is RED today fails on an assertion and never starts the person's own Kimi.</summary>
    private Task<int> KimiAsync(TempDir folder, params string[] flags) =>
        AskAcpMode.RunAsync(
            ["--ask-acp", "--profile", "kimi", "--model", "kimi-code/k3", "--cwd", folder.Path, "--executable", folder.At("no-such-kimi.exe"), .. flags],
            new StringReader("say hello"), _stderr.Add, _stdout);

    [Fact]
    public async Task ATurnWithoutAHome_IsRefused65()
    {
        using var folder = TempDir.For("testsToDelete-kimi-shim-");

        var code = await KimiAsync(folder);

        code.Should().Be(65, "a Kimi turn without --home would run on the person's own home and environment (D19): " + Stderr);
        Stderr.Should().Contain("--home");
        _stdout.ToString().Should().BeEmpty("a refused request prints no report — nothing was started");
    }

    [Fact]
    public async Task AHomeThatIsNotThere_AndAMissingPersonsBin_AreRefused65_EachNamed()
    {
        using var folder = TempDir.For("testsToDelete-kimi-shim-");
        Directory.CreateDirectory(folder.At("root"));

        var absent = await KimiAsync(folder, "--home", folder.At("nowhere"), "--persons-bin", folder.At("bin"));
        var noBin = await KimiAsync(folder, "--home", folder.At("root"));

        new[] { absent, noBin }.Should().AllBeEquivalentTo(65);
        Stderr.Should().Contain("nowhere").And.Contain("--persons-bin");
    }

    [Fact]
    public async Task AnAgentEnvironmentThatIsNotCoais_IsRefusedBeforeStart()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")]);
        const string sentence = "the os/ folder is missing — this agent request is refused (D19)";

        var code = await AskAcpMode.RunAsync(
            ["--ask-acp", "--profile", "fake", "--model", "m-1", "--cwd", agent.Cwd], new StringReader("say hello"), _stderr.Add, _stdout, [agent.Profile(agentRefusal: sentence)]);

        code.Should().Be(65, Stderr);
        Stderr.Should().Contain(sentence);
        Report().GetProperty("end").GetString().Should().Be("not_started");
        File.Exists(agent.Log).Should().BeFalse("the agent never started, so it never wrote its log");
    }

    [Fact]
    public async Task AKimiProfileWithoutLocks_IsRefused65()
    {
        using var folder = TempDir.For("testsToDelete-kimi-shim-");
        Directory.CreateDirectory(folder.At("root"));
        Directory.CreateDirectory(folder.At("bin"));

        var code = await KimiAsync(folder, "--home", folder.At("root"), "--persons-bin", folder.At("bin"));

        code.Should().Be(65, "no --locks: the hold that keeps two refreshers apart cannot be taken, and there is no warning-only fallback: " + Stderr);
        Stderr.Should().Contain("--locks");
    }

    [Fact]
    public async Task AFakeProfileWithoutLocks_RunsItsTurn_AndTakesNoHold()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")]);
        using var locks = TempDir.For("testsToDelete-kimi-locks-");
        using var rooted = CoaiMcp.Runners.Kimi.KimiCredentialsLock.RootFor(locks.Path);

        var code = await RunAsync(agent);

        code.Should().Be(0, Stderr);
        Directory.EnumerateFileSystemEntries(locks.Path).Should().BeEmpty("a stand-in profile has no sign-in to hold, so the lock folder is never even made into");
    }

    /// <summary>E4.3 review minor 1: a hold taken late leaves the turn under a second — the client would refuse "a deadline of 0s" — so the shim gives the hold back and answers Busy.</summary>
    [Theory]
    [InlineData(1300, 2)]
    [InlineData(1300, 1)]
    public async Task AHoldTakenLate_LeavesNoTime_SoItIsGivenBack_AndTheTurnIsBusy69(int takenAfterMs, int deadlineSeconds)
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")]);
        var hold = new RecordingHold();
        var profile = new LateHoldProfile(agent.Profile(), TimeSpan.FromMilliseconds(takenAfterMs), hold);

        var code = await AskAcpMode.RunAsync(
            ["--ask-acp", "--profile", "late", "--model", "m-1", "--cwd", agent.Cwd, "--deadline-seconds", deadlineSeconds.ToString()],
            new StringReader("say hello"), _stderr.Add, _stdout, [profile]);

        code.Should().Be(69, Stderr);
        Stderr.Should().Contain("still running").And.NotContain("outside 1..", "the client's own refusal of a spent deadline is not the sentence a person should read");
        hold.Disposed.Should().Be(1, "the hold taken late was given back, exactly once");
        agent.Entries().Should().BeEmpty("the agent was never started");
        Report().GetProperty("end").GetString().Should().Be("not_started");
    }

    /// <summary>E4.3 review minor 2: a hold answer this shim has no word for fails CLOSED (65), never as an answered turn (0).</summary>
    [Fact]
    public async Task AHoldAnswerNobodyKnows_FailsClosed65_NeverOk()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")]);
        var profile = new LateHoldProfile(agent.Profile(), TimeSpan.Zero, new RecordingHold(), answerNothing: true);

        var code = await AskAcpMode.RunAsync(
            ["--ask-acp", "--profile", "late", "--model", "m-1", "--cwd", agent.Cwd, "--deadline-seconds", "30"],
            new StringReader("say hello"), _stderr.Add, _stdout, [profile]);

        code.Should().Be(65, "a hold this shim cannot read is a request it will not make: " + Stderr);
        agent.Entries().Should().BeEmpty("the agent was never started");
    }

    private sealed class RecordingHold : IDisposable
    {
        private int _disposed;

        public int Disposed => _disposed;

        public void Dispose() => Interlocked.Increment(ref _disposed);
    }

    /// <summary>The fake profile with a hold that takes its time (or answers nothing the shim knows).</summary>
    private sealed class LateHoldProfile(IAcpProfile inner, TimeSpan takes, RecordingHold hold, bool answerNothing = false) : IAcpProfile
    {
        public string Name => "late";

        public string DefaultExecutable => inner.DefaultExecutable;

        public IReadOnlyList<string> Arguments => inner.Arguments;

        public string ModelOptionId => inner.ModelOptionId;

        public IReadOnlyList<string> ExcludedNames => inner.ExcludedNames;

        public IReadOnlyList<AcpKnownFailure> KnownFailures => inner.KnownFailures;

        public AcpHolding Hold(AcpAsk ask, TimeSpan within)
        {
            Thread.Sleep(takes);

            return answerNothing ? null! : new AcpHolding.Held(hold);
        }
    }

    [Fact]
    public void EveryExitCodeIsDistinct_AndNoneIs64()
    {
        int[] codes = [AskAcpMode.Ok, AskAcpMode.BadRequest, AskAcpMode.Unavailable, AskAcpMode.Failed, AskAcpMode.Spent, AskAcpMode.Protocol, AskAcpMode.Refused];

        codes.Should().OnlyHaveUniqueItems().And.NotContain(64, "64 means 'this binary has never heard of that mode' (.agents/PROJECT.md)");
    }
}
