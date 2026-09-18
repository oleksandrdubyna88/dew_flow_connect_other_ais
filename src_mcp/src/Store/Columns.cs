using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>
/// Reading a column by NAME off a row, for the readers that project a JOIN.
/// </summary>
/// <remarks>
/// <para>Three one-liners, and they are a file because they were private to <see cref="BugsQuery"/>
/// and the review page's projection needed the same three. Sixteen columns read by ordinal is a
/// reader that silently answers the wrong field the day somebody reorders the SELECT; a reader that
/// asks by name throws on a column the SELECT no longer has, which is the direction a defect should
/// fail in. Copying the helpers into <see cref="RoundsDb"/> would have been the duplication the
/// reuse rule names, so they moved out instead (story 2.1 of the review-page plan).</para>
/// <para>SQLite names a selected column by its own name when it is not aliased — <c>f.id</c> arrives
/// as <c>id</c> — which is what every caller here relies on. A computed column is aliased at the
/// SELECT.</para>
/// </remarks>
internal static class Columns
{
    internal static string Text(SqliteDataReader rows, string column) =>
        rows.GetString(rows.GetOrdinal(column));

    internal static int Number(SqliteDataReader rows, string column) =>
        rows.GetInt32(rows.GetOrdinal(column));

    internal static long Id(SqliteDataReader rows, string column) =>
        rows.GetInt64(rows.GetOrdinal(column));
}
