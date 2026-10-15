using System.Text.Json;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--log</c> carries each round's size (E2.S5 of <c>todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E2.md</c>,
/// contract C3): <c>LoggedRound.Size</c> is read in one grouped query per page from <c>round_changes</c> and the state columns
/// of schema step 22, absent on a round that has none, and a database written before step 22 is read exactly as before —
/// read-only, and left at the version it had.
/// </summary>
/// <remarks>
/// <para>Real SQLite over a temp directory, seeded through <c>RecordRound</c> — the writer E2.S3 shipped — so the reader is
/// proved against what the writer writes. A binary has no line count: NULL in the table, null in the record, ABSENT in the
/// JSON; a reader that took it for 0 would say "a file changed and nothing in it did".</para>
/// </remarks>
public sealed class TheLogNamesEachRoundsSizeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-logsize-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }
    }

    private static SessionState State => new("s1", "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };

    private static readonly DateTime Start = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    private static RoundRecord Round(int number, RoundSize? size = null) =>
        new("CodeReview", number, "proceed", 0, "answered", Start.AddMinutes(number))
        {
            StartedUtc = Start.AddMinutes(number),
            Size = size,
        };

    private static RoundSize AFullSize(string origin = SizeOrigin.Live) => new(
        new ScopeSize(ScopeState.Recorded, "origin/main",
        [
            new SizeCell(ChangeCoverage.Shown, ChangeKind.Code, 3, 40, 5),
            new SizeCell(ChangeCoverage.Excluded, ChangeKind.Config, 1, 100, 20),
            new SizeCell(ChangeCoverage.Binary, ChangeKind.Unknown, 2, null, null),
        ]),
        new ScopeSize(ScopeState.Recorded, "abc123", [new SizeCell(ChangeCoverage.Shown, ChangeKind.Test, 1, 7, 0)]),
        origin);

    private static RoundSize SizeWithAdded(int added) => new(
        new ScopeSize(ScopeState.Recorded, "origin/main", [new SizeCell(ChangeCoverage.Shown, ChangeKind.Code, 1, added, 0)]),
        ScopeSize.Unavailable(ScopeState.FirstRound));

    private void Record(params (RoundRecord Round, string BaseRef)[] rounds)
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        foreach (var (round, baseRef) in rounds)
        {
            db.RecordRound(State, round, [], new RoundContext { BaseRef = baseRef });
        }
    }

    [Fact]
    public void ARoundWithASize_IsLoggedWithIt_AndOneWithoutIsNot()
    {
        Record((Round(1, AFullSize()), "origin/main"), (Round(2), ""));

        var rounds = RoundsQuery.Read(_dir).Rounds;

        var sized = rounds.Single(r => r.Number == 1).Size;
        sized.Should().NotBeNull("the round was recorded with a size");
        sized!.Whole.State.Should().Be(ScopeState.Recorded);
        sized.Whole.From.Should().Be("origin/main", "the whole scope starts at the round's resolved base");
        sized.Whole.Cells.Should().Equal(
            [
                new SizeCell(ChangeCoverage.Shown, ChangeKind.Code, 3, 40, 5),
                new SizeCell(ChangeCoverage.Excluded, ChangeKind.Config, 1, 100, 20),
                new SizeCell(ChangeCoverage.Binary, ChangeKind.Unknown, 2, null, null),
            ], "the cells come back in the order they were written, a binary's line count still null");
        sized.Delta.State.Should().Be(ScopeState.Recorded);
        sized.Delta.From.Should().Be("abc123");
        sized.Delta.Cells.Should().Equal([new SizeCell(ChangeCoverage.Shown, ChangeKind.Test, 1, 7, 0)]);
        sized.Origin.Should().Be(SizeOrigin.Live);
        rounds.Single(r => r.Number == 2).Size.Should().BeNull("a round with no size reads not recorded, never a zero-line change");
    }

    [Fact]
    public void AnUnavailableScope_IsLoggedAsItsStateWithNoCells()
    {
        var size = new RoundSize(
            ScopeSize.Unavailable(ScopeState.NoMergeBase, "tip"),
            ScopeSize.Unavailable(ScopeState.FirstRound));
        Record((Round(1, size), "tip"));

        var logged = RoundsQuery.Read(_dir).Rounds.Single().Size;
        logged.Should().NotBeNull("the round was recorded with a size");

        logged!.Whole.State.Should().Be(ScopeState.NoMergeBase);
        logged.Whole.Cells.Should().BeEmpty("an unavailable scope has a state and no cells — not a zero");
        logged.Delta.State.Should().Be(ScopeState.FirstRound);
        logged.Delta.Cells.Should().BeEmpty();
    }

    [Fact]
    public void ABackfilledSize_IsLoggedAsBackfill()
    {
        Record((Round(1, AFullSize(SizeOrigin.Backfill)), "origin/main"), (Round(2, AFullSize()), "origin/main"));

        var rounds = RoundsQuery.Read(_dir).Rounds;

        SizeOf(rounds, 1).Origin.Should().Be(SizeOrigin.Backfill, "a rebuilt size must not read as a live one");
        SizeOf(rounds, 2).Origin.Should().Be(SizeOrigin.Live);
    }

    [Fact]
    public void EachSizeLandsOnItsOwnRound_AcrossPages()
    {
        Record([.. Enumerable.Range(1, 30).Select(n => (Round(n, SizeWithAdded(n)), "origin/main"))]);

        var first = RoundsQuery.Read(_dir, limit: 15);
        var second = RoundsQuery.Read(_dir, limit: 15, before: first.Rounds[^1].Cursor);

        first.Rounds.Select(r => r.Number).Should().Equal(Enumerable.Range(16, 15).Reverse(), "the newest fifteen");
        second.Rounds.Select(r => r.Number).Should().Equal(Enumerable.Range(1, 15).Reverse(), "the other fifteen, none repeated");
        foreach (var round in first.Rounds.Concat(second.Rounds))
        {
            round.Size.Should().NotBeNull();
            round.Size!.Whole.Cells.Should().ContainSingle().Which.Added.Should().Be(round.Number, "every round carries its own cells, none of another page's");
        }
    }

    [Fact]
    public void AStepTwentyOneFile_IsLoggedAsBefore()
    {
        var steps = Schema.Steps[..Array.FindIndex(Schema.Steps, step => step.Contains("round_changes", StringComparison.Ordinal))];
        steps.Should().HaveCount(21, "the fixture is the previous release's database");
        MakeAt(steps);

        var (code, text, _) = OneShotProcess.Run(_dir, "--log", "--paged");

        code.Should().Be(0, "a database from before the step is read exactly as before — not 74, no such column");
        using var doc = JsonDocument.Parse(text);
        var round = doc.RootElement.GetProperty("rounds").EnumerateArray().Single();
        round.TryGetProperty("size", out _).Should().BeFalse("a round from before step 22 has no size");
        Version().Should().Be(21, "a read-only reader never migrates the file");
    }

    private static RoundSize SizeOf(IReadOnlyList<LoggedRound> rounds, int number)
    {
        var size = rounds.Single(r => r.Number == number).Size;
        size.Should().NotBeNull($"round {number} was recorded with a size");

        return size!;
    }

    private string Database => Path.Combine(_dir, RoundsDb.FileName);

    private void MakeAt(IReadOnlyList<string> steps)
    {
        Directory.CreateDirectory(_dir);
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False");
        db.Open();
        using var make = db.CreateCommand();
        make.CommandText = string.Join(";\n", steps) + $"; PRAGMA user_version={steps.Count};"
            + "INSERT INTO sessions (id, repo_path, branch, opened_utc) VALUES ('s', 'D:/repo', 'main', '2026-10-01T00:00:00.0000000Z');"
            + "INSERT INTO rounds (id, session_id, stage, number, status, verdict, started_utc, completed_utc) VALUES (1, 's', 'CodeReview', 1, 'done', 'proceed', '2026-10-01T00:00:00.0000000Z', '2026-10-01T00:05:00.0000000Z');";
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
