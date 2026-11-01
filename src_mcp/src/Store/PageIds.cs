using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>
/// Handing a page's row ids to a read-only query — the one binder every page-scoped reader of <c>--log</c> shares
/// (<see cref="RoundChangesLog"/>, <see cref="RoundPlanSources"/>, <see cref="RoundAuthorsLog"/>).
/// </summary>
internal static class PageIds
{
    /// <summary>
    /// The page's ids as ONE bound parameter <c>$ids</c>, a JSON array of integers that the constant SQL unpacks with
    /// <c>json_each</c> — no SQL text is ever built from data, and no page can reach SQLite's variable limit.
    /// </summary>
    internal static void Bind(SqliteCommand command, IReadOnlyCollection<long> ids) =>
        command.Parameters.AddWithValue("$ids", "[" + string.Join(',', ids) + "]");

    /// <summary>
    /// The database id of a logged round, read from the round's OWN cursor (<c>started_utc|id</c>) — so a reader that attaches
    /// something to a page correlates by what each round says it is, never by where it happens to sit in a list. 0 when the
    /// cursor does not carry one, which matches no row.
    /// </summary>
    internal static long Of(LoggedRound round) =>
        long.TryParse(round.Cursor[(round.Cursor.LastIndexOf('|') + 1)..], out var id) ? id : 0L;
}
