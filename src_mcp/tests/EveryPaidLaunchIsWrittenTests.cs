using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Every paid launch is written, and written whole — story 3.3 of <c>todo/PLAN_every_round_is_counted.md</c>, the
/// local half: one ledger line per launch of a reviewer turn, each saying what it was and sharing its turn's id, and a
/// vendor's reason kept whole in the ledger and the log.
/// </summary>
/// <remarks>
/// <para><b>What was wrong.</b> A ledger line was one terminal TURN. A repair, a follow-up and every step of the
/// rate-limit ladder were folded into the line of their turn (<c>ReviewerExecutor.AfterTheRepair</c>,
/// <c>first.Usage.Add(second.Usage)</c>; <c>BoundedScheduler.LadderAsync</c>), so how many retries, repairs and
/// follow-ups a week took could only be inferred from sums — the paid-path census in
/// <c>research/RESULTS_vendor_quota_gauges.md</c> §5.1 lists every fold. And a vendor's refusal reached the ledger and
/// the log through the summary's 160-character cut: agy's weekly line arrived as <c>…Resets in 110h12m4…</c>.</para>
/// <para><b>What must hold.</b> One line per launch; the lines of one turn share a non-empty turn id and name their
/// attempt; their tokens add up to EXACTLY what the folded line said, so no total anywhere moves; a split that would
/// not add up is never written; and the reason a vendor gave is the ledger's outcome and the log's reason, whole, to a
/// safety cap far past any sentence a vendor writes.</para>
/// <para>The launcher is faked IN PROCESS — the seam rule of the plan's test plan: the defect sits above
/// <see cref="IProcessLauncher"/>, in the executor, the ladder and the ledger, so the real ones run.</para>
/// </remarks>
public sealed class EveryPaidLaunchIsWrittenTests : IDisposable
{
    /// <summary>A vendor that answers prose and reports its usage beside it — billed, and malformed.</summary>
    private const string ProseWithUsage = """{"note":"prose, not findings","input_tokens":1000,"output_tokens":50}""";

    /// <summary>A clean review that reports its usage beside it.</summary>
    private const string CleanWithUsage = """{"findings": [], "input_tokens": 200, "output_tokens": 7}""";

    /// <summary>agy 1.3.3's refusal on stderr, verbatim but for the error id (RESULTS_vendor_quota_gauges.md §3.1).</summary>
    private const string AgyQuotaLine =
        "error: Individual quota reached. Please upgrade your subscription to increase your limits. Resets in 88h49m9s.";

    private const string AgyErrorLine =
        """AGY_ERROR: {"short_error":"RESOURCE_EXHAUSTED (code 429): Individual quota reached. Please upgrade your subscription to increase your limits. Resets in 88h49m9s.","status":"RESOURCE_EXHAUSTED","error_code":429,"code_kind":"http","retryable":true,"error_id":"<redacted>"}""";

    private readonly TempDir _dir = TempDir.For("coai-every-launch-");

    public void Dispose() => _dir.Dispose();

    private static ReviewerInvocation Bare(string provider = "gemini") =>
        new(provider, RoleCatalog.ArchitectureRole, new ProcessRequest("vendor", [], ".") { Timeout = TimeSpan.FromMinutes(1) });

    private static ProcessResult Exited(int code, string stdOut, string stdErr = "") => new(code, stdOut, stdErr, TimedOut: false);

    private IReadOnlyList<UsageEntry> Recorded(ReviewerOutcome outcome, string under, string provider = "gemini")
    {
        var ledger = new UsageLedger(_dir.At(under));
        ledger.Record(Bare(provider), outcome, "m", "CodeReview", TimeSpan.FromSeconds(9));

        return [.. File.ReadAllLines(ledger.Path).Select(l => JsonSerializer.Deserialize(l, LedgerJsonContext.Default.UsageEntry)!)];
    }

    /// <summary>What the turn's ONE folded line used to say — every launch of the last turn, summed.</summary>
    private static (long In, long Out, long Cached, long Reasoning) Folded(ReviewerOutcome outcome) =>
        (outcome.LastTurnUsage.TokensIn, outcome.LastTurnUsage.TokensOut, outcome.LastTurnUsage.TokensCached, outcome.LastTurnUsage.TokensReasoning);

