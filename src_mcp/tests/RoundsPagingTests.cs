using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The log asks for a PAGE, and lets SQL do the counting.
/// </summary>
/// <remarks>
/// <para>Measured on the real database before any of this was written: <c>--log --limit 300</c>
/// answers <b>3.83 MB</b>, of which the 236 round rows are <b>0.05 MB</b>. The other 98.7 % is
/// 3 484 findings shipped for every round although the page opens them one at a time, on a click.
/// So the page is not what costs; the findings inside it are.</para>
/// <para>The cursor is a PAIR — <c>started_utc</c> and the row id — and one of these tests is about
/// nothing else. Two rounds can start in the same second, and a cursor that is only a timestamp
/// either skips the second one or hands it back twice, which is the offset defect arrived at from
/// the other direction.</para>
/// </remarks>
public sealed class RoundsPagingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-p-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    private static SessionState Session(string id = "s1") =>
        new(id, "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };

    private static Finding Found(string title, string provider = "codex") =>
        new(Severity.Major, Category.Reliability, "src/Panel.cs", 40, title, title + " — because", "do this", [provider])
        {
            Role = "SecurityReliability",
        };

    private static RoundRecord Round(int number, DateTime started) =>
        new("CodeReview", number, "proceed", 2, "all 3 reviewers answered", started)
        {
            StartedUtc = started,
            Subject = "SCOPE — something",
            ReviewerStates = [new ReviewerState("codex", "Architecture", ReviewerState.Done, 1, "", 23.4)],
        };

    /// <summary>Rounds numbered 1..n, oldest first, one second apart unless they are told to tie.</summary>
    private void Fill(int howMany, bool allAtOnce = false)
    {
        var start = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        using var db = RoundsDb.Open(_dir, _log)!;
        for (var n = 1; n <= howMany; n++)
        {
            var started = allAtOnce ? start : start.AddSeconds(n);
            db.RecordRound(Session(), Round(n, started), [Found($"finding of round {n}")]);
        }
    }

    [Fact]
    public void APageIsTwoHundredRounds_AndTheTotalSaysHowManyThereAre()
    {
        // The instruction, verbatim: "возвр все из БД не нужно. у нас есть пагинаций. 200 на стр
        // достаточно." A page is what is drawn; the total is what SQL knows.
        Fill(250);

        var log = RoundsQuery.Read(_dir);

        log.Rounds.Should().HaveCount(200, "200 per page is enough");
        log.Totals.Rounds.Should().Be(250, "the total is counted by the database, not by the array that was sent");
    }

    [Fact]
    public void TheNextPageStartsWhereTheLastOneEnded()
    {
        Fill(250);

        var first = RoundsQuery.Read(_dir);
        var second = RoundsQuery.Read(_dir, before: first.Rounds[^1].Cursor);

        second.Rounds.Should().HaveCount(50);
        second.Rounds.Select(r => r.Number).Should().NotIntersectWith(first.Rounds.Select(r => r.Number),
            "a second page that repeats the first is a page nobody can trust");
        second.Rounds[0].Number.Should().Be(first.Rounds[^1].Number - 1, "newest first, and continuous");
    }

    [Fact]
    public void ACursorIsAPair_SoTwoRoundsStartingInTheSameSecondAreNeitherSkippedNorRepeated()
    {
        // The whole reason paging is keyed rather than offset — and the reason the key is not just
        // the timestamp it is ordered by. `started_utc` is not unique; a review that runs three
        // rounds in a burst writes three rows in one second.
        Fill(10, allAtOnce: true);

        var seen = new List<int>();
        var cursor = string.Empty;
        for (var page = 0; page < 10; page++)
        {
            var got = RoundsQuery.Read(_dir, limit: 3, before: cursor);
            if (got.Rounds.Count == 0)
            {
                break;
            }

            seen.AddRange(got.Rounds.Select(r => r.Number));
            cursor = got.Rounds[^1].Cursor;
        }

        seen.Should().BeEquivalentTo(Enumerable.Range(1, 10),
            "every round exactly once, however many share a second");
    }

    [Fact]
    public void AListedRoundCarriesNoFindings_TheyAreAskedForWhenARowIsOpened()
    {
        Fill(3);

        var json = System.Text.Json.JsonSerializer.Serialize(
            RoundsQuery.Read(_dir), ServerJsonContext.Default.LoggedLog);

        json.Should().NotContain("finding of round",
            "98.7 % of the payload was findings for rounds nobody opened");
    }

    [Fact]
    public void AListedRoundSaysHOWMANYItFound_EvenThoughItNoLongerCarriesThem()
    {
        // Load-bearing, not decoration: the page tells a gate still open from a round that raised
        // nothing, and the only evidence for the second is that no finding exists. Take the count
        // away with the findings and every clean round reads as awaiting a resolve nobody owes.
        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            db.RecordRound(Session(), Round(1, DateTime.UtcNow.AddMinutes(-6)), [Found("a"), Found("b")]);
            db.RecordRound(Session(), Round(2, DateTime.UtcNow.AddMinutes(-5)), []);
        }

        var rounds = RoundsQuery.Read(_dir).Rounds;

        rounds.Single(r => r.Number == 1).FoundCount.Should().Be(2);
        rounds.Single(r => r.Number == 2).FoundCount.Should().Be(0, "a clean round is not an unknown one");
        rounds.Should().OnlyContain(r => r.Findings.Count == 0);
    }

    [Fact]
    public void TheTotalsCountTheWholeTable_NotThePage()
    {
        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            var findings = new[] { Found("a"), Found("b"), Found("c") };
            db.RecordRound(Session(), Round(1, DateTime.UtcNow.AddMinutes(-5)), findings);
            db.RecordDecisions("s1", "CodeReview", 1,
            [
                new Decision.Accepted(findings[0]),
                new Decision.Accepted(findings[1]),
                new Decision.Rejected(findings[2], "not worth the machinery"),
            ]);
        }

        var totals = RoundsQuery.Read(_dir, limit: 1).Totals;

        totals.Rounds.Should().Be(1);
        totals.Findings.Should().Be(3);
        totals.Accepted.Should().Be(2);
        totals.Rejected.Should().Be(1);
    }

    [Fact]
    public void TheFindingsOfOneRound_ComeBackInOrdinalOrder()
    {
        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            db.RecordRound(Session(), Round(1, DateTime.UtcNow.AddMinutes(-5)),
                [Found("first"), Found("second"), Found("third")]);
        }

        var answer = RoundsQuery.FindingsOf(_dir, "s1", "CodeReview", 1);

        answer.Known.Should().BeTrue();
        answer.Findings.Select(f => f.Title).Should().Equal("first", "second", "third");
    }

    [Fact]
    public void ARoundTheDatabaseDoesNotHold_IsNotAnEmptyList()
    {
        // The distinction the page's five states rest on. An empty answer cannot say whether the
        // round was clean or was never recorded, so the protocol says it instead of the renderer
        // guessing. (Plan round, codex — Blocking.)
        Fill(1);

        RoundsQuery.FindingsOf(_dir, "s1", "CodeReview", 1).Known.Should().BeTrue();
        RoundsQuery.FindingsOf(_dir, "s1", "CodeReview", 99).Known.Should()
            .BeFalse("a round nobody recorded is not a round that found nothing");
    }

    [Fact]
    public void ALimitOutsideItsBounds_IsClamped()
    {
        // Plan round: the CLI is a boundary, and a boundary that trusts its input is not one.
        Program.Limit(["--log", "--limit", "50"]).Should().Be(50);
        Program.Limit(["--log"]).Should().Be(RoundsQuery.DefaultLimit);
        Program.Limit(["--log", "--limit", "not a number"]).Should().Be(RoundsQuery.DefaultLimit);
        Program.Limit(["--log", "--limit", "0"]).Should().Be(1, "nothing is not a page size");
        Program.Limit(["--log", "--limit", "-4"]).Should().Be(1);
        Program.Limit(["--log", "--limit", "100000"]).Should().Be(RoundsQuery.MaxLimit);
    }

    [Fact]
    public void AMalformedCursor_IsTreatedAsAbsent()
    {
        Fill(5);

        RoundsQuery.Read(_dir, before: "not a cursor").Rounds.Should().HaveCount(5,
            "an unreadable cursor asks for the first page, it does not ask for nothing");
        Program.Before(["--log", "--paged"]).Should().BeEmpty();
        Program.Before(["--log", "--before", "2026-09-01T12:00:03.0000000Z|7"]).Should()
            .Be("2026-09-01T12:00:03.0000000Z|7");
    }

    [Fact]
    public void PagingAndTheFindingsReadAreTheirOwnStartupModes()
    {
        Program.Classify(["--findings"]).Should().Be(Program.Startup.Findings);
        Program.Classify(["--log"]).Should().Be(Program.Startup.Log, "and the old shape still answers");
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
}
