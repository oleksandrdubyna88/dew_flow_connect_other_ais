using System.Text;

namespace CoaiMcp.Server;

/// <summary>
/// Every document snapshot a round has reviewed, on disk, named by its content hash.
/// </summary>
/// <remarks>
/// <para><b>Its own file rather than a field on the session, and that is a write-path decision.</b>
/// The scope a plan round agreed on rides the session file as <c>PlanText</c>, and the obvious move
/// was to put a document there beside it — but the session file is rewritten on every reviewer
/// transition, and a nine-reviewer round does that many times. A quarter of a megabyte through that
/// loop is the kind of cost nobody sees until a round is slow for no reason anyone can point at. The
/// snapshot is written ONCE per round; the session carries only its id.</para>
/// <para>Written temp-then-rename, like the session file: a process killed mid-write must not leave
/// a half-document that reads as the thing a round reviewed. A scratch name per write, because this
/// machine runs several servers over one data directory and a fixed scratch path is what made two
/// of them fight over one file in the store next door.</para>
/// <para>It is kept for a PERSON. Nothing in this program reads it back: the round names the
/// snapshot by id, and what the file is for is opening it a week later to see what the reviewers
/// were actually given — the same reason the prompt bodies are files in this directory rather than
/// strings in a binary.</para>
/// </remarks>
public sealed class ArtifactStore(string dataDir)
{
    private string Dir => Path.Combine(dataDir, "documents");

    private string FileFor(string artifactId) => Path.Combine(Dir, $"{artifactId}.txt");

    public bool Has(string artifactId) => File.Exists(FileFor(artifactId));

    /// <summary>
    /// How long a snapshot is kept.
    /// </summary>
    /// <remarks>
    /// <para>Three reviewers said the same thing: a store that only ever writes is a disk that only
    /// ever fills. A quarter of a megabyte per round, for ever, in a directory nobody looks at until
    /// it is the reason something else failed.</para>
    /// <para>Thirty days rather than six hours — the window the scratch sweeper next door uses —
    /// because these are not scratch. A person opens one to see what the reviewers were actually
    /// given, and the question "what did round 1 read" is asked weeks later or not at all. It is also
    /// long enough that a session still open cannot outlive its own snapshot in any real use.</para>
    /// </remarks>
    public static readonly TimeSpan Keeps = TimeSpan.FromDays(30);

    /// <summary>
    /// Keeps this snapshot, or says why it could not be kept.
    /// </summary>
    /// <returns>Null when it was written, a sentence when it was not.</returns>
    /// <remarks>
    /// <b>A failure here never fails the round.</b> The snapshot is a record for a person; the
    /// review is the product. Refusing to review a document because a file could not be written
    /// would be the tail wagging the dog — so the sentence travels back and is logged, and the round
    /// goes ahead.
    /// </remarks>
    public string? Keep(string artifactId, string text)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            PruneOlderThan(DateTime.UtcNow - Keeps);
            var scratch = Path.Combine(Dir, $"{artifactId}.{Guid.NewGuid():N}.writing");
            File.WriteAllText(scratch, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(scratch, FileFor(artifactId), overwrite: true);

            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"the document snapshot {artifactId} could not be kept: {e.Message}";
        }
    }

    /// <summary>
    /// Drops snapshots — and abandoned scratch files — last written before <paramref name="cutoff"/>.
    /// </summary>
    /// <remarks>
    /// <para>On the WRITE path, like <c>PruneOldAnswerDirs</c> next door, because a store with no
    /// background of its own has no other moment: a server that never reviews another document
    /// should not be doing work, and one that does pays a directory listing per round.</para>
    /// <para>The LAST WRITE, not the creation time, for the reason the scratch sweeper records:
    /// Windows file tunnelling hands a recreated name its predecessor's creation time. And
    /// <c>.writing</c> files are swept too — a process killed between the write and the rename leaves
    /// one, and nothing else would ever remove it.</para>
    /// <para>Never throws. Failing to tidy is not a reason to fail a review.</para>
    /// </remarks>
    internal void PruneOlderThan(DateTime cutoff)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Dir))
            {
                Forget(file, cutoff);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // Nothing to sweep, or nothing we may sweep. Either way the round goes ahead.
        }
    }

    private static void Forget(string file, DateTime cutoff)
    {
        try
        {
            if (File.GetLastWriteTimeUtc(file) < cutoff)
            {
                File.Delete(file);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Somebody is reading it, or it went away between the listing and here. Next round.
        }
    }
}
