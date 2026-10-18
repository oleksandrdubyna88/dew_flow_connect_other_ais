namespace CoaiMcp.Server;

/// <summary>A folder holding a <c>.git</c> beneath the granted root, and the walk's own names from the root down to it.</summary>
/// <param name="Path">The folder's full path.</param>
/// <param name="Segments">The names below the root, e.g. <c>["group", "repo"]</c> — never a string git gave.</param>
public sealed record FoundRepository(string Path, IReadOnlyList<string> Segments);

/// <summary>What the walk came to: the candidates and the notes of what it could not list, or why the root is refused.</summary>
public abstract record WalkOutcome
{
    private WalkOutcome()
    {
    }

    /// <summary>Candidates in ordinal order of their relative path (D7), and one note per folder that could not be listed.</summary>
    public sealed record Found(IReadOnlyList<FoundRepository> Repositories, IReadOnlyList<string> Notes) : WalkOutcome;

    /// <summary>The root is refused, and the sentence the row carries.</summary>
    public sealed record Refused(string Reason) : WalkOutcome;
}

/// <summary>
/// Finds the folders holding a <c>.git</c> beneath a granted root that is not itself a work tree (E1.1 of
/// <c>todo/PLAN_vibe_reads_a_folder_of_repositories.md</c>; D2, D3's folder bound, D7). Pure: it lists through a seam and never
/// asks git — whether a candidate really is a work tree is the copy's question (D4's discovery side).
/// </summary>
/// <remarks>
/// Depth counts directory levels below the root: its children are depth 1, theirs depth 2. A candidate is added and not
/// descended; hidden folders, <c>node_modules</c>, <c>bin</c>, <c>obj</c> and every link are skipped without a word; a folder
/// that cannot be listed is skipped with a note. The 50-repository bound is NOT applied here — a candidate is not yet a
/// repository, so it counts confirmed ones, in the caller.
/// </remarks>
internal static class RepositoryWalk
{
    public const int MaxDepth = 2;
    public const int MaxFolders = 10_000;
    public const int MaxRepositories = 50;

    private const string GitName = ".git";

    /// <summary>The candidates beneath <paramref name="root"/>, or the refusal of a root with too many folders or none.</summary>
    /// <exception cref="OperationCanceledException">The token was cancelled before a listing — the row's budget ran out.</exception>
    public static WalkOutcome Find(string root, Func<string, FolderListing> list, CancellationToken ct) =>
        new Walk(root, list, ct).Run();

    private sealed class Walk(string root, Func<string, FolderListing> list, CancellationToken ct)
    {
        private readonly List<FoundRepository> _found = [];
        private readonly List<string> _notes = [];
        private int _listed;

        public WalkOutcome Run() =>
            !Visit(root, []) ? TooManyFolders(root)
            : _found.Count == 0 ? NoRepository(root)
            : new WalkOutcome.Found([.. _found.OrderBy(one => string.Join('/', one.Segments), StringComparer.Ordinal)], [.. _notes]);

        /// <summary>Lists one folder and, when it may be, the folders beneath it — false once the folder bound is passed.</summary>
        private bool Visit(string path, IReadOnlyList<string> segments)
        {
            ct.ThrowIfCancellationRequested();
            if (++_listed > MaxFolders)
            {
                return false;
            }

            return list(path) switch
            {
                FolderListing.Listed listed => Entered(path, segments, [.. listed.Entries.OrderBy(entry => entry.Name, StringComparer.Ordinal)]),
                FolderListing.Unreadable unreadable => Skipped(path, unreadable.Reason),
                _ => throw new InvalidOperationException("the union is closed"),
            };
        }

        private bool Entered(string path, IReadOnlyList<string> segments, FolderEntry[] entries)
        {
            if (IsCandidate(segments, entries))
            {
                _found.Add(new FoundRepository(path, segments));

                return true;
            }

            return segments.Count >= MaxDepth
                || entries.Where(Descends).All(entry => Visit(Path.Combine(path, entry.Name), [.. segments, entry.Name]));
        }

        private bool Skipped(string path, string reason)
        {
            _notes.Add($"{path} was skipped: it could not be listed ({reason})");

            return true;
        }
    }

    /// <summary>The root is depth 0 and never a candidate; any other folder holding a <c>.git</c> folder or file is.</summary>
    private static bool IsCandidate(IReadOnlyList<string> segments, FolderEntry[] entries) =>
        segments.Count > 0 && entries.Any(entry => entry.Name == GitName && entry.Kind is EntryKind.Folder or EntryKind.File);

    private static bool Descends(FolderEntry entry) =>
        entry.Kind == EntryKind.Folder && !entry.Name.StartsWith('.') && !IsNoise(entry.Name);

    private static bool IsNoise(string name) =>
        name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
        || name.Equals("bin", StringComparison.OrdinalIgnoreCase)
        || name.Equals("obj", StringComparison.OrdinalIgnoreCase);

    private static WalkOutcome.Refused TooManyFolders(string root) =>
        new($"the folder {root} holds more than {MaxFolders} folders within two levels — grant the project folders themselves");

    private static WalkOutcome.Refused NoRepository(string root) =>
        new($"the folder {root} holds no Git repository within two levels — Vibe reads copies coai makes through Git");
}
