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
}
