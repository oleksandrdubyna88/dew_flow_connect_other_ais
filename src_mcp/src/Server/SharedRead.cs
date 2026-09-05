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
/// <para><b>Sharing is half of it.</b> Measured in this repository twice over: with the reader
/// sharing delete and the rename retried ten times over half a second, hot readers still starved
/// the writer. So a POLLING reader in this directory pairs this with <see cref="SessionTurn"/>,
/// which <see cref="SessionStore"/> and <c>Escalations</c> both do — this alone shortens the window
/// and does not close it.</para>
/// </remarks>
internal static class SharedRead
{
    /// <summary>
    /// The file's text.
    /// </summary>
    /// <remarks>
    /// It THROWS — <see cref="IOException"/> when the file is busy, <see cref="UnauthorizedAccessException"/>
    /// when it is being replaced underneath — and every caller here treats both as "nothing yet, the
    /// next poll will find it whole". Said plainly because the first version of this summary claimed
    /// an empty string, and a future reader would have written no catch at all.
    /// </remarks>
    public static string Text(string path)
    {
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
