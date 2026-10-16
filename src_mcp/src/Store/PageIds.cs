using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>
/// Handing a page's row ids to a read-only query — the one binder every page-scoped reader of <c>--log</c> shares
/// (<see cref="RoundChangesLog"/>, <see cref="RoundPlanSources"/>).
/// </summary>
internal static class PageIds
{
    /// <summary>
    /// The page's ids as ONE bound parameter <c>$ids</c>, a JSON array of integers that the constant SQL unpacks with
    /// <c>json_each</c> — no SQL text is ever built from data, and no page can reach SQLite's variable limit.
    /// </summary>
    internal static void Bind(SqliteCommand command, IReadOnlyCollection<long> ids) =>
        command.Parameters.AddWithValue("$ids", "[" + string.Join(',', ids) + "]");
}
