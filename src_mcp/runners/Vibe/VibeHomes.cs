using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;

namespace CoaiMcp.Runners.Vibe;

/// <summary>
/// Where a one-shot Vibe launch keeps its home — a round's reviewers, a question row, a consultant check — and how that home
/// is owned, held and swept (D1 and D3 of <c>research/PLAN_vibe_live_round_fixes.md</c>).
/// </summary>
/// <remarks>
/// <para><b>One short root, never TEMP</b> (D1): <c>&lt;root&gt;\coai-vh\&lt;8 hex&gt;</c>, the root being the consultant homes
/// root (<c>COAI_CONSULTANT_HOMES</c>, else the local application data). A deep home broke on a path over Windows' limit
/// (<see cref="VibePathBudget"/>), and TEMP is as long as the profile name and the person's own setting make it.
/// <c>coai-vh</c> is a sibling of <c>coai-vibe</c> and OUTSIDE <c>ConsultantHomes.All</c>, so the conversation-home sweep, which
/// walks only those, never judges a round's home as a foreign or idle conversation.</para>
/// <para><b>Owned for its life:</b> the owner (<see cref="OneShotHome"/>) makes the folder, holds <see cref="LockName"/> in it, and
/// deletes the folder when it ends, whatever ended it. Two servers share one root per user; <c>VibeHomeTurns</c> is in-process
/// and cannot see another server's round, so the LOCK protects a live home across processes
/// (<see cref="SweepOneShot"/>), and the age rule is the second guard for the instant between the folder's creation and its lock.</para>
/// </remarks>
public static class VibeHomes
{
    /// <summary>The folder under the homes root that holds every one-shot home: <c>coai-vh</c>.</summary>
    public const string OneShotFolder = "coai-vh";

    /// <summary>The lock file in each one-shot folder, held by its owner — the same name a conversation folder's lock has.</summary>
    public const string LockName = "coai-turn.lock";

    /// <summary><c>&lt;root&gt;\coai-vh\&lt;id&gt;</c> — pure.</summary>
    public static string OneShotIn(string root, string id) => PrivateFolder.PathOf(PrivateFolder.PathOf(root, OneShotFolder), id);

    /// <summary>
    /// A new one-shot folder under <paramref name="root"/>, made, owned and locked — <see cref="OneShotReservation.None"/> for an
    /// empty root (settings built by hand: every site then keeps its home in its own scratch, so no test writes the machine's
    /// folder), or a refusal with the sentence that says why.
    /// </summary>
    public static OneShotReservation ReserveOneShot(string root, Action<string>? warn = null) =>
        ReserveOneShot(root, warn ?? (static _ => { }), OneShotSteps.Real);

    /// <summary><see cref="ReserveOneShot(string, Action{string}?)"/> with its two steps handed in — a test's seam for a folder that cannot be made.</summary>
    internal static OneShotReservation ReserveOneShot(string root, Action<string> warn, OneShotSteps steps)
    {
        if (root.Length == 0)
        {
            return OneShotReservation.None.Instance;
        }

        try
        {
            return Reserved(root, warn, steps);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new OneShotReservation.Refused($"the Vibe homes folder {root} could not be used for a Vibe home ({e.Message})");
        }
    }

    private static OneShotReservation Reserved(string root, Action<string> warn, OneShotSteps steps)
    {
        // The folder under the root is entered through the one type that makes it private or refuses it (D7).
        var opened = steps.Enter(root, OneShotFolder, warn);
        if (opened is PrivateFolderOutcome.Refused refused)
        {
            return new OneShotReservation.Refused(refused.Sentence);
        }

        var pin = ((PrivateFolderOutcome.Opened)opened).Held;

        // The pin is given back on EVERY way out that does not move it into the home: a refused lock and a throw while the folder
        // is made or locked alike (the caller of a refusal never held it, so nobody else can let it go).
        try
        {
            return Locked(pin, OneShotIn(root, Guid.NewGuid().ToString("N")[..8]), warn, steps);
        }
        catch
        {
            pin.Dispose();
            throw;
        }
    }

