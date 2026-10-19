namespace CoaiMcp.Runners.Files;

/// <summary>
/// A folder whose owner HOLDS a lock file inside it for its whole life (<see cref="HeldFile"/>), and the one way a sweep removes it:
/// only when the sweep can take that lock itself — the keep-test of the one-shot Vibe homes (<c>VibeHomes.SweepOneShot</c>, D3 of
/// <c>research/PLAN_vibe_live_round_fixes.md</c>) and of a question row's scratch (D8 of <c>todo/PLAN_vibe_reads_a_folder_of_repositories.md</c>).
/// </summary>
/// <remarks>
/// <para><b>Age alone is never the test.</b> A deep write does not move a folder's own timestamp, and a row's budget has no upper bound, so
/// a folder older than any window can still belong to a live owner. The lock says so, across processes — the kernel releases it when
/// its holder dies, which is exactly the folder a killed server stranded. The caller asks its own age question FIRST and this second:
/// taking the lock creates the lock file, which moves the folder's own time.</para>
/// <para>Extracted from <c>VibeHomes</c>, not written a second time (reuse-first, step 2.2): the sweep of a question row's scratch is
/// the same "take the lock, empty the folder around it, let go, remove the folder" and a copy of it would drift.</para>
/// </remarks>
public static class FolderLock
{
    /// <summary><see cref="RemoveIfFree(string, string, Action{string}, Func{bool})"/> with nothing to do first: the folder goes whenever its lock can be taken.</summary>
    public static bool RemoveIfFree(string folder, string lockName, Action<string> delete) =>
        RemoveIfFree(folder, lockName, delete, static () => true);

    /// <summary>
    /// Removes <paramref name="folder"/> when its lock <paramref name="lockName"/> can be taken — every entry but the lock first, then
    /// (the lock let go) the folder itself, lock file included. False, with nothing touched, when somebody holds the lock, when it cannot
    /// be opened, or when <paramref name="ready"/> (asked under the lock) says the folder must stay this time.
    /// </summary>
    /// <param name="delete">Removes one entry, or the folder itself; it must take a file as well as a folder (<see cref="GitScratch.DeleteEntry"/>).</param>
    /// <param name="ready">Work that must precede the removal and needs no live owner — asked only under the lock; false keeps the folder this time.</param>
    public static bool RemoveIfFree(string folder, string lockName, Action<string> delete, Func<bool> ready)
    {
        if (HeldFile.Take(Path.Combine(folder, lockName)) is not Hold.Taken locked)
        {
            return false;
        }

        using (locked.Stream)
        {
            if (!ready())
            {
                return false;
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(folder).Where(entry => !IsTheLock(entry, lockName)))
            {
                delete(entry);
            }
        }

        delete(folder);

        return true;
    }

    /// <summary>Whether <paramref name="entry"/> is the lock file — the one entry a sweep must not delete while it holds it.</summary>
    public static bool IsTheLock(string entry, string lockName) => Path.GetFileName(entry).Equals(lockName, StringComparison.OrdinalIgnoreCase);
}
