using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A repair that fails on its OWN launch — times out, exits non-zero, is rate limited — must not lose
/// what the first attempt cost: that attempt completed, was billed, and only its ANSWER was malformed.
/// </summary>
/// <remarks>
/// <para><b>The defect</b> (found by the epic 3 risk consultation 159f0397, verified against the code):
/// <c>ReviewerExecutor.RunAsync</c> returned the repair's terminal outcome exactly as the launch produced
/// it, so the first launch's usage — read off a process that ran to completion — went nowhere: not on the
/// outcome, not on its ledger line, not in the round's total. A repaired reviewer had been billed for both
/// launches since 2026-09-01; a reviewer whose repair then FAILED was billed for neither. Every stage
/// repairs the same way, and the turn loop (S3.2) repairs each turn the same way — a turn whose repair
/// times out is the same loss, one turn in (<c>AReviewerThatAsksForSourceIsAskedAgainTests</c>).</para>
/// <para>What must be true: the terminal outcome carries BOTH attempts' usage exactly once — the first's,
/// plus the repair's own when its launch reported any — the ledger writes it once, the round total counts
/// it once, the reviewer is one terminal failure, and its slot is released.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class AFailedRepairStillCountsTheMalformedAttemptTests : IDisposable
{
    /// <summary>What a vendor says when it answers prose and reports its usage beside it.</summary>
    private const string ProseWithUsage = """{"note":"prose, not findings","input_tokens":1000,"output_tokens":50}""";

    private readonly ReviewerExecutor _executor = new(new ProcessLauncher());
    private readonly TempDir _dir = TempDir.For("coai-failed-repair-");

    public void Dispose() => _dir.Dispose();

    private static ReviewerInvocation Malformed() => FakeCliInvocations.Invoke("gemini", ["emit", ProseWithUsage]);

    private static ReviewerInvocation Bare() => new("gemini", RoleCatalog.ArchitectureRole, new ProcessRequest("x", [], "."));

    /// <summary>The two endings a repair's own launch can have that the executor returns as they are.</summary>
    public static TheoryData<string, string[], int> RepairEndings => new()
    {
        { "a timeout", ["sleep", "5000"], 700 },
        { "a non-zero exit", ["stderr-exit", "the model fell over", "3"], 60_000 },
    };

    private IReadOnlyList<UsageEntry> Recorded(ReviewerOutcome outcome, string under)
    {
        var ledger = new UsageLedger(_dir.At(under));
        ledger.Record(Bare(), outcome, "m", "CodeReview", TimeSpan.FromSeconds(3));

        return [.. File.ReadAllLines(ledger.Path).Select(l => JsonSerializer.Deserialize(l, LedgerJsonContext.Default.UsageEntry)!)];
    }

    private RoundRecord RoundTotal(ReviewerOutcome outcome, string under)
    {
        var store = new SessionStore(_dir.At(under));
        var session = new PersistedSession(new SessionState("s", "D:/r", "main", new PanelConfig()), []);
        var live = new LiveRound(store, session, 1, [new ReviewerWork(Bare())], "", Noticing.None);

        return live.Finish("revise", 0, "0 of 1", [(Bare(), outcome)]);
    }

    [Theory]
    [MemberData(nameof(RepairEndings))]
    public async Task WhenTheRepairsOwnLaunchFails_TheFirstAttemptsCost_IsInTheLedgerOnce_AndInTheRoundTotalOnce(
        string ending, string[] repairArgs, int repairTimeoutMs)
    {
        var repair = FakeCliInvocations.Invoke("gemini", repairArgs, TimeSpan.FromMilliseconds(repairTimeoutMs));

        var outcome = await _executor.RunAsync(Malformed(), repair, TestContext.Current.CancellationToken);

        outcome.Should().NotBeOfType<ReviewerOutcome.Ok>($"the repair ended in {ending}")
            .And.NotBeOfType<ReviewerOutcome.Unparseable>("the repair's own launch failed before there was an answer to parse");
        outcome.EarlierTurns.Should().BeEmpty("a single-turn reviewer has no earlier turn — this is the same turn's earlier launch");
        outcome.EarlierLaunches.TokensIn.Should().Be(1000, "the malformed attempt rides on the failure as this turn's earlier launch");
        outcome.LastTurnUsage.TokensIn.Should().Be(1000, "the turn's usage is every launch of it — and the repair's own launch reported nothing before it failed");
        outcome.LastTurnUsage.TokensOut.Should().Be(50);
        outcome.TotalUsage.TokensIn.Should().Be(1000, "exactly once: no earlier turn to add it to twice");

        var lines = Recorded(outcome, "ledger-" + repairArgs[0]);
        lines.Should().ContainSingle("one reviewer, one turn, one line — the malformed attempt is on the failure's line, never a line of its own");
        lines[0].TokensIn.Should().Be(1000, "the first launch completed and reported what it consumed; only its answer was malformed");
        lines[0].TokensOut.Should().Be(50);
        lines[0].Outcome.Should().NotBe("ok", "the reviewer failed, and the ledger says so beside what it cost");

        var record = RoundTotal(outcome, "store-" + repairArgs[0]);
        record.TokensIn.Should().Be(1000, "the round's total counts a failure that still burned tokens — a repair that failed is one");
        record.TokensOut.Should().Be(50);
    }

    /// <summary>
    /// The rate-limit ladder retries the whole reviewer when the REPAIR is what was rate limited, and every
    /// step's malformed first launch was billed too: the ladder's last outcome carries all of them.
    /// </summary>
    [Fact]
    public async Task WhenTheRepairIsRateLimited_EveryStepsMalformedAttemptIsStillBilled()
    {
        var counter = Path.Combine(_dir, "ladder.txt");
        var work = new ReviewerWork(
            FakeCliInvocations.Invoke("codex", ["count", counter, "emit", ProseWithUsage]),
            FakeCliInvocations.Invoke("codex", ["stderr-exit", "429 Too Many Requests", "1"]));

        var results = await new BoundedScheduler(retryLadder: [TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1)])
            .RunAllAsync([work], _executor, TestContext.Current.CancellationToken);

        var outcome = results.Single().Outcome;
        outcome.Should().BeOfType<ReviewerOutcome.RateLimited>().Which.Attempts.Should().Be(3, "one launch and a step for each of the two waits");
        (await File.ReadAllLinesAsync(counter, TestContext.Current.CancellationToken)).Should().HaveCount(3, "three malformed first launches, one per step");
        outcome.TotalUsage.TokensIn.Should().Be(3000, "each step's cost rides forward on the next step's outcome");

        var lines = Recorded(outcome, "ledger-ladder");
        lines.Should().ContainSingle("one reviewer, one line, however many steps it climbed");
        lines[0].TokensIn.Should().Be(3000, "each step's first launch completed and was billed before its repair was refused");
        RoundTotal(outcome, "store-ladder").TokensIn.Should().Be(3000);
    }
}
