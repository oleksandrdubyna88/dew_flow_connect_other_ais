using System.Collections.Immutable;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Tests that redirect <c>Console.Out</c>, which is one object for the whole process.
/// </summary>
/// <remarks>
/// <para>A test that captures stdout replaces it globally, so two of them running at once read each
/// other's output: the second JSON document lands in the first one's writer and the second writer
/// gets nothing at all. Measured the moment this file's mode tests joined
/// <see cref="ABatchFindingsReadTests"/> — one run failed here with "'{' is invalid after a single
/// JSON value", the next failed THERE with "the input does not contain any JSON tokens", which is
/// the same defect seen from each end.</para>
/// <para>The classes in it also share <c>COAI_DATA_DIR</c>, so serialising them settles both.</para>
/// </remarks>
[CollectionDefinition("console-out", DisableParallelization = true)]
public sealed class ConsoleOutCollection;

/// <summary>
/// The accepted findings read back as material for a corpus, rather than as a log.
/// </summary>
/// <remarks>
/// <para>An accepted finding is, by definition, a defect somebody confirmed — the blind-spot corpus
/// the rounds database was built for on 2026-09-05. Nothing read it back that way until this: the
/// log page shows what happened in a round, which is a different question from which defects are
/// worth keeping.</para>
/// <para>Real SQLite over a temp directory, as <see cref="RoundsDbTests"/> does and for the same
/// reason: the thing under test is the SQL.</para>
/// </remarks>
[Collection("console-out")]
public sealed class BugsQueryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-bugs-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    private static readonly SessionState Session =
        new("s1", "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };

    private static Finding Found(
        string title,
        Category category = Category.Reliability,
        Severity severity = Severity.Major,
        string file = "src/Panel.cs",
        int line = 40) =>
        new(severity, category, file, line, title, title + ", and here is why", "the fix for " + title, ["codex"])
        {
            Role = "SecurityReliability",
        };

    private static RoundRecord Round(string stage = "CodeReview", int number = 1) =>
        new(stage, number, "revise", 2, "all 3 reviewers answered", DateTime.UtcNow)
        {
            StartedUtc = DateTime.UtcNow.AddMinutes(-4),
            Subject = "SCOPE — the cost column",
        };

    /// <summary>Record a round and accept every finding in it, in the order given.</summary>
    private static void AcceptAll(RoundsDb db, RoundRecord round, params Finding[] findings)
    {
        db.RecordRound(Session, round, findings, new RoundContext("SCOPE", "7133c2f", "claude-code"));
        db.RecordDecisions(Session.SessionId, round.Stage, round.Number,
            [.. findings.Select((_, i) => Decisions.Accept(findings, i))]);
    }

    /// <summary>
    /// The interval ends at the next round that actually MOVED, and a re-review does not count.
    /// </summary>
    /// <remarks>
    /// <para>`later_sha` is the state after this round's fixes, and for an orphaned commit it is
    /// the only interval there is — 37 % of measured candidates. It had no test at all.</para>
    /// <para>A round that reviewed the same commit again — a repair, a reviewer that timed out and
    /// was re-run — carries the SAME `head_sha`. Offered as the interval end it gives `head..head`,
    /// which is empty, so the walk finds nothing and the candidate is filed `fix_commit_not_found`
    /// while the fix sits one round further on. (Code round, codex.)</para>
    /// </remarks>
    [Fact]
    public void TheIntervalEndsAtTheNextRoundThatMoved()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        var first = Found("a race on the cache");
        db.RecordRound(Session, Round(), [first], new RoundContext("SCOPE", "aaaa111", "claude-code"));
        db.RecordDecisions(Session.SessionId, "CodeReview", 1, [Decisions.Accept([first], 0)]);

        // Round two re-reviewed the same commit: nothing was pushed between them.
        db.RecordRound(
            Session, Round(number: 2), [Found("another")], new RoundContext("SCOPE", "aaaa111", "claude-code"));
        // Round three is where the fixes landed.
        db.RecordRound(
            Session, Round(number: 3), [Found("a third")], new RoundContext("SCOPE", "bbbb222", "claude-code"));

        BugsQuery.Read(_dir).Candidates.Should().ContainSingle()
            .Which.LaterSha.Should().Be("bbbb222", "head..head is an empty interval, not an end");
    }

    /// <summary>With nothing after it, a candidate has no bounded interval and says so.</summary>
    [Fact]
    public void ARoundWithNothingAfterIt_HasNoInterval()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        var only = Found("a race on the cache");
        db.RecordRound(Session, Round(), [only], new RoundContext("SCOPE", "aaaa111", "claude-code"));
        db.RecordDecisions(Session.SessionId, "CodeReview", 1, [Decisions.Accept([only], 0)]);

        BugsQuery.Read(_dir).Candidates.Should().ContainSingle().Which.LaterSha.Should().BeEmpty();
    }

    [Fact]
    public void AnAcceptedRuntimeFindingOnCode_IsMaterialForTheCorpus()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        AcceptAll(db, Round(), Found("the session file is opened without FileShare"));

        var candidate = BugsQuery.Read(_dir).Candidates.Should().ContainSingle().Subject;

        candidate.Title.Should().Be("the session file is opened without FileShare");
        candidate.File.Should().Be("src/Panel.cs");
        candidate.Line.Should().Be(40);
        candidate.Category.Should().Be("Reliability");
        candidate.Severity.Should().Be("Major");
        candidate.RepoPath.Should().Be("D:/repo", "the collector has to find the checkout again");
        candidate.Branch.Should().Be("feat/x");
        candidate.HeadSha.Should().Be("7133c2f", "and read the method as the reviewers saw it");
        candidate.CollectState.Should().BeEmpty("nothing has looked at it yet");
    }

    /// <summary>
    /// Everything the corpus cannot use is left out, and each for its own reason.
    /// </summary>
    /// <remarks>
    /// One round carrying every exclusion at once rather than a test per predicate: the risk worth
    /// covering is not that any single clause works, it is that the six compose into the set the
    /// funnel claims. A test per clause would pass on a query that dropped one of them.
    /// </remarks>
    [Fact]
    public void EverythingTheCorpusCannotUse_IsLeftOut()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        var wanted = Found("a timeout nobody sets");
        var findings = new[]
        {
            wanted,
            Found("trailing whitespace", severity: Severity.Nit),
            Found("the parser reaches into the transport", Category.Architecture),
            Found("the error message says nothing", Category.Ux),
            Found("a remark about the whole repository", file: "", line: 0),
        };

        db.RecordRound(Session, Round(), findings, new RoundContext("SCOPE", "7133c2f", "claude-code"));
        db.RecordDecisions(Session.SessionId, "CodeReview", 1,
            [.. findings.Select((_, i) => Decisions.Accept(findings, i))]);

        // A plan round's remarks: accepted, gating, and about a document rather than a runtime.
        AcceptAll(db, Round("PlanReview", 1), Found("the plan does not say what happens on failure"));

        // And one that was argued with, plus one nobody has decided at all.
        var second = new[] { Found("a race on the cache"), Found("an unbounded queue") };
        db.RecordRound(Session, Round(number: 2), second, new RoundContext("SCOPE", "7133c2f", "claude-code"));
        db.RecordDecisions(Session.SessionId, "CodeReview", 2,
            [Decisions.Reject(second, 0, "the cache is per request")]);

        BugsQuery.Read(_dir).Candidates.Select(c => c.Title).Should().BeEquivalentTo(["a timeout nobody sets"]);
    }

    /// <summary>
    /// The funnel counts every step, and its last step IS the candidate list.
    /// </summary>
    /// <remarks>
    /// The property worth holding: the numbers describe the set actually on offer. A funnel built
    /// from a second copy of the filter would drift from it the first time one of the two was
    /// edited, and the symptom — a count that promises more material than the list contains — is
    /// invisible until somebody sizes work off it.
    /// </remarks>
    [Fact]
    public void TheFunnelCountsEachStep_AndItsLastStepIsTheListItself()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        var findings = new[]
        {
            Found("a timeout nobody sets"),
            Found("a race on the cache", Category.Security),
            Found("trailing whitespace", severity: Severity.Nit),
            Found("the parser reaches into the transport", Category.Architecture),
        };
        db.RecordRound(Session, Round(), findings, new RoundContext("SCOPE", "7133c2f", "claude-code"));
        db.RecordDecisions(Session.SessionId, "CodeReview", 1,
            [.. findings.Select((_, i) => Decisions.Accept(findings, i))]);
        AcceptAll(db, Round("PlanReview", 1), Found("the plan is silent on failure"));

        var corpus = BugsQuery.Read(_dir);

        corpus.Funnel.All.Should().Be(5, "every finding in the database");
        corpus.Funnel.OnCode.Should().Be(4, "the plan round's remark is not about code");
        corpus.Funnel.Accepted.Should().Be(4);
        corpus.Funnel.Gating.Should().Be(3, "the Nit is not a defect worth teaching");
        corpus.Funnel.Runtime.Should().Be(2, "Architecture is a judgement about shape");
        corpus.Funnel.Located.Should().Be(2);
        corpus.Funnel.Unprocessed.Should().Be(2);
        corpus.Candidates.Should().HaveCount(corpus.Funnel.Unprocessed, "the last step is the list");
    }

    /// <summary>A finding a run has already handled is not offered to the next one.</summary>
    /// <remarks>
    /// This is what <c>collect_state</c> is for, and why it is text: `all: true` can still see it,
    /// so a run with a better prompt reaches the findings an earlier run skipped. A flag would have
    /// spent them.
    /// </remarks>
    [Fact]
    public void AFindingAlreadyHandled_IsNotOfferedAgain_ButCanStillBeAskedFor()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        AcceptAll(db, Round(), Found("a timeout nobody sets"), Found("a race on the cache"));
        Handled("a race on the cache", "skipped", "fix_commit_not_found");

        BugsQuery.Read(_dir).Candidates.Select(c => c.Title)
            .Should().BeEquivalentTo(["a timeout nobody sets"]);

        var everything = BugsQuery.Read(_dir, all: true).Candidates;
        everything.Should().HaveCount(2);
        var handled = everything.Single(c => c.Title == "a race on the cache");
        handled.CollectState.Should().Be("skipped");
        handled.CollectReason.Should().Be("fix_commit_not_found", "a skip rate nobody can read is not a measurement");
    }

    [Fact]
    public void NoDatabaseAtAll_IsAnEmptyCorpus_NotAFailure()
    {
        var corpus = BugsQuery.Read(Path.Combine(_dir, "nothing-here"));

        corpus.Candidates.Should().BeEmpty();
        corpus.Funnel.All.Should().Be(0);
    }

    [Fact]
    public void TheListIsCapped_ButTheCountIsNot()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        AcceptAll(db, Round(), [.. Enumerable.Range(0, 5).Select(i => Found($"defect {i}"))]);

        var corpus = BugsQuery.Read(_dir, limit: 2);

        corpus.Candidates.Should().HaveCount(2);
        corpus.Funnel.Unprocessed.Should().Be(5, "a caller that sized the work off the list would size it off the cap");
    }

    /// <summary>The mode has its own argument, so a binary without it refuses rather than guesses.</summary>
    [Fact]
    public void TheCorpusHasItsOwnMode_AndTheOtherReadsAreUntouched()
    {
        Program.Classify(["--bugs-json"]).Should().Be(Program.Startup.Bugs);
        Program.Classify(["--log"]).Should().Be(Program.Startup.Log, "the log read is untouched");
        Program.Classify(["--bugs"]).Should().Be(Program.Startup.Usage, "a near miss is refused, not guessed");
    }

    /// <summary>The mode prints the corpus as JSON, which is the only shape the panel can read.</summary>
    /// <remarks>
    /// Driven through <c>Program.BugsJson</c> rather than through <c>BugsQuery</c> because the
    /// serialisation is the half that can fail on its own: this binary has reflection-free JSON, so
    /// a shape missing from <c>ServerJsonContext</c> throws at run time while every unit test of the
    /// reader stays green.
    /// </remarks>
    [Fact]
    public void TheModePrintsTheCorpusAsJson()
    {
        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            AcceptAll(db, Round(), Found("a timeout nobody sets"), Found("a race on the cache"));
        }

        var answer = RunMode(["--bugs-json"], out var code);

        code.Should().Be(0);
        answer.GetProperty("funnel").GetProperty("unprocessed").GetInt32().Should().Be(2);
        var titles = answer.GetProperty("candidates").EnumerateArray()
            .Select(c => c.GetProperty("title").GetString());
        titles.Should().BeEquivalentTo(["a timeout nobody sets", "a race on the cache"]);
    }

    /// <summary>A data directory with no database is nought candidates and a success.</summary>
    /// <remarks>
    /// The mode OPENS the database before it reads, which on an empty directory creates one. That
    /// is what makes this the happy path rather than an error: a machine where no round has ever run
    /// has no corpus, and saying so is the right answer.
    /// </remarks>
    [Fact]
    public void TheModeOnAMachineThatHasReviewedNothing_SaysSo()
    {
        var answer = RunMode(["--bugs-json"], out var code);

        code.Should().Be(0);
        answer.GetProperty("funnel").GetProperty("all").GetInt32().Should().Be(0);
        answer.GetProperty("candidates").GetArrayLength().Should().Be(0);
    }

    /// <summary><c>--all</c> reaches the findings a run has already handled.</summary>
    [Fact]
    public void TheModePassesAllThrough_SoASecondLookIsPossible()
    {
        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            AcceptAll(db, Round(), Found("a timeout nobody sets"), Found("a race on the cache"));
            Handled("a race on the cache", "collected", "");
        }

        RunMode(["--bugs-json"], out _).GetProperty("candidates").GetArrayLength().Should().Be(1);
        RunMode(["--bugs-json", "--all"], out _).GetProperty("candidates").GetArrayLength().Should().Be(2);
    }

    /// <summary>Run the one-shot mode against this test's data directory and parse what it printed.</summary>
    private System.Text.Json.JsonElement RunMode(string[] args, out int code)
    {
        Environment.SetEnvironmentVariable("COAI_DATA_DIR", _dir);
        var written = new StringWriter();
        var was = Console.Out;
        Console.SetOut(written);
        try
        {
            code = Program.BugsJson(args);
        }
        finally
        {
            Console.SetOut(was);
            Environment.SetEnvironmentVariable("COAI_DATA_DIR", null);
        }

        return System.Text.Json.JsonDocument.Parse(written.ToString()).RootElement.Clone();
    }

    /// <summary>Mark one finding as a collector run would, by its title.</summary>
    private void Handled(string title, string state, string reason)
    {
        using var write = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={Path.Combine(_dir, RoundsDb.FileName)};Pooling=False");
        write.Open();
        using var command = write.CreateCommand();
        command.CommandText =
            "UPDATE findings SET collect_state = $state, collect_reason = $reason WHERE title = $title";
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$reason", reason);
        command.Parameters.AddWithValue("$title", title);
        command.ExecuteNonQuery().Should().Be(1, "the fixture must mark exactly the finding it meant to");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
