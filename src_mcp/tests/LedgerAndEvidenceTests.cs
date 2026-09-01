using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a round SPENT and what it can still show for a failure — the two things an independent
/// verification pass found wrong on 2026-09-01, both of which had a doc comment claiming otherwise.
/// </summary>
public sealed class LedgerAndEvidenceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-ledger-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ReviewerInvocation Invocation(string provider = "antigravity") =>
        new(provider, ReviewRole.Architecture, new ProcessRequest("x", [], "."));

    /// <summary>
    /// Reads the ledger the way anything else would have to while a server is writing it.
    /// </summary>
    /// <remarks>
    /// <c>File.ReadAllLines</c> asks for <c>FileShare.Read</c>, which refuses to open a file some
    /// other handle has open for WRITING — so the first version of this helper failed the very
    /// test that proves two servers can both append. The panel reads this file too; a reader that
    /// cannot tolerate a live writer is the same defect one level up.
    /// </remarks>
    private IReadOnlyList<string> Lines()
    {
        var path = System.IO.Path.Combine(_dir, "usage.jsonl");
        if (!File.Exists(path))
        {
            return [];
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    // ---------- the ledger ----------

    [Fact]
    public void EveryEntry_IsExactlyOneLine_BecauseTheReaderIsLineBased()
    {
        var ledger = new UsageLedger(_dir);

        ledger.Record(Invocation(), new ReviewerOutcome.Ok(new NormalisedReview([], []), false, new Usage(10, 2, 0.5)),
            "gemini-3.7-flash-high", "CodeReview", TimeSpan.FromSeconds(3));
        ledger.Record(Invocation("codex"), new ReviewerOutcome.TimedOut(), "gpt", "CodeReview", TimeSpan.FromSeconds(9));

        Lines().Should().HaveCount(2, "an indented serializer would spread one entry over fifteen lines");
        Lines().Should().OnlyContain(l => l.StartsWith('{') && l.EndsWith('}'));
    }

    [Fact]
    public void AFailedReviewer_IsRecordedWithWhatItActuallyConsumed()
    {
        // Measured: two reviewers fell over after 107 and 128 seconds beside a sibling that cost
        // 210k input tokens on the same diff, and the round wrote them down as free.
        new UsageLedger(_dir).Record(
            Invocation(),
            new ReviewerOutcome.Unparseable("the vendor returned an empty answer", new Usage(210_555, 3_000, null)),
            "gemini-3.7-flash-high",
            "CodeReview",
            TimeSpan.FromSeconds(107.4));

        var line = Lines().Should().ContainSingle().Subject;
        line.Should().Contain("210555", "an unparseable answer is a COMPLETED run that reported its usage");
        line.Should().Contain("empty answer", "the outcome travels with the spending");
    }

    [Fact]
    public void ASecondServerHoldingTheFileOpen_DoesNotCostUsALine()
    {
        // Two servers on one data directory is the normal case here. `File.AppendAllText` takes a
        // write lock the other cannot pass, and the loser's line disappeared into a catch.
        var ledger = new UsageLedger(_dir);
        ledger.Record(Invocation(), new ReviewerOutcome.TimedOut(), "m", "PlanReview", TimeSpan.FromSeconds(1));

        using var otherServer = new FileStream(
            ledger.Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);

        ledger.Record(Invocation(), new ReviewerOutcome.TimedOut(), "m", "PlanReview", TimeSpan.FromSeconds(2));

        Lines().Should().HaveCount(2, "a spending record with a silent gap is worse than one that errors");
    }

    // ---------- the round total ----------

    [Fact]
    public void TheRoundTotal_CountsAFailureThatStillBurnedTokens()
    {
        var store = new SessionStore(_dir);
        var session = new PersistedSession(new SessionState("s", "D:/r", "main", new PanelConfig()), []);
        var work = new List<ReviewerWork> { new(Invocation()) };
        var live = new LiveRound(store, session, work);

        var record = live.Finish("revise", 1, "1 of 2", [
            (Invocation(), new ReviewerOutcome.Ok(new NormalisedReview([], []), false, new Usage(100, 10, null))),
            (Invocation("codex"), new ReviewerOutcome.Unparseable("empty", new Usage(900, 90, null))),
        ]);

        record.TokensIn.Should().Be(1000, "counting only the answers halves what the round really cost");
        record.TokensOut.Should().Be(100);
    }

    // ---------- the evidence ----------

    [Fact]
    public async Task AnUnparseableAnswer_LeavesTheVendorsOwnTextOnDisk()
    {
        var kept = System.IO.Path.Combine(_dir, "unparseable");
        var executor = new ReviewerExecutor(new ProcessLauncher(), kept);
        var prose = FakeCliInvocations.Invoke("gemini", ["emit", "I have reviewed it and here are my thoughts."]);

        var outcome = await executor.RunAsync(prose, repair: prose, ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Unparseable>();
        var file = Directory.GetFiles(kept).Should().ContainSingle().Subject;
        new FileInfo(file).Length.Should().BeGreaterThan(0, "a named file that is empty is not evidence");
        (await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken))
            .Should().Contain("here are my thoughts");
    }

    [Fact]
    public async Task WhenTheRepairSaysNothing_TheFirstAttemptIsWhatIsKept()
    {
        // The failure that started this: the repair came back with an empty envelope and erased
        // the only text anybody could have read.
        var kept = System.IO.Path.Combine(_dir, "unparseable");
        var executor = new ReviewerExecutor(new ProcessLauncher(), kept);

        var outcome = await executor.RunAsync(
            FakeCliInvocations.Invoke("gemini", ["emit", "the first attempt said this much"]),
            repair: FakeCliInvocations.Invoke("gemini", ["emit", ""]),
            ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Unparseable>();
        var file = Directory.GetFiles(kept).Should().ContainSingle().Subject;
        (await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken))
            .Should().Contain("the first attempt said this much");
    }

    [Fact]
    public async Task AnEmptyEnvelope_IsReportedAsEmpty_NotAsMalformedJson()
    {
        var executor = new ReviewerExecutor(new ProcessLauncher());
        var silent = FakeCliInvocations.Invoke("gemini", ["emit", ""]);

        var outcome = await executor.RunAsync(silent, repair: silent, ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Unparseable>()
            .Which.Reason.Should().Contain("empty answer").And.NotContain("not the schema");
    }

    [Fact]
    public async Task WhenTheVendorSaysNOTHING_TheProcessTranscriptIsWhatIsKept()
    {
        // The failure that would not explain itself: the envelope came back empty, so the field
        // the adapter reads held nothing and the kept file was zero bytes — twice, in real runs.
        // The diagnosis was in the process's own streams all along.
        var kept = System.IO.Path.Combine(_dir, "unparseable");
        var executor = new ReviewerExecutor(new ProcessLauncher(), kept);
        var silent = FakeCliInvocations.Invoke("gemini", ["stderr-emit", "quota check failed upstream", ""]);

        var outcome = await executor.RunAsync(silent, repair: silent, ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Unparseable>();
        var file = Directory.GetFiles(kept).Should().ContainSingle().Subject;
        var text = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
        new FileInfo(file).Length.Should().BeGreaterThan(0, "an empty evidence file explains nothing");
        text.Should().Contain("stderr").And.Contain("quota check failed upstream");
    }
}
