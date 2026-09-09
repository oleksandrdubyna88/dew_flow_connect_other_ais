using System.Collections.Immutable;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Reading the rounds database back — for the log page, and for the two questions it exists for.
/// </summary>
/// <remarks>
/// The page shows counts because counts were all there was. Now there are findings, and this is the
/// read side of them: the round with its findings, what was decided about each, and the two
/// aggregates the operator asked for on 2026-09-05 — what the caller ACCEPTS (a blind spot it
/// admitted) grouped by category, role and vendor, and what it rejected and had raised again.
/// </remarks>
public sealed class RoundsQueryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-q-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    private static readonly SessionState Session =
        new("s1", "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };

    private static Finding Found(
        string title,
        Category category = Category.Reliability,
        string role = "SecurityReliability",
        string provider = "codex") =>
        new(Severity.Major, category, "src/Panel.cs", 40, title, title + " — because", "do this", [provider])
        {
            Role = role,
        };

    private static RoundRecord Round(int number = 1) =>
        new("CodeReview", number, "proceed", 2, "all 3 reviewers answered", DateTime.UtcNow)
        {
            StartedUtc = DateTime.UtcNow.AddMinutes(-4),
            Subject = "SCOPE — something",
            ReviewerStates = [new ReviewerState("codex", "Architecture", ReviewerState.Done, 2, "", 23.4)],
        };

    [Fact]
    public void ARoundComesBackWithTheFindingsItProduced_AndWhatWasDecided()
    {
        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            var findings = new[] { Found("session file opened without FileShare"), Found("the retry never gives up") };
            db.RecordRound(Session, Round(), findings);
            db.RecordDecisions("s1", "CodeReview", 1,
                [new Decision.Accepted(findings[0]), new Decision.Rejected(findings[1], "the loop has a timeout")]);
        }

        var round = RoundsQuery.Read(_dir).Rounds.Should().ContainSingle().Subject;
        round.RepoPath.Should().Be("D:/repo");
        round.Branch.Should().Be("feat/x");
        round.Stage.Should().Be("CodeReview");
        round.Number.Should().Be(1);
        round.Accepted.Should().Be(1);
        round.Rejected.Should().Be(1);
        round.Findings.Should().BeEmpty("a LISTED round carries counts; its findings are asked for when a row is opened");

        // The findings themselves, through the read an opened row makes. They used to ride the list,
        // which cost 3.78 MB of a 3.83 MB payload for rounds nobody had opened.
        var found = RoundsQuery.FindingsOf(_dir, "s1", "CodeReview", 1);
        found.Known.Should().BeTrue();
        found.Findings.Should().HaveCount(2);
        found.Findings[0].Title.Should().Be("session file opened without FileShare");
        found.Findings[0].Why.Should().NotBeEmpty("the page shows what the finding SAID, not that it existed");
        found.Findings[0].Resolution.Should().Be("accept");
        found.Findings[1].Resolution.Should().Be("reject");
        found.Findings[1].Reason.Should().Be("the loop has a timeout");
    }

    [Fact]
    public void WhatTheCallerACCEPTS_IsGroupedByCategoryRoleAndVendor()
    {
        // The blind-spot corpus: an accepted finding is something the caller had not seen and then
        // agreed was worth having. Accepted over TOTAL, because a category that produces fifty and
        // gets two accepted says something different from one that produces two and gets both.
        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            var findings = new[]
            {
                Found("a", Category.Security, "SecurityReliability", "codex"),
                Found("b", Category.Security, "SecurityReliability", "codex"),
                Found("c", Category.Ux, "UxDxPerformance", "gemini"),
            };
            db.RecordRound(Session, Round(), findings);
            db.RecordDecisions("s1", "CodeReview", 1,
            [
                new Decision.Accepted(findings[0]),
                new Decision.Accepted(findings[1]),
                new Decision.Rejected(findings[2], "not worth the machinery"),
            ]);
        }

        var spots = RoundsQuery.Read(_dir).BlindSpots;

        spots.Should().Contain(s => s.Kind == "category" && s.Name == "Security" && s.Accepted == 2 && s.Total == 2);
        spots.Should().Contain(s => s.Kind == "category" && s.Name == "Ux" && s.Accepted == 0 && s.Total == 1);
        spots.Should().Contain(s => s.Kind == "role" && s.Name == "SecurityReliability" && s.Accepted == 2);
        spots.Should().Contain(s => s.Kind == "providers" && s.Name == "codex" && s.Accepted == 2);
    }

    [Fact]
    public void AnUndecidedFinding_CountsTowardsNeither()
    {
        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            db.RecordRound(Session, Round(), [Found("nobody has judged this")]);
        }

        RoundsQuery.Read(_dir).BlindSpots.Should().BeEmpty("a finding nobody decided is not evidence either way");
    }

    [Fact]
    public void AFindingRaisedAgainAndRejectedAgain_IsListedOnItsOwn()
    {
        // The more interesting kind of disagreement, and a much shorter list than the accepted one.
        var standing = Found("session file opened without FileShare");
        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            db.RecordRound(Session, Round(2), [standing, Found("something new")],
                new RoundContext("SCOPE", "7133c2f", "claude-code", [standing]));
            db.RecordDecisions("s1", "CodeReview", 2,
                [new Decision.Rejected(standing, "still no"), new Decision.Accepted(Found("something new"))]);
        }

        var defended = RoundsQuery.Read(_dir).Defended;

        defended.Should().ContainSingle().Which.Title.Should().Be("session file opened without FileShare");
        defended[0].ReRaised.Should().BeTrue();
        defended[0].Reason.Should().Be("still no");
    }

    [Fact]
    public void AFindingRaisedAgainAndThenACCEPTED_IsNotADefendedDisagreement()
    {
        // The opposite case, and the one the code gate caught: a repeat the caller was PERSUADED by
        // is not a disagreement it is defending. Listing it there would report a false one.
        var persuaded = Found("session file opened without FileShare");
        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            db.RecordRound(Session, Round(2), [persuaded],
                new RoundContext("SCOPE", "7133c2f", "claude-code", [persuaded]));
            db.RecordDecisions("s1", "CodeReview", 2, [new Decision.Accepted(persuaded)]);
        }

        RoundsQuery.Read(_dir).Defended.Should().BeEmpty();
    }

    [Fact]
    public void AFindingRaisedAgainAndNotYetDecided_IsNotListedEither()
    {
        var open = Found("session file opened without FileShare");
        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            db.RecordRound(Session, Round(2), [open], new RoundContext("SCOPE", "7133c2f", "claude-code", [open]));
        }

        RoundsQuery.Read(_dir).Defended.Should().BeEmpty("nobody has argued with it a second time yet");
    }

    [Fact]
    public void NewestRoundsFirst_AndNoMoreThanAsked()
    {
        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            for (var number = 1; number <= 5; number++)
            {
                db.RecordRound(
                    Session,
                    Round(number) with { StartedUtc = DateTime.UtcNow.AddMinutes(-10 + number) },
                    [Found("round " + number)]);
            }
        }

        var rounds = RoundsQuery.Read(_dir, limit: 2).Rounds;

        rounds.Should().HaveCount(2);
        rounds[0].Number.Should().Be(5, "newest first, because that is what a log page opens on");
    }

    [Fact]
    public void ADatabaseThatIsNotThereIsAnEmptyLog_NotAFailure()
    {
        // A machine that has never run a round is asking a fair question and deserves an answer it
        // can render.
        var log = RoundsQuery.Read(Path.Combine(_dir, "nothing here"));

        log.Rounds.Should().BeEmpty();
        log.BlindSpots.Should().BeEmpty();
        log.Defended.Should().BeEmpty();
    }

    [Fact]
    public void TheLogIsAWholeStartupMode_LikeVersionAndHelp()
    {
        Program.Classify(["--log"]).Should().Be(Program.Startup.Log);
        Program.Classify([]).Should().Be(Program.Startup.Serve, "and an MCP client still gets the protocol");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not a failing test.
        }
    }

    [Fact]
    public void AMissingNativeLibrary_IsAnEmptyLogAndAnExplanation_NotACrash()
    {
        // 0.18.1 shipped without e_sqlite3 beside the binary, and `--log` died with a
        // TypeInitializationException wrapping DllNotFoundException - from the type initializer, so
        // the handler's filter (SqliteException, IOException, UnauthorizedAccessException) never saw
        // it. The one command a person runs to find out what happened was the one that crashed.
        // The REAL chain, taken from the published 0.18.2 binary with its library removed. The first
        // version of this test built it by hand as TypeInitializationException wrapping
        // DllNotFoundException, and the fix then agreed with the fixture rather than with reality:
        // the runtime puts a TargetInvocationException between them, the recursion stopped at a type
        // it did not know, and the shipped binary crashed exactly as before. A fixture the code
        // accepts proves nothing about the fault it was written for.
        var missing = new TypeInitializationException(
            "SQLitePCL.raw",
            new System.Reflection.TargetInvocationException(
                new DllNotFoundException("Unable to load DLL 'e_sqlite3' or one of its dependencies")));

        Program.Unreadable(missing).Should().BeTrue("an empty log beats a stack trace");
        Program.WhyUnreadable(missing).Should().Contain("e_sqlite3")
            .And.Contain("beside", "the fix is to put the library next to the executable");
    }

    [Fact]
    public void AnOrdinaryDatabaseProblem_StillReadsAsItself()
    {
        var empty = new Microsoft.Data.Sqlite.SqliteException("no such table: findings", 1);

        Program.Unreadable(empty).Should().BeTrue();
        Program.WhyUnreadable(empty).Should().Contain("no such table");
    }

    [Fact]
    public void SomethingThatIsNotADatabaseProblem_IsNotSwallowed()
    {
        Program.Unreadable(new InvalidOperationException("a bug of ours")).Should().BeFalse(
            "a fault that has nothing to do with the database must not come back as an empty log");
    }
}
