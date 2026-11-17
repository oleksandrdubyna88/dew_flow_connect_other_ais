using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// One round's note in <c>coai.db</c>, written by a statement that names one column of one row
/// (<c>todo/PLAN_auto_never_asks_a_person_E1_S1_spec.md</c>, § S1.5.1).
/// </summary>
/// <remarks>
/// Real SQLite over a test folder, as <c>RoundsDbTests</c>: the point is that the SQL runs. The writer is the narrow one a release uses
/// because it holds no <c>RoundContext</c> — going through <c>RecordRound</c> would write that context as empty.
/// </remarks>
public sealed class RoundNotesTests : IDisposable
{
    private const string SessionId = "s";

    private readonly TempDir _dir = TempDir.For("coai-roundnotes-");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _dir.Dispose();
    }

    private static readonly SessionState Session = new(SessionId, "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.PlanReview };

    private static RoundRecord Round(int number) =>
        new("PlanReview", number, number == 1 ? "revise" : "call_human", 1, "all 1 reviewers answered", DateTime.UtcNow)
        {
            StartedUtc = DateTime.UtcNow.AddMinutes(-4),
            Subject = $"plan round {number}",
            TokensIn = 100 * number,
            TokensOut = 10 * number,
        };

    private static Finding Found(string title) =>
        new(Severity.Major, Category.Reliability, "src/Panel.cs", 40, title, title + ", and here is why", "fix " + title, ["codex"]);

    private RoundsDb SeededDb()
    {
        var db = RoundsDb.Open(_dir.Path, Serilog.Core.Logger.None)!;
        foreach (var number in new[] { 1, 2 })
        {
            db.RecordRound(
                Session,
                Round(number),
                [Found($"finding of round {number}")],
                new RoundContext(PlanText: "PLAN", HeadSha: $"sha{number}", Caller: "claude"));
        }

        return db;
    }

    /// <summary>Every column of the row, read back as text — what "byte for byte" is measured on.</summary>
    private Dictionary<string, string> RowOf(string stage, int number)
    {
        using var db = new SqliteConnection($"Data Source={Path.Combine(_dir.Path, RoundsDb.FileName)}");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT * FROM rounds WHERE session_id = $s AND stage = $stage AND number = $n";
        read.Parameters.AddWithValue("$s", SessionId);
        read.Parameters.AddWithValue("$stage", stage);
        read.Parameters.AddWithValue("$n", number);
        using var reader = read.ExecuteReader();
        var row = new Dictionary<string, string>();
        if (reader.Read())
        {
            for (var column = 0; column < reader.FieldCount; column++)
            {
                row[reader.GetName(column)] = CellText(reader, column);
            }
        }

        return row;
    }

    private static string CellText(SqliteDataReader reader, int column) =>
        reader.IsDBNull(column) ? "<null>" : reader.GetValue(column).ToString() ?? string.Empty;

    private long RowCount()
    {
        using var db = new SqliteConnection($"Data Source={Path.Combine(_dir.Path, RoundsDb.FileName)}");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT COUNT(*) FROM rounds";
        return (long)read.ExecuteScalar()!;
    }

    [Fact]
    public void Write_ChangesOneColumnOfOneRow_AndCreatesNoRow()
    {
        using var db = SeededDb();
        var roundOne = RowOf("PlanReview", 1);
        var roundTwoBefore = RowOf("PlanReview", 2);

        RoundNotes.Write(db, SessionId, "PlanReview", 2, LegacyHold.Note).Should().Be(1);
        RowOf("PlanReview", 2)["note"].Should().Be(LegacyHold.Note);

        RoundNotes.Write(db, SessionId, "PlanReview", 2, LegacyHold.Note).Should().Be(1, "written twice, it writes the same note");
        var roundTwo = RowOf("PlanReview", 2);
        roundTwo["note"].Should().Be(LegacyHold.Note);
        foreach (var (column, value) in roundTwoBefore.Where(c => c.Key != "note"))
        {
            roundTwo[column].Should().Be(value, $"column {column} of the written row is not touched");
        }

        RoundNotes.Write(db, SessionId, "CodeReview", 1, LegacyHold.Note).Should().Be(0, "the database holds no such round");
        RowCount().Should().Be(2, "nothing is inserted");

        RowOf("PlanReview", 1).Should().Equal(roundOne, "round 1's row is what it was, every column");
    }
}
