using CoaiMcp.Runners.Files;

namespace CoaiMcp.Runners.Vibe;

/// <summary>
/// Where a one-shot Vibe launch keeps its home — a round's reviewers, a question row, a consultant check — and how that home
/// is owned, held and swept (D1 and D3 of <c>todo/PLAN_vibe_live_round_fixes.md</c>).
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
    public static OneShotReservation ReserveOneShot(string root, Action<string>? warn = null)
    {
        if (root.Length == 0)
        {
            return OneShotReservation.None.Instance;
        }

        try
        {
            return Reserved(root, warn ?? (static _ => { }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new OneShotReservation.Refused($"the Vibe homes folder {root} could not be used for a Vibe home ({e.Message})");
        }
    }

    private static OneShotReservation Reserved(string root, Action<string> warn)
    {
        // The folder under the root is entered through the one type that makes it private or refuses it (D7).
        var opened = PrivateFolder.Open(root, OneShotFolder, warn);
        if (opened is PrivateFolderOutcome.Refused refused)
        {
            return new OneShotReservation.Refused(refused.Sentence);
        }

        var pin = ((PrivateFolderOutcome.Opened)opened).Held;
        var folder = OneShotIn(root, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
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
    public static int SweepOneShot(string root, DateTime cutoffUtc, Action<string> warn)
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
            return Listed(vh, warn).Count(entry => SweptOne(entry, cutoffUtc, warn));
        }
    }

    private static List<string> Listed(string vh, Action<string> warn)
    {
        try
        {
            return [.. Directory.EnumerateFileSystemEntries(vh)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warn($"one-shot Vibe homes: {vh} could not be listed, and the next sweep will try again: {e.Message}");

            return [];
        }
    }

    private static bool SweptOne(string entry, DateTime cutoffUtc, Action<string> warn)
    {
        try
        {
            if (OwnedPaths.IsLinkIfThere(entry))
            {
                warn($"one-shot Vibe homes: {entry} is a link, and it is left alone");

                return false;
            }

            return Directory.Exists(entry) && RemovedUnderItsLock(entry, cutoffUtc);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warn($"one-shot Vibe homes: {entry} could not be removed, and the next sweep will try again: {e.Message}");

            return false;
        }
    }

    /// <summary>Old, and its owner gone (the lock is free): every entry but the held lock file removed, then the lock, then the folder.</summary>
    private static bool RemovedUnderItsLock(string folder, DateTime cutoffUtc)
    {
        if (NewestWrite(folder) >= cutoffUtc || HeldFile.Take(Path.Combine(folder, LockName)) is not Hold.Taken locked)
        {
            return false;
        }

        using (locked.Stream)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder).Where(entry => !IsTheLock(entry)))
            {
                GitScratch.DeleteEvenIfReadOnly(entry);
            }
        }

        GitScratch.DeleteEvenIfReadOnly(folder);

        return true;
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
public sealed class OneShotHome(string folder, IDisposable held, Action<string> warn) : IDisposable
{
    private int _disposed;

    /// <summary>The folder: the Vibe home lives inside it, and so does what must stay outside the home (a launch's export).</summary>
    public string Folder => folder;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        held.Dispose();
        try
        {
            if (Directory.Exists(folder))
            {
                GitScratch.DeleteEvenIfReadOnly(folder);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warn($"the Vibe home folder {folder} could not be deleted ({e.Message}); the scratch sweep takes it");
        }
    }
}
