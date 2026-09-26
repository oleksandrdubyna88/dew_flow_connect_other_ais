using System.Globalization;
using System.Text;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The gate's history of one piece of work (S2.3 of the feature-review plan, §4.8): which earlier
/// rounds and consultations are attached to a feature review, which are not, and what is said when
/// the history cannot be read.
/// </summary>
/// <remarks>
/// <para>Real SQLite seeded through the product's own writer (<see cref="RoundsDb"/>), and a real
/// temporary git repository, because every rule here is a claim about one of the two: a squash merge
/// makes an epic's commits unreachable from main, a rebase-merged epic is reviewed before the base
/// commit's committer time, and a fake of either would assert what we believe about git rather than
/// what git does.</para>
/// <para><b>T0 is read back from git</b>, never assumed: the base commit is made during the test, and
/// every round is seeded at an offset from the committer time git reports for it.</para>
/// </remarks>
public sealed class GateHistoryQueryTests : IAsyncLifetime
{
    private const string EpicBranch = "feat/e1";
    private const string FeatureSession = "d:/repo#:feature#feature:todo/plan_widget.md";

    private const string PlanText = """
        # PLAN — the widget grows a handle, and the handle turns

        > Status: **plan only, nothing implemented yet, 2026-09-26.**

        ## 1. The goal
        """;

    private readonly IProcessLauncher _launcher = new ProcessLauncher();
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "coai-history-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;
    private TempGitRepo _repo = null!;
    private string _base = string.Empty;
    private DateTime _t0;

    public async ValueTask InitializeAsync()
    {
        _repo = await TempGitRepo.InitAsync(_launcher, "coai-history-git-");
        await _repo.WriteAsync("a.txt", "base\n");
        await _repo.CommitAsync("base");
        _base = await _repo.HeadAsync();
        var at = await _repo.RunAsync("log", "-1", "--format=%ct", _base);
        _t0 = DateTimeOffset.FromUnixTimeSeconds(long.Parse(at.StdOut.Trim(), CultureInfo.InvariantCulture)).UtcDateTime;
    }

