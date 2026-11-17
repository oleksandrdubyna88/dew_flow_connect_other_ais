namespace CoaiMcp.Store;

/// <summary>One round's note, and nothing else of its row — for a writer that holds no <see cref="RoundContext"/>.</summary>
/// <remarks>
/// <para>Why not <c>RecordRound</c>: that method rewrites every column of the row from a <see cref="RoundContext"/> (plan text, head sha, base
/// ref, caller, agent log, commands, plan shape) and re-records the findings. A writer with no context would write all of that as empty.
/// This is the shape of <see cref="RoundsDb.RecordConsultMissed"/>, on the connection's <see cref="RoundsDb.Command"/> seam, because
/// <c>RoundsDb.cs</c> is past the file ceiling. No schema step: <c>rounds.note</c> is step 16's.</para>
/// <para>A round the database does not hold is not created: the projection is rebuilt from the session files, and the note is in the
/// session file.</para>
/// </remarks>
internal static class RoundNotes
{
    /// <returns>The rows changed: 1, or 0 when the database holds no such round — nothing is inserted.</returns>
    internal static int Write(RoundsDb db, string sessionId, string stage, int number, string note)
    {
        using var write = db.Command();
        write.CommandText = """
            UPDATE rounds SET note = $note
            WHERE session_id = $session AND stage = $stage AND number = $number
            """;
        write.Parameters.AddWithValue("$note", note);
        write.Parameters.AddWithValue("$session", sessionId);
        write.Parameters.AddWithValue("$stage", stage);
        write.Parameters.AddWithValue("$number", number);

        return write.ExecuteNonQuery();
    }
}
