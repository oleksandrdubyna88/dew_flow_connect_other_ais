using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoaiMcp.Runners.Worktrees;

/// <summary>What this machine knows about one review tree, written beside it.</summary>
/// <param name="Repository">The git common dir the tree was made from — the identity's first half.</param>
/// <param name="Sha">The commit checked out — the identity's second half.</param>
/// <param name="Created">When it was made, ISO-8601 UTC, round-trip format.</param>
/// <param name="EmptyMounts">Submodule mounts that stayed empty, so a reuse can say so too.</param>
public sealed record ReviewTreeRecord(
    string Repository,
    string Sha,
    string Created,
    IReadOnlyList<string> EmptyMounts);

/// <summary>Source-generated, because the host publishes with reflection-free serialization.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(ReviewTreeRecord))]
public sealed partial class ReviewTreeJsonContext : JsonSerializerContext;

/// <summary>
/// The readiness signal for a review tree: the record exists exactly when the tree is finished.
/// </summary>
/// <remarks>
/// <para><b>Why a record and not the directory.</b> <c>git worktree add</c> creates the directory
/// before it has checked anything out, and submodule population runs after that again. A directory
/// therefore means "somebody started", never "this is ready" — so a second press that trusted the
/// directory would hand a person a half-checked-out tree. The record is written LAST, and only
/// after everything that can fail has not.</para>
/// <para><b>Written through a temp file with a unique name</b>, the shape
/// <c>ConsultSchemaFile</c> already uses: a reader never sees a half-written record, and two writers
/// racing cannot corrupt each other's temp. (Six places in this repository now open-code this same
/// three-line dance; it wants extracting, which is a change of its own rather than a side effect of
/// this one.)</para>
/// <para>An unreadable or truncated record is treated as ABSENT rather than as an error: the tree it
/// describes is then unfinished by definition, and the unfinished path already knows what to do with
/// one. Nothing here throws.</para>
/// </remarks>
public static class ReviewTreeRecords
{
    /// <summary>Where the record for a tree of this name lives.</summary>
    public static string FileFor(string root, string name) => Path.Combine(root, $"{name}.json");

    /// <summary>The record, or an empty one when there is none that can be read.</summary>
    public static ReviewTreeRecord Read(string file)
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(file), ReviewTreeJsonContext.Default.ReviewTreeRecord)
                   ?? Missing;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return Missing;
        }
    }

    /// <summary>True when the record is a real one rather than the empty stand-in for "none".</summary>
    public static bool Exists(ReviewTreeRecord record) => record.Sha.Length > 0;

    /// <summary>Writes the record last, atomically, so its presence means the tree is finished.</summary>
    public static void Write(string file, ReviewTreeRecord record)
    {
        var temp = $"{file}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(record, ReviewTreeJsonContext.Default.ReviewTreeRecord));
        File.Move(temp, file, overwrite: true);
    }

    /// <summary>Every record under the root, oldest first — what the budget counts and names.</summary>
    public static IReadOnlyList<ReviewTreeRecord> All(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(root, "*.json")
            .Select(Read)
            .Where(Exists)
            .OrderBy(r => r.Created, StringComparer.Ordinal)];
    }

    private static readonly ReviewTreeRecord Missing = new("", "", "", []);
}
