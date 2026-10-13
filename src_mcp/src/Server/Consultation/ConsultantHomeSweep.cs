using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Vibe;

namespace CoaiMcp.Server;

/// <summary>What one home sweep is given: where the homes are, whose store it is, what its records say, and the clock.</summary>
/// <param name="Store">This server's store as an owner (<see cref="ConversationFolder.StoreOf"/>) — the only folders it judges by its records.</param>
/// <param name="Idle">The conversation's own idle budget, <c>ConsultIdle</c>.</param>
public sealed record HomeSweepInputs(string HomesRoot, string Store, Func<string, ConsultationRecord?> RecordOf, DateTime NowUtc, TimeSpan Idle)
{
    /// <summary>
    /// How long a folder ANOTHER store made (or nobody stamped) is left alone: its owner's records are not this server's to
    /// read, so it is judged by its age alone — the record store's own retention (seven days, after which even a finished
    /// record of its owner is gone), or twice the idle window if that is longer.
    /// </summary>
    public TimeSpan ForeignAge => ConsultationStore.Retention > Idle * 2 ? ConsultationStore.Retention : Idle * 2;
}

/// <summary>
/// The bounded life of a persistent consultant home (D20 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>): every folder under
/// <c>&lt;homes root&gt;/coai-vibe/</c> and <c>&lt;homes root&gt;/coai-kimi/</c> — a conversation's home and its snapshot, named by
/// the consultation's id — is removed once its conversation is over, unknown to the store, or idle past
/// <c>COAI_CONSULT_IDLE_MINUTES</c>, and never while a turn in it could still be running.
/// </summary>
/// <remarks>
/// <para><b>Where it runs.</b> On the consultation sweep's beat (<see cref="ConsultationSweeper"/>) and once at server start
/// (the <see cref="PanelService"/> constructor) — AFTER the records, so an open record idle past the window, lapsed by the
/// record pass of the same beat, takes its home with it.</para>
/// <para><b>Whose folder it is (R1 of the S3 risk consultation).</b> The homes root is the person's local application data,
/// SHARED by every server on the machine — a second server with another data directory has other records. Each folder's
/// stamp (<see cref="ConversationFolder"/>) names the store that made it; a server judges only its OWN store's folders by
/// its records. A folder another store made, or one with no stamp, is left alone until it has not been written for
/// <see cref="HomeSweepInputs.ForeignAge"/>.</para>
/// <para><b>The rules for its own</b>, the window being <c>ConsultIdle</c>: never while the last turn's ceiling has not
/// passed and the turn did not end where it ran (<see cref="ConversationStamp.TurnIsOver"/>, R4: a server killed with a
/// Vibe child alive leaves it to run to its <c>--time-limit</c>, which is under the launch's timeout, which is under that
/// ceiling); then removed when its record is over, when its record is still <c>asking</c> (a host died mid-turn — P0 of the
/// plan round: the turn's ceiling has passed and nobody holds its lock), or when nothing was written inside the window —
/// no record, or an open one.</para>
/// <para><b>A live turn is judged by locks, never by a stamp</b>: the folder's cross-process turn lock
/// (<see cref="ConversationFolder.TakeTurn"/>) and the in-process turns of the folder and every home in it
/// (<see cref="VibeHomeTurns.TryTake"/>) are all taken with no wait, the rules asked AGAIN under them, and a folder any of
/// them is held in is SKIPPED this beat.</para>
/// <para><b>Never through a link (R2).</b> A homes folder or a conversation folder that is a link is refused and said, never
/// walked; the deleter (<see cref="GitScratch.DeleteEvenIfReadOnly"/>) unlinks a link inside by its own name. These are
/// checks by path at one moment: a folder swapped for a link between the check and the delete is not seen — they narrow
/// that race, they do not close it (no deletion here is by handle).</para>
/// <para><b>Never the reason a sweep fails</b>: a folder that cannot be listed, read or removed is said in the log and
/// skipped, and the next beat tries again.</para>
/// </remarks>
public static class ConsultantHomeSweep
{
    /// <summary>One pass over every homes folder; answers how many conversation folders went.</summary>
    /// <param name="delete">The deleter of one directory entry of a folder — the shared one; a test's seam.</param>
    public static int Sweep(HomeSweepInputs inputs, Action<string> warn, Action<string>? delete = null)
    {
        if (inputs.HomesRoot.Length == 0)
        {
            return 0;
        }

        var pass = new Pass(inputs, warn, delete ?? GitScratch.DeleteEvenIfReadOnly);

        return ConsultantHomes.All.Sum(homes => pass.Over(Path.Combine(inputs.HomesRoot, homes)));
    }

    /// <summary>
    /// The folders of ONE conversation removed now, by the same rules — what <c>close_consult</c> does as it closes, rather
    /// than leave it to the next beat. A folder whose turn is held, or whose rules say keep, is left to the beat.
    /// </summary>
    public static void RemoveNow(HomeSweepInputs inputs, string consultationId, Action<string> warn)
    {
        if (inputs.HomesRoot.Length == 0 || !ConsultationStore.IsWellFormedId(consultationId))
        {
            return;
        }

        var pass = new Pass(inputs, warn, GitScratch.DeleteEvenIfReadOnly);
        foreach (var homes in ConsultantHomes.All)
        {
            pass.Settle(ConsultantHomes.ConversationFolder(inputs.HomesRoot, homes, consultationId));
        }
    }

