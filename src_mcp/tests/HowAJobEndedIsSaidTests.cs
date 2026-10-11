using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// How a job ended is said, not guessed — story 3.2 of <c>todo/PLAN_every_round_is_counted.md</c>, the half both
/// binaries share: a launch the CALLER's token killed is its own ending, <see cref="ReviewerOutcome.Cancelled"/>, and
/// never the vendor's timeout.
/// </summary>
/// <remarks>
/// <para><b>What was wrong.</b> <c>ProcessLauncher</c> has reported a kill by the caller's token as
/// <c>TimedOut: true, Cancelled: true</c> since the launcher's own code round asked for the distinction, and
/// <c>ReviewerExecutor.LaunchAsync</c> read only <c>TimedOut</c> — so a review the Team server's client withdrew after
/// 8.4 s was filed as the vendor's own timeout, with a free 0/0 beside it (the deployed server's own line,
/// <c>src_server/tests/fixtures/usage/timed-out-withdrawal.jsonl</c>). Every switch over the endings — the summary's
/// sentence, the notice map, a consultation's reading of a killed launch — then had no word for it either.</para>
/// <para><b>What must hold.</b> <c>Cancelled</c> is read BEFORE <c>TimedOut</c>, with <c>Usage.Unknown</c>; a real
/// deadline is still <c>TimedOut</c>; the remote adapter's <c>AbandonAsync</c> still runs on that branch, because it is
/// what DELETEs a withdrawn Team-server job and an early return without it leaves the job running on the company's
/// subscription; the ending has its own sentence and its own notice code; the ledger writes it as one line saying
/// <c>cancelled</c> with its usage not captured, a billed first launch kept beside it; and a consultation's killed
/// launch is still what the caller decided from the tokens.</para>
/// <para>The launcher is faked in process — the seam rule of the plan's test plan: the defect sits above
/// <see cref="IProcessLauncher"/>, in the executor and the switches over its outcome, so the real ones run. The Team
/// server's half — the end cause set at the store's transition and read from its terminal record — is
/// <c>src_server/tests/HowAJobEndedIsSaidTests.cs</c>.</para>
/// </remarks>
public sealed class HowAJobEndedIsSaidTests : IDisposable
{
    /// <summary>A vendor that answered prose and reported its usage beside it — billed, and malformed.</summary>
    private const string ProseWithUsage = """{"note":"prose, not findings","input_tokens":1000,"output_tokens":50}""";

    /// <summary>What the real launcher reports when the CALLER's token killed the tree: both flags.</summary>
    private static readonly ProcessResult Withdrawn = new(-1, string.Empty, string.Empty, TimedOut: true, Cancelled: true);

    /// <summary>What it reports when the launch ran out of its OWN budget: the one flag.</summary>
    private static readonly ProcessResult Deadline = new(-1, string.Empty, string.Empty, TimedOut: true);

    private readonly TempDir _dir = TempDir.For("coai-how-it-ended-");

    public void Dispose() => _dir.Dispose();

    private static ReviewerInvocation Bare(IReviewerRuntime? adapter = null) =>
        new("gemini", RoleCatalog.ArchitectureRole, new ProcessRequest("vendor", [], ".") { Timeout = TimeSpan.FromMinutes(1) },
            Adapter: adapter, JobFile: adapter is null ? string.Empty : "a.job");

    private IReadOnlyList<UsageEntry> Recorded(ReviewerOutcome outcome)
    {
        var ledger = new UsageLedger(_dir.Path);
        ledger.Record(Bare(), outcome, "m", "CodeReview", TimeSpan.FromSeconds(8.4));

        return [.. File.ReadAllLines(ledger.Path).Select(l => JsonSerializer.Deserialize(l, LedgerJsonContext.Default.UsageEntry)!)];
    }

    // ---------- the executor reads the launcher's flags in the right order ----------

    [Fact]
    public async Task AWithdrawnLaunch_IsItsOwnEnding_NotTheVendorsTimeout()
    {
        var launch = await new ReviewerExecutor(new ScriptedResults(Withdrawn)).LaunchAsync(Bare(), CancellationToken.None);

        launch.Terminal.Should().BeOfType<ReviewerOutcome.Cancelled>(
            "the launcher said the CALLER's token killed the tree, and the executor read only TimedOut — a withdrawal filed as the vendor's slowness");
        launch.Usage.NotCaptured.Should().BeTrue("a launch killed before it answered never reported — unknown, never a free 0/0");
        launch.Terminal!.TotalUsage.NotCaptured.Should().BeTrue("and the ending itself carries the same unknown");
    }

