using System.Diagnostics;
using System.Globalization;

namespace CoaiMcp.Runners.Snapshots;

/// <summary>What copying a plan came to.</summary>
internal abstract record Copying
{
    private Copying()
    {
    }

    public sealed record Done : Copying;

    /// <summary>A file changed under the copy; the whole copy is tried again, once.</summary>
    public sealed record Changed(string Path) : Copying;

    public sealed record Refused(string Reason) : Copying;
}

/// <summary>
/// Copies a plan file by file, and notices a file that changed while it was being copied.
/// </summary>
/// <remarks>
/// <para><b>The change check</b> is the source's size and last-write time before and after the copy, plus the copied
/// length against the size seen before: a consultant's live tree is the person's working tree, edited while the
/// consultation runs, and a snapshot holding half of an edit is a snapshot of nothing. A changed file is a
/// <see cref="Copying.Changed"/>, which the maker answers with one more attempt over a fresh listing, then a refusal —
/// the tree is moving, and a reviewer of a moving tree reviews nothing.</para>
/// <para><b>The time ceiling</b> is checked between files, with the clock the maker started before the listing, and a
/// ceiling reached mid-copy is a refusal: the maker deletes what was copied. A Windows long-path failure
/// (<see cref="PathTooLongException"/>, or the <see cref="DirectoryNotFoundException"/> the runtime raises for a path
/// past the limit) is a refusal naming the file, never a snapshot missing it.</para>
/// </remarks>
internal static class SnapshotCopier
{
    public static Copying CopyAll(CopyPlan plan, SnapshotOptions options, int attempt, Stopwatch clock, CancellationToken ct)
    {
        foreach (var file in plan.Files)
        {
            ct.ThrowIfCancellationRequested();
            if (clock.Elapsed > options.MaxElapsed)
            {
                return new Copying.Refused(TookTooLong(clock.Elapsed, options.MaxElapsed));
            }

            var one = CopyOne(plan.SourceRoot, file, options, attempt);
            if (one is not Copying.Done)
            {
                return one;
            }
        }

        return clock.Elapsed > options.MaxElapsed ? new Copying.Refused(TookTooLong(clock.Elapsed, options.MaxElapsed)) : new Copying.Done();
    }

    public static string TookTooLong(TimeSpan elapsed, TimeSpan ceiling) =>
        $"the copy took {elapsed.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} s, over the ceiling of {ceiling.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} s";

    /// <summary>
    /// One file: stamped, checked for a link on the way AGAIN, copied, stamped again.
    /// </summary>
    /// <remarks>
    /// The planner asked about links when it planned; the copy may be seconds later, and a directory on the way that
    /// became a junction in between would hand <c>File.Copy</c> its target's bytes under the planned name — with the
    /// same length and time, the stamp alone cannot see it (the review of S1). So the walk runs once more, immediately
    /// before the copy. A window remains between this check and the open, as it does for every path-based API; what
    /// is closed is the plan-to-copy gap.
    /// </remarks>
    private static Copying CopyOne(string sourceRoot, PlannedFile file, SnapshotOptions options, int attempt)
    {
        try
        {
            var before = Stamp.Of(file.Source);
            // Between the stamp and the copy: the one moment a test can change the file and the check must see it.
            options.BeforeCopyOf(attempt, file.Relative);
            var linked = SnapshotLinks.FirstLinkOnTheWay(sourceRoot, file.Relative);
            if (linked.Length > 0)
            {
                return new Copying.Refused($"{linked} is a link, or cannot be inspected, since the copy was planned; a snapshot follows none");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(file.Destination)!);
            File.Copy(file.Source, file.Destination);

            return before == Stamp.Of(file.Source) && new FileInfo(file.Destination).Length == before.Length
                ? new Copying.Done()
                : new Copying.Changed(file.Relative);
        }
        catch (FileNotFoundException)
        {
            // Gone between the listing and the copy: the tree moved, which is the retry's case.
            return new Copying.Changed(file.Relative);
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

    /// <summary>What a file looked like at one moment: enough to see it change, cheap enough to ask twice per file.</summary>
    private readonly record struct Stamp(long Length, DateTime WrittenUtc)
    {
        public static Stamp Of(string path)
        {
            var info = new FileInfo(path);

            return new Stamp(info.Length, info.LastWriteTimeUtc);
        }
    }
}