    /// <summary>The home folder, made and locked, owning the pin from here on — or the refusal, with the pin already given back.</summary>
    private static OneShotReservation Locked(IDisposable pin, string folder, Action<string> warn, OneShotSteps steps)
    {
        steps.Make(folder);
        if (HeldFile.Take(Path.Combine(folder, LockName)) is not Hold.Taken taken)
        {
            pin.Dispose();

            return new OneShotReservation.Refused($"{folder} could not be locked for its owner, so no Vibe home was made there");
        }

        return new OneShotReservation.Reserved(new OneShotHome(folder, new Pins(taken.Stream, pin), warn));
    }

    /// <summary>The lock and the folder's pinned handle, given back together.</summary>
    private sealed class Pins(params IDisposable[] held) : IDisposable
    {
        public void Dispose()
        {
            foreach (var one in held)
            {
                one.Dispose();
            }
        }
    }

    /// <summary>
    /// The scratch sweep's pass over <c>&lt;root&gt;\coai-vh\*</c> (D3): a folder whose lock this pass cannot take is skipped — its
    /// owner is alive, in this process or ANOTHER server's on the same root — and one whose newest write (the folder, its entries
    /// and theirs) is older than <paramref name="cutoffUtc"/> is removed under the lock, the lock file last. A link is said and
    /// left alone, never entered; <c>coai-vh</c> itself a link, or not a folder, sweeps nothing. How many folders went.
    /// </summary>
    public static int SweepOneShot(string root, DateTime cutoffUtc, Action<string> warn) =>
        SweepOneShot(root, cutoffUtc, warn, HomeIo.Real);

    /// <summary><see cref="SweepOneShot(string, DateTime, Action{string})"/> with the disk handed in — a test's seam for a folder that cannot be listed or removed.</summary>
    internal static int SweepOneShot(string root, DateTime cutoffUtc, Action<string> warn, HomeIo io)
    {
        if (root.Length == 0)
        {
            return 0;
        }

        var vh = PrivateFolder.PathOf(root, OneShotFolder);
        if (!Directory.Exists(vh))
        {
            // A junction whose target is gone is not a directory either, and is still not a thing to leave unsaid.
            if (OwnedPaths.IsLinkIfThere(vh))
            {
                warn($"one-shot Vibe homes: {vh} is a link to nothing, and no folder is swept through one");
            }

            return 0;
        }

        // A folder that is not private (a link, a foreign owner) is said and nothing is swept through it.
        var opened = PrivateFolder.Open(root, OneShotFolder, warn);
        if (opened is PrivateFolderOutcome.Refused refused)
        {
            warn($"one-shot Vibe homes: nothing is swept — {refused.Sentence}");

            return 0;
        }

        using (((PrivateFolderOutcome.Opened)opened).Held)
        {
            return Listed(vh, warn, io).Count(entry => SweptOne(entry, cutoffUtc, warn, io));
        }
    }

