namespace CoaiMcp.Server;

/// <summary>What one entry of a folder is, as far as the repository walk cares.</summary>
public enum EntryKind
{
    Folder,
    File,

    /// <summary>A symbolic link, or on Windows a junction too — anything the walk never follows.</summary>
    Link,
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
    /// <summary>
    /// The real listing: <see cref="FileAttributes.ReparsePoint"/> is a <see cref="EntryKind.Link"/> (a symlink on Linux, a
    /// symlink or a junction on Windows), a directory is a <see cref="EntryKind.Folder"/>, anything else a <see cref="EntryKind.File"/>.
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

    private static FolderEntry EntryOf(FileSystemInfo info) =>
        new(info.Name, info.Attributes.HasFlag(FileAttributes.ReparsePoint) ? EntryKind.Link : info is DirectoryInfo ? EntryKind.Folder : EntryKind.File);
}
