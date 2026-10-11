using System.Security.Cryptography;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The local round counter (story 2.1 of <c>todo/PLAN_every_round_is_counted.md</c>): every round in
/// the rounds database is counted ONCE, under its own stage, for every vendor row that ran in it, over
/// three trailing windows — and nothing is written.
/// </summary>
/// <remarks>
/// <para>Real SQLite over a temp directory, no fakes: the point of the test is that the SQL counts. The
/// fixture is one database with twelve rounds placed on and around every window edge, so one shape
/// answers every question below; what each test asserts is named in its own method.</para>
/// <para>The clock is a constant handed to the reader rather than read off the machine — a window
/// anchored on the real clock is a test that expires overnight (testing.md).</para>
/// </remarks>
public sealed class RoundCountsTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private const string Codex = "codex";
    private const string Gemini = "gemini";
    private const string TeamCodex = "team-codex";
    private const string Luna = "gpt-6-luna";
    private const string FlashMedium = "gemini-3.8-flash-medium";
    private const string OldLuna = "gpt-5.6-luna";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-counts-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }
    }

    private string Database => Path.Combine(_dir, RoundsDb.FileName);

    private static ReviewerState Ran(string provider, string model, string role = "Architecture") =>
        new(provider, role, ReviewerState.Done, 0, string.Empty, 1.0, model);

    private static void Record(RoundsDb db, string session, string stage, DateTime started, params ReviewerState[] reviewers)
    {
        var state = new SessionState(session, "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };
        db.RecordRound(
            state,
            new RoundRecord(stage, 1, "proceed", 0, "answered", started) { StartedUtc = started, ReviewerStates = [.. reviewers] },
            []);
    }

    /// <summary>
    /// Twelve rounds: one per stage inside the shortest window, one on EACH window's starting edge, one
    /// exactly at the end, one in the future, one before every window, a Team-server row, and a round
    /// recorded before models were — the whole shape the counter has to read.
    /// </summary>
    private void Fixture()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        Record(db, "a", nameof(Stage.PlanReview), Now.AddHours(-1), Ran(Codex, Luna), Ran(Gemini, FlashMedium));
        Record(db, "b", nameof(Stage.CodeReview), Now.AddHours(-1),
            Ran(Codex, Luna), Ran(Codex, Luna, "SecurityReliability"), Ran(Codex, Luna, "UxDxPerformance"),
            Ran(Gemini, FlashMedium), Ran(Gemini, FlashMedium, "SecurityReliability"), Ran(Gemini, FlashMedium, "UxDxPerformance"));
        Record(db, "c", nameof(Stage.CodeReview), Now.AddHours(-5), Ran(Codex, Luna), Ran(Gemini, FlashMedium));
        Record(db, "d", nameof(Stage.DocumentReview), Now.AddHours(-6), Ran(Codex, Luna));
        Record(db, "e", nameof(Stage.FeatureReview), Now.AddHours(-23), Ran(Gemini, FlashMedium));
        Record(db, "f", nameof(Stage.CodeReview), Now.AddHours(-24), Ran(Codex, Luna));
        Record(db, "g", nameof(Stage.PlanReview), Now.AddDays(-6), Ran(Codex, string.Empty));
        Record(db, "h", nameof(Stage.CodeReview), Now.AddDays(-7), Ran(Codex, Luna));
        Record(db, "i", nameof(Stage.CodeReview), Now.AddDays(-8), Ran(Codex, Luna));
        Record(db, "j", nameof(Stage.CodeReview), Now, Ran(Codex, Luna));
        Record(db, "k", nameof(Stage.CodeReview), Now.AddHours(-2), Ran(TeamCodex, OldLuna), Ran(Codex, Luna));
        Record(db, "l", nameof(Stage.CodeReview), Now.AddHours(1), Ran(Codex, Luna));
    }

    private static RoundCountsRow Row(RoundCountsAnswer answer, string window, string provider) =>
        answer.Windows.Single(w => w.Name == window).Rows.Single(r => r.Provider == provider);

    private static int RoundsOf(RoundCountsRow row, string model) => row.Models.Single(m => m.Model == model).Rounds;

    [Fact]
    public void EveryStage_IsCountedUnderItsOwnName_PerVendorRow_InEachWindow()
    {
        Fixture();

        var answer = RoundCounts.Read(_dir, Now);

        answer.NowUtc.Should().Be(Now.ToString("O"));
        answer.Windows.Select(w => w.Name).Should().Equal(["5h", "24h", "7d"], "every window in one call");
        Row(answer, "5h", Codex).Rounds.Should().Be(new RoundsByStage(Plan: 1, Code: 3, Document: 0, Feature: 0));
        Row(answer, "5h", Gemini).Rounds.Should().Be(new RoundsByStage(Plan: 1, Code: 1, Document: 0, Feature: 0));
        Row(answer, "24h", Codex).Rounds.Should().Be(new RoundsByStage(Plan: 1, Code: 4, Document: 1, Feature: 0));
        Row(answer, "24h", Gemini).Rounds.Should().Be(new RoundsByStage(Plan: 1, Code: 2, Document: 0, Feature: 1));
        Row(answer, "7d", Codex).Rounds.Should().Be(new RoundsByStage(Plan: 2, Code: 5, Document: 1, Feature: 0));
        Row(answer, "7d", Gemini).Rounds.Should().Be(new RoundsByStage(Plan: 1, Code: 2, Document: 0, Feature: 1));
    }

    [Fact]
    public void ARound_IsCountedOnce_HoweverManyReviewersItRan_AndTheReviewersAreCountedBesideIt()
    {
        Fixture();

        var codex = Row(RoundCounts.Read(_dir, Now), "5h", Codex);

        codex.Rounds.Code.Should().Be(3, "round b ran three codex reviewers and is ONE round");
        codex.Reviewers.Should().Be(6, "a + b(3) + j + k — the honest numerator of reviewers per round");
    }

    [Fact]
    public void ATeamServerRow_IsItsOwnProvider_NeverFoldedIntoTheVendor()
    {
        Fixture();

        var answer = RoundCounts.Read(_dir, Now);

        answer.Windows.Single(w => w.Name == "5h").Rows.Select(r => r.Provider).Should().Equal([Codex, Gemini, TeamCodex]);
        Row(answer, "5h", TeamCodex).Rounds.Should().Be(new RoundsByStage(0, 1, 0, 0));
        Row(answer, "5h", TeamCodex).Reviewers.Should().Be(1);
        RoundsOf(Row(answer, "5h", TeamCodex), OldLuna).Should().Be(1, "the Team server's model stays on the Team server's row");
        Row(answer, "5h", Codex).Models.Should().NotContain(m => m.Model == OldLuna);
    }

    /// <summary>A window is <c>(from, to]</c>: a round ON the starting edge belongs to the next window up, one AT the end belongs to all.</summary>
    [Fact]
    public void TheWindows_AreHalfOpen_TheStartExcluded_TheEndIncluded()
    {
        Fixture();

        var answer = RoundCounts.Read(_dir, Now);

        var fiveHours = answer.Windows.Single(w => w.Name == "5h");
        fiveHours.FromUtc.Should().Be(Now.AddHours(-5).ToString("O"));
        fiveHours.ToUtc.Should().Be(Now.ToString("O"));
        // c sits exactly on the 5 h edge: out of 5 h (codex code would be 4 otherwise), in 24 h.
        Row(answer, "5h", Codex).Rounds.Code.Should().Be(3, "the round exactly 5 h ago is not in the last 5 h");
        // f sits exactly on the 24 h edge: out of 24 h, in 7 d.
        Row(answer, "24h", Codex).Rounds.Code.Should().Be(4, "the round exactly 24 h ago is not in the last 24 h");
        Row(answer, "7d", Codex).Rounds.Code.Should().Be(5, "and it is in the last 7 days; the one exactly 7 d ago is not");
        // j is exactly at `now`, l one hour after it.
        answer.Windows.Should().AllSatisfy(w => Row(answer, w.Name, Codex).Rounds.Code.Should().BeGreaterThan(0));
        Row(answer, "7d", Codex).Reviewers.Should().Be(10, "h, i and l contribute nothing: before every window, or after now");
    }

    [Fact]
    public void PerModelRounds_ComeOnlyFromRoundsThatRecordedAModel_AndModelsKnownSinceSaysFromWhen()
    {
        Fixture();

        var answer = RoundCounts.Read(_dir, Now);

        RoundsOf(Row(answer, "7d", Codex), Luna).Should().Be(7, "a, b, c, d, f, j, k — not g, whose reviewer recorded no model");
        Row(answer, "7d", Codex).Models.Should().ContainSingle("the empty model is no model, never a line of its own");
        Row(answer, "7d", Codex).Rounds.Plan.Should().Be(2, "g is still a round, counted under its stage");
        RoundsOf(Row(answer, "24h", Gemini), FlashMedium).Should().Be(4);
        answer.ModelsKnownSince.Should().Be(Now.AddDays(-8).ToString("O"), "the oldest round that recorded a model, over the whole table");
    }

    /// <summary>
    /// The once-a-minute read must not cost more as the history grows: the oldest modelled round is found by walking
    /// rounds oldest first through <c>rounds_by_time</c> and stopping at the first one, each looked up through
    /// <c>reviewers_by_round</c> — so the cost is the rounds written before step 20, a number that never grows. A join
    /// that aggregates every reviewer row (the first form) scans the whole table on every refresh.
    /// </summary>
    [Fact]
    public void ModelsKnownSince_WalksTheRoundsByTime_AndStopsAtTheFirst_NeverAggregatingEveryReviewer()
    {
        Fixture();
        using var db = new SqliteConnection($"Data Source={Database};Mode=ReadOnly");
        db.Open();
        using var explain = db.CreateCommand();
        explain.CommandText = "EXPLAIN QUERY PLAN " + RoundCounts.ModelsKnownSinceSql;
        var plan = new List<string>();
        using (var reader = explain.ExecuteReader())
        {
            while (reader.Read())
            {
                plan.Add(reader.GetString(3));
            }
        }

        plan.Should().Contain(step => step.Contains("rounds_by_time", StringComparison.Ordinal), "the walk is in time order, by the index");
        plan.Should().Contain(step => step.Contains("reviewers_by_round", StringComparison.Ordinal), "each round's reviewers by their round");
        plan.Should().NotContain(step => step.Contains("TEMP B-TREE", StringComparison.Ordinal), "no sort of the whole table");
        // The plan text reads the same for a walk and for a full aggregate over the same indexes; what makes it a walk
        // that stops is the order and the limit, so those are pinned.
        RoundCounts.ModelsKnownSinceSql.Should().Contain("ORDER BY r.started_utc LIMIT 1", "the walk stops at the first modelled round");
    }

    [Fact]
    public void AWindowNothingRanIn_HasNoRows_AndIsStillAnswered()
    {
        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            Record(db, "old", nameof(Stage.CodeReview), Now.AddDays(-3), Ran(Codex, Luna));
        }

        var answer = RoundCounts.Read(_dir, Now);

        answer.Windows.Single(w => w.Name == "5h").Rows.Should().BeEmpty();
        answer.Windows.Single(w => w.Name == "24h").Rows.Should().BeEmpty();
        Row(answer, "7d", Codex).Rounds.Code.Should().Be(1);
    }

    [Fact]
    public void AMissingDatabase_IsAnEmptyAnswer_NotAFailure()
    {
        Directory.CreateDirectory(_dir);

        var answer = RoundCounts.Read(_dir, Now);

        answer.Windows.Should().HaveCount(3).And.AllSatisfy(w => w.Rows.Should().BeEmpty());
        answer.ModelsKnownSince.Should().BeNull();
        File.Exists(Database).Should().BeFalse("asking the count must not create the database");
    }

    /// <summary>
    /// A database last written by a build without step 20 has no <c>reviewers.model</c>: its rounds are
    /// still counted, and it knows no models — and the read must not migrate it on the way.
    /// </summary>
    [Fact]
    public void ADatabaseWithoutTheModelColumn_CountsItsRounds_KnowsNoModels_AndIsNotMigrated()
    {
        var stopBefore = Array.FindIndex(Schema.Steps, step => step.Contains("ALTER TABLE reviewers ADD COLUMN model", StringComparison.Ordinal));
        stopBefore.Should().BeGreaterThan(0, "the step that adds the column is what this test stops before");
        Directory.CreateDirectory(_dir);
        using (var make = new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            make.Open();
            using var sql = make.CreateCommand();
            sql.CommandText = string.Join(";\n", Schema.Steps[..stopBefore]) + $"; PRAGMA user_version={stopBefore};"
                + "INSERT INTO sessions (id, repo_path, branch, opened_utc) VALUES ('s', 'D:/repo', 'main', '2026-10-01T00:00:00.0000000Z');"
                + $"INSERT INTO rounds (id, session_id, stage, number, status, verdict, started_utc, completed_utc) VALUES (1, 's', 'CodeReview', 1, 'done', 'proceed', '{Now.AddHours(-1):O}', '{Now:O}');"
                + "INSERT INTO reviewers (round_id, provider, role, status) VALUES (1, 'codex', 'Architecture', 'done');"
                + "INSERT INTO reviewers (round_id, provider, role, status) VALUES (1, 'gemini', 'Architecture', 'done');";
            sql.ExecuteNonQuery();
        }

        var answer = RoundCounts.Read(_dir, Now);

        Row(answer, "5h", Codex).Rounds.Code.Should().Be(1);
        Row(answer, "5h", Gemini).Reviewers.Should().Be(1);
        Row(answer, "5h", Codex).Models.Should().BeEmpty("no column, no model");
        answer.ModelsKnownSince.Should().BeNull();
        Version().Should().Be(stopBefore, "a read-only reader cannot migrate, and must not try");
    }

    /// <summary>The read leaves a CURRENT file byte for byte — the guard against a data write.</summary>
    /// <remarks>
    /// What this does NOT prove: that the read never migrates. On a file already at the head a migrating
    /// open changes nothing — observed 2026-10-10: with <c>Read</c> made to open for writing and run the
    /// migrator, this stayed green — so the migration guard is
    /// <see cref="ADatabaseWithoutTheModelColumn_CountsItsRounds_KnowsNoModels_AndIsNotMigrated"/> and its
    /// process twin in <c>RoundCountsCliScenarioTests</c>, whose fixture is a step-19 file a migration WOULD change.
    /// </remarks>
    [Fact]
    public void TheRead_WritesNothing_TheFileIsByteForByteWhatItWas()
    {
        Fixture();
        SqliteConnection.ClearAllPools();
        var before = (Bytes: SHA256.HashData(File.ReadAllBytes(Database)), Written: File.GetLastWriteTimeUtc(Database), Version: Version());

        RoundCounts.Read(_dir, Now);

        SqliteConnection.ClearAllPools();
        SHA256.HashData(File.ReadAllBytes(Database)).Should().Equal(before.Bytes);
        File.GetLastWriteTimeUtc(Database).Should().Be(before.Written);
        Version().Should().Be(before.Version);
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
