using System.Diagnostics;
using System.Globalization;

namespace CoaiMcp.Runners.Snapshots;

/// <summary>What copying a plan came to.</summary>
internal abstract record Copying
{
    private Copying()
    {
    }

    /// <summary>Every file copied, with the stamp each one carried at its own copy — what the inventory check compares against.</summary>
    public sealed record Done(IReadOnlyDictionary<string, SnapshotStamp> Stamps) : Copying;

    /// <summary>A path moved under the copy — <c>changed</c>, <c>was added</c> or <c>was removed</c>; the whole copy is tried again, once.</summary>
    public sealed record Changed(string Path, string How) : Copying;

    public sealed record Refused(string Reason) : Copying;
}

/// <summary>
/// Copies a plan file by file, notices a file that changed while it was being copied, and — through
/// <see cref="Settled"/> — notices the tree that moved anywhere else while the copy ran.
/// </summary>
/// <remarks>
/// <para><b>The per-file check</b> is the source's size and last-write time before and after the copy, plus the copied
/// length against the size seen before: a consultant's live tree is the person's working tree, edited while the
/// consultation runs, and a snapshot holding half of an edit is a snapshot of nothing.</para>
/// <para><b>The inventory check</b> (D6 as amended by the plan gate, codex, 2026-10-12) is what per-file stamps cannot
/// do: a file copied early stays unchanged while a later one changes before its own copy, or a file is added or removed
/// while the copy runs, and every per-file check passes on a snapshot that is a MIX of times. So after the last file the
/// maker lists and plans the tree again and hands the fresh plan to <see cref="Settled"/>, which compares the whole
/// inventory — every copied file's stamp at its copy against its stamp now, every path that appeared or went, the
/// excluded and the unavailable included. Either kind of movement is a <see cref="Copying.Changed"/>, which the maker
/// answers with one more attempt over a fresh listing, then a refusal — the tree is moving, and a reviewer of a moving
/// tree reviews nothing.</para>
/// <para><b>The time ceiling</b> is checked between files, with the clock the maker started before the listing, and a
/// ceiling reached mid-copy is a refusal: the maker deletes what was copied. A Windows long-path failure
/// (<see cref="PathTooLongException"/>, or the <see cref="DirectoryNotFoundException"/> the runtime raises for a path
/// past the limit) is a refusal naming the file, never a snapshot missing it.</para>
/// </remarks>
internal static class SnapshotCopier
{
    public static Copying CopyAll(CopyPlan plan, SnapshotOptions options, int attempt, Stopwatch clock, CancellationToken ct)
    {
        var stamps = new Dictionary<string, SnapshotStamp>(StringComparer.Ordinal);
        foreach (var file in plan.Files)
        {
            ct.ThrowIfCancellationRequested();
            if (clock.Elapsed > options.MaxElapsed)
            {
                return new Copying.Refused(TookTooLong(clock.Elapsed, options.MaxElapsed));
            }

            var one = CopyOne(plan.SourceRoot, file, options, attempt, stamps);
            if (one is not Copying.Done)
            {
                return one;
            }
        }

        return Finished(stamps, options, clock);
    }

    /// <summary>The copy as it ended: refused when the last file took it past the time ceiling, else done.</summary>
    private static Copying Finished(Dictionary<string, SnapshotStamp> stamps, SnapshotOptions options, Stopwatch clock) =>
        clock.Elapsed > options.MaxElapsed ? new Copying.Refused(TookTooLong(clock.Elapsed, options.MaxElapsed)) : new Copying.Done(stamps);

    /// <summary>
    /// The whole inventory after the last file against the copy: <see cref="Copying.Done"/> when nothing moved since the
    /// copy began, else the first path that did and how.
    /// </summary>
    /// <param name="copied">The plan that was copied.</param>
    /// <param name="stamps">What each copied file looked like at its own copy.</param>
    /// <param name="fresh">The tree listed and planned again, after the last file.</param>
    public static Copying Settled(CopyPlan copied, IReadOnlyDictionary<string, SnapshotStamp> stamps, CopyPlan fresh)
    {
        var files = CopiedFilesSettled(copied, stamps, fresh);

        return files is Copying.Done ? InventorySettled(copied, stamps, fresh) : files;
    }

