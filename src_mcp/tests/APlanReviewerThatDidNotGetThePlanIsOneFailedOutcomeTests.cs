using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// S3 of <c>todo/PLAN_a_reviewer_is_not_sent_more_than_it_can_read.md</c> (D8, D9): a plan reviewer that answered only
/// <c>PLAN_INCOMPLETE &lt;id&gt;</c> is ONE failed outcome everywhere — the spending ledger, the live view, the audit, the
/// local stand-down and the round's own results — never <c>ok</c> in four places and a failure in the fifth.
/// </summary>
/// <remarks>
/// <para><b>The defect.</b> The fold ran on <c>results</c> AFTER the fan-out, while the progress callback had already shown
/// the reviewer as finished, written its ledger line (<c>outcome: ok</c>), logged it as answered and — for the local
/// reviewer's stand-down (issue #485) — counted its token finding as one quiet remark. The spending record said a
/// reviewer answered that never saw the plan, and a cloud reviewer that answered ONLY the token could make the cloud look
/// quiet, so the one reviewer able to read the plan was never started.</para>
/// <para><b>What must be true.</b> The fold is applied once, at the scheduler's terminal point, so every reader sees the
/// settled outcome; the tokens it spent stay on the line (D9).</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class APlanReviewerThatDidNotGetThePlanIsOneFailedOutcomeTests : FakeCliRoundTests
{
    private const string Plan = "# PLAN — a thing\n\n## Build order\n\n1. one\n2. two\n\nthe very last line of the plan";

    private const string Spent = """{"input_tokens": 61900, "output_tokens": 71}""";

    private static readonly string Id = DocumentRules.ArtifactIdOf(Plan);

    private static readonly string OnlyTheToken =
        "{\"findings\": [{\"severity\": \"blocking\", \"category\": \"completeness\", \"file\": \"\", \"line\": 0, "
        + $"\"title\": \"PLAN_INCOMPLETE {Id}\", \"why\": \"the plan did not reach me\", \"fix\": \"send it whole\"}}]}}";

    /// <summary>The cloud reviewer reports what it spent beside its answer: codex reads the file after <c>-o</c>, stdout carries its usage.</summary>
    private static void CloudAnswersOnlyTheToken()
    {
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", OnlyTheToken);
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", Spent);
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "0");
    }

    private JsonElement[] Ledger() =>
        [.. File.ReadAllLines(Path.Combine(_data, "usage.jsonl")).Select(line => JsonDocument.Parse(line).RootElement)];

    private ReviewerState Persisted(string provider) =>
        new SessionStore(_data).Load(_repo, "feature")!.Rounds.Single().ReviewerStates.Single(s => s.Provider == provider);

    private PanelService ServiceLogging(ListSink sink, PanelSettings settings) =>
        new(settings, VaultKeys.None("no vault in tests"), default, _launcher,
            new Serilog.LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger(), Noticing.None);

    [Fact]
    public async Task AReviewerThatDidNotGetThePlan_IsWrittenToTheLedgerAsAFailure_WithItsTokens()
    {
        CloudAnswersOnlyTheToken();
        var service = ServiceFor(Defaults());
        await service.OpenAsync(_repo, "feature");

        var answer = Parse(await service.ReviewPlanAsync(_repo, "feature", Plan, TestContext.Current.CancellationToken));

        answer.GetProperty("reviewers").GetString().Should().Contain("0 of 1 reviewers answered", "the reply already read it as a failure");
        var line = Ledger().Should().ContainSingle("one reviewer, one turn, one launch").Subject;
        line.GetProperty("outcome").GetString().Should().StartWith(
            "unparseable: the plan did not reach this reviewer whole", "the spending record must not say the reviewer answered");
        line.GetProperty("tokensIn").GetInt64().Should().Be(61900, "what the reviewer spent is still spent (D9)");
        line.GetProperty("tokensOut").GetInt64().Should().Be(71);
    }

    [Fact]
    public async Task TheLiveViewAndTheAudit_ShowTheFoldedReviewerAsFailed()
    {
        CloudAnswersOnlyTheToken();
        var sink = new ListSink();
        var service = ServiceLogging(sink, Defaults());
        await service.OpenAsync(_repo, "feature");

        await service.ReviewPlanAsync(_repo, "feature", Plan, TestContext.Current.CancellationToken);

        var reviewer = Persisted("codex");
        reviewer.Status.Should().Be(ReviewerState.Failed, "the live view must not show a reviewer that never read the plan as done");
        reviewer.Note.Should().Contain("the plan did not reach this reviewer whole");
        sink.Lines.Should().Contain(line => line.Contains("codex/") && line.Contains("FAILED") && line.Contains("the plan did not reach this reviewer whole"));
        sink.Lines.Should().NotContain(line => line.Contains("codex/") && line.Contains(" answered in "), "the audit must not say it answered");
    }

    /// <summary>The same three guarantees one layer down, on the scheduler's own terminal point — what the round-level tests above stand on.</summary>
    [Fact]
    public async Task TheSchedulerReportsAndReturnsTheSettledOutcome_AndHandsThatOneToTheStandDownCount()
    {
        var title = PlanIncomplete.TitleOf(Plan);
        var work = new ReviewerWork(new ReviewerInvocation("codex", RoleCatalog.ArchitectureRole, new ProcessRequest("x", [], ".")));
        var standDown = StandDown.For([work]);
        var reported = new List<ReviewerProgress>();

        var results = await new BoundedScheduler().RunAllAsync(
            [work],
            new ReviewerExecutor(new AnswersWith(OnlyTheToken)),
            TestContext.Current.CancellationToken,
            reported.Add,
            standDown,
            settle: outcome => PlanIncomplete.Settle(Stage.PlanReview, title, outcome));

        var finished = reported.Single(p => p.Outcome is not null);
        finished.Status.Should().Be("failed");
        finished.Outcome.Should().BeOfType<ReviewerOutcome.Unparseable>().Which.Reason.Should().Contain("did not reach this reviewer whole");
        results.Single().Outcome.Should().BeSameAs(finished.Outcome, "ONE outcome: what was reported is what was returned");
        standDown.Quiet.Should().BeFalse("a reviewer that never saw the plan is a failed cloud row, not a quiet one");
    }

    /// <summary>A launcher whose every launch answers the same text on stdout — not FakeCli, which this class steers by environment.</summary>
    private sealed class AnswersWith(string stdout) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ProcessResult(0, stdout, string.Empty, false));
    }
}
