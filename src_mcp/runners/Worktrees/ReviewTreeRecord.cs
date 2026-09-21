using System.Text.Json;
using System.Text.Json.Serialization;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.Runners.Worktrees;

/// <summary>What this machine knows about one review tree, written beside it.</summary>
/// <remarks>
/// Every property is nullable and every reader goes through <see cref="ReviewTreeRecords.Read"/>,
/// which normalises. A DTO field the file omitted deserialises as NULL whatever a non-nullable
/// declaration says, and this record is read from a file a person can edit, truncate or half-write —
/// so declaring the fields non-nullable would have been a promise the deserialiser does not keep.
/// Raised twice on the code round, with the crash named: <c>record.Sha.Length</c> on an omitted
/// <c>sha</c>.
/// </remarks>
/// <param name="Repository">The git common dir the tree was made from — the identity's first half.</param>
/// <param name="RepoPath">
/// A WORKING tree of that repository, as the row recorded it. Kept because git refuses
/// <c>worktree unlock</c> and <c>worktree remove</c> run from a bare <c>.git</c> directory — *this
/// operation must be run in a work tree* — so story 3.2b needs somewhere to run them FROM, and
/// discovering it later would mean migrating every record written before. (Code round, gemini.)
/// </param>
/// <param name="Sha">The commit checked out — the identity's second half.</param>
/// <param name="Created">When it was made, ISO-8601 UTC, round-trip format.</param>
/// <param name="EmptyMounts">Submodule mounts that stayed empty, so a reuse can say so too.</param>
public sealed record ReviewTreeRecord(
    string? Repository,
    string? RepoPath,
    string? Sha,
    string? Created,
    IReadOnlyList<string>? EmptyMounts);

/// <summary>Source-generated, because the host publishes with reflection-free serialization.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(ReviewTreeRecord))]
public sealed partial class ReviewTreeJsonContext : JsonSerializerContext;

/// <summary>A record as the rest of the code may use it: every field present, nothing null.</summary>
/// <param name="Repository">The git common dir.</param>
/// <param name="RepoPath">A working tree of it.</param>
/// <param name="Sha">The commit.</param>
/// <param name="Created">ISO-8601 UTC.</param>
/// <param name="EmptyMounts">Mounts that stayed empty.</param>
public sealed record HeldRecord(
    string Repository,
    string RepoPath,
    string Sha,
    string Created,
    IReadOnlyList<string> EmptyMounts);

/// <summary>
/// What a read of a record file found. THREE states, not two.
/// </summary>
/// <remarks>
/// The distinction is not pedantry, and the code round named the consequence: with
/// <see cref="Unreadable"/> collapsed into <see cref="Absent"/>, a finished tree whose record became
/// temporarily unreadable — a permission, a lock, a half-written file — looked like a tree that never
/// finished, and once its directory was old and clean the cleanup path unlocked and DELETED a
/// checkout a person may have been reading. Absence must be positively established before anything
/// is removed.
/// </remarks>
public enum RecordState
{
    /// <summary>No file. The tree, if there is a directory, never finished being made.</summary>
    Absent,

    /// <summary>A file that could not be read or parsed. We know nothing; touch nothing.</summary>
    Unreadable,

    /// <summary>A record with every field present.</summary>
    Found,
}

/// <summary>One record FILE under the root: its name, its tree, and what reading it found.</summary>
/// <param name="Name">The directory name, which is also the record's own file name without .json.</param>
/// <param name="Tree">The checkout it describes, on disk.</param>
/// <param name="Read">What reading the record found — which may be that it could not be read.</param>
public sealed record HeldTreeFile(string Name, string Tree, RecordRead Read);

/// <summary>A record read, with the state that says how much it is worth.</summary>
/// <param name="State">Which of the three.</param>
/// <param name="Record">The normalised record — meaningful only when <see cref="RecordState.Found"/>.</param>
public sealed record RecordRead(RecordState State, HeldRecord Record)
{
    /// <summary>Nothing there at all.</summary>
    public static RecordRead Absent { get; } = new(RecordState.Absent, Empty);

    /// <summary>There is a file and it told us nothing.</summary>
    public static RecordRead Unreadable { get; } = new(RecordState.Unreadable, Empty);

    /// <summary>
    /// Computed rather than a shared instance, because a static initialiser that runs BEFORE the one
    /// it depends on hands out a null — which the compiler caught here, and which would otherwise
    /// have been the very NullReferenceException this type exists to prevent.
    /// </summary>
    private static HeldRecord Empty => new("", "", "", "", []);
}

