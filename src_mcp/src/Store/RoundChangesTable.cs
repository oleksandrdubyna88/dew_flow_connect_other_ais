using CoaiMcp.Core.Context;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>
/// The writer of schema step 22: a round's size as one row per (scope, coverage, kind) cell, and the scope states beside it
/// on the round's own row.
/// </summary>
/// <remarks>
/// Its own file rather than more lines in <see cref="RoundsDb"/>, which is past the family's ceiling already; it reaches the
/// connection through <see cref="RoundsDb.Command"/>, the one seam that class opens for a table owned elsewhere. Called
/// from <c>RecordRound</c> inside its transaction, and best-effort by the caller's <see cref="Projection"/> — the session
/// file is the truth and this is a view of it.
/// </remarks>
public static class RoundChangesTable
{
    /// <summary>What <c>rounds.size_gitlinks</c> holds when the round recorded no count — a count of 0 is a measurement.</summary>
    private const int GitlinksNotRecorded = -1;

    internal const string WholeScope = "whole";
    internal const string DeltaScope = "delta";

    /// <summary>
    /// Replaces the round's size rows and states with <paramref name="size"/>'s. A null size writes NOTHING — the gitlink
    /// count included, which is measured with the size or not at all — so a size recorded earlier (a backfill) survives.
    /// </summary>
    public static void Record(RoundsDb db, long roundId, RoundSize? size, int? gitlinks)
    {
        if (size is null)
        {
            return;
        }

        Delete(db, roundId);
        Insert(db, roundId, WholeScope, size.Whole.Cells);
        Insert(db, roundId, DeltaScope, size.Delta.Cells);
        UpdateStates(db, roundId, size, gitlinks);
    }

    private static void Delete(RoundsDb db, long roundId)
    {
        using var write = db.Command();
        write.CommandText = "DELETE FROM round_changes WHERE round_id = $id";
        Bind(write, "$id", roundId);
        write.ExecuteNonQuery();
    }

    private static void Insert(RoundsDb db, long roundId, string scope, IReadOnlyList<SizeCell> cells)
    {
        foreach (var cell in cells)
        {
            using var write = db.Command();
            write.CommandText = """
                INSERT INTO round_changes (round_id, scope, coverage, kind, files, added, removed)
                VALUES ($id, $scope, $coverage, $kind, $files, $added, $removed)
                """;
            Bind(write, "$id", roundId);
            Bind(write, "$scope", scope);
            Bind(write, "$coverage", cell.Coverage);
            Bind(write, "$kind", cell.Kind);
            Bind(write, "$files", cell.Files);
            // A binary has no line count: NULL, never 0 — a zero would say "a file changed and nothing in it did".
            Bind(write, "$added", cell.Added is { } added ? added : DBNull.Value);
            Bind(write, "$removed", cell.Removed is { } removed ? removed : DBNull.Value);
            write.ExecuteNonQuery();
        }
    }

    private static void UpdateStates(RoundsDb db, long roundId, RoundSize size, int? gitlinks)
    {
        using var write = db.Command();
        write.CommandText = """
            UPDATE rounds
               SET size_state = $s, delta_state = $d, delta_from = $f, size_origin = $o, size_gitlinks = $g
             WHERE id = $id
            """;
        Bind(write, "$s", size.Whole.State);
        Bind(write, "$d", size.Delta.State);
        Bind(write, "$f", size.Delta.From);
        Bind(write, "$o", size.Origin);
        Bind(write, "$g", gitlinks ?? GitlinksNotRecorded);
        Bind(write, "$id", roundId);
        write.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);
}
