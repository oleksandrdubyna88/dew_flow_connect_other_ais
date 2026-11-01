using System.Security.Cryptography;
using System.Text.Json;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using static CoaiMcp.Tests.AuthorsSnapshotFixtures;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--log</c> carries the state of each round's authors (E5.S3 of <c>research/PLAN_a_round_records_who_wrote_the_code_and_how_much_E5.md</c>):
/// <c>LoggedRound.AuthorsState</c> is read in one query per page from <c>rounds.authors_state</c> of schema step 24, ABSENT on a round that
/// has none, and a database written before step 24 is read exactly as before — read-only, and left at the version it had.
/// </summary>
/// <remarks>
/// <para>Real SQLite over a <c>testsToDelete-…</c> temp directory, seeded through <c>RecordRound</c> — the writer S2 shipped — so the reader is
/// proved against what the writer writes. <b>Not recorded is never a word</b>: an empty column and a value the shared rule refuses
/// (<see cref="Core.Authors.AuthorsSnapshot.IsWellFormed"/>) read as an ABSENT member, and a malformed row never fails the page.</para>
/// </remarks>
public sealed class TheLogNamesEachRoundsAuthorsTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("testsToDelete-coai-logauthors-");
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _dir.Dispose();
    }

    private string Database => _dir.At(RoundsDb.FileName);

    private void Record(params RoundRecord[] rounds)
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        foreach (var round in rounds)
        {
            db.RecordRound(State(), round, []);
        }
    }

    private void Execute(string sql, params (string Name, string Value)[] values)
    {
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False");
        db.Open();
        using var write = db.CreateCommand();
        write.CommandText = sql;
        foreach (var (name, value) in values)
        {
            write.Parameters.AddWithValue(name, value);
        }

        write.ExecuteNonQuery();
    }

    private static Core.Authors.AuthorsSnapshot With(string state, params string[] reasons) =>
        Observed() with { State = state, Reasons = [.. reasons] };

    [Fact]
    public void ARoundAfterTheStep_CarriesItsState()
    {
        Record(
            Round("CodeReview", 1, Observed()),
            Round("CodeReview", 2, With("partial", "read budget")),
            Round("CodeReview", 3, With("partial", "peer running")),
            Round("PlanReview", 4, Unobserved()));

        var log = RoundsQuery.Read(_dir);

        log.Rounds.Single(r => r.Number == 1).AuthorsState.Should().Be("observed");
        log.Rounds.Single(r => r.Number == 2).AuthorsState.Should().Be("partial: read budget");
        log.Rounds.Single(r => r.Number == 3).AuthorsState.Should().Be("partial: peer running", "a provisional round (E4 G3) is logged as it is stored");
        log.Rounds.Single(r => r.Number == 4).AuthorsState.Should().Be("unobserved: transcript not found");
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(log, ServerJsonContext.Default.LoggedLog));
        doc.RootElement.GetProperty("rounds").EnumerateArray().Single(r => r.GetProperty("number").GetInt32() == 2)
            .GetProperty("authorsState").GetString().Should().Be("partial: read budget", "camelCase on the wire");
    }

    [Fact]
    public void ARoundBeforeTheStep_CarriesNone()
    {
        Record(Round("CodeReview", 1, Observed()), Round("CodeReview", 2), Round("PlanReview", 3));
        Execute("UPDATE rounds SET authors_state = '' WHERE number = 1");

        var log = RoundsQuery.Read(_dir);

        log.Rounds.Should().OnlyContain(r => r.AuthorsState == null, "an empty column says not recorded — never a made-up word");
        JsonSerializer.Serialize(log, ServerJsonContext.Default.LoggedLog).Should().NotContain("authorsState", "absent, not null and not the words 'not recorded'");
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("observed: x")]
    [InlineData("done")]
    [InlineData("partial: read budget ×2")]
    public void AMalformedState_IsAbsentNotAnError(string stored)
    {
        Record(Round("CodeReview", 1, Observed()), Round("CodeReview", 2, Observed(number: 2)));
        Execute("UPDATE rounds SET authors_state = $stored WHERE number = 1", ("$stored", stored));

        var rounds = RoundsQuery.Read(_dir).Rounds;

        rounds.Should().HaveCount(2, "a malformed row never fails the page");
        rounds.Single(r => r.Number == 1).AuthorsState.Should().BeNull("a value the shared rule refuses is not recorded");
        rounds.Single(r => r.Number == 2).AuthorsState.Should().Be("observed", "the next row's state is still read");
    }

    [Fact]
    public void EachStateLandsOnItsOwnRound_AcrossPages()
    {
        Record([.. Enumerable.Range(1, 30).Select(n => Round("CodeReview", n, n % 2 == 0 ? Observed(number: n) : With("partial", "read budget")))]);

        var first = RoundsQuery.Read(_dir, limit: 15);
        var second = RoundsQuery.Read(_dir, limit: 15, before: first.Rounds[^1].Cursor);

        first.Rounds.Select(r => r.Number).Should().Equal(Enumerable.Range(16, 15).Reverse(), "the newest fifteen");
        second.Rounds.Select(r => r.Number).Should().Equal(Enumerable.Range(1, 15).Reverse(), "the other fifteen, none repeated");
        foreach (var round in first.Rounds.Concat(second.Rounds))
        {
            round.AuthorsState.Should().Be(round.Number % 2 == 0 ? "observed" : "partial: read budget", "every round carries its own state, none of another page's");
        }
    }

    [Fact]
    public void ARoundList_InAnotherOrder_OrFiltered_StillGetsEachStateOnItsOwnRound()
    {
        Record([.. Enumerable.Range(1, 6).Select(n => Round("CodeReview", n, n % 2 == 0 ? Observed(number: n) : With("partial", "peer running")))]);
        var page = RoundsQuery.Read(_dir).Rounds.Select(r => r with { AuthorsState = null }).ToList();
        var shuffled = page.Where(r => r.Number != 3).OrderBy(r => r.Number % 2).ThenBy(r => r.Number).ToList();

        using var db = new SqliteConnection($"Data Source={Database};Pooling=False;Mode=ReadOnly");
        db.Open();
        var attached = RoundAuthorsLog.Attach(db, shuffled);

        attached.Select(r => r.Number).Should().Equal(shuffled.Select(r => r.Number), "attaching changes no round and no order");
        attached.Should().OnlyContain(r => r.AuthorsState == (r.Number % 2 == 0 ? "observed" : "partial: peer running"),
            "each state is found by the round's own id — a list in another order, or one round short, is not paired by position");
    }

    [Fact]
    public void AStepTwentyThreeFile_IsLoggedAsBefore_AndTheRoundCountsLeaveItByteForByte()
    {
        var steps = Schema.Steps[..Array.IndexOf(Schema.Steps, Schema.TheRoundsAuthors)];
        steps.Should().HaveCount(23, "the fixture is the previous release's database");
        MakeAt(steps);
        var before = SHA256.HashData(File.ReadAllBytes(Database));

        var (code, text, _) = OneShotProcess.Run(_dir, "--log", "--paged");

        code.Should().Be(0, "a database from before the step is read exactly as before — not 74, no such column");
        using var doc = JsonDocument.Parse(text);
        var round = doc.RootElement.GetProperty("rounds").EnumerateArray().Single();
        round.TryGetProperty("authorsState", out _).Should().BeFalse("a round from before step 24 has no state");
        Version().Should().Be(23, "a read-only reader never migrates the file");
        SHA256.HashData(File.ReadAllBytes(Database)).Should().Equal(before, "--log leaves the file byte for byte");

        var (countCode, _, _) = OneShotProcess.Run(_dir, "--round-counts", "--now", new DateTime(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc).ToString("O"));

        countCode.Should().Be(0);
        SHA256.HashData(File.ReadAllBytes(Database)).Should().Equal(before, "--round-counts reads nothing new and leaves the file byte for byte");
    }

    private void MakeAt(IReadOnlyList<string> steps)
    {
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False");
        db.Open();
        using var make = db.CreateCommand();
        make.CommandText = string.Join(";\n", steps) + $"; PRAGMA user_version={steps.Count};"
            + "INSERT INTO sessions (id, repo_path, branch, opened_utc) VALUES ('s', 'D:/repo', 'main', '2026-10-01T00:00:00.0000000Z');"
            + "INSERT INTO rounds (id, session_id, stage, number, status, verdict, started_utc, completed_utc) VALUES (1, 's', 'CodeReview', 1, 'done', 'proceed', '2026-10-01T00:00:00.0000000Z', '2026-10-01T00:05:00.0000000Z');";
        make.ExecuteNonQuery();
        db.Close();
        SqliteConnection.ClearAllPools();
    }

    private long Version()
    {
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var ask = db.CreateCommand();
        ask.CommandText = "PRAGMA user_version";

        return (long)ask.ExecuteScalar()!;
    }
}