/// <summary>
/// The readiness signal for a review tree: the record exists exactly when the tree is finished.
/// </summary>
/// <remarks>
/// <para><b>Why a record and not the directory.</b> <c>git worktree add</c> creates the directory
/// before it has checked anything out, and submodule population runs after that again. A directory
/// therefore means "somebody started", never "this is ready" — so a second press that trusted the
/// directory would hand a person a half-checked-out tree. The record is written LAST, and only
/// after everything that can fail has not.</para>
/// <para>Written through <see cref="AtomicFile"/>, so a reader never sees a half-written record and
/// two writers racing cannot corrupt each other.</para>
/// </remarks>
public static class ReviewTreeRecords
{
    /// <summary>Where the record for a tree of this name lives.</summary>
    public static string FileFor(string root, string name) => Path.Combine(root, $"{name}.json");

    /// <summary>The record, and whether it is absent, unreadable, or real.</summary>
    public static RecordRead Read(string file)
    {
        if (!File.Exists(file))
        {
            return RecordRead.Absent;
        }

        try
        {
            var raw = JsonSerializer.Deserialize(File.ReadAllText(file), ReviewTreeJsonContext.Default.ReviewTreeRecord);

            return raw is null ? RecordRead.Unreadable : Normalised(raw);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Not swallowed into "there is no record": a permission or a corrupt file is a state in
            // which we may not delete anything, and the caller is told which it was.
            return RecordRead.Unreadable;
        }
    }

    /// <summary>
    /// A deserialised record with every field made present — or <see cref="RecordState.Unreadable"/>
    /// when the two fields that carry the IDENTITY are not.
    /// </summary>
    /// <remarks>
    /// A record missing its repository or its commit cannot be matched against a request, so it
    /// cannot prove a tree is the right one. It is refused rather than normalised into a record that
    /// would silently compare equal to nothing.
    /// </remarks>
    private static RecordRead Normalised(ReviewTreeRecord raw)
    {
        var repository = raw.Repository ?? string.Empty;
        var sha = raw.Sha ?? string.Empty;
        var repoPath = raw.RepoPath ?? string.Empty;

        // `repoPath` is required, not merely kept: `worktree unlock` and `worktree remove` refuse to
        // run from a bare `.git`, so a record without it describes a checkout NOTHING can ever
        // remove — a cap slot spent forever. Handing it back as finished would be promising a
        // lifecycle we could not carry out. (Code round, codex.)
        return repository.Length == 0 || sha.Length == 0 || repoPath.Length == 0
            ? RecordRead.Unreadable
            : new RecordRead(RecordState.Found, new HeldRecord(
                repository,
                repoPath,
                sha,
                raw.Created ?? string.Empty,
                raw.EmptyMounts ?? []));
    }

    /// <summary>Writes the record last, atomically, so its presence means the tree is finished.</summary>
    public static void Write(string file, HeldRecord record)
    {
        AtomicFile.Write(file, JsonSerializer.Serialize(
            new ReviewTreeRecord(
                record.Repository, record.RepoPath, record.Sha, record.Created, record.EmptyMounts),
            ReviewTreeJsonContext.Default.ReviewTreeRecord));
    }

    /// <summary>
    /// Every record under the root whose TREE is still on disk, oldest first — what the budget counts.
    /// </summary>
    /// <remarks>
    /// <para>A record whose directory somebody deleted by hand is not a tree; counting it would wedge
    /// a cap slot forever against a checkout that no longer exists.</para>
    /// <para><b>An UNREADABLE record is still counted.</b> A record we cannot read beside a directory
    /// that is there is a tree we must assume is somebody's — it occupies disk and it occupies the
    /// cap. This docblock said so while the code filtered it out, which is exactly the kind of
    /// disagreement a reviewer reads a docblock to find. (Code round, gemini.)</para>
    /// <para>The NAME travels with the record, because the path of an unreadable one cannot be
    /// recomputed from fields it does not have.</para>
    /// </remarks>
    public static IReadOnlyList<HeldTreeFile> All(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(root, "*.json")
            .Where(file => Directory.Exists(TreeOf(file)))
            .Select(file => new HeldTreeFile(
                Path.GetFileNameWithoutExtension(file), TreeOf(file), Read(file)))
            .Where(held => held.Read.State != RecordState.Absent)
            .OrderBy(held => held.Read.Record.Created, StringComparer.Ordinal)];
    }

    /// <summary>The tree a record file describes: the same path without the <c>.json</c>.</summary>
    public static string TreeOf(string recordFile) =>
        Path.Combine(
            Path.GetDirectoryName(recordFile) ?? string.Empty,
            Path.GetFileNameWithoutExtension(recordFile));
}
