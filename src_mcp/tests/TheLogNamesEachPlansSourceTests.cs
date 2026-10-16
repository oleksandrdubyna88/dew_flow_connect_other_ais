using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--log</c> carries what each plan round reviewed (E2.S2 of <c>todo/PLAN_the_plan_gate_reads_the_committed_plan_E2.md</c>,
/// contract C2): <c>LoggedRound.PlanSource</c> is read in one query per page from <c>rounds.plan_source</c> of schema step 23,
/// absent on a round that has none, and a database written before step 23 is read exactly as before — read-only, and left at
/// the version it had.
/// </summary>
/// <remarks>
/// <para>Real SQLite over a temp directory, seeded through <c>RecordRound</c> — the writer E2.S1 shipped — so the reader is
/// proved against what the writer writes. <b>Not recorded is never empty</b>: the column's <c>''</c>, a value that does not parse
/// and an origin nobody knows all read as an ABSENT member, and a malformed row never fails the page.</para>
/// </remarks>
public sealed class TheLogNamesEachPlansSourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-logplansrc-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }
    }

    private static SessionState State => new("s1", "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.PlanReview };

    private static readonly DateTime Start = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    private static PlanSource CommittedAt(int n) => new(
        PlanOrigin.Commit, new string('a', 40), $"todo/PLAN_{n}.md", new string('b', 40), 1000 + n, "0123456789abcdef");

    private static readonly PlanSource Pasted = new(PlanOrigin.Text, new string('c', 40), "", "", 512, "fedcba9876543210");

    private static RoundRecord Round(int number, PlanSource? source = null, string stage = "PlanReview") =>
        new(stage, number, "proceed", 0, "answered", Start.AddMinutes(number))
        {
            StartedUtc = Start.AddMinutes(number),
            PlanSource = source,
        };

    private void Record(params RoundRecord[] rounds)
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        foreach (var round in rounds)
        {
            db.RecordRound(State, round, []);
        }
    }

    private string Database => Path.Combine(_dir, RoundsDb.FileName);

    private void Execute(string sql)
    {
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False");
        db.Open();
        using var write = db.CreateCommand();
        write.CommandText = sql;
        write.ExecuteNonQuery();
    }

    [Fact]
    public void ARoundAfterTheStep_CarriesItsSource()
    {
        Record(Round(1, CommittedAt(1)), Round(2, Pasted));

        var log = RoundsQuery.Read(_dir);

        log.Rounds.Single(r => r.Number == 1).PlanSource.Should().Be(CommittedAt(1), "the round was recorded with a committed source");
        log.Rounds.Single(r => r.Number == 2).PlanSource.Should().Be(Pasted, "a pasted plan keeps its origin, text id and byte count");
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(log, ServerJsonContext.Default.LoggedLog));
        var wire = doc.RootElement.GetProperty("rounds").EnumerateArray().Single(r => r.GetProperty("number").GetInt32() == 1)
            .GetProperty("planSource");
        wire.GetProperty("origin").GetString().Should().Be("commit");
        wire.GetProperty("path").GetString().Should().Be("todo/PLAN_1.md");
        wire.GetProperty("bytes").GetInt64().Should().Be(1001);
        wire.GetProperty("textId").GetString().Should().Be("0123456789abcdef");
    }

    [Fact]
    public void ARoundBeforeTheStep_CarriesNone()
    {
        Record(Round(1, CommittedAt(1)), Round(2), Round(3, source: null, stage: "CodeReview"));
        Execute("UPDATE rounds SET plan_source = '' WHERE number = 1");

        var log = RoundsQuery.Read(_dir);

        log.Rounds.Should().OnlyContain(r => r.PlanSource == null, "an empty column says not recorded — never an empty source");
        JsonSerializer.Serialize(log, ServerJsonContext.Default.LoggedLog).Should().NotContain("planSource", "absent, not null");
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"origin\":\"ftp\",\"commit\":\"a\",\"path\":\"p\",\"blob\":\"b\",\"bytes\":1,\"textId\":\"t\"}")]
    [InlineData("{\"origin\":\"commit\",\"bytes\":\"x\"}")]
    public void AMalformedColumn_IsAbsentNotAnError(string stored)
    {
        Record(Round(1, CommittedAt(1)), Round(2, CommittedAt(2)));
        using (var db = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            db.Open();
            using var write = db.CreateCommand();
            write.CommandText = "UPDATE rounds SET plan_source = $stored WHERE number = 1";
            write.Parameters.AddWithValue("$stored", stored);
            write.ExecuteNonQuery();
        }

        var rounds = RoundsQuery.Read(_dir).Rounds;

        rounds.Should().HaveCount(2, "a malformed row never fails the page");
        rounds.Single(r => r.Number == 1).PlanSource.Should().BeNull("a value that does not parse, or names an origin nobody knows, is not recorded");
        rounds.Single(r => r.Number == 2).PlanSource.Should().Be(CommittedAt(2), "the next row's source is still read");
    }

    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Oid = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static string Stored(string origin = "commit", string commit = Sha, string path = "todo/PLAN_x.md", string blob = Oid,
        string bytes = "98304", string textId = "0123456789abcdef") =>
        $$"""{"origin":"{{origin}}","commit":"{{commit}}","path":"{{path}}","blob":"{{blob}}","bytes":{{bytes}},"textId":"{{textId}}"}""";

    public static TheoryData<string, bool> ShapedSources => new()
    {
        { Stored(), true },
        { Stored("text", path: "", blob: "", textId: "fedcba9876543210"), true },
        { Stored("text", commit: "", path: "", blob: ""), true },
        { Stored(bytes: "0"), true },
        { Stored("commit", commit: "", path: "", blob: "", bytes: "-1", textId: ""), false },
        { Stored(bytes: "-1"), false },
        { Stored(textId: ""), false },
        { Stored(path: ""), false },
        { Stored(path: "/etc/passwd"), false },
        { Stored(path: "C:/x/PLAN.md"), false },
        { Stored(path: "../PLAN.md"), false },
        { Stored(blob: ""), false },
        { Stored(blob: "zzzzzzzz"), false },
        { Stored(commit: "not-a-sha"), false },
        { Stored(commit: "abc"), false },
        { Stored("text", path: "todo/PLAN_x.md", blob: ""), false },
        { Stored("text", path: "", blob: Oid), false },
        { Stored("text", path: "", blob: "", textId: ""), false },
        { Stored("text", path: "", blob: "", bytes: "-5"), false },
    };

    [Theory]
    [MemberData(nameof(ShapedSources))]
    public void ASourceIsAttachedOnlyWhenItsShapeIsWellFormed(string stored, bool attached)
    {
        Record(Round(1, CommittedAt(1)), Round(2, CommittedAt(2)));
        using (var db = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            db.Open();
            using var write = db.CreateCommand();
            write.CommandText = "UPDATE rounds SET plan_source = $stored WHERE number = 1";
            write.Parameters.AddWithValue("$stored", stored);
            write.ExecuteNonQuery();
        }

        var rounds = RoundsQuery.Read(_dir).Rounds;

        (rounds.Single(r => r.Number == 1).PlanSource is not null).Should().Be(attached,
            "a well-formed origin-specific shape is attached; a negative size, an empty path, text id or hash, an absolute path or a text round with a path is not recorded");
        rounds.Single(r => r.Number == 2).PlanSource.Should().Be(CommittedAt(2));
    }

    [Fact]
    public void EachSourceLandsOnItsOwnRound_AcrossPages()
    {
        Record([.. Enumerable.Range(1, 30).Select(n => Round(n, CommittedAt(n)))]);

        var first = RoundsQuery.Read(_dir, limit: 15);
        var second = RoundsQuery.Read(_dir, limit: 15, before: first.Rounds[^1].Cursor);

        first.Rounds.Select(r => r.Number).Should().Equal(Enumerable.Range(16, 15).Reverse(), "the newest fifteen");
        second.Rounds.Select(r => r.Number).Should().Equal(Enumerable.Range(1, 15).Reverse(), "the other fifteen, none repeated");
        foreach (var round in first.Rounds.Concat(second.Rounds))
        {
            round.PlanSource.Should().Be(CommittedAt(round.Number), "every round carries its own source, none of another page's");
        }
    }

    [Fact]
    public void ARoundList_InAnotherOrder_OrFiltered_StillGetsEachSourceOnItsOwnRound()
    {
        Record([.. Enumerable.Range(1, 6).Select(n => Round(n, CommittedAt(n)))]);
        var page = RoundsQuery.Read(_dir).Rounds.Select(r => r with { PlanSource = null }).ToList();
        var shuffled = page.Where(r => r.Number != 3).OrderBy(r => r.Number % 2).ThenBy(r => r.Number).ToList();

        using var db = new SqliteConnection($"Data Source={Database};Pooling=False;Mode=ReadOnly");
        db.Open();
        var attached = RoundPlanSources.Attach(db, shuffled);

        attached.Select(r => r.Number).Should().Equal(shuffled.Select(r => r.Number), "attaching changes no round and no order");
        attached.Should().OnlyContain(r => r.PlanSource == CommittedAt(r.Number),
            "each source is found by the round's own id — a list in another order, or one round short, is not paired by position");
    }

    [Fact]
    public void AStepTwentyTwoFile_IsLoggedAsBefore()
    {
        var steps = Schema.Steps[..Array.IndexOf(Schema.Steps, Schema.ThePlanSource)];
        steps.Should().HaveCount(22, "the fixture is the previous release's database");
        MakeAt(steps);

        var (code, text, _) = OneShotProcess.Run(_dir, "--log", "--paged");

        code.Should().Be(0, "a database from before the step is read exactly as before — not 74, no such column");
        using var doc = JsonDocument.Parse(text);
        var round = doc.RootElement.GetProperty("rounds").EnumerateArray().Single();
        round.TryGetProperty("planSource", out _).Should().BeFalse("a round from before step 23 has no source");
        Version().Should().Be(22, "a read-only reader never migrates the file");
    }

    private void MakeAt(IReadOnlyList<string> steps)
    {
        Directory.CreateDirectory(_dir);
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False");
        db.Open();
        using var make = db.CreateCommand();
        make.CommandText = string.Join(";\n", steps) + $"; PRAGMA user_version={steps.Count};"
            + "INSERT INTO sessions (id, repo_path, branch, opened_utc) VALUES ('s', 'D:/repo', 'main', '2026-10-01T00:00:00.0000000Z');"
            + "INSERT INTO rounds (id, session_id, stage, number, status, verdict, started_utc, completed_utc) VALUES (1, 's', 'PlanReview', 1, 'done', 'proceed', '2026-10-01T00:00:00.0000000Z', '2026-10-01T00:05:00.0000000Z');";
        make.ExecuteNonQuery();
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
