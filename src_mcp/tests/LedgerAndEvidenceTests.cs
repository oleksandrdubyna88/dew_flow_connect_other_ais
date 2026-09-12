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
/// <remarks>
/// In the <c>fakecli-env</c> collection because it LAUNCHES the fake CLI, whose behaviour is
/// steered by process-wide environment variables that other classes set and clear. A verb this
/// class passes in argv is only read when <c>FAKECLI_MODE</c> is unset, so running beside a class
/// that sets it is a race with nothing holding it off.
/// </remarks>
[Collection("fakecli-env")]
public sealed class LedgerAndEvidenceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-ledger-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ReviewerInvocation Invocation(string provider = "antigravity") =>
        new(provider, RoleCatalog.ArchitectureRole, new ProcessRequest("x", [], "."));

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

    // ---------- a reviewer that found NOTHING ----------
    //
    // Measured 2026-09-08, 16:12 UTC: eight remote reviewers answered `{"findings": []}` on a diff
    // the local reviewer found eleven things in — codex in 4.5 to 24.8 seconds, for 44 to 94 output
    // tokens each. Across every code round since 2026-09-01 codex had done that ONCE in ~400 runs,
    // and four of those runs are in that one round.
    //
    // It could not be diagnosed, because an `ok` outcome with zero findings kept nothing: the raw
    // text was dropped, and only a PARSE failure reached `unparseable/`. So "did the model answer
    // emptily, or was it handed a prompt that deserved an empty answer" had no artefact behind it.

    private const string OneFinding = """
        {"findings": [
          {"severity": "major", "category": "security", "file": "app.cs", "line": 10,
           "title": "token compared with ==", "why": "timing side channel", "fix": "FixedTimeEquals"}
        ]}
        """;

    [Fact]
    public async Task AReviewWithNoFindings_KeepsWhatTheVendorActuallySaid()
    {
        var empty = System.IO.Path.Combine(_dir, "empty");
        var executor = new ReviewerExecutor(new ProcessLauncher(), keepEmptyIn: empty);

        var outcome = await executor.RunAsync(
            FakeCliInvocations.Invoke("gemini", ["emit", FakeCliInvocations.CleanReview]),
            ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Ok>()
            .Which.Review.Findings.Should().BeEmpty();
        var file = Directory.GetFiles(empty).Should().ContainSingle().Subject;
        var text = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
        text.Should().Contain("findings",
            "the whole point is reading what the vendor said, not being told that it said nothing");
    }

    [Fact]
    public async Task AReviewWithFindings_KeepsNothing()
    {
        // The other side of it. A directory that also collects the healthy case is a directory
        // whose name lies, and the file that matters is then one of hundreds.
        var empty = System.IO.Path.Combine(_dir, "empty");
        var executor = new ReviewerExecutor(new ProcessLauncher(), keepEmptyIn: empty);

        var outcome = await executor.RunAsync(
            FakeCliInvocations.Invoke("gemini", ["emit", OneFinding]),
            ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Ok>()
            .Which.Review.Findings.Should().HaveCount(1);
        Directory.Exists(empty).Should().BeFalse("a reviewer that found something is not evidence of silence");
    }

    [Fact]
    public async Task TheEmptyAnswerIsKeptSeparatelyFromTheUnparseableOne()
    {
        // Two different questions — "it said nothing" and "it said something I could not read" —
        // and somebody chasing one must not have to wade through the other.
        var unparseable = System.IO.Path.Combine(_dir, "unparseable");
        var empty = System.IO.Path.Combine(_dir, "empty");
        var executor = new ReviewerExecutor(new ProcessLauncher(), unparseable, empty);

        await executor.RunAsync(
            FakeCliInvocations.Invoke("gemini", ["emit", FakeCliInvocations.CleanReview]),
            ct: TestContext.Current.CancellationToken);
        var prose = FakeCliInvocations.Invoke("gemini", ["emit", "not JSON at all"]);
        await executor.RunAsync(prose, repair: prose, ct: TestContext.Current.CancellationToken);

        Directory.GetFiles(empty).Should().ContainSingle();
        Directory.GetFiles(unparseable).Should().ContainSingle();
    }

    [Fact]
    public async Task AKeptAnswerNamesTheReviewerThatGaveIt()
    {
        // An operator reading a silent round has twelve reviewers and needs the file belonging to
        // ONE of them. The name is the index; nothing else in the file identifies its author.
        var empty = System.IO.Path.Combine(_dir, "empty");
        var executor = new ReviewerExecutor(new ProcessLauncher(), keepEmptyIn: empty);

        var outcome = await executor.RunAsync(
            FakeCliInvocations.Invoke("codex", ["emit", FakeCliInvocations.CleanReview]),
            ct: TestContext.Current.CancellationToken);

        var kept = outcome.Should().BeOfType<ReviewerOutcome.Ok>().Which.Evidence;
        kept.Should().NotBeEmpty("the outcome carries the path, so the round's log line can name it");
        System.IO.Path.GetFileName(kept).Should().StartWith("codex-Architecture-");
        File.Exists(kept).Should().BeTrue();
    }

    [Fact]
    public async Task KeepingEvidenceNeverFailsARound()
    {
        // A full disk, a permission change, a directory that is really a file: none of them may
        // turn a review the vendor answered perfectly well into a failed one. Already true of the
        // unparseable path; it has to stay true of this one.
        var blocked = System.IO.Path.Combine(_dir, "blocked");
        await File.WriteAllTextAsync(blocked, "not a directory", TestContext.Current.CancellationToken);
        var executor = new ReviewerExecutor(new ProcessLauncher(), keepEmptyIn: blocked);

        var outcome = await executor.RunAsync(
            FakeCliInvocations.Invoke("gemini", ["emit", FakeCliInvocations.CleanReview]),
            ct: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Ok>()
            .Which.Evidence.Should().BeEmpty("nothing was kept, and the round carried on regardless");
    }

    [Fact]
    public async Task AKeptAnswerIsWholeOrAbsent_NeverHalfWritten()
    {
        // `File.WriteAllText` truncates first and writes second, so a process killed between those
        // two steps leaves a file that exists and says nothing — which reads exactly like a vendor
        // that answered with nothing. The same trap `RemoteRuntime.Claim` documents, and the same
        // cure: write a sibling, then one rename.
        var empty = System.IO.Path.Combine(_dir, "empty");
        var executor = new ReviewerExecutor(new ProcessLauncher(), keepEmptyIn: empty);

        await executor.RunAsync(
            FakeCliInvocations.Invoke("gemini", ["emit", FakeCliInvocations.CleanReview]),
            ct: TestContext.Current.CancellationToken);

        Directory.GetFiles(empty, "*.writing").Should().BeEmpty("the sibling is renamed, never left behind");
        Directory.GetFiles(empty).Should().ContainSingle()
            .Which.Should().EndWith(".txt");
    }
}
