using CoaiMcp.Core.Authors;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>
/// The writer of schema step 24: a round's authors as one <c>round_authors</c> row per row of its snapshot, and the state, the versions,
/// the write counts and the ambiguous totals on the round's own row.
/// </summary>
/// <remarks>
/// <para>Its own file, as <see cref="RoundChangesTable"/> is, and it reaches the connection the same way — <see cref="RoundsDb.Command"/>.
/// It is called from <c>RecordRound</c> inside that method's transaction and, later, from E7's backfill inside its own.
/// <b>It opens no transaction of its own</b>: SQLite refuses one inside another, <see cref="Projection"/> would swallow that as a
/// warning and the authors would simply be missing. Its statements join whatever transaction the caller holds.</para>
/// <para><b>Projected FROM the round's snapshot, never from a <c>RoundContext</c></b> — one fact, one source, as the caller's
/// declaration is — so a database deleted and rebuilt from the session files comes back with the same rows. A null snapshot writes NOTHING:
/// what a backfill wrote earlier survives a live re-record that measured nothing, as a null size does.</para>
/// <para><b>An unknown is <c>-1</c></b> and is written as the snapshot carries it; this class never turns a missing measurement into a zero.</para>
/// </remarks>
public static class RoundAuthorsTable
{
    /// <summary>
    /// Replaces the round's authors rows and columns with <paramref name="authors"/>'s. A null snapshot writes NOTHING.
    /// </summary>
    public static void Record(RoundsDb db, long roundId, AuthorsSnapshot? authors)
    {
        if (authors is null)
        {
            return;
        }

        UpdateColumns(db, roundId, authors);
        Delete(db, roundId);
        for (var ordinal = 0; ordinal < authors.Rows.Length; ordinal++)
        {
            Insert(db, roundId, ordinal, authors.Rows[ordinal]);
        }
    }

    /// <summary>
    /// The one mapping from E4's agent key to the column: <c>main</c> is the EMPTY string, because "3 Sonnet coders" counts distinct agent
    /// ids and contributors deduplicate on one (parent §11 step 4); E4 spells it <c>main</c> in the core. Nothing else turns a key into the
    /// column.
    /// </summary>
    internal static string AgentColumn(AgentKey agent) => agent.IsMain ? string.Empty : agent.Id;

    /// <summary>The one inverse, beside the mapping: the empty column is <c>main</c>. E6's read and E7's heal from the database call it.</summary>
    internal static AgentKey AgentKeyOf(string column) => column.Length == 0 ? AgentKey.Main : new AgentKey(column);

    private static void UpdateColumns(RoundsDb db, long roundId, AuthorsSnapshot authors)
    {
        using var write = db.Command();
        write.CommandText = """
            UPDATE rounds
               SET authors_state = $state, authors_session = $session, authors_id_source = $idSource,
                   authors_reader = $reader, authors_attribution = $attribution,
                   submodule_writes = $submodules, uncovered_writes = $uncovered,
                   ambiguous_agents = $agents, ambiguous_launches = $launches, ambiguous_turns = $turns,
                   ambiguous_input_tokens = $input, ambiguous_output_tokens = $output,
                   ambiguous_cache_creation_tokens = $creation, ambiguous_cache_read_tokens = $read
             WHERE id = $id
            """;
        Bind(write, "$state", authors.Render());
        Bind(write, "$session", authors.Session);
        Bind(write, "$idSource", authors.IdSource);
        Bind(write, "$reader", authors.Reader);
        Bind(write, "$attribution", authors.Attribution);
        Bind(write, "$submodules", authors.SubmoduleWrites);
        Bind(write, "$uncovered", authors.UncoveredWrites);
        Bind(write, "$agents", authors.Ambiguous.Agents);
        Bind(write, "$launches", authors.Ambiguous.Launches);
        Bind(write, "$turns", authors.Ambiguous.Turns);
        Bind(write, "$input", authors.Ambiguous.Tokens.Input);
        Bind(write, "$output", authors.Ambiguous.Tokens.Output);
        Bind(write, "$creation", authors.Ambiguous.Tokens.CacheCreation);
        Bind(write, "$read", authors.Ambiguous.Tokens.CacheRead);
        Bind(write, "$id", roundId);
        write.ExecuteNonQuery();
    }

    private static void Delete(RoundsDb db, long roundId)
    {
        using var write = db.Command();
        write.CommandText = "DELETE FROM round_authors WHERE round_id = $id";
        Bind(write, "$id", roundId);
        write.ExecuteNonQuery();
    }

    private static void Insert(RoundsDb db, long roundId, int ordinal, AuthorRow row)
    {
        using var write = db.Command();
        write.CommandText = """
            INSERT INTO round_authors (round_id, ordinal, who, agent, requested, model, effort, agent_type, association, window,
                                       launches, turns, input_tokens, output_tokens, cache_creation_tokens, cache_read_tokens, write_calls)
            VALUES ($id, $ordinal, $who, $agent, $requested, $model, $effort, $type, $association, $window,
                    $launches, $turns, $input, $output, $creation, $read, $writes)
            """;
        Bind(write, "$id", roundId);
        Bind(write, "$ordinal", ordinal);
        Bind(write, "$who", row.Who);
        Bind(write, "$agent", AgentColumn(row.Agent));
        Bind(write, "$requested", row.Requested);
        Bind(write, "$model", row.Model);
        Bind(write, "$effort", row.Effort);
        Bind(write, "$type", row.AgentType);
        Bind(write, "$association", row.Association);
        Bind(write, "$window", row.Window);
        Bind(write, "$launches", row.Launches);
        Bind(write, "$turns", row.Turns);
        Bind(write, "$input", row.Tokens.Input);
        Bind(write, "$output", row.Tokens.Output);
        Bind(write, "$creation", row.Tokens.CacheCreation);
        Bind(write, "$read", row.Tokens.CacheRead);
        Bind(write, "$writes", row.WriteCalls);
        write.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);
}
