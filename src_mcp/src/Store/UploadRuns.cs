using CoaiMcp.Core.Collecting;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>
/// Reading a send back, in the one place both readers go through.
/// </summary>
/// <remarks>
/// A static over a connection rather than a method on either class, exactly as
/// <see cref="CollectRuns"/> is: the CLI owns a read-write connection it migrates, and `--bugs-json`
/// opens a read-only one it promises never to write through. What they share is the question, and
/// the reason that file exists at all is that the same twelve columns were once mapped twice.
/// </remarks>
internal static class UploadRuns
{
    /// <summary>The columns, in the order <see cref="Map"/> reads them.</summary>
    internal const string SqlLast = """
        SELECT id, started_utc, finished_utc, heartbeat_utc, state, server,
               offered, sent, duplicate, refused, trouble
          FROM upload_runs ORDER BY started_utc DESC LIMIT 1
        """;

    /// <summary>The most recent send, or an empty row when none has ever started.</summary>
    internal static UploadRunRow Last(SqliteConnection db)
    {
        using var read = db.CreateCommand();
        read.CommandText = SqlLast;
        using var rows = read.ExecuteReader();

        return rows.Read() ? Map(rows) : new UploadRunRow();
    }

    /// <summary>One row, by position, matching <see cref="SqlLast"/>.</summary>
    /// <remarks>
    /// Every text column is declared <c>NOT NULL</c> with a default, which is what makes a positional
    /// read safe: <c>finished_utc</c> is <c>''</c> while a send is in flight rather than null,
    /// because "" is the value the panel renders as "still going".
    /// </remarks>
    private static UploadRunRow Map(SqliteDataReader rows) => new(
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
        rows.GetString(10));
}
