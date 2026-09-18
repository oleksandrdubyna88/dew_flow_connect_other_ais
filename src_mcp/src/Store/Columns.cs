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
/// <remarks>
/// <para><b>A null reads as the type's own empty, and that is not a new kindness — it is the guard
/// this extraction dropped.</b> The private <c>Number</c> these came from read
/// <c>rows.IsDBNull(at) ? 0 : Convert.ToInt32(rows.GetValue(at), …)</c>; the first extracted copy
/// called <c>GetInt32</c> straight, which throws <i>"The data is NULL at ordinal 0"</i>. Two
/// reviewers on story 2.1's code round noticed, and they were right about the principle even though
/// they were wrong about the consequence: every column the review page's projection reads is
/// declared <c>NOT NULL DEFAULT</c>, so no caller TODAY can reach it. A shared reader with a hole in
/// it is still a shared reader with a hole in it, waiting for the first SELECT of a nullable
/// column.</para>
/// </remarks>
internal static class Columns
{
    internal static string Text(SqliteDataReader rows, string column) =>
        At(rows, column) is var at && rows.IsDBNull(at) ? string.Empty : rows.GetString(at);

    internal static int Number(SqliteDataReader rows, string column) =>
        At(rows, column) is var at && rows.IsDBNull(at)
            ? 0
            : Convert.ToInt32(rows.GetValue(at), System.Globalization.CultureInfo.InvariantCulture);

    internal static long Id(SqliteDataReader rows, string column) =>
        At(rows, column) is var at && rows.IsDBNull(at)
            ? 0L
            : Convert.ToInt64(rows.GetValue(at), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The ordinal of a named column — the one place the name becomes a position.</summary>
    /// <remarks>
    /// <c>GetOrdinal</c> throws <see cref="ArgumentOutOfRangeException"/> for a column the SELECT
    /// does not have, which is the direction this reader is meant to fail in: by name it refuses,
    /// by ordinal it would answer the wrong field.
    /// </remarks>
    private static int At(SqliteDataReader rows, string column) => rows.GetOrdinal(column);
}
