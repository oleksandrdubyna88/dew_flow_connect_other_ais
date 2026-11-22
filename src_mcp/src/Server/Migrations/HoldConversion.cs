using CoaiMcp.Core.Rounds;
using CoaiMcp.Store;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Server.Migrations;

/// <summary>
/// The one conversion of a held session, shared by its two doors: the migration (<see cref="MigrationV2"/>) and the load of a session that is still held when it is
/// read (<see cref="SessionStore.Load"/>, SPEC §5.1).
/// </summary>
/// <remarks>
/// Two doors, one function, so a session converted by a client request and one converted by the migration are the same bytes — the tests compare them.
/// </remarks>
internal static class HoldConversion
{
    /// <summary>What the held round's record says after the conversion. The verdict stays what it was.</summary>
    public const string Note = "good_enough — converted from a held gate by v2";

    /// <summary>The session as v2 reads it: the hold left, the stage advancing on the next resolve, and the held round's note. Verdict, stage, rejections and pending stay.</summary>
    public static PersistedSession Converted(PersistedSession session) =>
        session with
        {
            State = LegacyHold.Released(session.State),
            Rounds = session.Rounds.Count == 0
                ? session.Rounds
                : [.. session.Rounds[..^1], session.Rounds[^1] with { Note = Note }],
        };

    /// <summary>
    /// The journal line for a session converted outside a migration run (<c>kind: session</c>, <c>state: done</c>, the detail naming the door). Best effort: the journal is
    /// evidence, not authority, and a folder that cannot take the line has still had its session converted.
    /// </summary>
    public static void Journaled(string dataDir, string sessionFile, string detail)
    {
        try
        {
            Directory.CreateDirectory(MigrationV2.MigrationsDir(dataDir));
            new MigrationV2Journal(MigrationV2.JournalFile(dataDir), TimeProvider.System).Append("session", Path.GetFileName(sessionFile), "done", detail);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // evidence only
        }
    }

    /// <summary>The note into <c>coai.db</c>, best effort: the database is a projection rebuilt from the session files, and one that is absent or will not open is left alone.</summary>
    public static void WriteNote(string dataDir, string sessionId, RoundRecord round, Action<string> said)
    {
        if (!File.Exists(Path.Combine(dataDir, RoundsDb.FileName)))
        {
            return;
        }

        try
        {
            using var db = RoundsDb.OpenExisting(dataDir, Serilog.Core.Logger.None);
            if (db is not null)
            {
                RoundNotes.Write(db, sessionId, round.Stage, round.Number, Note);
            }
        }
        catch (SqliteException e)
        {
            said($"v2 migration: the note of round {round.Stage} {round.Number} could not be written into coai.db ({e.Message}); the session file has it");
        }
    }
}
