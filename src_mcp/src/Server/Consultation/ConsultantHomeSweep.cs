using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Vibe;

namespace CoaiMcp.Server;

/// <summary>
/// The bounded life of a persistent consultant home (D20 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>): every folder under
/// <c>&lt;homes root&gt;/coai-vibe/</c> and <c>&lt;homes root&gt;/coai-kimi/</c> — a conversation's home and its snapshot, named by
/// the consultation's id — is removed once its conversation is over, unknown to the store, or idle past
/// <c>COAI_CONSULT_IDLE_MINUTES</c>.
/// </summary>
/// <remarks>
/// <para><b>Where it runs.</b> On the consultation sweep's beat (<see cref="ConsultationSweeper"/>, every
/// <c>ServerPace.SweepEvery</c>) and once at server start (the <see cref="PanelService"/> constructor) — AFTER the records,
/// as the retention pass runs: an open record idle past the window is lapsed by the record pass of the same beat, and its
/// home goes here by rule (b).</para>
/// <para><b>The rules</b>, the window being the conversation's own idle budget (<c>PanelSettings.ConsultIdle</c>):
/// (a) no record for the folder's id AND the folder last written longer ago than the window — never a home made a moment
/// ago whose record write is in flight; (b) the record is over — closed or failed; (c) the record is open, not asking, and
/// the folder last written longer ago than the window. Kept: an open record's folder written inside the window, and a
/// record that is <c>asking</c> — a turn in flight, which the record pass settles first.</para>
/// <para><b>A live launch is judged by the lock, never by a stamp</b> (the store's own rule for a running turn): the folder's
/// turn and every home's turn in it are taken with no wait (<see cref="VibeHomeTurns.TryTake"/>), and a folder any of them
/// is held in is SKIPPED this beat — the next beat takes it once the turn is given back.</para>
/// <para><b>Never entered through a link.</b> The deleter is <see cref="GitScratch.DeleteEvenIfReadOnly"/>, which unlinks a
/// junction by its own name and never walks through it — so a Kimi home's <c>credentials</c> junction (D19) goes and the
/// person's folder it points at keeps every file. Kimi's own disposal (the junction removed FIRST and the person's folder
/// verified after) is S4's, in its home's owner; this sweep is the backstop for a home whose owner died.</para>
/// <para><b>Never the reason a sweep fails</b> (<see cref="ConsultationRetention"/>'s rule): a folder that cannot be listed,
/// read or removed is said in the log and skipped, and the next beat tries again.</para>
/// <para><b>The bound, stated.</b> While a server runs, no home outlives its conversation by more than the window plus one
/// beat (15 min + 60 s by default); a host killed mid-conversation leaves its homes until the next start, whose sweep
/// removes them. A machine that never starts the server again keeps them — the bound the record store itself has.</para>
/// </remarks>
public static class ConsultantHomeSweep
{
    /// <summary>One pass over every homes folder; answers how many conversation folders went.</summary>
    /// <param name="delete">The deleter — the shared one; a test's seam for a folder that cannot be removed.</param>
    public static int Sweep(string homesRoot, Func<string, ConsultationRecord?> recordOf, DateTime nowUtc, TimeSpan idle, Action<string> warn, Action<string>? delete = null)
    {
        if (homesRoot.Length == 0)
        {
            return 0;
        }

        var pass = new Pass(recordOf, nowUtc, idle, warn, delete ?? GitScratch.DeleteEvenIfReadOnly);

        return ConsultantHomes.All.Sum(homes => pass.Over(Path.Combine(homesRoot, homes)));
    }

    /// <summary>
    /// The folders of ONE conversation removed now, in every homes folder — what <c>close_consult</c> does as it closes,
    /// rather than leave it to the next beat. A folder whose turn is held is left to the beat.
    /// </summary>
    public static void RemoveNow(string homesRoot, string consultationId, Action<string> warn)
    {
        if (homesRoot.Length == 0 || !ConsultationStore.IsWellFormedId(consultationId))
        {
            return;
        }

        var pass = new Pass(_ => null, DateTime.UtcNow, TimeSpan.Zero, warn, GitScratch.DeleteEvenIfReadOnly);
        foreach (var homes in ConsultantHomes.All)
        {
            pass.Remove(ConsultantHomes.ConversationFolder(homesRoot, homes, consultationId));
        }
    }

    private sealed class Pass(Func<string, ConsultationRecord?> recordOf, DateTime nowUtc, TimeSpan idle, Action<string> warn, Action<string> delete)
    {
        /// <summary>Every conversation folder of one homes folder; how many went.</summary>
        public int Over(string homes)
        {
            try
            {
                return Directory.Exists(homes) ? Directory.EnumerateDirectories(homes).ToList().Count(Settled) : 0;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                warn($"consultant homes: {homes} could not be listed, and the next beat will try again: {e.Message}");
                return 0;
            }
        }

        /// <summary>One folder: removed when its rule says so; kept, or skipped, otherwise — and never an exception.</summary>
        private bool Settled(string folder)
        {
            try
            {
                return IsDue(recordOf(Path.GetFileName(folder)), LastWrite(folder)) && Remove(folder);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                warn($"consultant homes: {folder} could not be read, and the next beat will try again: {e.Message}");
                return false;
            }
        }

        /// <summary>Rules (a), (b) and (c) — a record over, or no live record and nothing written inside the window.</summary>
        private bool IsDue(ConsultationRecord? record, DateTime lastWriteUtc) => record switch
        {
            { IsOver: true } => true,
            { Status: ConsultationStatuses.Asking } => false,
            _ => nowUtc - lastWriteUtc > idle,
        };

        /// <summary>
        /// The folder removed under its turn and every home's turn in it — or skipped when any is held. Its snapshot, if this
        /// process holds it, is let go first, so its bytes go back to the budget.
        /// </summary>
        public bool Remove(string folder)
        {
            if (!Directory.Exists(folder))
            {
                return false;
            }

            List<IDisposable> turns = [];
            try
            {
                if (!AllTaken(folder, turns))
                {
                    return false;
                }

                ConversationSnapshots.Release(folder);
                delete(folder);

                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                warn($"consultant homes: {folder} could not be removed, and the next beat will try again: {e.Message}");
                return false;
            }
            finally
            {
                turns.ForEach(turn => turn.Dispose());
            }
        }

        /// <summary>The folder's turn and each sub-folder's (a home), with no wait — false the moment one is held.</summary>
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

        /// <summary>The newest write of the folder and its direct entries — a home a turn just wrote is young.</summary>
        private static DateTime LastWrite(string folder) =>
            Directory.EnumerateFileSystemEntries(folder)
                .Select(Directory.GetLastWriteTimeUtc)
                .Append(Directory.GetLastWriteTimeUtc(folder))
                .Max();
    }
}
