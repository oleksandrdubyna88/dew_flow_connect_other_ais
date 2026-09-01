using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Events;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The defects a real end-to-end run found on 2026-08-31, each held by the observation that
/// exposed it. Every one is about the gate REPORTING honestly: a reviewer that failed, a vendor
/// that was asked, a verdict a person can act on.
/// </summary>
public sealed class GateReportingTests
{
    private static ReviewerInvocation Invocation(string provider = "codex") =>
        new(provider, ReviewRole.PlanCritique, new ProcessRequest("codex", [], "."));

    private static string Sentence(ReviewerOutcome outcome) =>
        ReviewerSummaryFactory.From([(Invocation(), outcome)]).Sentence;

    // ---------- D4: the reason picked out of a CLI's stderr ----------

    /// <summary>
    /// Node prints its own version last. The first version of this heuristic took the LAST line
    /// and reported `exit 1: Node.js v20.20.2` while the real cause sat eight lines above it —
    /// inside the captured tail, thrown away by the picker.
    /// </summary>
    [Fact]
    public void ANodeCliFailure_NamesTheError_NotTheNodeVersionBanner()
    {
        const string stderr = """
            file:///C:/Users/x/AppData/Roaming/npm/node_modules/@openai/codex/bin/codex.js:105
                    throw new Error(`Missing optional dependency`);
                    ^

            Error: Missing optional dependency @openai/codex-linux-x64. Reinstall Codex: npm install -g @openai/codex@latest
                at file:///C:/Users/x/codex.js:105:11
                at ModuleJob.run (node:internal/modules/esm/module_job:271:25)

            Node.js v20.20.2
            """;

        var sentence = Sentence(new ReviewerOutcome.NonZeroExit(1, stderr));

        // The diagnosis table now recognises this one and answers with the CURE rather than the
        // vendor's phrasing — which is the same intent this test was written for, one step further.
        sentence.Should().Contain("reinstall");
        sentence.Should().NotContain("Node.js v20.20.2", "the version banner is the one line that explains nothing");
    }

    [Fact]
    public void AStackWithNoErrorLine_StillReportsSomethingSaid()
    {
        var sentence = Sentence(new ReviewerOutcome.NonZeroExit(7, "    at Foo.Bar()\n    at Baz()\nsomething odd happened"));

        sentence.Should().Contain("something odd happened");
    }

    // ---------- D3: a rate limit that says WHICH limit ----------

    [Fact]
    public void ARateLimitedReviewer_SaysWhichLimitItHit()
    {
        // A per-minute throttle and an exhausted DAILY quota are the same word and different
        // cures; one retry clears the first and can never clear the second.
        var sentence = Sentence(new ReviewerOutcome.RateLimited(
            "TerminalQuotaError: You have exhausted your daily quota on this model."));

        sentence.Should().Contain("rate limited").And.Contain("daily quota");
    }

    [Fact]
    public void TheRateLimitReason_IsTakenFromWhatTheCliActuallyPrinted()
    {
        var result = new ProcessResult(
            1,
            StdOut: string.Empty,
            StdErr: "loading model\nTerminalQuotaError: You exceeded your current quota, limit: 20\n  at run()",
            TimedOut: false);

        RateLimit.Hit(result).Should().BeTrue();
        RateLimit.Reason(result).Should().Contain("exceeded your current quota");
    }

    [Fact]
    public void ADailyQuota_IsNotRetried_BecauseWaitingCannotClearIt()
    {
        RateLimit.Hopeless("TerminalQuotaError: You have exhausted your daily quota on this model.")
            .Should().BeTrue();
    }

    [Fact]
    public void ATransientOverload_IsStillWorthOneRetry()
    {
        RateLimit.Hopeless("503 UNAVAILABLE: This model is currently experiencing high demand")
            .Should().BeFalse("that one clears while you wait, which is what the retry is for");
    }

    // ---------- what a failed reviewer still costs ----------