    public async ValueTask DisposeAsync()
    {
        await _repo.DisposeAsync();
        try
        {
            Directory.Delete(_dataDir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }
    }

    // ----------------------------------------------------------------------------------------------
    // The RED list of §7.3 S2.3
    // ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task ASquashMergedEpic_IsFoundByItsBranch_ThoughItsCommitIsInNoRange()
    {
        await _repo.GitAsync("checkout", "-b", EpicBranch);
        await _repo.WriteAsync("b.txt", "epic\n");
        await _repo.CommitAsync("epic 1");
        var epicCommit = await _repo.HeadAsync();
        await _repo.GitAsync("checkout", "main");
        await _repo.GitAsync("merge", "--squash", EpicBranch);
        await _repo.CommitAsync("epic 1, squashed");
        await _repo.GitAsync("branch", "-D", EpicBranch);
        var head = await _repo.HeadAsync();
        SeedRound("repo#feat/e1", EpicBranch, "CodeReview", 1, _t0.AddMinutes(1), epicCommit,
            ("The handle leaks a file descriptor", "the descriptor is owned by the pool, not the handle"));

        var (history, text) = await HistoryAsync(head, [EpicBranch]);

        history.Rounds.Should().ContainSingle().Which.Admission.Should().Be(Admission.EpicBranch,
            "a squashed epic's commit is reachable from nothing, so only its branch can tie it here");
        history.Rejections.Should().ContainSingle().Which.Reason.Should().Be("the descriptor is owned by the pool, not the handle");
        text.Should().Contain("the descriptor is owned by the pool, not the handle");
    }

    [Fact]
    public async Task AMergedEpic_IsFoundByRevList_AndLabelledACandidate()
    {
        await _repo.WriteAsync("c.txt", "merged\n");
        await _repo.CommitAsync("merged work");
        var merged = await _repo.HeadAsync();
        SeedRound("repo#feat/unnamed", "feat/unnamed", "CodeReview", 1, _t0.AddMinutes(1), merged,
            ("The turn is not idempotent", "it is, a second turn is a no-op by design"));

        var (history, text) = await HistoryAsync(merged, [EpicBranch]);

        history.Rounds.Should().ContainSingle().Which.Admission.Should().Be(Admission.CommitInRange);
        text.Should().Contain("CANDIDATE").And.Contain("it is, a second turn is a no-op by design");
    }

    [Fact]
    public async Task ARebaseMergedEpic_ReviewedBeforeTheBaseCommit_IsFoundThanksToTheNinetyDayWindow()
    {
        var head = await CommitOnMainAsync("rebased epic");
        SeedRound("repo#feat/e1", EpicBranch, "CodeReview", 1, _t0.AddDays(-3), Unreachable('a'),
            ("The handle is not thread-safe", "it is only ever touched on the UI thread"));
        SeedRound("repo#feat/e1-old", EpicBranch, "CodeReview", 1, _t0.AddDays(-91), Unreachable('b'),
            ("Too old to belong", "outside any window"));

        var (history, _) = await HistoryAsync(head, [EpicBranch]);

        history.Rounds.Should().ContainSingle(
            "a rebase-merged epic is reviewed on its branch BEFORE the base commit's committer time, and the "
            + "trial found 3 of 5 real features attached nothing without the widening")
            .Which.StartedUtc.Should().StartWith(_t0.AddDays(-3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        history.Rejections.Should().NotContain(r => r.Title == "Too old to belong", "ninety days is the window, not all time");
    }

    [Fact]
    public async Task MainAndMaster_AreNeverAnEpicBranch_EvenWhenTheCallerNamesThem()
    {
        var head = await CommitOnMainAsync("work");
        SeedRound("repo#main", "main", "CodeReview", 1, _t0.AddMinutes(1), Unreachable('c'),
            ("Trunk finding", "trunk reason"));
        SeedRound("repo#master", "master", "CodeReview", 1, _t0.AddMinutes(2), Unreachable('d'),
            ("Master finding", "master reason"));

        var (history, text) = await HistoryAsync(head, ["main", "master", "origin/main", "HEAD", EpicBranch]);

        history.Rounds.Should().BeEmpty("every piece of work passes through the trunk, so the trunk ties nothing to THIS work");
        history.NotAttached.Should().Be(2);
        text.Should().NotContain("trunk reason").And.NotContain("master reason");
    }

    [Fact]
    public async Task AnUnrelatedBranchInTheWindow_IsNotAttached_AndIsCounted()
    {
        var head = await CommitOnMainAsync("work");
        SeedRound("repo#feat/unrelated", "feat/unrelated", "CodeReview", 1, _t0.AddMinutes(1), Unreachable('e'),
            ("Unrelated title", "unrelated reason"));

        var (history, text) = await HistoryAsync(head, [EpicBranch]);

        history.Rounds.Should().BeEmpty();
        history.NotAttached.Should().Be(1);
        text.Should().Contain("NOT attached: 1 round").And.NotContain("unrelated reason");
    }

    [Fact]
    public async Task APlanRoundBeforeTheBase_IsFoundByItsHeading()
    {
        var head = await CommitOnMainAsync("work");
        // An epic's plan round is the plan PREFACED by a paragraph naming the epic (§7.0), so its text
        // does not open with the heading — its SUBJECT is the heading, shortened.
        var prefaced = "Epic 1 of 3: the handle. Epics 2 and 3 are out of scope.\n\n" + PlanText;
        SeedRound("repo#plan/widget", "plan/widget", "PlanReview", 1, _t0.AddDays(-10), string.Empty,
            RoundSubject.From(prefaced, _ => false), prefaced, ("The plan names no budget", "section 4.12 names it"));
        // Opened with the heading, under a subject that is a file name — a caller that passed a path.
        SeedRound("repo#plan/widget-2", "plan/widget-2", "PlanReview", 1, _t0.AddDays(-9), string.Empty,
            "PLAN_widget.md", PlanText, ("The plan has no test plan", "section 8 is the test plan"));
        SeedRound("repo#plan/other", "plan/other", "PlanReview", 1, _t0.AddDays(-8), string.Empty,
            "PLAN — another thing entirely", "# PLAN — another thing entirely\n", ("Other plan", "other reason"));

        var (history, text) = await HistoryAsync(head, [EpicBranch]);

        history.Rounds.Should().HaveCount(2).And.OnlyContain(r => r.Admission == Admission.PlanHeading);
        text.Should().Contain("section 4.12 names it").And.Contain("section 8 is the test plan").And.NotContain("other reason");
    }

    [Fact]
    public async Task AConsultation_IsFoundByItsBranch()
    {
        var head = await CommitOnMainAsync("work");
        SeedConsultation("c1", EpicBranch, Unreachable('f'), _t0.AddHours(1), "Why does the handle stick?", "Because the pool holds it.");
        SeedConsultation("c2", "feat/unrelated", Unreachable('0'), _t0.AddHours(2), "Unrelated question", "Unrelated advice");

        var (history, text) = await HistoryAsync(head, [EpicBranch]);

        history.Consultations.Should().ContainSingle().Which.Admission.Should().Be(Admission.EpicBranch);
        text.Should().Contain("Because the pool holds it.").And.NotContain("Unrelated advice");
    }

    [Fact]
    public async Task ARevListOverTheCap_SwitchesRuleAOff_AndSaysSo()
    {
        await CommitOnMainAsync("one");
        var inRange = await CommitOnMainAsync("two");
        var head = await CommitOnMainAsync("three");
        SeedRound("repo#feat/unnamed", "feat/unnamed", "CodeReview", 1, _t0.AddMinutes(1), inRange,
            ("Would be a candidate", "by its commit"));

        var history = await GateHistoryQuery.ReadAsync(Ask(head, [EpicBranch]), _launcher, maxCommits: 2, CancellationToken.None);
        var text = GateHistoryText.Render(history);

        history.Rounds.Should().BeEmpty("a list cut at the cap is not the range, and must not be read as one");
        history.Notes.Should().ContainSingle(n => n.Contains("more than 2 commits", StringComparison.Ordinal));
        text.Should().Contain("more than 2 commits");
    }

    [Fact]
    public async Task NoDatabase_IsOneSentence_AndCreatesNoDatabase()
    {
        var head = await CommitOnMainAsync("work");
        Directory.CreateDirectory(_dataDir);

        var text = await GateHistoryQuery.RenderAsync(Ask(head, [EpicBranch]), _launcher);

        text.Should().StartWith("Gate history unavailable:").And.NotContain("\n");
        File.Exists(Path.Combine(_dataDir, RoundsDb.FileName)).Should().BeFalse("a read-only reader creates nothing");
    }

    [Fact]
    public async Task ARangeThatIsNotTwoCommitIds_IsOneSentence_AndGitIsNotAsked()
    {
        await CommitOnMainAsync("work");

        var text = await GateHistoryQuery.RenderAsync(Ask("--upload-pack=evil", [EpicBranch]) with { BaseSha = "main" }, _launcher);

        text.Should().StartWith("Gate history unavailable:").And.Contain("not two full commit ids").And.NotContain("\n");
    }

    [Fact]
    public async Task TheCutAtTwentyFourKilobytes_KeepsTheCountsSentence_AndSaysWhatItLeftOut()
    {
        var head = await CommitOnMainAsync("work");
        // Every title shares no word with any other, so de-duplication merges none of the 200.
        var many = Enumerable.Range(0, 200)
            .Select(i => ($"{Words(i)} {Words(i + 7000)} {Words(i + 8000)}", $"{Words(i + 1000)} {new string('x', 150)}"))
            .ToArray();
        SeedRound("repo#feat/e1", EpicBranch, "CodeReview", 1, _t0.AddMinutes(1), Unreachable('1'), many);

        var text = GateHistoryText.Render((await HistoryAsync(head, [EpicBranch])).History);

        Encoding.UTF8.GetByteCount(text).Should().BeLessThanOrEqualTo(GateHistoryText.MaxBytes);
        text.Should().Contain("200 rejections from 1 round");
        text.Should().MatchRegex(@"\d+ more rejections? .*not shown");
    }

    // ----------------------------------------------------------------------------------------------
    // What else §4.8 promises
    // ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheFeatureSessionsOwnRejections_AreNotAttached()
    {
        var head = await CommitOnMainAsync("work");
        SeedRound(FeatureSession, EpicBranch, "CodeReview", 1, _t0.AddMinutes(1), Unreachable('2'),
            ("Own finding", "own reason"));
        SeedRound("repo#feat/e1", EpicBranch, "CodeReview", 1, _t0.AddMinutes(2), Unreachable('9'),
            ("The epic's finding", "the epic's reason"));

        var (history, _) = await HistoryAsync(head, [EpicBranch]);

        history.Rejections.Should().ContainSingle(
            "the gate already counts the feature session's own rejections — and the epic's, on the same branch, still belong")
            .Which.Title.Should().Be("The epic's finding");
    }

    [Fact]
    public async Task TheSameRemarkRejectedTwice_IsShownOnce_WithHowOftenItWasRejected()
    {
        var head = await CommitOnMainAsync("work");
        SeedRound("repo#feat/e1", EpicBranch, "CodeReview", 1, _t0.AddMinutes(1), Unreachable('3'),
            ("The handle leaks a descriptor on close", "the pool owns it"));
        SeedRound("repo#feat/e1", EpicBranch, "CodeReview", 2, _t0.AddMinutes(5), Unreachable('4'),
            ("The handle leaks a descriptor on close again", "still the pool owns it"));

        var (history, text) = await HistoryAsync(head, [EpicBranch]);

        history.RejectionsFound.Should().Be(2);
        history.Rejections.Should().ContainSingle().Which.Should().Match<HistoryRejection>(
            r => r.Times == 2 && r.Reason == "still the pool owns it");
        text.Should().Contain("2 rejections (1 after removing repeats)");
    }

    [Fact]
    public async Task PlanRoundsComeBeforeCode_AndNewestFirst_EpicsBeforeCandidates()
    {
        var merged = await CommitOnMainAsync("merged");
        SeedRound("repo#feat/e1", EpicBranch, "CodeReview", 1, _t0.AddMinutes(1), Unreachable('5'), ("Code older", "r1"));
        SeedRound("repo#feat/e1", EpicBranch, "CodeReview", 2, _t0.AddMinutes(9), Unreachable('6'), ("Code newer", "r2"));
        SeedRound("repo#feat/e1", EpicBranch, "PlanReview", 1, _t0.AddMinutes(-5), string.Empty, ("Plan finding", "r3"));
        SeedRound("repo#feat/x", "feat/x", "CodeReview", 1, _t0.AddMinutes(20), merged, ("Candidate finding", "r4"));

        var (history, _) = await HistoryAsync(merged, [EpicBranch]);

        history.Rejections.Select(r => r.Title).Should().Equal("Plan finding", "Code newer", "Code older", "Candidate finding");
    }

    [Fact]
    public async Task ReadingTheHistory_WritesNothing()
    {
        var head = await CommitOnMainAsync("work");
        SeedRound("repo#feat/e1", EpicBranch, "CodeReview", 1, _t0.AddMinutes(1), Unreachable('7'), ("A finding", "a reason"));
        var file = Path.Combine(_dataDir, RoundsDb.FileName);
        var before = await File.ReadAllBytesAsync(file);

        var (history, _) = await HistoryAsync(head, [EpicBranch]);

        history.Rounds.Should().ContainSingle("the read must actually have happened for its silence to mean anything");
        (await File.ReadAllBytesAsync(file)).Should().Equal(before);
    }

    [Fact]
    public async Task ARoundOfAnotherRepository_NeverBelongs()
    {
        var head = await CommitOnMainAsync("work");
        SeedRound("elsewhere#feat/e1", EpicBranch, "CodeReview", 1, _t0.AddMinutes(1), Unreachable('8'),
            ("Elsewhere", "another repository"), repoPath: "D:/somewhere/else");
        // The same repository written the other way round — back-slashed, upper-cased, a trailing
        // separator — is still this repository, as the session key already decides.
        SeedRound("here#feat/e1", EpicBranch, "CodeReview", 1, _t0.AddMinutes(2), Unreachable('9'),
            ("Here", "this repository"), repoPath: _repo.Path.Replace('/', '\\').ToUpperInvariant() + "\\");

        var (history, _) = await HistoryAsync(head, [EpicBranch]);

        history.Rounds.Should().ContainSingle().Which.SessionId.Should().Be("here#feat/e1");
        history.NotAttached.Should().Be(0, "a round of another repository is not a round of this one at all");
    }

    /// <summary>
    /// The same repository reached through a directory link is still this repository — a consultation
    /// records git's REAL path, and a feature review may be asked through the linked one.
    /// </summary>
    /// <remarks>
    /// On macOS those always differ: a checkout under <c>/var/folders/…</c> is <c>/private/var/folders/…</c>
    /// to git, because <c>/var</c> is a link. A comparison that only normalises spelling therefore dropped
    /// every consultation from the history of a review asked the other way round. The link is made here,
    /// so the test fails on every platform rather than only on the one whose temp directory shows it.
    /// </remarks>
    [Fact]
    public async Task RoundsAndConsultationsUnderTheRealPath_BelongToTheRepositoryReachedThroughALink()
    {
        var head = await CommitOnMainAsync("work");
        SeedRound("here#feat/e1", EpicBranch, "CodeReview", 1, _t0.AddMinutes(1), Unreachable('9'), ("Here", "this repository"));
        SeedConsultation("c1", EpicBranch, Unreachable('7'), _t0.AddMinutes(2), "Why does the handle stick?", "Because the pool holds it.");
        var links = Directory.CreateTempSubdirectory("coai-history-link-").FullName;
        var linked = Path.Combine(links, "repo");
        try
        {
            await DirectoryLink.MakeAsync(_launcher, linked, _repo.Path);

            var history = await GateHistoryQuery.ReadAsync(Ask(head, [EpicBranch]) with { RepoPath = linked }, _launcher);

            history.Unavailable.Should().BeEmpty();
            history.Rounds.Should().ContainSingle("the round's session recorded the real path of this same repository")
                .Which.SessionId.Should().Be("here#feat/e1");
            history.Consultations.Should().ContainSingle("the consultation recorded git's answer, the real path")
                .Which.Problem.Should().Be("Why does the handle stick?");
        }
        finally
        {
            DirectoryLink.Remove(linked);
            Directory.Delete(links, recursive: true);
        }
    }

    /// <summary>
    /// A database written before a consultation could end (<c>outcome</c>, step 9) or say what it was for
    /// (<c>kind</c>, step 15) still gives the history its consultations — each missing column read as what
    /// it means.
    /// </summary>
    /// <remarks>
    /// The reader is read-only and cannot migrate. The step each case stops before is FOUND by asking
    /// which step adds the column, never by counting — a literal index rots the moment anybody appends.
    /// <c>outcome TEXT</c> rather than <c>outcome</c>, because <c>outcome_by</c> contains the shorter one.
    /// </remarks>
    [Theory]
    [InlineData("ADD COLUMN outcome TEXT", "")]
    [InlineData("ADD COLUMN kind", "solved")]
    public async Task AConsultationFromAnOlderSchema_IsStillRead(string stopBefore, string outcome)
    {
        var head = await CommitOnMainAsync("work");
        var adds = Array.FindIndex(Schema.Steps, step => step.Contains(stopBefore, StringComparison.Ordinal));
        adds.Should().BeGreaterThan(0, "the step that adds the column is what this test stops before");
        Directory.CreateDirectory(_dataDir);
        using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(_dataDir, RoundsDb.FileName)};Pooling=False"))
        {
            db.Open();
            using var make = db.CreateCommand();
            make.CommandText = string.Join(";\n", Schema.Steps[..adds]) + $"; PRAGMA user_version={adds}";
            make.ExecuteNonQuery();

            using var insert = db.CreateCommand();
            insert.CommandText = outcome.Length == 0
                ? "INSERT INTO consultations (id, repo_path, branch, head_sha, status, started_utc, problem, advice) VALUES ('old1', $repo, $branch, '', 'closed', $at, 'Old question', 'Old advice')"
                : "INSERT INTO consultations (id, repo_path, branch, head_sha, status, started_utc, problem, advice, outcome) VALUES ('old1', $repo, $branch, '', 'closed', $at, 'Old question', 'Old advice', 'solved')";
            insert.Parameters.AddWithValue("$repo", _repo.Path);
            insert.Parameters.AddWithValue("$branch", EpicBranch);
            insert.Parameters.AddWithValue("$at", _t0.AddMinutes(1).ToString("O", CultureInfo.InvariantCulture));
            insert.ExecuteNonQuery();
        }

        var history = await GateHistoryQuery.ReadAsync(Ask(head, [EpicBranch]), _launcher);

        history.Unavailable.Should().BeEmpty("an older file is still a file this reader can read");
        var read = history.Consultations.Should().ContainSingle("the row is there and its branch is the epic's").Subject;
        read.Outcome.Should().Be(outcome, "a row written before the column existed carries no verdict, and empty is not one");
        read.Kind.Should().Be("stuck", "before the kinds every consultation was an agent that was stuck");
    }

    // ----------------------------------------------------------------------------------------------

    private async Task<string> CommitOnMainAsync(string message)
    {
        await _repo.WriteAsync("a.txt", message + "\n");
        await _repo.CommitAsync(message);

        return await _repo.HeadAsync();
    }

    private GateHistoryAsk Ask(string head, IReadOnlyList<string> epics) =>
        new(_dataDir, _repo.Path, _base, head, epics, PlanText, FeatureSession);

    private async Task<(GateHistory History, string Text)> HistoryAsync(string head, IReadOnlyList<string> epics)
    {
        var history = await GateHistoryQuery.ReadAsync(Ask(head, epics), _launcher);

        return (history, GateHistoryText.Render(history));
    }

    /// <summary>A full commit id no ref in the test repository reaches.</summary>
    private static string Unreachable(char digit) => new(digit, 40);

    private static string Words(int n) =>
        string.Join(' ', n.ToString(CultureInfo.InvariantCulture).Select(d => "w" + d + "q" + n));

    private void SeedRound(
        string sessionId, string branch, string stage, int number, DateTime started, string headSha,
        params (string Title, string Reason)[] rejected) =>
        Seed(sessionId, branch, stage, number, started, headSha, "SCOPE — something", "SCOPE — something", rejected, _repo.Path);

    private void SeedRound(
        string sessionId, string branch, string stage, int number, DateTime started, string headSha,
        (string Title, string Reason) rejected, string repoPath) =>
        Seed(sessionId, branch, stage, number, started, headSha, "SCOPE", "SCOPE", [rejected], repoPath);

    private void SeedRound(
        string sessionId, string branch, string stage, int number, DateTime started, string headSha,
        string subject, string planText, params (string Title, string Reason)[] rejected) =>
        Seed(sessionId, branch, stage, number, started, headSha, subject, planText, rejected, _repo.Path);

    private void Seed(
        string sessionId, string branch, string stage, int number, DateTime started, string headSha,
        string subject, string planText, (string Title, string Reason)[] rejected, string repoPath)
    {
        var state = new SessionState(sessionId, repoPath, branch, new PanelConfig());
        Finding[] findings = [.. rejected.Select((r, i) =>
            new Finding(Severity.Major, Category.Reliability, "src/Handle.cs", 10 + (i * 20), r.Title, r.Title + " — why", "fix it", ["codex"]))];
        var round = new RoundRecord(stage, number, "revise", findings.Length, "3 reviewers answered", started.AddMinutes(2))
        {
            StartedUtc = started,
            Subject = subject,
        };
        using var db = RoundsDb.Open(_dataDir, _log)!;
        db.RecordRound(state, round, findings, new RoundContext(PlanText: planText, HeadSha: headSha));
        db.RecordDecisions(sessionId, stage, number, [.. findings.Select((_, i) => Decisions.Reject(findings, i, rejected[i].Reason))]);
    }

    private void SeedConsultation(string id, string branch, string headSha, DateTime started, string problem, string advice)
    {
        using var db = RoundsDb.Open(_dataDir, _log)!;
        db.RecordConsultation(new ConsultationRow(
            id, "claude:session-1", "claude", _repo.Path, branch, headSha, "codex", "gpt-5", 2, "closed", "",
            "solved", "caller", started.ToString("O"), started.AddMinutes(3).ToString("O"), 180, 100, 50, null,
            problem, advice, string.Empty));
    }
}
