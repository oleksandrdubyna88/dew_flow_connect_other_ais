using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Schema step 21: <c>rounds.round_key</c> (story 4.1 of <c>todo/PLAN_every_round_is_counted.md</c>) — appended after
/// step 20, filled on every recorded round from the record's own key, empty on every round written before it, and
/// reached by every database shape in the field — exactly as step 20 was (<see cref="TheReviewersModelIsStepTwentyTests"/>).
/// </summary>
/// <remarks>
/// <para>The round's identity across the wire is minted at the round's start and sent on every launch; the projection
/// keeps it so the local record and the Team server's ledger name one round by one key.</para>
/// <para>Real SQLite over a temp directory; the two preview-fork shapes are built as step 20's test builds them, because a
/// step appended after those repairs has to be reached THROUGH them. A read-only reader (<c>--round-counts</c>) on a
/// step-20 file must still read, and leave the file at twenty.</para>
/// </remarks>
public sealed class TheRoundKeyIsStepTwentyOneTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-step21-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }
    }

    private string Database => Path.Combine(_dir, RoundsDb.FileName);

    private static int KeyStep => Array.FindIndex(Schema.Steps, step => step.Contains("ALTER TABLE rounds ADD COLUMN round_key", StringComparison.Ordinal));

    private static int QuestionsStep => Array.IndexOf(Schema.Steps, Schema.TheQuestionsAsked);

    private void MakeAt(IEnumerable<string> steps, string alsoRun = "")
    {
        Directory.CreateDirectory(_dir);
        var list = steps.ToArray();
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False");
        db.Open();
        using var make = db.CreateCommand();
        make.CommandText = string.Join(";\n", list) + $"; PRAGMA user_version={list.Length};" + alsoRun;
        make.ExecuteNonQuery();
    }

    private (long Version, bool Key, bool Model) Shape()
    {
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var ask = db.CreateCommand();
        ask.CommandText = "PRAGMA user_version";
        return ((long)ask.ExecuteScalar()!, RoundsQuery.HasColumn(db, "rounds", "round_key"), RoundsQuery.HasColumn(db, "reviewers", "model"));
    }

    private void OpensAndEndsWithEveryStep(string because)
    {
        using (var opened = RoundsDb.Open(_dir, _log))
        {
            opened.Should().NotBeNull(because);
        }

        Shape().Should().Be(((long)Schema.Steps.Length, true, true));
    }

    private const string OneOldRound =
        "INSERT INTO sessions (id, repo_path, branch, opened_utc) VALUES ('s', 'D:/repo', 'main', '2026-10-01T00:00:00.0000000Z');"
        + "INSERT INTO rounds (id, session_id, stage, number, status, verdict, started_utc, completed_utc) VALUES (1, 's', 'CodeReview', 1, 'done', 'proceed', '2026-10-01T00:00:00.0000000Z', '2026-10-01T00:05:00.0000000Z');"
        + "INSERT INTO reviewers (round_id, provider, role, status, model) VALUES (1, 'codex', 'Architecture', 'done', 'gpt-6-luna');";

    [Fact]
    public void TheRoundKey_IsTheTwentyFirstStep_AppendedNeverInserted()
    {
        KeyStep.Should().Be(20, "step 21 by position: step 20 is the reviewers' model, and a step is never inserted ahead of one that shipped");
        Schema.Steps.Length.Should().Be(21);
        Schema.Steps[20].Should().Be(Schema.TheRoundsKey);
    }

    [Fact]
    public void ADatabaseAtStepTwenty_GainsTheColumn_AndItsOlderRoundsReadAnEmptyKey()
    {
        MakeAt(Schema.Steps[..KeyStep], OneOldRound);
        Shape().Should().Be(((long)KeyStep, false, true), "the fixture is the previous release's database, not today's");

        OpensAndEndsWithEveryStep("the ordinary path: a database at step 20 migrates forward");

        Query("SELECT round_key FROM rounds").Should().Equal([""], "a round recorded before the column exists had no key, and empty is what says so");
    }

    [Fact]
    public void ARecordedRound_CarriesItsKey_AndOneWithoutReadsEmpty()
    {
        var key = RoundKeys.Mint();
        var state = new SessionState("s1", "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };
        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            db.RecordRound(state, new RoundRecord("CodeReview", 1, "proceed", 0, "answered", DateTime.UtcNow) { StartedUtc = DateTime.UtcNow, Key = key }, []);
            db.RecordRound(state, new RoundRecord("CodeReview", 2, "proceed", 0, "answered", DateTime.UtcNow) { StartedUtc = DateTime.UtcNow }, []);
        }

        Query("SELECT number || '=' || round_key FROM rounds ORDER BY number").Should().Equal([$"1={key}", "2="],
            "the key as the round was minted it; a record with none writes none");
    }

    [Fact]
    public void AKey_IsThirtyTwoLowerCaseHexCharacters_AndTwoMintsDiffer()
    {
        var one = RoundKeys.Mint();
        var two = RoundKeys.Mint();

        RoundKeys.IsWellFormed(one).Should().BeTrue(one);
        one.Should().NotBe(two, "128 random bits — two rounds anywhere never share one");
        RoundKeys.IsWellFormed(string.Empty).Should().BeFalse();
        RoundKeys.IsWellFormed(one.ToUpperInvariant()).Should().BeFalse("one spelling, so the server can compare bytes");
        RoundKeys.IsWellFormed(one[..31] + "g").Should().BeFalse();
    }

    [Fact]
    public void AReadOnlyReader_OnAStepTwentyFile_StillReads_AndLeavesItAtTwenty()
    {
        MakeAt(Schema.Steps[..KeyStep], OneOldRound);

        var counts = RoundCounts.Read(_dir, new DateTime(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc));

        counts.Windows.Should().NotBeEmpty("the counter reads a file without the column — it never asks for it");
        Shape().Should().Be(((long)KeyStep, false, true), "a reader opened read-only runs no step and leaves the file byte for byte");
    }

    [Fact]
    public void TheConsultantPreviewDatabase_StillReachesTheHead_KeyColumnIncluded()
    {
        MakeAt([.. Schema.Steps[..QuestionsStep], Schema.WhyAConsultationFailed]);
        Shape().Version.Should().Be(17, "the consultant preview's own stamp");

        OpensAndEndsWithEveryStep("a database the consultant preview wrote is repaired, then migrated to step 21");
    }

    [Fact]
    public void TheSecurityPreviewDatabase_StillReachesTheHead_KeyColumnIncluded()
    {
        MakeAt([.. Schema.Steps[..QuestionsStep], Schema.SecurityEvidence]);
        Shape().Version.Should().Be(17, "the security preview's own stamp");

        OpensAndEndsWithEveryStep("a database the security preview wrote is repaired, then migrated to step 21");
    }

    [Fact]
    public void AMigratedDatabase_OpensAgainUnchanged()
    {
        MakeAt(Schema.Steps[..KeyStep]);
        using (RoundsDb.Open(_dir, _log)) { }

        OpensAndEndsWithEveryStep("the ALTER ran once; a second open is the ordinary path at the head and re-runs nothing");
    }

    private IReadOnlyList<string> Query(string sql)
    {
        using var db = new SqliteConnection($"Data Source={Database};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = sql;
        using var rows = read.ExecuteReader();
        var values = new List<string>();
        while (rows.Read())
        {
            values.Add(rows.GetValue(0).ToString() ?? string.Empty);
        }

        return values;
    }
}