    /// <summary>The first pass: every copied file still there, at the stamp it had at its own copy.</summary>
    private static Copying CopiedFilesSettled(CopyPlan copied, IReadOnlyDictionary<string, SnapshotStamp> stamps, CopyPlan fresh)
    {
        var now = fresh.Files.ToDictionary(static file => file.Relative, static file => file.Stamp, StringComparer.Ordinal);
        foreach (var file in copied.Files)
        {
            if (!now.TryGetValue(file.Relative, out var stamp))
            {
                return new Copying.Changed(file.Relative, "was removed");
            }

            if (stamp != stamps[file.Relative])
            {
                return new Copying.Changed(file.Relative, "changed");
            }
        }

        return new Copying.Done(stamps);
    }

    /// <summary>The second pass: no file added since the copy, and the excluded and the unavailable as they were.</summary>
    private static Copying InventorySettled(CopyPlan copied, IReadOnlyDictionary<string, SnapshotStamp> stamps, CopyPlan fresh)
    {
        var added = Added(stamps, fresh);
        if (added.Length > 0)
        {
            return new Copying.Changed(added, "was added");
        }

        var elsewhere = MovedElsewhere(copied, fresh);

        return elsewhere.Length > 0 ? new Copying.Changed(elsewhere, "appeared or went") : new Copying.Done(stamps);
    }

    /// <summary>The first file the fresh plan has and the copy did not, or empty.</summary>
    private static string Added(IReadOnlyDictionary<string, SnapshotStamp> stamps, CopyPlan fresh) =>
        fresh.Files.Select(static file => file.Relative).FirstOrDefault(relative => !stamps.ContainsKey(relative)) ?? string.Empty;

    /// <summary>The first excluded path, else the first unavailable one, that appeared or went — or empty.</summary>
    private static string MovedElsewhere(CopyPlan copied, CopyPlan fresh)
    {
        var elsewhere = Moved(copied.Excluded, fresh.Excluded);
        if (elsewhere.Length == 0)
        {
            elsewhere = Moved(copied.Unavailable.Select(static u => u.Path), fresh.Unavailable.Select(static u => u.Path));
        }

        return elsewhere;
    }

    /// <summary>The first path in one list and not the other, or empty — the excluded and the unavailable are inventory too.</summary>
    private static string Moved(IEnumerable<string> before, IEnumerable<string> after)
    {
        var was = before.ToHashSet(StringComparer.Ordinal);
        var @is = after.ToHashSet(StringComparer.Ordinal);

        return was.FirstOrDefault(path => !@is.Contains(path)) ?? @is.FirstOrDefault(path => !was.Contains(path)) ?? string.Empty;
    }

    public static string TookTooLong(TimeSpan elapsed, TimeSpan ceiling) =>
        $"the copy took {elapsed.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} s, over the ceiling of {ceiling.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} s";

