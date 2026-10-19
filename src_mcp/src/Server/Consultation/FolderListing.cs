namespace CoaiMcp.Server;

/// <summary>What one entry of a folder is, as far as the repository walk cares.</summary>
public enum EntryKind
{
    Folder,
    File,

    /// <summary>A symbolic link, or on Windows a junction too — anything the walk never follows — whose target is not a folder.</summary>
    Link,

    /// <summary>
    /// A link to a FOLDER, or any other folder reparse point (a cloud provider's on-demand folder). The walk never follows it, and —
    /// unlike a link to a file — says so, because the person may have repositories behind it.
    /// </summary>
    FolderLink,
}

/// <summary>One entry of a folder: its own name (never a path), its kind, and whether Windows hides it.</summary>
/// <param name="Hidden">The folder carries the Hidden or System attribute (<c>$RECYCLE.BIN</c>, <c>System Volume Information</c>) — set on Windows only, because on Unix a hidden name is a dot name and the walk already reads that.</param>
public sealed record FolderEntry(string Name, EntryKind Kind, bool Hidden = false);

/// <summary>What listing a folder came to: its entries, or why it could not be listed — a value, never an exception.</summary>
public abstract record FolderListing
{
    private FolderListing()
    {
    }

    public sealed record Listed(IReadOnlyList<FolderEntry> Entries) : FolderListing;

    /// <summary>Access denied, vanished, an I/O error: the sentence the walk puts in its note.</summary>
    public sealed record Unreadable(string Reason) : FolderListing;

    /// <summary>The folder holds more entries than one listing reads (<see cref="RepositoryWalk.MaxEntries"/>): the walk refuses the row and names the folder.</summary>
    public sealed record TooMany : FolderListing;
}

/// <summary>The one place the repository walk touches the disk (<see cref="RepositoryWalk"/> takes it as a seam).</summary>
public static class FolderListings
{
    private const string GitName = ".git";

    /// <summary>
    /// The real listing: <see cref="FileAttributes.ReparsePoint"/> is a <see cref="EntryKind.Link"/> — a <see cref="EntryKind.FolderLink"/>
    /// when it is a directory (a symlink on Linux, a symlink or a junction on Windows), a directory is a <see cref="EntryKind.Folder"/>,
    /// anything else a <see cref="EntryKind.File"/>. At most <see cref="RepositoryWalk.MaxEntries"/> entries are read: the entries stream,
    /// and only what the walk needs is kept (a folder, a link to one, a <c>.git</c> file) — a plain file is counted and let go, so a folder of a
    /// million files costs time up to the bound and no memory.
    /// </summary>
    public static FolderListing Read(string path) => Read(path, RepositoryWalk.MaxEntries);

    /// <summary><see cref="Read(string)"/> with the bound given — a test's seam for a folder bigger than the bound.</summary>
    internal static FolderListing Read(string path, int maxEntries)
    {
        try
        {
            return Streamed(new DirectoryInfo(path).EnumerateFileSystemInfos(), maxEntries);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new FolderListing.Unreadable(e.Message);
        }
    }

    private static FolderListing Streamed(IEnumerable<FileSystemInfo> infos, int maxEntries)
    {
        List<FolderEntry> kept = [];
        var seen = 0;
        foreach (var info in infos)
        {
            if (++seen > maxEntries)
            {
                return new FolderListing.TooMany();
            }

            kept.AddRange(Needed(EntryOf(info)));
        }

        return new FolderListing.Listed(kept);
    }

    private static IEnumerable<FolderEntry> Needed(FolderEntry entry) =>
        IsNeeded(entry) ? [entry] : [];

    /// <summary>What the walk reads from a listing: folders and links to folders (it enters or names them) and a <c>.git</c> file (a candidate).</summary>
    private static bool IsNeeded(FolderEntry entry) =>
        entry.Kind is EntryKind.Folder or EntryKind.FolderLink || (entry.Name == GitName && entry.Kind == EntryKind.File);

    /// <summary>
    /// Only whether <paramref name="folder"/> holds a <c>.git</c> — asked of that one name, never by listing the folder, so a folder of a
    /// million files costs one lookup. The answer has the shape of a listing: the <c>.git</c> entry when it is there, none when it is
    /// not, and why not when the lookup was refused.
    /// </summary>
    public static FolderListing GitEntryOf(string folder)
    {
        try
        {
            return new FolderListing.Listed([new FolderEntry(GitName, KindOf(File.GetAttributes(Path.Combine(folder, GitName))))]);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new FolderListing.Listed([]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new FolderListing.Unreadable(e.Message);
        }
    }

    private static FolderEntry EntryOf(FileSystemInfo info) =>
        new(info.Name, KindOf(info.Attributes), HiddenBySystem(info.Attributes));

    /// <summary>On Windows a Hidden or System attribute (the recycle bin, the volume information of a drive root); never elsewhere — a Unix dot name is read as a name.</summary>
    private static bool HiddenBySystem(FileAttributes attributes) =>
        OperatingSystem.IsWindows() && (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;

    private static EntryKind KindOf(FileAttributes attributes) =>
        attributes.HasFlag(FileAttributes.ReparsePoint) ? (attributes.HasFlag(FileAttributes.Directory) ? EntryKind.FolderLink : EntryKind.Link)
        : attributes.HasFlag(FileAttributes.Directory) ? EntryKind.Folder
        : EntryKind.File;
}
