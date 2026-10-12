using System.Globalization;

namespace CoaiMcp.Runners.Snapshots;

/// <summary>What a file looked like at one moment: its length and last write — enough to see it change, cheap enough to take per file, twice.</summary>
internal readonly record struct SnapshotStamp(long Length, DateTime WrittenUtc)
{
    public static SnapshotStamp Of(FileInfo info) => new(info.Length, info.LastWriteTimeUtc);

    public static SnapshotStamp Of(string path) => Of(new FileInfo(path));
}

/// <summary>One file to copy: where it is, where it goes, what it looked like when planned.</summary>
internal sealed record PlannedFile(string Relative, string Source, string Destination, SnapshotStamp Stamp)
{
    public long Bytes => Stamp.Length;
}

/// <summary>Everything the copy will do, decided before a byte moves.</summary>
/// <param name="SourceRoot">Where the files come from — what the copier walks again, just before each copy, for a link that appeared since.</param>
internal sealed record CopyPlan(
    string SourceRoot,
    IReadOnlyList<PlannedFile> Files,
    IReadOnlyList<string> Excluded,
    IReadOnlyList<SnapshotUnavailable> Unavailable,
    long Bytes);

/// <summary>What planning came to.</summary>
internal abstract record Planning
{
    private Planning()
    {
    }

    public sealed record Planned(CopyPlan Plan) : Planning;

    public sealed record Refused(string Reason) : Planning;
}

/// <summary>
/// Turns what git listed into what will be copied — and refuses, before anything is copied, everything D6 refuses.
/// </summary>
/// <remarks>
/// <para>In this order, because each check is cheaper than the next and a refusal should cost as little as it can: the
/// paths that could leave the root or that Windows would open under ANOTHER name (string matching — before the
/// exclusions, so a name that would fold into an excluded one is refused rather than quietly excluded), the exclusions,
/// the links git itself declared, the collisions under the destination's case rule (a set), the file-count ceiling (a
/// count), and only then one <c>stat</c> per file — for its size, for a reparse point on it or on any directory between
/// it and the root, and for the LFS pointer header — and the byte ceiling over the sum.</para>
/// <para><b>A directory on the way is checked too.</b> git on Windows lists the files under a junction as ordinary
/// files, so a junction planted in the tree would arrive as its target's content under a name inside the snapshot. Each
/// directory is asked once. A link git lists NOTHING under — a junction to an empty directory — is never copied through
/// and is therefore not refused either: what is refused is a byte that would come from outside, not a name.</para>
/// </remarks>
internal static class SnapshotPlanner
{
    /// <summary>The git-lfs specification caps a pointer file at 1024 bytes; nothing larger is read for the header.</summary>
    private const int LfsPointerUpTo = 1024;

    private static readonly byte[] LfsHeader = "version https://git-lfs.github.com/spec/v1"u8.ToArray();

    public static Planning Plan(
        string sourceRoot, string destinationRoot, IReadOnlyList<SnapshotEntry> entries, IReadOnlyList<SnapshotUnavailable> unavailable,
        SnapshotOptions options, bool live, StringComparison rule)
    {
        var sorted = Sort(entries, options.Exclusions, rule);
        if (sorted.Refusal.Length > 0)
        {
            return new Planning.Refused(sorted.Refusal);
        }

        if (sorted.Kept.Count > options.MaxFiles)
        {
            return new Planning.Refused($"{N(sorted.Kept.Count)} files in the tree, over the ceiling of {N(options.MaxFiles)}");
        }

        return Measured(sourceRoot, destinationRoot, sorted, unavailable, options, live);
    }

    private sealed record Sorted(IReadOnlyList<string> Kept, IReadOnlyList<string> Excluded, string Refusal);

    private static Sorted Sort(IReadOnlyList<SnapshotEntry> entries, SnapshotExclusions exclusions, StringComparison rule)
    {
        var kept = new List<string>();
        var excluded = new List<string>();
        var seen = new Dictionary<string, string>(rule == StringComparison.Ordinal ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var refusal = SortOne(entry, exclusions, rule, kept, excluded, seen);
            if (refusal.Length > 0)
            {
                return new Sorted(kept, excluded, refusal);
            }
        }

        return new Sorted(kept, excluded, string.Empty);
    }

    private static string SortOne(
        SnapshotEntry entry, SnapshotExclusions exclusions, StringComparison rule, List<string> kept, List<string> excluded, Dictionary<string, string> seen)
    {
        var unplaceable = SnapshotPaths.Unsafe(entry.Path);
        if (unplaceable.Length > 0)
        {
            return unplaceable;
        }

        if (exclusions.Matching(entry.Path, rule).Length > 0)
        {
            excluded.Add(entry.Path);
            return string.Empty;
        }

        if (entry.Kind == EntryKind.Symlink)
        {
            return $"{entry.Path} is a symbolic link in the tree; a snapshot copies no link and follows none";
        }

        if (!seen.TryAdd(entry.Path, entry.Path))
        {
            return $"{seen[entry.Path]} and {entry.Path} are one name on the destination filesystem; a snapshot cannot hold both";
        }

        kept.Add(entry.Path);

        return string.Empty;
    }

