namespace CoaiMcp.Server;

/// <summary>
/// Reading a file WITHOUT forbidding anyone else to write it.
/// </summary>
/// <remarks>
/// <para>The cause nobody looks for, and this family has now paid for it four times:
/// <c>File.ReadAllText</c> opens with <see cref="FileShare.Read"/>, so a READER forbids writing. In a
/// data directory that five servers and a panel all touch, that turns an ordinary poll into a lock
/// somebody else's write fails on — and the failure surfaces at the writer, wearing the words
/// "Access to the path is denied", nowhere near the read that caused it.</para>
/// <para><see cref="FileShare.Delete"/> belongs in the set as well as <c>ReadWrite</c>: on Windows a
/// rename over an open file is a delete of that file, so a reader that permits writing but not
/// deleting still blocks the atomic save every writer here performs.</para>
/// <para><see cref="SessionStore"/> keeps its own copy of this because its read also takes the
/// session's turn; everything else in this directory should come through here.</para>
/// </remarks>
internal static class SharedRead
{
    /// <summary>The file's text, or empty when it cannot be read right now.</summary>
    public static string Text(string path)
    {
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
