using System.Text.Json;
using CoaiMcp.Core.Authors;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Store;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Server;

/// <summary>Where a write may be stopped by a test: the run "killed" between the two halves of one round's write.</summary>
internal enum WritePoint
{
    /// <summary>The session file holds the verified snapshot; no row has been written yet.</summary>
    FileSaved,

    /// <summary><see cref="RoundChangesTable.Record"/> deleted and re-inserted the round's size rows; the authors rows are not written yet.</summary>
    ChangesRecorded,
}

/// <summary>What one round's write comes to.</summary>
internal enum WriteResult
{
    Written,

    /// <summary>The session's claim is held by a live round, or another writer holds the database: nothing was written, the next run retries.</summary>
    DeferredBusy,

    /// <summary>A live server wrote the round — or the session file — since it was selected: nothing was written.</summary>
    DeferredChanged,
}

/// <summary>Which halves of a round's write happen.</summary>
internal enum WriteHalves
{
    /// <summary>The session file's snapshot, then the database projection FROM it, in one transaction.</summary>
    Both,

    /// <summary>The file only: a snapshot the file lost, put back (a heal is built from the database; an equal result is the computed snapshot).</summary>
    FileOnly,

    /// <summary>The database only: the gate session's file is gone or replaced, so there is none to hold the snapshot.</summary>
    DatabaseOnly,
}

/// <summary>One round's backfilled authors and size — null for a half there is nothing to change.</summary>
/// <param name="Expected">The round's <see cref="RoundFingerprint"/> when it was selected; the write re-reads it inside the lock.</param>
internal sealed record RoundWrite(StoredGate Gate, StoredRound Round, WriteHalves Halves, AuthorsSnapshot? Authors, RoundSize? Size, int? Gitlinks, string Expected);

/// <summary>
/// The narrow write (E7.S3 of <c>todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E7.md</c>): ONE round's authors and size into BOTH places —
/// the session file's snapshot and the database projection — without touching another byte of either, and so that running it again changes nothing.
/// </summary>
/// <remarks>
/// <para><b>Never <c>RecordRound</c>.</b> It upserts every column of the round row, deletes and re-inserts the round's reviewers and deletes every
/// finding past the count it is handed; a backfill holds none of those inputs, so through it a resolution would be lost. The writer calls only
/// <see cref="RoundAuthorsTable.Record"/> and <see cref="RoundChangesTable.Record"/> — never a second <c>INSERT</c>.</para>
/// <para><b>The order of one round's write</b> (plan gate finding 3, reshaped by the risk consultation): the session's claim
/// (<see cref="SessionClaim"/>; busy → deferred, nothing written) → <c>BEGIN IMMEDIATE</c> (<see cref="RoundsDb.Transaction"/>; a busy writer →
/// deferred) → VALIDATE inside the lock — the round's <see cref="RoundFingerprint"/> must be the one it was selected with, or a live server wrote it and
/// the round is deferred — → load the file, replace ONLY the matching <c>(stage, number)</c> round's authors and size, save, load again and VERIFY
/// (a snapshot the file did not keep → rollback, the database untouched) → the two row writers, FROM the verified copy → commit. The conflict check
/// comes BEFORE the file save: after it, a conflict would roll SQLite back and leave a stale snapshot for a later re-projection. The write lock spans
/// one bounded file save.</para>
/// <para><b>The file is written first</b>, so a run killed between the two halves leaves a snapshot without its projection (or over rows that disagree
/// with it) — which S1 re-projects on the next run without reading the transcript. A fault between the delete and the insert of a table leaves the
/// round's earlier rows, never none: both writers run inside the one transaction.</para>
/// <para><b>Two writers do not take the claim</b> — <c>open</c> and the startup orphan sweep save without it — so a snapshot can still be stripped
/// AFTER a verified write. The database row stays as written and the next run recomputes it while the transcript exists, heals it when it is gone.</para>
/// </remarks>
internal sealed class BackfillWriter(string dataDir, SessionStore store, RoundsDb db, TimeSpan? busyWait = null, Action<WritePoint>? at = null) : IBackfillWrites
{
    private static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(5);

    private static readonly PersistedSession Stub = new(new SessionState("stub", "stub", "stub", new PanelConfig()), []);

    private bool _waitSet;

    /// <summary>Writes one round; the halves are the <see cref="RoundWrite.Halves"/>'s.</summary>
    public WriteResult Write(RoundWrite write) => Locked(write.Gate, () => Apply(write));

    /// <summary>
    /// Puts back the snapshot the FILE lost, built from the database INSIDE the lock so the rows cannot change between the build and the save. The
    /// database is not written: the column keeps the live source and the healed snapshot says <c>healed</c>.
    /// </summary>
    public WriteResult Heal(StoredGate gate, StoredRound round, string expected) =>
        Locked(gate, () => Fingerprint(round) != expected || AuthorsHeal.From(db.Command, round) is not { } healed ? WriteResult.DeferredChanged : SaveOnly(gate, round, healed));

    private WriteResult SaveOnly(StoredGate gate, StoredRound round, AuthorsSnapshot snapshot) =>
        Save(new RoundWrite(gate, round, WriteHalves.FileOnly, snapshot, null, null, string.Empty)) is null ? WriteResult.DeferredChanged : WriteResult.Written;

    // ---------- the claim and the transaction ----------