    /// <summary>
    /// One file: stamped, checked for a link on the way AGAIN, copied, stamped again — the first stamp kept for the inventory check.
    /// </summary>
    /// <remarks>
    /// <para>The planner asked about links when it planned; the copy may be seconds later, and a directory on the way
    /// that became a junction in between would hand the copy its target's bytes under the planned name — with the
    /// same length and time, the stamp alone cannot see it (the review of S1). So the walk runs once more, immediately
    /// before the copy. A window remains between this check and the open, as it does for every path-based API; what
    /// is closed is the plan-to-copy gap.</para>
    /// <para><b>The stamp before the copy must be the PLANNED stamp</b> (finding A of the risk consultations): a
    /// file that grew between the plan and its own copy was copied whole while the reservation and the manifest
    /// carried the size the plan saw, so the budget understated. A difference is the retry's case, like any change.</para>
    /// <para><b>Only the unnamed stream is copied</b> (finding B): <c>File.Copy</c> is <c>CopyFileEx</c>, which copies
    /// every alternate data stream of an NTFS file — a one-byte file with a large <c>:payload</c> stream passed the
    /// ceilings, which count the unnamed stream, and landed whole in the snapshot. The bytes go through a stream
    /// opened on the source and a new destination file; the last-write time is carried over so the tree reads as the
    /// source does.</para>
    /// </remarks>
    private static Copying CopyOne(string sourceRoot, PlannedFile file, SnapshotOptions options, int attempt, Dictionary<string, SnapshotStamp> stamps)
    {
        try
        {
            var before = SnapshotStamp.Of(file.Source);
            var unmoved = Unmoved(sourceRoot, file, before, options, attempt, stamps);

            return unmoved is Copying.Done ? CopiedUnchanged(file, before, stamps) : unmoved;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Failed(file.Relative, e);
        }
    }

    /// <summary>
    /// The pre-check: the file still carries its PLANNED stamp, and — walked again just before the copy — nothing on
    /// its way is a link. <see cref="Copying.Done"/> (the stamps as they are) when the copy may go ahead.
    /// </summary>
    private static Copying Unmoved(
        string sourceRoot, PlannedFile file, SnapshotStamp before, SnapshotOptions options, int attempt, Dictionary<string, SnapshotStamp> stamps)
    {
        if (before != file.Stamp)
        {
            return new Copying.Changed(file.Relative, "changed");
        }

        // Between the stamp and the copy: the one moment a test can change the file and the check must see it.
        options.BeforeCopyOf(attempt, file.Relative);
        var linked = SnapshotLinks.FirstLinkOnTheWay(sourceRoot, file.Relative);
        if (linked.Length > 0)
        {
            return new Copying.Refused($"{linked} is a link, or cannot be inspected, since the copy was planned; a snapshot follows none");
        }

        return new Copying.Done(stamps);
    }

    /// <summary>The copy, then the post-copy comparison: the source's stamp unchanged and every byte it had written — the stamp kept for the inventory check.</summary>
    private static Copying CopiedUnchanged(PlannedFile file, SnapshotStamp before, Dictionary<string, SnapshotStamp> stamps)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file.Destination)!);
        var copied = CopyUnnamedStream(file.Source, file.Destination, before.WrittenUtc);
        if (before != SnapshotStamp.Of(file.Source) || copied != before.Length)
        {
            return new Copying.Changed(file.Relative, "changed");
        }

        stamps[file.Relative] = before;

        return new Copying.Done(stamps);
    }

    /// <summary>
    /// What a copy that threw came to. The named types are all <see cref="IOException"/>s, so the arms' order is the
    /// decision: the specific ones first, then any other I/O or access failure.
    /// </summary>
    private static Copying Failed(string relative, Exception e) => e switch
    {
        // Gone between the listing and the copy: the tree moved, which is the retry's case.
        FileNotFoundException => new Copying.Changed(relative, "was removed"),
        PathTooLongException or DirectoryNotFoundException =>
            new Copying.Refused($"{relative} could not be copied: the path is too long for this filesystem, or its directory went away ({e.Message})"),
        _ => new Copying.Refused($"{relative} could not be copied ({e.Message})"),
    };

    /// <summary>The source's unnamed stream into a NEW destination file, its last-write time carried over; the bytes written.</summary>
    private static long CopyUnnamedStream(string source, string destination, DateTime writtenUtc)
    {
        // ReadWrite | Delete: a file another process holds open for writing (a log, a database, an editor) is still
        // copied — FileShare.Read refused the whole live snapshot on Windows while that handle stayed open (CodeRabbit
        // on #785). A change during the copy is what the stamps before and after it, and the inventory check, catch.
        using (var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var to = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            from.CopyTo(to);
        }

        File.SetLastWriteTimeUtc(destination, writtenUtc);

        return new FileInfo(destination).Length;
    }
}
