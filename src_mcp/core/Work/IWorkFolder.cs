namespace CoaiMcp.Core.Work;

/// <summary>
/// What reading one file of a work plan gave. A closed set, so the validator names each case: absent, text, over the size it was told to read, or text that is not UTF-8.
/// </summary>
public abstract record WorkFile
{
    private WorkFile()
    {
    }

    /// <summary>There is no such file.</summary>
    public sealed record Missing : WorkFile;

    /// <summary>The file's text.</summary>
    public sealed record Text(string Content) : WorkFile;

    /// <summary>The file is larger than the limit it was read with.</summary>
    public sealed record TooLarge(long Bytes) : WorkFile;

    /// <summary>The bytes are not UTF-8.</summary>
    public sealed record NotUtf8 : WorkFile;
}

/// <summary>
/// A feature folder as the validator reads it: folder names and file texts, by path relative to the feature folder, written with <c>/</c> (the empty path is the feature
/// folder itself). Today the implementation reads the working tree; E3 can read a commit through the same two methods. The validator never writes.
/// </summary>
public interface IWorkFolder
{
    /// <summary>The names of the folders directly inside <paramref name="relative"/>, in no promised order; empty when there is none. A link is not a folder.</summary>
    IReadOnlyList<string> Folders(string relative);

    /// <summary>The file at <paramref name="relative"/>; a file longer than <paramref name="maxBytes"/> is <see cref="WorkFile.TooLarge"/> without being read whole.</summary>
    WorkFile Read(string relative, int maxBytes);
}
