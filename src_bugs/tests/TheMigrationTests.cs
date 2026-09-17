using System.Diagnostics;
using System.Globalization;
using CoaiMcp.Storage;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// A database written by step 1 ONLY — the shape the first host has — is carried forward.
/// </summary>
/// <remarks>
/// <para><b>A fresh file proves nothing.</b> Every step runs on it in order and the result looks
/// right whether or not the migration can handle a file that already has the tables. The file the
/// field has was made by a build that ran the schema as one statement and never stamped it:
/// tables present, <c>user_version = 0</c>. That is what these tests migrate.</para>
/// <para>The fixture that writes that shape is the frozen step 1 (<see cref="TheSchemaIsFrozenTests"/>),
/// through a plain connection that runs no migration on its way in.</para>
/// </remarks>
public sealed class TheMigrationTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "coai-migrate-" + Guid.NewGuid().ToString("N")[..8]);

    private const string TheOldBuildsKeyValue = "deadbeefcafef00d";

    private static readonly KeyId TheOldBuildsKey = new(TheOldBuildsKeyValue);

    public TheMigrationTests() => Directory.CreateDirectory(_dir);

    private string DbPath => Path.Combine(_dir, "coai-bugs.db");

    /// <summary>
    /// Writes a file exactly as the deployed <c>bugs-v0.1.0</c> left it: the released tables, one key
    /// that build issued, and <c>user_version = 0</c>.
    /// </summary>
    /// <remarks>Shared with the scenario over the real binary, which migrates the same shape.</remarks>
    internal static void WriteAFileFromStepOneOnly(string path)
    {
        TestSql.Run(path, File.ReadAllText(TheSchemaIsFrozenTests.FixturePath));
        TestSql.Run(
            path,
            $"INSERT INTO api_keys (id, key_hash, created_utc, note) VALUES ('{TheOldBuildsKeyValue}', "
            + "'a-hash-the-old-build-wrote', '2026-09-17T00:00:00Z', 'issued before the migration')");
        TestSql.Scalar(path, "PRAGMA user_version").Should().Be(
            "0", "the fixture must reproduce the field: the released build never stamped the file");
    }

    [Fact]
    public void AFileWrittenByStepOneOnly_LandsOnTheCurrentVersionWithTheNewColumns()
    {
        WriteAFileFromStepOneOnly(DbPath);

        using (Corpus.Open(DbPath))
        {
            // Opened and closed: the migration is the open.
        }

        TestSql.Scalar(DbPath, "PRAGMA user_version").Should().Be(
            CorpusSchema.Steps.Length.ToString(CultureInfo.InvariantCulture),
            "step 1 re-ran as a no-op and stamped the file, then every later step ran");
        TestSql.Column(DbPath, "SELECT name FROM pragma_table_info('api_keys')")
            .Should().Contain("last_seen_month", "step 2 adds it with ALTER, which IF NOT EXISTS never would");
        TestSql.Column(DbPath, "SELECT name FROM sqlite_master WHERE type = 'table'")
            .Should().Contain("admin_audit");
        TestSql.Column(DbPath, "SELECT name FROM sqlite_master WHERE type = 'index'")
            .Should().Contain("admin_audit_by_time", "the sweep and the admin API read it by time");
    }

    [Fact]
    public void TheKeyTheOldBuildIssuedSurvivesReadsAsNeverUsedAndCanBeRevoked()
    {
        WriteAFileFromStepOneOnly(DbPath);
        var clock = new FrozenClock(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero));

        using var corpus = Corpus.Open(DbPath);

        corpus.UsageOf(TheOldBuildsKey).Should().Be(
            new Usage.Known(new SubmissionCount(0), new LastSeen.Never()), "a row the old build wrote reads as never used, not as a blank");
        corpus.Revoke(TheOldBuildsKey, Audit.By(AdminId.Cli, clock)).Should().BeTrue();
        corpus.AuditTrail(10).Should().ContainSingle().Which.Target.Should().Be(TheOldBuildsKey);
    }

    [Fact]
    public void TheMigrationRunsOnce_SoASecondOpenIsNotASecondAlter()
    {
        WriteAFileFromStepOneOnly(DbPath);
        using (Corpus.Open(DbPath))
        {
        }

        // A repeated ALTER answers `duplicate column name`; a file that opens twice is one whose
        // version was stamped with the step that earned it.
        var opening = () => Corpus.Open(DbPath).Dispose();

        opening.Should().NotThrow();
        TestSql.Scalar(DbPath, "PRAGMA user_version").Should().Be(
            CorpusSchema.Steps.Length.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Two openers racing on the field's file both succeed, and the file is whole.
    /// </summary>
    /// <remarks>
    /// <para>The defect a code round traced: two openers both read <c>user_version = 0</c>, both run
    /// step 1, and the second then fails step 2's <c>ALTER</c> with <c>duplicate column name</c> —
    /// and because its own step-1 transaction had already set the version BACK to 1, the file is left
    /// at version 1 with the column present, so every later open fails the same way and the service
    /// cannot start on the field's database. The service and a one-shot starting together is exactly
    /// how it happens.</para>
    /// <para>Serialised by the runner taking an IMMEDIATE transaction and reading the version while
    /// holding it, so the second opener waits and then sees the version the first one left. Ten
    /// rounds on ten fresh files, released by a barrier: a race the scheduler is left to arrange is a
    /// race the scheduler may hide.</para>
    /// </remarks>
    [Fact]
    public void TwoOpenersRacingOnTheFieldsFile_BothSucceedAndTheFileIsWhole()
    {
        for (var round = 0; round < 10; round++)
        {
            var path = Path.Combine(_dir, $"race-{round}.db");
            WriteAFileFromStepOneOnly(path);
            var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();
            using var gate = new Barrier(2);
            var openers = Enumerable.Range(0, 2).Select(_ => new Thread(() =>
            {
                gate.SignalAndWait();
                try
                {
                    Corpus.Open(path).Dispose();
                }
                catch (Exception e)
                {
                    failures.Add(e);
                }
            })).ToList();
            openers.ForEach(opener => opener.Start());
            openers.ForEach(opener => opener.Join());

            failures.Should().BeEmpty(
                $"round {round}: both openers must migrate or wait, never fail — "
                + $"got: {string.Join(" | ", failures.Select(failure => failure.Message))}");
            TestSql.Scalar(path, "PRAGMA user_version").Should().Be(
                CorpusSchema.Steps.Length.ToString(CultureInfo.InvariantCulture), $"round {round}");
            TestSql.Column(path, "SELECT name FROM pragma_table_info('api_keys')")
                .Count(column => column == "last_seen_month")
                .Should().Be(1, $"round {round}: the column exists exactly once");
        }
    }

    [Fact]
    public void AFreshFileGetsEveryStepAndIsUsable()
    {
        using var corpus = Corpus.Open(DbPath);

        TestSql.Scalar(DbPath, "PRAGMA user_version").Should().Be(
            CorpusSchema.Steps.Length.ToString(CultureInfo.InvariantCulture));
        corpus.Keep("CSharp", "a", "b", new KeyId("key"), UtcMonth.Of(DateTimeOffset.UnixEpoch)).Kept.Should().Be(Kept.Stored);
        corpus.AuditCount().Should().Be(0);
    }

    /// <summary>
    /// A second opener — the service and a one-shot on the same file — WAITS for a writer rather than
    /// failing at once with <c>SQLITE_BUSY</c>.
    /// </summary>
    /// <remarks>
    /// <para>A writer holds the file's write lock for two and a half seconds; the corpus's own write
    /// must wait it out and land. The budget it waits with is <see cref="SqliteMigrator.BusyTimeoutMilliseconds"/>,
    /// which also sets the connection's <c>Default Timeout</c> — so shortening that one constant to a
    /// second is the way to watch this go red with <c>database is locked</c>, which is how its teeth
    /// are proved.</para>
    /// <para>Two and a half seconds of wall clock is the price of observing a wait at all; it is the
    /// one test here that takes longer than a blink, and it says so.</para>
    /// </remarks>
    [Fact]
    public async Task AConcurrentOpenerWaitsForAWriterRatherThanFailing()
    {
        using var corpus = Corpus.Open(DbPath);
        var clock = new FrozenClock(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero));
        using var writer = new SqliteConnection($"Data Source={DbPath};Pooling=False");
        writer.Open();
        var hold = TimeSpan.FromMilliseconds(2_500);

        // SIGNALLED, not slept for. A fixed delay does not prove the writer took SQLite's write
        // lock: if the scheduler is slow, `corpus.Issue` runs first, meets no lock at all, and the
        // elapsed-time assertion below measures nothing. The writer says when the lock is really
        // held. (CodeRabbit, #348.)
        using var locked = new SemaphoreSlim(0, 1);
        var holding = Task.Run(async () =>
        {
            using var transaction = writer.BeginTransaction();
            using var write = writer.CreateCommand();
            write.CommandText = "INSERT INTO admin_audit (admin_id, action, target, at_utc) VALUES ('cli', 'issue', 'held', 'now')";
            write.ExecuteNonQuery();
            locked.Release();
            await Task.Delay(hold, TestContext.Current.CancellationToken);
            transaction.Commit();
        }, TestContext.Current.CancellationToken);

        await locked.WaitAsync(TestContext.Current.CancellationToken);
        var waited = Stopwatch.StartNew();
        var issuing = () => corpus.Issue(new KeyId("waited-for"), "hash", "note", Audit.By(AdminId.Cli, clock));

        issuing.Should().NotThrow("the opener must wait for the writer, not fail with SQLITE_BUSY");
        await holding;

        waited.Elapsed.Should().BeGreaterThan(
            TimeSpan.FromSeconds(1), "it really waited behind the lock rather than slipping in before it");
        corpus.UsageOf(new KeyId("waited-for")).Should().Be(new Usage.Known(new SubmissionCount(0), new LastSeen.Never()), "and the write landed");
    }

    public void Dispose() => Scratch.Delete(_dir);
}
