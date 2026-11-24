namespace CoaiMcp.Server;

/// <summary>
/// A file in the data directory, written whole or not at all, under the turn every reader takes.
/// </summary>
/// <remarks>
/// Extracted from the escalation store the day a second writer needed it — the consultation
/// record — rather than copied there: the measurement behind it (382 of 400 plain writes refused
/// under a hot reader, 2026-09-05) belongs to one method with two callers, not to two methods that
/// will drift.
/// </remarks>
internal static class AtomicJson
{
    /// <summary>Takes the file's turn, then writes it whole (<see cref="WriteUnderTurn"/>).</summary>
    public static void Write(string path, string content)
    {
        using var turn = SessionTurn.Take(path);
        WriteUnderTurn(path, content);
    }

    /// <summary>
    /// The temp file and the move alone, for a caller that already HOLDS the file's turn — a read–modify–write
    /// (S4b item 14).
    /// </summary>
    /// <remarks>
    /// The turn is not re-entrant: <see cref="Write"/> called while this process holds it cannot take it, waits out
    /// its forty attempts and then writes WITHOUT it, which is the race the turn exists to close.
    /// </remarks>
    public static void WriteUnderTurn(string path, string content) => Runners.Files.AtomicFile.Write(path, content, Runners.Files.AtomicFile.SharingRetry);
}
