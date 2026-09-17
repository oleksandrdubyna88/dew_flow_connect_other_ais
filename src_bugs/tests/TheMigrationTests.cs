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

    private const string TheOldBuildsKey = "deadbeefcafef00d";

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
            $"INSERT INTO api_keys (id, key_hash, created_utc, note) VALUES ('{TheOldBuildsKey}', "
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
            new KeyUsage(0, string.Empty), "a row the old build wrote reads as never used, not as a blank");
        corpus.Revoke(TheOldBuildsKey, Audit.By(AdminIdentity.Cli, clock)).Should().BeTrue();
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

    [Fact]
    public void AFreshFileGetsEveryStepAndIsUsable()
    {
        using var corpus = Corpus.Open(DbPath);

        TestSql.Scalar(DbPath, "PRAGMA user_version").Should().Be(
            CorpusSchema.Steps.Length.ToString(CultureInfo.InvariantCulture));
        corpus.Keep("CSharp", "a", "b", "key", "2026-09-17T12:00:00Z").Kept.Should().Be(Kept.Stored);
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

        var holding = Task.Run(async () =>
        {
            using var transaction = writer.BeginTransaction();
            using var write = writer.CreateCommand();
            write.CommandText = "INSERT INTO admin_audit (admin_id, action, target, at_utc) VALUES ('cli', 'issue', 'held', 'now')";
            write.ExecuteNonQuery();
            await Task.Delay(hold, TestContext.Current.CancellationToken);
            transaction.Commit();
        }, TestContext.Current.CancellationToken);

        // Let the writer take the lock before the corpus tries to write behind it.
        await Task.Delay(300, TestContext.Current.CancellationToken);
        var waited = Stopwatch.StartNew();
        var issuing = () => corpus.Issue("waited-for", "hash", "note", Audit.By(AdminIdentity.Cli, clock));

        issuing.Should().NotThrow("the opener must wait for the writer, not fail with SQLITE_BUSY");
        await holding;

        waited.Elapsed.Should().BeGreaterThan(
            TimeSpan.FromSeconds(1), "it really waited behind the lock rather than slipping in before it");
        corpus.UsageOf("waited-for").Should().Be(new KeyUsage(0, string.Empty), "and the write landed");
    }

    public void Dispose() => Scratch.Delete(_dir);
}