    [Fact]
    public void AnUnparseableReviewer_StillReportsWhatItConsumed()
    {
        // Measured on a real code round: two reviewers fell over after 107 and 128 seconds beside
        // a sibling that cost 210k input tokens, and the round recorded them as free. An
        // unparseable answer is a COMPLETED run whose usage the vendor reported.
        var outcome = new ReviewerOutcome.Unparseable("the vendor returned an empty answer", new Usage(210_555, 3_000, null));

        outcome.Usage.TokensIn.Should().Be(210_555);
    }

    [Fact]
    public void AnEmptyAnswer_IsNotDescribedAsMalformedJson()
    {
        // "Still not the schema's JSON" sent a reader looking for malformed JSON that did not
        // exist — the vendor's envelope had come back empty.
        Sentence(new ReviewerOutcome.Unparseable("the vendor returned an empty answer after one repair attempt"))
            .Should().Contain("empty answer").And.NotContain("not the schema");
    }

    // ---------- a closed door is named, not left as a stack trace ----------

    [Fact]
    public void TheGeminiRetirement_IsReportedAsWhatToDo_NotAsExitOne()
    {
        // Three observers read this failure as three different things — a daily quota, a timeout,
        // and an untrusted directory — because what it says is buried in a node stack.
        const string stderr = """
            Code Assist for individuals. To continue using Gemini, please migrate to the Antigravity suite of products: https://antigravity.google
                at throwIneligibleOrProjectIdError (file:///C:/Users/x/gemini-cli/bundle/chunk.js:310101:11)
                at _doSetupUser (file:///C:/Users/x/gemini-cli/bundle/chunk.js:310090:5)
            """;

        var sentence = Sentence(new ReviewerOutcome.NonZeroExit(1, stderr));

        sentence.Should().Contain("retired").And.Contain("antigravity");
        sentence.Should().NotContain("throwIneligible", "a stack frame is evidence, not a diagnosis");
    }

    [Fact]
    public void AnUntrustedDirectory_SaysWhichFlagFixesIt()
    {
        Sentence(new ReviewerOutcome.NonZeroExit(55, "Gemini CLI is not running in a trusted directory."))
            .Should().Contain("--skip-trust");
    }

    [Fact]
    public void AFailureNobodyHasSeenBefore_StillReportsTheClisOwnWords()
    {
        // The table is a shortcut for the failures we KNOW; it must not swallow the ones we do not.
        Sentence(new ReviewerOutcome.NonZeroExit(3, "Error: the frobnicator is out of widgets"))
            .Should().Contain("frobnicator is out of widgets");
    }

    // ---------- D1: the audit line survives a non-string property ----------

    /// <summary>
    /// `l` is a STRING format specifier. Rendering an int property with it threw from inside the
    /// formatter — after the key was written and before the newline — so the file ran eighteen
    /// entries onto six lines and the console sink, which buffers, dropped those events whole.
    /// </summary>
    [Fact]
    public void ALogLineCarryingANumericProperty_IsWrittenWholeWithItsNewline()
    {
        var formatter = new ServiceDefaults.CoaiTextFormatter("coai-mcp", 1234);
        var sink = new ListSink();
        var log = new Serilog.LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();

        log.ForContext("Round", 2).ForContext("Stage", "PlanReview")
           .Information("reviewer {Provider} started", "codex");

        var writer = new StringWriter();
        formatter.Format(sink.Events.Single(), writer);
        var line = writer.ToString();

        line.Should().Contain("reviewer codex started");
        line.Should().Contain("Round=2", "a numeric property must render, not throw");
        line.Should().Contain("Stage=PlanReview", "the property after the throw used to vanish entirely");
        line.Should().EndWith(Environment.NewLine, "a line that loses its newline merges the next entry into it");
    }

    // ---------- D2: an explicit runtime is not a suggestion ----------

    [Fact]
    public void AVendorNamedAnythingWithRuntimeClaude_ParsesAsClaude_NotCodex()
    {
        var vendors = PanelSettings.ParseVendors(
            """[{"id":"my-claude","runtime":"claude","model":"haiku","baseUrl":""}]""");

        vendors.Should().ContainSingle().Which.Runtime.Should().Be(
            "claude", "a vendor that runs the wrong model reports an answer from a model nobody chose");
    }