    private sealed class Pass(HomeSweepInputs inputs, Action<string> warn, Action<string> delete)
    {
        /// <summary>Every conversation folder of one homes folder; how many went. A homes folder that is a link is refused.</summary>
        public int Over(string homes)
        {
            if (OwnedPaths.IsLinkIfThere(homes))
            {
                warn($"consultant homes: {homes} is a link, and no folder is swept through one");
                return 0;
            }

            try
            {
                return Directory.Exists(homes) ? Directory.EnumerateDirectories(homes).ToList().Count(Settle) : 0;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                warn($"consultant homes: {homes} could not be listed, and the next beat will try again: {e.Message}");
                return 0;
            }
        }

        /// <summary>One folder: removed when its rules say so under its locks; kept or skipped otherwise — never an exception.</summary>
        public bool Settle(string folder)
        {
            try
            {
                return Admissible(folder) && IsDue(folder) && Removed(folder);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                warn($"consultant homes: {folder} could not be read or removed, and the next beat will try again: {e.Message}");
                return false;
            }
        }

        /// <summary>There, and not a link — a conversation folder that is a link is said and never touched (R2).</summary>
        private bool Admissible(string folder)
        {
            if (!OwnedPaths.IsLinkIfThere(folder))
            {
                return Directory.Exists(folder);
            }

            warn($"consultant homes: {folder} is a link, and it is left alone");
            return false;
        }

        /// <summary>The rules — whose folder it is, whether its last turn can still run, and what its record says.</summary>
        private bool IsDue(string folder)
        {
            var stamp = ConversationFolder.Read(folder);
            var idleFor = inputs.NowUtc - LastWrite(folder);

            return stamp.Store != inputs.Store
                ? idleFor > inputs.ForeignAge
                : stamp.TurnIsOver(inputs.NowUtc) && IsOwnDue(inputs.RecordOf(Path.GetFileName(folder)), idleFor);
        }

        /// <summary>Over, or still asking with its turn's ceiling passed (P0), or nothing written inside the window.</summary>
        private bool IsOwnDue(ConsultationRecord? record, TimeSpan idleFor) => record switch
        {
            { IsOver: true } => true,
            { Status: ConsultationStatuses.Asking } => true,
            _ => idleFor > inputs.Idle,
        };

        /// <summary>
        /// The folder removed under every lock, the rules asked again under them — or skipped when one is held. Its snapshot,
        /// if this process holds it, is let go first; the lock file goes last, after its handle.
        /// </summary>
        private bool Removed(string folder)
        {
            List<IDisposable> turns = [];
            try
            {
                if (ConversationFolder.TakeTurn(folder) is not Hold.Taken locked)
                {
                    return false;
                }

                using (locked.Stream)
                {
                    if (!AllTaken(folder, turns) || !IsDue(folder))
                    {
                        return false;
                    }

                    ConversationSnapshots.Release(folder);
                    DeleteAllBut(folder, Path.Combine(folder, ConversationFolder.LockName));
                }

                GitScratch.DeleteEvenIfReadOnly(folder);

                return true;
            }
            finally
            {
                turns.ForEach(turn => turn.Dispose());
            }
        }

        /// <summary>Every entry of the folder but the held lock file — a directory or a link by the deleter, a file as a file.</summary>
        private void DeleteAllBut(string folder, string held)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder).Where(entry => !string.Equals(entry, held, StringComparison.OrdinalIgnoreCase)))
            {
                if (Directory.Exists(entry) || OwnedPaths.IsLinkIfThere(entry))
                {
                    delete(entry);
                }
                else
                {
                    File.SetAttributes(entry, FileAttributes.Normal);
                    File.Delete(entry);
                }
            }
        }

        /// <summary>The folder's in-process turn and each sub-folder's (a home), with no wait — false the moment one is held.</summary>
        private static bool AllTaken(string folder, List<IDisposable> turns)
        {
            foreach (var path in Directory.EnumerateDirectories(folder).Prepend(folder))
            {
                if (!VibeHomeTurns.TryTake(path, out var turn))
                {
                    return false;
                }

                turns.Add(turn);
            }

            return true;
        }

        /// <summary>
        /// The newest write of the folder's direct entries — a home a turn just wrote is young — or of the folder itself when
        /// it holds nothing. Not the turn lock, and not the folder's own stamp when it holds entries: taking the lock creates
        /// it, and creating it moves the folder's time, so the sweep would otherwise make every folder young by looking.
        /// </summary>
        private static DateTime LastWrite(string folder)
        {
            var times = Directory.EnumerateFileSystemEntries(folder)
                .Where(entry => !Path.GetFileName(entry).Equals(ConversationFolder.LockName, StringComparison.OrdinalIgnoreCase))
                .Select(Directory.GetLastWriteTimeUtc)
                .ToList();

            return times.Count > 0 ? times.Max() : Directory.GetLastWriteTimeUtc(folder);
        }
    }
}
