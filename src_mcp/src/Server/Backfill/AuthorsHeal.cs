using System.Collections.Immutable;
using CoaiMcp.Core.Authors;
using CoaiMcp.Store;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Server;

/// <summary>
/// A snapshot rebuilt from the DATABASE, for the session file that lost its own (E7.S3, S1 rule 2(b)): what an older server's save stripped while
/// the transcript it was read from is gone. The rows come back through <see cref="RoundAuthorsTable.AgentKeyOf"/> — never a second spelling of
/// <c>"" → main</c> — and the state and reasons are the column's, split on <c>": "</c> and then <c>", "</c>, which is exact because no reason contains
/// the joiner (E5 §10 item 4).
/// </summary>
/// <remarks>
/// <para><b>What the database never held is UNKNOWN, not empty.</b> <c>Elsewhere</c> and <c>ClaimedByPeer</c> are
/// <see cref="ActivityTotals.Unknown"/>, <c>UnobservedLaunches</c> is <c>-1</c>, <c>Windows</c> and <c>PeerGates</c> are empty — and
/// <c>IdSource</c> is <c>healed</c>, which is what tells S1 that an empty <c>PeerGates</c> here means an UNKNOWN peer set (H1). A healed snapshot is
/// the file's copy only: its source is not projected, so the column keeps the live server's choice.</para>
/// </remarks>
internal static class AuthorsHeal
{
    public const string Source = "healed";

    private const string Columns = """
        SELECT authors_state, authors_session, authors_reader, authors_attribution, submodule_writes, uncovered_writes,
               ambiguous_agents, ambiguous_launches, ambiguous_turns, ambiguous_input_tokens, ambiguous_output_tokens,
               ambiguous_cache_creation_tokens, ambiguous_cache_read_tokens
          FROM rounds WHERE id = $id
        """;

    private const string Rows = """
        SELECT who, agent, requested, model, effort, agent_type, association, window,
               launches, turns, input_tokens, output_tokens, cache_creation_tokens, cache_read_tokens, write_calls
          FROM round_authors WHERE round_id = $id ORDER BY ordinal
        """;

    /// <summary>The snapshot of the round as the database holds it, or null when the state it holds is not one the store could have written.</summary>
    public static AuthorsSnapshot? From(Func<SqliteCommand> command, StoredRound round)
    {
        using var read = command();
        read.CommandText = Columns;
        read.Parameters.AddWithValue("$id", round.RoundId);
        using var columns = read.ExecuteReader();

        return columns.Read() && AuthorsSnapshot.IsWellFormed(columns.GetString(0)) ? Build(columns, round, RowsOf(command, round)) : null;
    }

    private static AuthorsSnapshot Build(SqliteDataReader columns, StoredRound round, ImmutableArray<AuthorRow> rows)
    {
        var (state, reasons) = Split(columns.GetString(0));

        return new AuthorsSnapshot(
            round.Stage, round.Number, columns.GetString(1), state, reasons, rows,
            Totals(columns, 6), ActivityTotals.Unknown, ActivityTotals.Unknown, AuthorsSnapshot.NotMeasured, [], [],
            Source, columns.GetInt32(2), columns.GetInt32(3), columns.GetInt32(4), columns.GetInt32(5));
    }

    /// <summary>The seven ambiguous columns, starting at <paramref name="at"/>: agents, launches, turns, then input, output, cache creation, cache read.</summary>
    private static ActivityTotals Totals(SqliteDataReader columns, int at) =>
        new(columns.GetInt32(at), columns.GetInt32(at + 1), columns.GetInt32(at + 2),
            new TokenTerms(columns.GetInt64(at + 3), columns.GetInt64(at + 4), columns.GetInt64(at + 5), columns.GetInt64(at + 6)));

    private static (string State, ImmutableArray<string> Reasons) Split(string stored)
    {
        var at = stored.IndexOf(": ", StringComparison.Ordinal);

        return at < 0 ? (stored, []) : (stored[..at], [.. stored[(at + 2)..].Split(", ")]);
    }

    private static ImmutableArray<AuthorRow> RowsOf(Func<SqliteCommand> command, StoredRound round)
    {
        using var read = command();
        read.CommandText = Rows;
        read.Parameters.AddWithValue("$id", round.RoundId);
        using var rows = read.ExecuteReader();
        var found = ImmutableArray.CreateBuilder<AuthorRow>();
        while (rows.Read())
        {
            found.Add(Row(rows, round));
        }

        return found.ToImmutable();
    }

    private static AuthorRow Row(SqliteDataReader rows, StoredRound round) => new(
        rows.GetString(7), round.Stage, round.Number, rows.GetString(0), RoundAuthorsTable.AgentKeyOf(rows.GetString(1)), rows.GetString(5),
        rows.GetString(2), rows.GetString(3), rows.GetString(4), rows.GetString(6), rows.GetInt32(8), rows.GetInt32(9),
        new TokenTerms(rows.GetInt64(10), rows.GetInt64(11), rows.GetInt64(12), rows.GetInt64(13)), rows.GetInt32(14));
}