    private static (long In, long Out, long Cached, long Reasoning) Summed(IEnumerable<UsageEntry> lines) =>
        lines.Aggregate((In: 0L, Out: 0L, Cached: 0L, Reasoning: 0L),
            (sum, l) => (sum.In + l.TokensIn, sum.Out + l.TokensOut, sum.Cached + l.TokensCached, sum.Reasoning + l.TokensReasoning));

    private static void OneTurn(IReadOnlyList<UsageEntry> lines)
    {
        lines[0].Turn.Should().NotBeEmpty("every launch of a turn carries the turn's id");
        lines.Select(l => l.Turn).Distinct().Should().ContainSingle("the launches of one turn share one id");
    }

    // ---------- one line per launch ----------

    [Fact]
    public async Task ARepair_WritesTwoLinesSharingATurn()
    {
        var launcher = new Scripted(Exited(0, ProseWithUsage), Exited(0, CleanWithUsage));

        var outcome = await new ReviewerExecutor(launcher).RunAsync(Bare(), Bare(), TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Ok>().Which.Repaired.Should().BeTrue();
        var lines = Recorded(outcome, "repair");
        lines.Should().HaveCount(2, "the malformed first launch and its repair were two paid launches — the ledger folded them into one line");
        lines.Select(l => l.Attempt).Should().Equal(LaunchAttempts.First, LaunchAttempts.Repair);
        OneTurn(lines);
        lines[0].TokensIn.Should().Be(1000, "the first launch's own usage, on its own line");
        lines[0].Outcome.Should().StartWith("unparseable", "the first launch answered something that did not parse");
        lines[1].TokensIn.Should().Be(200);
        lines[1].Outcome.Should().Be("ok");
        Summed(lines).Should().Be(Folded(outcome), "the lines add up to exactly what the folded line said — no total moves");
    }

    [Fact]
    public async Task AFollowUp_WritesTwoLinesSharingATurn()
    {
        var first = new AntigravityRuntime().Build("Architecture", "review this", Path.GetTempPath(), "schema.json", Path.GetTempPath(),
            new ReviewerSettings("antigravity") { Timeout = TimeSpan.FromMinutes(1) });

        var outcome = await new ReviewerExecutor(new DeniedThenAnswered()).RunAsync(first, repair: null, TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Ok>("the continued conversation answered");
        var lines = Recorded(outcome, "follow-up", "antigravity");
        lines.Should().HaveCount(2, "the denied launch and its follow-up in the same conversation were two paid launches");
        lines.Select(l => l.Attempt).Should().Equal(LaunchAttempts.First, LaunchAttempts.FollowUp);
        OneTurn(lines);
        lines.Select(l => l.TokensIn).Should().Equal([100, 40], "each line carries what ITS launch reported (the cumulative difference is PLAN_the_reviewer_follow_up_is_billed_once's)");
        Summed(lines).Should().Be(Folded(outcome));
    }

    [Fact]
    public async Task ARetryAfterARateLimit_WritesItsOwnLine()
    {
        var launcher = new Scripted(Exited(1, string.Empty, "429 Too Many Requests"), Exited(0, CleanWithUsage));

        var results = await new BoundedScheduler(retryLadder: [TimeSpan.FromMilliseconds(1)])
            .RunAllAsync([new ReviewerWork(Bare())], new ReviewerExecutor(launcher), TestContext.Current.CancellationToken);

        var outcome = results.Single().Outcome;
        outcome.Should().BeOfType<ReviewerOutcome.Ok>("the ladder's second step answered");
        var lines = Recorded(outcome, "retry");
        lines.Should().HaveCount(2, "the refused launch and the retry are two launches — the ladder folded the first into the second's line");
        lines.Select(l => l.Attempt).Should().Equal(LaunchAttempts.First, LaunchAttempts.Retry);
        OneTurn(lines);
        lines[0].Outcome.Should().StartWith("rate limited");
        lines[0].TokensIn.Should().Be(0, "a refused launch reported nothing, which reads as nothing — never as the retry's tokens");
        Summed(lines).Should().Be(Folded(outcome));
    }

    [Fact]
    public async Task ARepairThatExitsNonZero_IsBilledOnce()
    {
        var launcher = new Scripted(Exited(0, ProseWithUsage), Exited(3, """{"input_tokens":200,"output_tokens":5}""", "the model fell over"));

        var outcome = await new ReviewerExecutor(launcher).RunAsync(Bare(), Bare(), TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.NonZeroExit>();
        outcome.LastTurnUsage.TokensIn.Should().Be(1200,
            "the malformed launch (1000) and the repair that exited non-zero (200), each ONCE — the repair's usage is its outcome's own");
        Recorded(outcome, "non-zero").Select(l => l.TokensIn).Should().Equal([1000, 200]);
    }

    [Fact]
    public void ASingleLaunch_IsOneLine_NamedFirst_WithATurn()
    {
        var lines = Recorded(new ReviewerOutcome.Ok(new NormalisedReview([], []), false, new Usage(300, 9, null)), "single");

        var line = lines.Should().ContainSingle().Subject;
        line.Attempt.Should().Be(LaunchAttempts.First);
        line.Turn.Should().NotBeEmpty();
    }

    [Fact]
    public void PartsThatDoNotAddUpToTheTurn_AreNeverWritten_TheTurnIsOneLineAsItWas()
    {
        // Two parts summing to 1000 on a turn whose own usage says 1300: a split would hand the ledger a total the
        // round, the audit and the panel do not have.
        var outcome = new ReviewerOutcome.Ok(new NormalisedReview([], []), true, new Usage(1300, 9, null))
        {
            Launches =
            [
                new LaunchPart(LaunchAttempts.First, new Usage(700, 4, null), TimeSpan.FromSeconds(2), "unparseable: x"),
                new LaunchPart(LaunchAttempts.Repair, new Usage(300, 5, null), TimeSpan.FromSeconds(3), "ok"),
            ],
        };

        var lines = Recorded(outcome, "not-adding-up");

        var line = lines.Should().ContainSingle("a split that does not add up to the turn is not trusted").Subject;
        line.TokensIn.Should().Be(1300, "the turn's own usage, as the one line always said");
        line.Attempt.Should().Be(LaunchAttempts.First);
    }

    [Fact]
    public void AFeatureTurnsLaunches_AreSplitPerTurn_EachTurnItsOwnId()
    {
        var earlier = new TurnUsage(1, new Usage(1200, 57, null), TimeSpan.FromSeconds(4))
        {
            Launches =
            [
                new LaunchPart(LaunchAttempts.First, new Usage(1000, 50, null), TimeSpan.FromSeconds(1), "unparseable: prose"),
                new LaunchPart(LaunchAttempts.Repair, new Usage(200, 7, null), TimeSpan.FromSeconds(3), "ok"),
            ],
        };
        var outcome = new ReviewerOutcome.Ok(new NormalisedReview([], []), false, new Usage(10, 1, null), Turns: 2) { EarlierTurns = [earlier] };

        var lines = Recorded(outcome, "turns");

        lines.Select(l => l.Attempt).Should().Equal(LaunchAttempts.First, LaunchAttempts.Repair, LaunchAttempts.First);
        lines.Select(l => l.Outcome).Should().Equal(lines[0].Outcome, "ok", "ok");
        lines[0].Turn.Should().Be(lines[1].Turn, "turn 1's two launches share turn 1's id").And.NotBe(lines[2].Turn, "turn 2 is another turn");
        lines.Sum(l => l.TokensIn).Should().Be(outcome.TotalUsage.TokensIn, "every turn, every launch, once");
    }

    // ---------- reasons whole ----------

    [Fact]
    public async Task AVendorReason_IsNotClipped()
    {
        var launcher = new Scripted(Exited(3, string.Empty, AgyQuotaLine + "\n" + AgyErrorLine));

        var outcome = await new ReviewerExecutor(launcher).RunAsync(Bare("antigravity"), null, TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.RateLimited>();
        Recorded(outcome, "agy", "antigravity").Single().Outcome.Should().Contain(AgyErrorLine,
            "the ledger kept 160 characters of agy's refusal — which is how the plan lost its verbatim weekly line");

        var sink = new ListSink();
        new RoundAudit(new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger(), "CodeReview", 1)
            .Moved(new ReviewerProgress("antigravity", RoleCatalog.ArchitectureRole, ReviewerState.Failed, outcome, TimeSpan.FromSeconds(4)));
        sink.All.Should().Contain(AgyErrorLine, "the log's reason is the vendor's whole sentence too");
        ReviewerSummaryFactory.Describe(outcome).Should().EndWith("…", "the summary a person reads in a table cell may stay short");
    }

    [Fact]
    public async Task ALongExitReason_IsNotClipped()
    {
        var reason = "error: " + string.Join(' ', Enumerable.Repeat("the vendor explained itself at length", 16));
        var launcher = new Scripted(Exited(1, string.Empty, reason));

        var outcome = await new ReviewerExecutor(launcher).RunAsync(Bare("codex"), null, TestContext.Current.CancellationToken);

        reason.Length.Should().BeGreaterThan(400, "longer than the stderr tail's budget, which kept only its opening");
        Recorded(outcome, "long", "codex").Single().Outcome.Should().Contain(reason);
    }

    [Fact]
    public void AReasonIsKeptWhole_UpToTheSafetyCap_AndNoFurther()
    {
        var runaway = "error: " + new string('x', 9_000);

        var said = ReviewerSummaryFactory.DescribeWhole(new ReviewerOutcome.NonZeroExit(1, ReviewerExecutor.TailOf(runaway)));

        said.Should().Contain(runaway[..3_000], "far past any sentence a vendor writes");
        said.Length.Should().BeLessThan(4_100, "a stack trace on one line is bounded, never written to the ledger whole");
        said.Should().EndWith(Core.Notices.Redaction.Truncated, "the cut says it is one");
    }

    [Fact]
    public void AWholeReason_IsStillRedacted()
    {
        const string bearer = "abcdefghijklmnopqrstuvwxyz0123456789";
        var said = ReviewerSummaryFactory.DescribeWhole(new ReviewerOutcome.NonZeroExit(1,
            $"error: Authorization: Bearer {bearer} was refused by the gateway, which then explained at length why it refused it"));

        said.Should().NotContain(bearer, "a vendor's stderr is written down whole only after it is redacted")
            .And.Contain("which then explained at length why it refused it", "and redaction is not a cut");
    }

    /// <summary>Answers each launch with the next result in order.</summary>
    private sealed class Scripted(params ProcessResult[] results) : IProcessLauncher
    {
        private int _launched;

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            var at = Interlocked.Increment(ref _launched);

            return Task.FromResult(results[Math.Min(at, results.Length) - 1]);
        }
    }

    /// <summary>An agy conversation whose first launch was auto-denied, continued once and answered — each with usage.</summary>
    private sealed class DeniedThenAnswered : IProcessLauncher
    {
        private const string Denied =
            "jetski: no output produced — a tool required the \"command\" permission that headless mode cannot "
            + "prompt for, so it was auto-denied.";

        private static readonly string DeniedStream =
            """{"event":"init","conversation_id":"conv-7","init":{"model":"gemini-3.8-flash-low"}}""" + "\n"
            + """{"event":"result","result":{"conversation_id":"conv-7","status":"SUCCESS","response":"","usage":{"input_tokens":100,"output_tokens":5}}}""" + "\n";

        private static readonly string AnsweredStream =
            """{"event":"result","result":{"conversation_id":"conv-7","status":"SUCCESS","response":"{\"findings\":[]}","usage":{"input_tokens":40,"output_tokens":20}}}""" + "\n";

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(request.Arguments.Contains("--conversation")
                ? new ProcessResult(0, AnsweredStream, string.Empty, TimedOut: false)
                : new ProcessResult(0, DeniedStream, Denied, TimedOut: false));
    }
}
