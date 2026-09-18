using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The column reader, and the guard the extraction lost.
/// </summary>
/// <remarks>
/// <para><b>Why this file exists.</b> <c>Columns</c> was extracted from <c>BugsQuery</c>'s private
/// helpers in story 2.1 so the review page's projection could reuse them rather than copy them —
/// which is the right move, and it silently dropped something on the way. The original read
/// <c>rows.IsDBNull(at) ? 0 : Convert.ToInt32(rows.GetValue(at), …)</c>; the extracted copy called
/// <c>GetInt32</c> and <c>GetString</c> straight. Two reviewers on the code round noticed, and they
/// were right that a shared reader must not be weaker than the private one it replaced.</para>
/// <para><b>What is NOT claimed.</b> They also said this crashes <c>--pairs-json</c>. It does not:
/// every column that projection reads is declared <c>NOT NULL DEFAULT</c> in <c>Schema.cs</c>, so
/// SQLite will not hand back a null for one. The defect is that the helper is now a general-purpose
/// reader with a hole in it, waiting for the first caller who selects a nullable column — so it is
/// tested here against a nullable column made on purpose, which is the only way to reach it.</para>
/// </remarks>
public sealed class TheColumnReaderTests
{
    /// <summary>A table with columns that really can hold null, which `findings` cannot.</summary>
    private static SqliteConnection Nullable()
    {
        var db = new SqliteConnection("Data Source=:memory:");
        db.Open();
        using var make = db.CreateCommand();
        make.CommandText = """
            CREATE TABLE loose (word TEXT NULL, tally INTEGER NULL, big INTEGER NULL);
            INSERT INTO loose (word, tally, big) VALUES (NULL, NULL, NULL);
            """;
        make.ExecuteNonQuery();

        return db;
    }

    [Fact]
    public void ANullColumnReadsAsAnEmptyStringRatherThanThrowing()
    {
        using var db = Nullable();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT word, tally, big FROM loose";
        using var rows = read.ExecuteReader();
        rows.Read().Should().BeTrue();

        Columns.Text(rows, "word").Should().BeEmpty("a reader shared by every projection may not throw on a null");
        Columns.Number(rows, "tally").Should().Be(0);
        Columns.Id(rows, "big").Should().Be(0);
    }

    [Fact]
    public void AValueThatIsThereStillReadsAsItself()
    {
        // The companion: a reader that answered the default for EVERYTHING would pass the test above.
        using var db = new SqliteConnection("Data Source=:memory:");
        db.Open();
        using var make = db.CreateCommand();
        make.CommandText = """
            CREATE TABLE tight (word TEXT NOT NULL, tally INTEGER NOT NULL, big INTEGER NOT NULL);
            INSERT INTO tight (word, tally, big) VALUES ('said', 7, 9000000000);
            """;
        make.ExecuteNonQuery();

        using var read = db.CreateCommand();
        read.CommandText = "SELECT word, tally, big FROM tight";
        using var rows = read.ExecuteReader();
        rows.Read().Should().BeTrue();

        Columns.Text(rows, "word").Should().Be("said");
        Columns.Number(rows, "tally").Should().Be(7);
        Columns.Id(rows, "big").Should().Be(9_000_000_000L, "an id is 64-bit and must not be truncated");
    }

    [Fact]
    public void AColumnTheSelectDoesNotHaveThrowsRatherThanAnsweringSomethingElse()
    {
        // The reason for reading by name at all: sixteen columns read by ordinal answer the wrong
        // field the day somebody reorders the SELECT. By name it fails, which is the right direction.
        using var db = Nullable();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT word FROM loose";
        using var rows = read.ExecuteReader();
        rows.Read().Should().BeTrue();

        var reading = () => Columns.Text(rows, "no_such_column");

        // `GetOrdinal` answers with ArgumentOutOfRangeException naming the parameter, not an index one —
        // checked rather than assumed, because the first version of this test guessed wrong.
        reading.Should().Throw<ArgumentOutOfRangeException>();
    }
}
