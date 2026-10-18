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

/// <summary>One entry of a folder: its own name (never a path) and its kind.</summary>
public sealed record FolderEntry(string Name, EntryKind Kind);

/// <summary>What listing a folder came to: its entries, or why it could not be listed — a value, never an exception.</summary>
public abstract record FolderListing
{
    private FolderListing()
    {
    }

    public sealed record Listed(IReadOnlyList<FolderEntry> Entries) : FolderListing;

    /// <summary>Access denied, vanished, an I/O error: the sentence the walk puts in its note.</summary>
    public sealed record Unreadable(string Reason) : FolderListing;
}

/// <summary>The one place the repository walk touches the disk (<see cref="RepositoryWalk"/> takes it as a seam).</summary>
public static class FolderListings
{
    private const string GitName = ".git";

    /// <summary>
    /// The real listing: <see cref="FileAttributes.ReparsePoint"/> is a <see cref="EntryKind.Link"/> — a <see cref="EntryKind.FolderLink"/>
    /// when it is a directory (a symlink on Linux, a symlink or a junction on Windows), a directory is a <see cref="EntryKind.Folder"/>,
    /// anything else a <see cref="EntryKind.File"/>.
    /// </summary>
    public static FolderListing Read(string path)
    {
        try
        {
            return new FolderListing.Listed([.. new DirectoryInfo(path).EnumerateFileSystemInfos().Select(EntryOf)]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new FolderListing.Unreadable(e.Message);
        }
    }

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
        new(info.Name, KindOf(info.Attributes));

    private static EntryKind KindOf(FileAttributes attributes) =>
        attributes.HasFlag(FileAttributes.ReparsePoint) ? (attributes.HasFlag(FileAttributes.Directory) ? EntryKind.FolderLink : EntryKind.Link)
        : attributes.HasFlag(FileAttributes.Directory) ? EntryKind.Folder
        : EntryKind.File;
}
