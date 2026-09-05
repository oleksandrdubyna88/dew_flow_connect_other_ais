using CoaiMcp.Core.Findings;

namespace CoaiMcp.Server;

/// <summary>
/// The finding schema on disk — one file, shared by every server on this machine.
/// </summary>
/// <remarks>
/// <para>Every round hands its reviewers a path to this file, and every round used to rewrite it
/// first. The data directory belongs to every window, so with several servers running that is
/// several processes writing one file at the same instant — and on Windows the losers get an
/// exception rather than a queue. The seven-lane matrix of 2026-09-05 killed a whole round on it:
/// <c>the round failed: The process cannot access the file 'finding-schema.json' because it is being
/// used by another process</c>, for a file whose content is a compile-time constant and was already
/// correct on disk.</para>
/// <para>So the write happens only when the file is missing or different, and losing the race is not
/// an error: the neighbour is writing the same bytes. It fails OPEN — the caller always gets the
/// path — because a reviewer that cannot read the schema answers unshaped JSON, which a round
/// already handles, while a round that never launches over a locked constant is strictly worse.</para>
/// </remarks>
public static class SchemaFile
{
    public const string Name = "finding-schema.json";

    /// <summary>The schema's path in <paramref name="dataDir"/>, written there if it is not already right.</summary>
    public static string Ensure(string dataDir)
    {
        var file = Path.Combine(dataDir, Name);
        try
        {
            Directory.CreateDirectory(dataDir);
            if (!File.Exists(file) || !SameAsSchema(file))
            {
                File.WriteAllText(file, FindingSchema.Json);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Somebody else is writing it, with the same content. Nothing to do and nothing to say.
        }

        return file;
    }

    /// <summary>Whether the file already holds the schema — a read that a concurrent write may refuse.</summary>
    private static bool SameAsSchema(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            return reader.ReadToEnd() == FindingSchema.Json;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Unreadable right now: assume a neighbour is mid-write with the same bytes and leave it.
            return true;
        }
    }
}
