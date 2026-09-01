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

        sentence.Should().Contain("Missing optional dependency");
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
}