    [Fact]
    public async Task ARealDeadline_IsStillATimeout()
    {
        var launch = await new ReviewerExecutor(new ScriptedResults(Deadline)).LaunchAsync(Bare(), CancellationToken.None);

        launch.Terminal.Should().BeOfType<ReviewerOutcome.TimedOut>(
            "TimedOut alone means the launch ran out of its own budget — the retry ladder's case, never a withdrawal");
    }

    [Fact]
    public async Task AWithdrawnRemoteReviewer_StillSendsTheDelete()
    {
        var spy = new ExecutorAbandonsRemoteWorkTests.SpyRuntime();

        var launch = await new ReviewerExecutor(new ScriptedResults(Withdrawn)).LaunchAsync(Bare(spy), CancellationToken.None);

        launch.Terminal.Should().BeOfType<ReviewerOutcome.Cancelled>();
        spy.Abandoned.Should().Be(1,
            "the tree was killed, so the shim never reached its own DELETE — an early return without AbandonAsync leaves the job running on the company's subscription");
    }

    // ---------- every switch over the endings has a word for it ----------

    [Fact]
    public void ACancelledEnding_HasItsOwnSentence_AndItsOwnNotice()
    {
        ReviewerSummaryFactory.Describe(new ReviewerOutcome.Cancelled()).Should().Be("cancelled",
            "'unknown' is the switch's default arm — the word a sixth ending falls into when nobody wrote its sentence");
        ReviewerNotices.ByType.Should().ContainKey(typeof(ReviewerOutcome.Cancelled),
            "an ending outside the map is an ending nothing writes down").WhoseValue.Should().Be(ServerNoticeCodes.ReviewerCancelled);
    }

    [Fact]
    public void AConsultationLaunchKilledByItsCaller_IsStillWhatTheCallerDecided()
    {
        // The consultation decides from the TOKENS what killed a launch (`ConsultationFailing.KilledAs`) and hands it in;
        // a Cancelled ending is a killed launch like TimedOut and takes the same word, never the default arm's.
        var killed = new ReviewerLaunch(new ReviewerOutcome.Cancelled(), null, Usage.Unknown, string.Empty, Withdrawn);

        ConsultFailures.Classify(new CodexConsultant(new CodexRuntime()), killed, "codex", new ConsultFailure.Cancelled())
            .Should().BeOfType<ConsultFailure.Cancelled>("the caller already said its own token cancelled it");
        ConsultFailures.Classify(new CodexConsultant(new CodexRuntime()), killed, "codex", new ConsultFailure.Deadline(TimeSpan.FromMinutes(12.5)))
            .Should().BeOfType<ConsultFailure.Deadline>("and a turn deadline kill, flagged Cancelled by the linked token, is still the deadline's");
    }

    // ---------- the ledger ----------

    [Fact]
    public void ACancelledTurn_IsOneLedgerLine_SayingCancelled_WithUsageNotCaptured()
    {
        var line = Recorded(new ReviewerOutcome.Cancelled()).Should().ContainSingle("one killed launch is one line, never none and never two").Subject;

        line.Outcome.Should().Be("cancelled", "the ledger's word is the summary's, whole");
        line.UsageNote.Should().Be(CostText.UsageNotCaptured, "a killed launch's usage is unknown, and a bare 0/0 says it was free");
        line.Attempt.Should().Be(LaunchAttempts.First);
        line.EndCause.Should().BeEmpty("a local round does not know which token fired — the field is the Team server's to fill");
    }

    [Fact]
    public async Task ACancelledRepair_KeepsTheFirstLaunchsLine()
    {
        var launcher = new ScriptedResults(new ProcessResult(0, ProseWithUsage, string.Empty, TimedOut: false), Withdrawn);

        var outcome = await new ReviewerExecutor(launcher).RunAsync(Bare(), Bare(), CancellationToken.None);

        outcome.Should().BeOfType<ReviewerOutcome.Cancelled>("the repair was the launch the caller killed");
        outcome.LastTurnUsage.TokensIn.Should().Be(1000, "the billed, malformed first launch rides on the cancelled ending");
        outcome.LastTurnUsage.NotCaptured.Should().BeTrue("and the repair's own share is unknown, which makes the turn a floor");
        var lines = Recorded(outcome);
        lines.Should().HaveCount(2, "two paid launches, two lines (story 3.3) — the cancellation is exactly one of them");
        lines.Select(l => l.Attempt).Should().Equal(LaunchAttempts.First, LaunchAttempts.Repair);
        lines[0].TokensIn.Should().Be(1000);
        lines[1].Outcome.Should().Be("cancelled");
        lines[1].UsageNote.Should().Be(CostText.UsageNotCaptured);
        lines.Select(l => l.Turn).Distinct().Should().ContainSingle("the launches of one turn share one id");
    }
}