    /// <summary>One <c>stat</c> per kept file: its size, that it is no link, that nothing on the way is, and that it is not an LFS pointer.</summary>
    private static Planning Measured(
        string sourceRoot, string destinationRoot, Sorted sorted, IReadOnlyList<SnapshotUnavailable> listed, SnapshotOptions options, bool live)
    {
        var files = new List<PlannedFile>();
        var unavailable = new List<SnapshotUnavailable>(listed);
        var directories = new Directories(sourceRoot);
        foreach (var relative in sorted.Kept)
        {
            var refusal = MeasureOne(sourceRoot, destinationRoot, relative, live, directories, files, unavailable);
            if (refusal.Length > 0)
            {
                return new Planning.Refused(refusal);
            }
        }

        var bytes = files.Sum(file => file.Bytes);

        return bytes > options.MaxBytes
            ? new Planning.Refused($"{N(bytes)} bytes to copy, over the ceiling of {N(options.MaxBytes)}")
            : new Planning.Planned(new CopyPlan(sourceRoot, files, sorted.Excluded, unavailable, bytes));
    }

    private static string MeasureOne(
        string sourceRoot, string destinationRoot, string relative, bool live, Directories directories, List<PlannedFile> files, List<SnapshotUnavailable> unavailable)
    {
        var source = Path.Combine(sourceRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        var info = new FileInfo(source);
        // A link is judged BEFORE existence. On Unix git lists a symlink to a directory as ONE entry, and to FileInfo
        // that entry does not "exist" (its target is a directory) — so the old order, existence first, skipped it as a
        // file deleted from a live tree, and the link was never judged: the snapshot was MADE where Windows refused (CI on
        // Linux and macOS, 2026-10-12). What the entry IS — its link target, its reparse bit — does not depend on what it
        // reaches, or on whether that exists; a dangling link is a link too.
        var onTheWay = directories.LinkOnTheWayTo(relative);
        if (onTheWay.Length > 0 || IsLink(info))
        {
            return $"{(onTheWay.Length > 0 ? onTheWay : relative)} is a link (a symbolic link, junction or other reparse point); a snapshot follows none";
        }

        if (!info.Exists)
        {
            // A live tree's file deleted since the listing is simply absent; a pinned tree's is a broken tree.
            return live ? string.Empty : $"{relative} is listed in the pinned tree but is not there";
        }

        if (info.Length <= LfsPointerUpTo && IsLfsPointer(source))
        {
            unavailable.Add(new SnapshotUnavailable(relative, "a git-LFS pointer: the content was not fetched, so it is not reviewed source"));
            return string.Empty;
        }

        files.Add(new PlannedFile(relative, source, Path.Combine(destinationRoot, relative.Replace('/', Path.DirectorySeparatorChar)), SnapshotStamp.Of(info)));

        return string.Empty;
    }

    /// <summary>
    /// Whether the entry ITSELF is a link, by lstat-level facts: its link target (a symlink on Unix, a symlink or
    /// junction on Windows, dangling or not), else its reparse bit. An entry that is not there at all is not a link —
    /// its attributes read as -1, every bit set, which the flag test alone would take for a reparse point. Anything that
    /// cannot be inspected counts as a link.
    /// </summary>
    private static bool IsLink(FileSystemInfo info)
    {
        try
        {
            if (info.LinkTarget is not null)
            {
                return true;
            }

            var attributes = info.Attributes;

            return attributes != (FileAttributes)(-1) && attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool IsLfsPointer(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var head = new byte[LfsHeader.Length];

            return stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) == head.Length && head.AsSpan().SequenceEqual(LfsHeader);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Unreadable now: the copy will say so, with the path, if it still is.
            return false;
        }
    }

    private static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>The directories between the root and each file, each asked ONCE whether it is a reparse point.</summary>
    private sealed class Directories(string root)
    {
        private readonly Dictionary<string, bool> _isLink = new(StringComparer.Ordinal);

        /// <summary>The first linked directory on the way to <paramref name="relative"/>, or empty.</summary>
        public string LinkOnTheWayTo(string relative)
        {
            var parts = relative.Split('/');
            var at = string.Empty;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                at = at.Length == 0 ? parts[i] : at + "/" + parts[i];
                if (IsLinked(at))
                {
                    return at;
                }
            }

            return string.Empty;
        }

        private bool IsLinked(string relativeDirectory)
        {
            if (!_isLink.TryGetValue(relativeDirectory, out var linked))
            {
                linked = IsLink(new DirectoryInfo(Path.Combine(root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar))));
                _isLink[relativeDirectory] = linked;
            }

            return linked;
        }
    }
}
