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

        return clock.Elapsed > options.MaxElapsed ? new Copying.Refused(TookTooLong(clock.Elapsed, options.MaxElapsed)) : new Copying.Done(stamps);
    }

    /// <summary>
    /// The whole inventory after the last file against the copy: <see cref="Copying.Done"/> when nothing moved since the
    /// copy began, else the first path that did and how.
    /// </summary>
    /// <param name="copied">The plan that was copied.</param>
    /// <param name="stamps">What each copied file looked like at its own copy.</param>
    /// <param name="fresh">The tree listed and planned again, after the last file.</param>
    public static Copying Settled(CopyPlan copied, IReadOnlyDictionary<string, SnapshotStamp> stamps, CopyPlan fresh)
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

        var added = fresh.Files.Select(static file => file.Relative).FirstOrDefault(relative => !stamps.ContainsKey(relative)) ?? string.Empty;
        if (added.Length > 0)
        {
            return new Copying.Changed(added, "was added");
        }

        var elsewhere = Moved(copied.Excluded, fresh.Excluded);
        if (elsewhere.Length == 0)
        {
            elsewhere = Moved(copied.Unavailable.Select(static u => u.Path), fresh.Unavailable.Select(static u => u.Path));
        }

        return elsewhere.Length > 0 ? new Copying.Changed(elsewhere, "appeared or went") : new Copying.Done(stamps);
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
    /// The planner asked about links when it planned; the copy may be seconds later, and a directory on the way that
    /// became a junction in between would hand <c>File.Copy</c> its target's bytes under the planned name — with the
    /// same length and time, the stamp alone cannot see it (the review of S1). So the walk runs once more, immediately
    /// before the copy. A window remains between this check and the open, as it does for every path-based API; what
    /// is closed is the plan-to-copy gap.
    /// </remarks>
    private static Copying CopyOne(string sourceRoot, PlannedFile file, SnapshotOptions options, int attempt, Dictionary<string, SnapshotStamp> stamps)
    {
        try
        {
            var before = SnapshotStamp.Of(file.Source);
            // Between the stamp and the copy: the one moment a test can change the file and the check must see it.
            options.BeforeCopyOf(attempt, file.Relative);
            var linked = SnapshotLinks.FirstLinkOnTheWay(sourceRoot, file.Relative);
            if (linked.Length > 0)
            {
                return new Copying.Refused($"{linked} is a link, or cannot be inspected, since the copy was planned; a snapshot follows none");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(file.Destination)!);
            File.Copy(file.Source, file.Destination);
            if (before != SnapshotStamp.Of(file.Source) || new FileInfo(file.Destination).Length != before.Length)
            {
                return new Copying.Changed(file.Relative, "changed");
            }

            stamps[file.Relative] = before;

            return new Copying.Done(stamps);
        }
        catch (FileNotFoundException)
        {
            // Gone between the listing and the copy: the tree moved, which is the retry's case.
            return new Copying.Changed(file.Relative, "was removed");
        }
        catch (Exception e) when (e is PathTooLongException or DirectoryNotFoundException)
        {
            return new Copying.Refused($"{file.Relative} could not be copied: the path is too long for this filesystem, or its directory went away ({e.Message})");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Copying.Refused($"{file.Relative} could not be copied ({e.Message})");
        }
    }
}