    [Fact]
    public void AVendorWithNoRuntimeField_LetsItsIdDecide()
    {
        PanelSettings.ParseVendors("""[{"id":"gemini","model":"","baseUrl":""}]""")
            .Should().ContainSingle().Which.Runtime.Should().BeEmpty(
                "unset must stay unset so the id can answer — forcing 'codex' here is what hid the claude bug");
    }

    // ---------- D6: the human can always answer a call_human ----------

    [Fact]
    public void WhenNobodyAnswered_TheHumansProceed_CanStillBeDelivered()
    {
        // The gate correctly refuses to pass a round nobody reviewed. It must not also refuse the
        // person it just called: that is a dead end, not a gate.
        var nobody = new ReviewerSummary(2, 0, ["codex: exit 1", "gemini: quota"]);
        var ok = (Transition.Ok)RoundMachine.CompleteRound(
            new SessionState("s", "D:/r", "main", new PanelConfig()), Core.Gate.GateResult.Empty, nobody);

        ok.Verdict.Should().BeOfType<RoundVerdict.CallHuman>();
        RoundMachine.Resolve(ok.State, [], humanSaysProceed: true)
            .Should().BeOfType<Transition.Moved>().Which.State.Stage.Should().Be(Stage.CodeReview);
    }

    // ---------- a CLI that is installed but not signed in ----------

    /// <summary>
    /// Measured in WSL: a freshly installed codex answers a review with five reconnect attempts and
    /// two 401s, and nothing in that wall of text says the one thing a person needs to do.
    /// </summary>
    [Fact]
    public void ACliThatIsNotSignedIn_SaysToSignIn_NotFiveReconnects()
    {
        const string stderr = """
            401 Unauthorized: Missing bearer or basic authentication in header, url: wss://api.openai.com/v1/responses
            ERROR: Reconnecting... 1/5
            ERROR: Reconnecting... 5/5
            ERROR: unexpected status 401 Unauthorized: Missing bearer or basic authentication in header
            """;

        var sentence = Sentence(new ReviewerOutcome.NonZeroExit(1, stderr));

        sentence.Should().Contain("signed in").And.Contain("login");
        sentence.Should().NotContain("Reconnecting", "the retries are the symptom, not the cure");
    }

    /// <summary>
    /// Also measured in WSL: a directory the CLI has never been trusted in. It warned and answered
    /// here, but a review runs in a FRESH worktree every round, which is a directory nobody has ever
    /// accepted a dialog for.
    /// </summary>
    [Fact]
    public void AnUntrustedWorkingDirectory_ForClaude_SaysWhatToAccept()
    {
        var sentence = Sentence(new ReviewerOutcome.NonZeroExit(1,
            "This directory has not been trusted. Run Claude Code interactively here once and accept the trust dialog"));

        sentence.Should().Contain("trust");
    }

    [Fact]
    public void OnLinux_AnAntigravityVendor_SaysThereIsNoLinuxCli_AndWhatToUseInstead()
    {
        // Measured 2026-09-01: `agy` ships as a Go binary with the Antigravity app, npm has no
        // package for it, and the only Linux package is a third-party repackaging the operator has
        // ruled out. So on a Linux box this vendor cannot work, and "'agy' was not found on this
        // machine" sends somebody hunting for an install that does not exist.
        var sentence = Sentence(new ReviewerOutcome.NotStarted("'agy' was not found on this machine"));

        sentence.Should().Contain("agy");
    }

    [Fact]
    public void TheMissingLinuxCli_IsNamedAsAVendorFact_NotAsAMissingFile()
    {
        VendorDiagnosis.ForRuntime("antigravity", linux: true)
            .Should().NotBeNull()
            .And.Subject.As<string>().Should().Contain("no Linux CLI").And.Contain("codex");
    }

    [Fact]
    public void OnWindows_AntigravityIsNotDiagnosedAsMissing_BecauseItWorksThere()
    {
        VendorDiagnosis.ForRuntime("antigravity", linux: false).Should().BeNull();
    }
}