    private static List<string> Listed(string vh, Action<string> warn, HomeIo io)
    {
        try
        {
            return [.. io.List(vh)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warn($"one-shot Vibe homes: {vh} could not be listed, and the next sweep will try again: {e.Message}");

            return [];
        }
    }

    private static bool SweptOne(string entry, DateTime cutoffUtc, Action<string> warn, HomeIo io)
    {
        try
        {
            if (OwnedPaths.IsLinkIfThere(entry))
            {
                warn($"one-shot Vibe homes: {entry} is a link, and it is left alone");

                return false;
            }

            return Directory.Exists(entry) && RemovedUnderItsLock(entry, cutoffUtc, warn, io);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warn($"one-shot Vibe homes: {entry} could not be removed, and the next sweep will try again: {e.Message}");

            return false;
        }
    }

    /// <summary>
    /// Old, and its owner gone (the lock is free): emptied under the lock (<see cref="EmptiedUnderItsLock"/>), then the lock given back, then
    /// the lock file and the folder. A Kimi stop keeps the folder this pass, said with the stop's own sentence (E5.3 decision g, judgement R4).
    /// </summary>
    private static bool RemovedUnderItsLock(string folder, DateTime cutoffUtc, Action<string> warn, HomeIo io)
    {
        if (NewestWrite(folder) >= cutoffUtc || HeldFile.Take(Path.Combine(folder, LockName)) is not Hold.Taken locked)
        {
            return false;
        }

        bool emptied;
        using (locked.Stream)
        {
            emptied = EmptiedUnderItsLock(folder, said => warn($"one-shot Vibe homes: {folder} is kept this pass: {said}"), io);
        }

        if (emptied)
        {
            RemovedAfterItsLock(folder, io);
        }

        return emptied;
    }

    /// <summary>
    /// The shared half of a one-shot folder's end, run by its OWNER and by <see cref="SweepOneShot"/> alike while the folder's lock is HELD
    /// (E5.3 decision g, judgement R5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>): first every Kimi home in it is disposed junction-first
    /// (<see cref="KimiHome.DisposeAllUnder"/> — the live-tree probe, the junction unlinked by its own name, the person's folder verified), and a
    /// <see cref="Disposal.Stopped"/> ends it with FALSE and nothing more deleted — the folder and its lock file are kept for the sweep, the stop said
    /// through <paramref name="kept"/> in its own words. Otherwise every entry but the lock file goes, under the lock, so no second deleter (another
    /// server's sweep) can take the lock and meet this one on the same tree. TRUE: the caller gives the lock back and calls <see cref="RemovedAfterItsLock"/>.
    /// </summary>
    internal static bool EmptiedUnderItsLock(string folder, Action<string> kept, HomeIo io)
    {
        var stopped = false;
        KimiHome.DisposeAllUnder(folder, said =>
        {
            stopped = true;
            kept(said);
        });
        if (stopped)
        {
            return false;
        }

        // The shared deleter's order at this level too: a link before its sibling folders (D19), so a lost target stops the end first.
        foreach (var entry in GitScratch.LinksFirst(Directory.EnumerateFileSystemEntries(folder).Where(entry => !IsTheLock(entry))))
        {
            io.Delete(entry);
        }

        return true;
    }

    /// <summary>After the lock was given back: the lock file and the folder, together. A folder that is already gone — a sweep took it in the window — is silent.</summary>
    internal static void RemovedAfterItsLock(string folder, HomeIo io)
    {
        if (Directory.Exists(folder))
        {
            io.Delete(folder);
        }
    }

    private static bool IsTheLock(string entry) => Path.GetFileName(entry).Equals(LockName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The newest write of the folder's entries and their entries — a home a launch just wrote into is young — or of the folder
    /// itself when it holds nothing else. NOT the lock file, and not the folder's own time when it has entries: taking the lock
    /// creates the file, and creating it moves the folder's time, so the sweep would make every folder young by looking.
    /// </summary>
    private static DateTime NewestWrite(string folder)
    {
        var times = Directory.EnumerateFileSystemEntries(folder).Where(entry => !IsTheLock(entry)).SelectMany(Below)
            .Select(Directory.GetLastWriteTimeUtc).ToList();

        return times.Count > 0 ? times.Max() : Directory.GetLastWriteTimeUtc(folder);
    }

    private static IEnumerable<string> Below(string entry) =>
        Directory.Exists(entry) && !OwnedPaths.IsLinkIfThere(entry) ? Directory.EnumerateFileSystemEntries(entry).Prepend(entry) : [entry];
}

/// <summary>The two disk operations a sweep makes that can fail for reasons a test cannot make a folder do: listing a folder and removing an entry.</summary>
/// <param name="List">The entries of a folder.</param>
/// <param name="Delete">Removes an entry, read-only or not.</param>
internal sealed record HomeIo(Func<string, IEnumerable<string>> List, Action<string> Delete)
{
    /// <summary>The real disk: an entry deleted as what it is — a link by its own name, a folder whole, a file as a file.</summary>
    public static HomeIo Real { get; } = new(static folder => Directory.EnumerateFileSystemEntries(folder), GitScratch.DeleteEntry);
}

/// <summary>The two steps of a reservation that touch the disk: entering <c>coai-vh</c> under the root, and making the home folder in it.</summary>
/// <param name="Enter">The root, the folder's name and where a note goes; the private folder, held, or the refusal.</param>
/// <param name="Make">Makes the home folder.</param>
internal sealed record OneShotSteps(Func<string, string, Action<string>, PrivateFolderOutcome> Enter, Action<string> Make)
{
    /// <summary>The real steps: <see cref="PrivateFolder.Open"/> and a created directory.</summary>
    public static OneShotSteps Real { get; } = new((root, name, note) => PrivateFolder.Open(root, name, note), static folder => Directory.CreateDirectory(folder));
}

/// <summary>What reserving a one-shot folder came to — a closed union, so "none asked" and "refused" are never one answer.</summary>
public abstract record OneShotReservation
{
    private OneShotReservation()
    {
    }

    /// <summary>No root is configured: the site keeps its home in its own scratch, as before this reservation existed.</summary>
    public sealed record None : OneShotReservation
    {
        /// <summary>The one value.</summary>
        public static readonly None Instance = new();
    }

    /// <summary>The folder was not made, and the sentence says why and what cures it.</summary>
    public sealed record Refused(string Sentence) : OneShotReservation;

    /// <summary>The folder, made and locked; the owner disposes the home when its work has ended.</summary>
    public sealed record Reserved(OneShotHome Home) : OneShotReservation;
}

/// <summary>
/// One one-shot folder <c>&lt;root&gt;\coai-vh\&lt;8&gt;</c>, owned by whoever reserved it: it holds the folder's lock for its life and
/// deletes the folder, best effort and said when it fails, when it is disposed.
/// </summary>
/// <remarks>
/// <b>The order of the end</b> (E5.3 decision g, judgement R5 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>): (1) UNDER the lock, every Kimi home
/// in the folder disposed junction-first and every other entry deleted (<see cref="VibeHomes.EmptiedUnderItsLock"/>); a Kimi stop — a live tree, a
/// target that lost a name — gives the lock back, KEEPS the folder with its lock file and says why; (2) the lock and the pin given back; (3) the lock
/// file and the folder last. Between (2) and (3) a sweep may take the lock and remove the now-empty folder first; then (3) finds nothing and says nothing.
/// </remarks>
public sealed class OneShotHome(string folder, IDisposable held, Action<string> warn) : IDisposable
{
    /// <summary>The disk the disposal deletes through, entry by entry; a test's seam for an entry that cannot be removed, and for the moment each one goes.</summary>
    internal HomeIo Io { get; init; } = HomeIo.Real;

    private int _disposed;

    /// <summary>The folder: the Vibe home lives inside it, and so does what must stay outside the home (a launch's export).</summary>
    public string Folder => folder;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        var emptied = Emptied();
        held.Dispose();
        if (emptied)
        {
            Removed();
        }
    }

    /// <summary>Step (1), under the lock: true when the folder is empty but for its lock file; false when a Kimi stop or a failure keeps it for the sweep.</summary>
    private bool Emptied()
    {
        try
        {
            return !Directory.Exists(folder) || VibeHomes.EmptiedUnderItsLock(folder, Kept, Io);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warn($"the Vibe home folder {folder} could not be deleted ({e.Message}); the scratch sweep takes it");

            return false;
        }
    }

    private void Kept(string reason) => warn($"the Kimi home in {folder} is kept: {reason}; the scratch sweep takes it once its tree has ended");

    /// <summary>Step (3), after the release: the lock file and the folder.</summary>
    private void Removed()
    {
        try
        {
            VibeHomes.RemovedAfterItsLock(folder, Io);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warn($"the Vibe home folder {folder} could not be deleted ({e.Message}); the scratch sweep takes it");
        }
    }
}
