namespace CoaiMcp.Core.Findings;

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
/// <para><b>It lives in the CORE because a SECOND binary needs it.</b> The Team server hands its
/// reviewers a schema path too, and it had no writer at all — every adapter that passes the path to
/// its CLI failed on the missing file, which is how codex and antigravity were broken on that server
/// while claude, whose adapter needs no schema file, appeared to work. The reuse rule's fourth move:
/// two projects need it and neither may reference the other, so it belongs in the common ancestor
/// rather than being written a second time.</para>
/// <para>So the write happens only when the file is missing or different, and losing the race is not
/// an error: the neighbour is writing the same bytes. It fails OPEN — the caller always gets the
/// path — because a reviewer that cannot read the schema answers unshaped JSON, which a round
/// already handles, while a round that never launches over a locked constant is strictly worse.</para>
/// </remarks>
public static class SchemaFile
{
    public const string Name = "finding-schema.json";

    /// <summary>The feature reviewer's schema file — its own name, never the one every other round reads.</summary>
    public const string FeatureName = "finding-schema-feature.json";

    /// <summary>The finding schema's path in <paramref name="dataDir"/>, written there if it is not already right.</summary>
    public static string Ensure(string dataDir) => Ensure(dataDir, SchemaShape.Finding);

    /// <summary>The path of one SHAPE's schema in <paramref name="dataDir"/>, written there if it is not already right.</summary>
    /// <remarks>
    /// One file per shape (S1.3): a code round launched beside a feature round reads the finding
    /// schema's file at the same moment the feature round ensures its own, so the two must never share
    /// a name — otherwise a code reviewer could be handed <c>sourceRequests</c>.
    /// </remarks>
    public static string Ensure(string dataDir, SchemaShape shape)
    {
        var (name, content) = Of(shape);
        var file = Path.Combine(dataDir, name);
        try
        {
            Directory.CreateDirectory(dataDir);
            if (!File.Exists(file) || !Holds(file, content))
            {
                File.WriteAllText(file, content);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Somebody else is writing it, with the same content. Nothing to do and nothing to say.
        }

        return file;
    }

#pragma warning disable CS8524 // an unnamed (cast) value throws; a NAMED shape without an arm is CS8509
    private static (string Name, string Content) Of(SchemaShape shape) => shape switch
    {
        SchemaShape.Finding => (Name, FindingSchema.Json),
        SchemaShape.Feature => (FeatureName, FindingSchema.FeatureJson),
    };
#pragma warning restore CS8524

    /// <summary>Whether the file already holds <paramref name="content"/> — a read that a concurrent write may refuse.</summary>
    private static bool Holds(string file, string content)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            return reader.ReadToEnd() == content;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Unreadable right now: assume a neighbour is mid-write with the same bytes and leave it.
            return true;
        }
    }
}

/// <summary>Which schema a reviewer answers in.</summary>
public enum SchemaShape
{
    /// <summary><see cref="FindingSchema.Json"/> — every plan, code and document round.</summary>
    Finding,

    /// <summary><see cref="FindingSchema.FeatureJson"/> — the feature review, which may ask for source.</summary>
    Feature,
}