    private WriteResult Locked(StoredGate gate, Func<WriteResult> body)
    {
        using var claim = SessionClaim.TryTake(dataDir, gate.RepoPath, gate.Branch);
        if (claim is null)
        {
            return WriteResult.DeferredBusy;
        }

        return Begin() is { } transaction ? Inside(transaction, body) : WriteResult.DeferredBusy;
    }

    private static WriteResult Inside(SqliteTransaction transaction, Func<WriteResult> body)
    {
        using (transaction)
        {
            var result = body();
            if (result == WriteResult.Written)
            {
                transaction.Commit();
            }

            return result;
        }
    }

    /// <summary><c>BEGIN IMMEDIATE</c>, so the write lock is taken here and a competing writer surfaces as busy HERE — never as a stale overwrite later.</summary>
    private SqliteTransaction? Begin()
    {
        SetWaitOnce();
        try
        {
            return db.Transaction();
        }
        catch (SqliteException e) when (e.SqliteErrorCode is 5 or 6)
        {
            return null;
        }
    }

    private void SetWaitOnce()
    {
        if (_waitSet)
        {
            return;
        }

        var wait = busyWait ?? DefaultWait;
        using var command = db.Command();
        command.Connection!.DefaultTimeout = (int)Math.Ceiling(wait.TotalSeconds);
        command.CommandText = $"PRAGMA busy_timeout = {(int)wait.TotalMilliseconds}";
        command.ExecuteNonQuery();
        _waitSet = true;
    }

    // ---------- inside the lock ----------

    private WriteResult Apply(RoundWrite write) =>
        Fingerprint(write.Round) != write.Expected ? WriteResult.DeferredChanged
        : Verify(write) is { } verified ? Continue(write, verified)
        : WriteResult.DeferredChanged;

    /// <summary>The file half, saved and verified — or, with no file, what the result was.</summary>
    private Verified? Verify(RoundWrite write) =>
        write.Halves == WriteHalves.DatabaseOnly ? new Verified(write.Authors, write.Size, write.Gitlinks) : Save(write);

    private WriteResult Continue(RoundWrite write, Verified verified)
    {
        at?.Invoke(WritePoint.FileSaved);

        return write.Halves == WriteHalves.FileOnly ? WriteResult.Written : Project(write, verified);
    }

    private string Fingerprint(StoredRound round) => RoundFingerprint.Read(db.Command, round.RoundId);

    /// <summary>What the session file now holds for the round — or, with no file, what the result was.</summary>
    private sealed record Verified(AuthorsSnapshot? Authors, RoundSize? Size, int? Gitlinks);

    /// <summary>
    /// The database half, FROM the verified copy and never from the in-memory result: the size rows, then the authors columns and rows. Both
    /// writers delete before they insert and join this transaction; a null half writes nothing.
    /// </summary>
    /// <remarks>
    /// A half is projected only when the WRITE carries it — the authors when it carries a snapshot, the size when it carries a size. A write without
    /// one means the decision kept the database row, and the file's own copy (a healed snapshot, an older reader's, a different size) must not
    /// replace it: a live size is never replaced.
    /// </remarks>
    private WriteResult Project(RoundWrite write, Verified verified)
    {
        var (size, gitlinks) = write.Size is null ? ((RoundSize?)null, (int?)null) : (verified.Size, verified.Gitlinks);
        RoundChangesTable.Record(db, write.Round.RoundId, size, gitlinks);
        at?.Invoke(WritePoint.ChangesRecorded);
        RoundAuthorsTable.Record(db, write.Round.RoundId, write.Authors is null ? null : verified.Authors);

        return WriteResult.Written;
    }

    // ---------- the file ----------

    /// <summary>
    /// Replaces ONLY the matching round's authors, size and gitlinks in the session file, saves, loads again and compares. Null when the file is gone,
    /// names another session, no longer lists the round, or did not keep what was saved.
    /// </summary>
    private Verified? Save(RoundWrite write) =>
        Loaded(write.Gate) is { } file && file.Rounds.FindIndex(round => round.Stage == write.Round.Stage && round.Number == write.Round.Number) is var index and >= 0
            ? SaveRound(file, index, write)
            : null;

    /// <summary>The gate's file as it is now — null when it is gone or names another session.</summary>
    private PersistedSession? Loaded(StoredGate gate) =>
        store.Load(gate.RepoPath, gate.Branch) is { } file && file.State.SessionId == gate.SessionId ? file : null;

    private Verified? SaveRound(PersistedSession file, int index, RoundWrite write)
    {
        var updated = Replaced(file.Rounds[index], write);
        var rounds = new List<RoundRecord>(file.Rounds) { [index] = updated };
        store.Save(file with { Rounds = rounds });

        return store.Load(write.Gate.RepoPath, write.Gate.Branch) is { } again && index < again.Rounds.Count && Same(again.Rounds[index], updated)
            ? new Verified(again.Rounds[index].Authors, again.Rounds[index].Size, again.Rounds[index].Gitlinks)
            : null;
    }

    private static RoundRecord Replaced(RoundRecord round, RoundWrite write) =>
        round with
        {
            Authors = write.Authors ?? round.Authors,
            Size = write.Size ?? round.Size,
            Gitlinks = write.Size is null ? round.Gitlinks : write.Gitlinks,
        };

    /// <summary>Two rounds are the same when the session file would write them the same: the serialised form, which is what was saved and what was read back.</summary>
    private static bool Same(RoundRecord left, RoundRecord right) => Json(left) == Json(right);

    private static string Json(RoundRecord round) =>
        JsonSerializer.Serialize(Stub with { Rounds = [round] }, ServerJsonContext.Default.PersistedSession);
}
