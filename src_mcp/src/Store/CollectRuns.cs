using CoaiMcp.Core.Collecting;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>
/// Reading a collector run back, in the one place both readers go through.
/// </summary>
/// <remarks>
/// <para>It was written twice for one commit — once in <see cref="RoundsDb"/> for the collector and
/// once in <see cref="BugsQuery"/> for `--bugs-json` — with the same twelve columns and the same
/// twelve-argument mapping copied between them. Two reviewers said the same thing about it, and the
/// second put the failure precisely: a column added to one and not the other makes the CLI and the
/// panel report different run states from the same table, and nothing would say so.</para>
/// <para>A static over a connection rather than a method on either class, because the two callers do
/// not share a base: one owns a read-write connection it migrates, the other opens a read-only one it
/// promises never to write through. What they share is the question.</para>
/// </remarks>
internal static class CollectRuns
{
    /// <summary>The columns, in the order <see cref="Map"/> reads them.</summary>
    internal const string SqlLast = """
        SELECT id, started_utc, finished_utc, heartbeat_utc, state, model,
               candidates, picked, collected, skipped, failed, reasons
          FROM collect_runs ORDER BY started_utc DESC LIMIT 1
        """;

    /// <summary>The most recent run, or an empty row when none has ever started.</summary>
    /// <remarks>
    /// Empty rather than null, per the family's rule about nulls in business logic, and it carries a
    /// meaning the panel needs: <i>no run has ever happened here</i> is a state the section renders,
    /// and it must not be confused with <i>this build cannot tell you</i>.
    /// </remarks>
    internal static CollectRunRow Last(SqliteConnection db)
    {
        using var read = db.CreateCommand();
        read.CommandText = SqlLast;
        using var rows = read.ExecuteReader();

        return rows.Read() ? Map(rows) : new CollectRunRow();
    }

    /// <summary>One row, by position, matching <see cref="SqlLast"/>.</summary>
    /// <remarks>
    /// Every text column is declared <c>NOT NULL</c> with a default, so none of these can be DBNull —
    /// which is what makes a positional read safe here. A reviewer of the code round expected
    /// <c>finished_utc</c> to be null while a run is in flight; it is <c>''</c>, because the schema
    /// says so and because "" is the value the panel renders as "still going".
    /// </remarks>
    private static CollectRunRow Map(SqliteDataReader rows) => new(
        rows.GetString(0),
        rows.GetString(1),
        rows.GetString(2),
        rows.GetString(3),
        rows.GetString(4),
        rows.GetString(5),
        rows.GetInt32(6),
        rows.GetInt32(7),
        rows.GetInt32(8),
        rows.GetInt32(9),
        rows.GetInt32(10),
        rows.GetString(11));
}
