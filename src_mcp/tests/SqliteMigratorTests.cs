using CoaiMcp.Storage;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The one migration runner both databases share: ordered steps, a version stamp, WAL and a wait.
/// </summary>
/// <remarks>
/// <para>It was <c>RoundsDb.Migrate</c> and had one test of its own — the step that fails part-way
/// (<c>RoundsDbTests.AStepThatFailsPartWay…</c>), which still drives it through the same seam. These
/// are the rest of its contract, written when it became shared: what a fresh file gets, what a file
/// part-way along gets, and the two pragmas a second opener depends on.</para>
/// <para>Real SQLite over a temp directory, no fakes: the point of the test is that the SQL runs.</para>
/// </remarks>
public sealed class SqliteMigratorTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "coai-migrator-" + Guid.NewGuid().ToString("N")[..8]);

    private static readonly string[] Three =
    [
        "CREATE TABLE IF NOT EXISTS a (x TEXT);",
        "ALTER TABLE a ADD COLUMN y TEXT;",
        "CREATE TABLE IF NOT EXISTS b (z TEXT);",
    ];

    public SqliteMigratorTests() => Directory.CreateDirectory(_dir);

    private SqliteConnection Open()
    {
        var db = new SqliteConnection($"Data Source={Path.Combine(_dir, "t.db")};Pooling=False");
        db.Open();

        return db;
    }

    private static IReadOnlyList<string> Column(SqliteConnection db, string sql)
    {
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

    private static void Run(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Fact]
    public void AFreshFileRunsEveryStepAndRecordsHowMany()
    {
        using var db = Open();

        SqliteMigrator.Migrate(db, Three);

        SqliteMigrator.Version(db).Should().Be(3);
        Column(db, "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name").Should().Equal(["a", "b"]);
        Column(db, "SELECT name FROM pragma_table_info('a')").Should().Equal(["x", "y"]);
    }

    /// <summary>A file part-way along runs only what follows, and a second run runs nothing.</summary>
    /// <remarks>
    /// The second half is what <c>CREATE TABLE IF NOT EXISTS</c> alone cannot give: an <c>ALTER</c>
    /// re-run answers <c>duplicate column name</c>, so a file that migrates twice is one whose version
    /// was stamped with the step that earned it.
    /// </remarks>
    [Fact]
    public void AFileAtAnEarlierVersionRunsOnlyWhatFollows_AndNothingTwice()
    {
        using var db = Open();
        Run(db, Three[0]);
        Run(db, "INSERT INTO a (x) VALUES ('written by the old build')");
        Run(db, "PRAGMA user_version=1");

        SqliteMigrator.Migrate(db, Three);
        var again = () => SqliteMigrator.Migrate(db, Three);

        again.Should().NotThrow("every step ran once; a second open must not re-run the ALTER");
        SqliteMigrator.Version(db).Should().Be(3);
        Column(db, "SELECT x FROM a").Should().Equal(["written by the old build"], "the row the old build wrote survives");
        Column(db, "SELECT name FROM pragma_table_info('a')").Should().Equal(["x", "y"]);
    }

    /// <summary>The two pragmas a second opener depends on, read back from the connection.</summary>
    [Fact]
    public void TheFileIsLeftInWalModeWithTheBusyTimeoutSet()
    {
        using var db = Open();

        SqliteMigrator.Migrate(db, Three);

        Column(db, "PRAGMA journal_mode").Should().Equal(["wal"]);
        Column(db, "PRAGMA busy_timeout").Should().Equal(
            [SqliteMigrator.BusyTimeoutMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        SqliteMigrator.DefaultTimeoutFragment.Should().Be(
            "Default Timeout=5", "the provider's own wait, in seconds, derived from the same number");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TestContext.Current.SendDiagnosticMessage(
                "the scratch directory {0} could not be removed: {1}", _dir, e.Message);
        }
    }
}
