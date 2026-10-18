namespace CoaiMcp.Server;

/// <summary>A folder holding a <c>.git</c> beneath the granted root, and the walk's own names from the root down to it.</summary>
/// <param name="Path">The folder's full path.</param>
/// <param name="Segments">The names below the root, e.g. <c>["group", "repo"]</c> — never a string git gave.</param>
public sealed record FoundRepository(string Path, IReadOnlyList<string> Segments);

/// <summary>What the walk came to: the candidates and the notes of what it skipped, or why the root is refused — still with its notes.</summary>
/// <param name="Notes">What the walk skipped, in the order it met it.</param>
public abstract record WalkOutcome(IReadOnlyList<string> Notes)
{
    private WalkOutcome()
        : this([])
    {
    }

    /// <summary>Candidates in ordinal order of their relative path (D7), and the notes of what the walk skipped.</summary>
    public sealed record Found(IReadOnlyList<FoundRepository> Repositories, IReadOnlyList<string> Notes) : WalkOutcome(Notes);

    /// <summary>The root is refused, the sentence the row carries, and what the walk had skipped before it refused — never lost with the refusal.</summary>
    public sealed record Refused(string Reason, IReadOnlyList<string> Notes) : WalkOutcome(Notes);
}

/// <summary>
/// Finds the folders holding a <c>.git</c> beneath a granted root that is not itself a work tree (E1.1 of
/// <c>todo/PLAN_vibe_reads_a_folder_of_repositories.md</c>; D2, D3's folder bound, D7). Pure: it reads through two seams and never
/// asks git — whether a candidate really is a work tree is the copy's question (D4's discovery side).
/// </summary>
/// <remarks>
/// <para>Depth counts directory levels below the root: its children are depth 1, theirs depth 2. The root and the depth-1 folders are
/// LISTED; a depth-2 folder is only asked whether it holds a <c>.git</c> (the second seam) — a folder of a million files costs one
/// lookup, not a listing. A candidate is added and not descended; hidden folders, <c>node_modules</c>, <c>bin</c> and <c>obj</c> are
/// skipped without a word; a link to a FILE is skipped without a word too, a link to a FOLDER is never followed and is named in one
/// note (a cloud provider's on-demand folder looks just like it, and the person's repositories may be behind it); a folder that cannot
/// be listed is skipped with a note — and a ROOT that cannot be listed is refused by name, never reported as an empty one. The
/// 50-repository bound is NOT applied here — a candidate is not yet a repository, so it counts confirmed ones, in the caller.</para>
/// </remarks>
internal static class RepositoryWalk
{
    public const int MaxDepth = 2;
    public const int MaxFolders = 10_000;
    public const int MaxRepositories = 50;

    /// <summary>How many linked folders the one note names; the rest are counted.</summary>
    private const int NamedLinks = 5;

    private const string GitName = ".git";

    /// <summary>The candidates beneath <paramref name="root"/>, or the refusal of a root that cannot be listed, has too many folders, or none.</summary>
    /// <param name="list">Lists a folder (the root and its children).</param>
    /// <param name="probe">Answers, in the shape of a listing, whether a folder at the deepest level holds a <c>.git</c> — never lists it.</param>
    /// <exception cref="OperationCanceledException">The token was cancelled before a listing — the row's budget ran out.</exception>
    public static WalkOutcome Find(string root, Func<string, FolderListing> list, Func<string, FolderListing> probe, CancellationToken ct) =>
        new Walk(root, list, probe, ct).Run();

    private sealed class Walk(string root, Func<string, FolderListing> list, Func<string, FolderListing> probe, CancellationToken ct)
    {
        private readonly List<FoundRepository> _found = [];
        private readonly List<string> _notes = [];
        private readonly List<string> _links = [];
        private int _listed;

        /// <summary>Why the ROOT could not be listed — empty while it could.</summary>
        private string _rootRefusal = string.Empty;

        public WalkOutcome Run()
        {
            var complete = Visit(root, []);

            return _rootRefusal.Length > 0 ? Refuse(_rootRefusal)
                : !complete ? Refuse(TooManyFolders(root))
                : _found.Count == 0 ? Refuse(NoRepository(root))
                : Found();
        }

        private WalkOutcome.Found Found() =>
            new([.. _found.OrderBy(one => string.Join('/', one.Segments), StringComparer.Ordinal)], [.. _notes, .. LinkNote()]);

        private WalkOutcome.Refused Refuse(string reason) => new(reason, [.. _notes, .. LinkNote()]);

        /// <summary>Lists one folder — or, at the deepest level, only looks for its <c>.git</c> — and, when it may be, the folders beneath it; false once the folder bound is passed.</summary>
        private bool Visit(string path, IReadOnlyList<string> segments)
        {
            ct.ThrowIfCancellationRequested();
            if (++_listed > MaxFolders)
            {
                return false;
            }

            return (segments.Count >= MaxDepth ? probe(path) : list(path)) switch
            {
                FolderListing.Listed listed => Entered(path, segments, [.. listed.Entries.OrderBy(entry => entry.Name, StringComparer.Ordinal)]),
                FolderListing.Unreadable unreadable => Unlisted(path, segments, unreadable.Reason),
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

            if (segments.Count >= MaxDepth)
            {
                return true;
            }

            _links.AddRange(entries.Where(Linked).Select(entry => string.Join('/', [.. segments, entry.Name])));

            return entries.Where(Descends).All(entry => Visit(Path.Combine(path, entry.Name), [.. segments, entry.Name]));
        }

        /// <summary>A folder that could not be listed: a child is skipped with a note and the walk goes on; the ROOT is the granted folder itself, so it ends the walk.</summary>
        private bool Unlisted(string path, IReadOnlyList<string> segments, string reason)
        {
            if (segments.Count == 0)
            {
                _rootRefusal = $"the folder {path} could not be listed ({reason}) — grant a folder coai can read";

                return false;
            }

            _notes.Add($"{path} was skipped: it could not be listed ({reason})");

            return true;
        }

        /// <summary>One note for the folders the walk saw but never followed — empty when there were none.</summary>
        private IEnumerable<string> LinkNote()
        {
            if (_links.Count == 0)
            {
                return [];
            }

            var named = string.Join(", ", _links.Order(StringComparer.Ordinal).Take(NamedLinks));
            var more = _links.Count > NamedLinks ? $" and {_links.Count - NamedLinks} more" : string.Empty;

            return [$"{root}: {_links.Count} linked {(_links.Count == 1 ? "folder was" : "folders were")} not entered, because a copy never follows a link ({named}{more})"];
        }
    }

    /// <summary>The root is depth 0 and never a candidate; any other folder holding a <c>.git</c> folder or file is.</summary>
    private static bool IsCandidate(IReadOnlyList<string> segments, FolderEntry[] entries) =>
        segments.Count > 0 && entries.Any(entry => entry.Name == GitName && entry.Kind is EntryKind.Folder or EntryKind.File);

    private static bool Descends(FolderEntry entry) =>
        entry.Kind == EntryKind.Folder && !Silent(entry.Name);

    /// <summary>A link to a folder the walk would have entered had it been a plain one: hidden and noise names stay silent.</summary>
    private static bool Linked(FolderEntry entry) =>
        entry.Kind == EntryKind.FolderLink && !Silent(entry.Name);

    private static bool Silent(string name) =>
        name.StartsWith('.') || IsNoise(name);

    private static bool IsNoise(string name) =>
        name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
        || name.Equals("bin", StringComparison.OrdinalIgnoreCase)
        || name.Equals("obj", StringComparison.OrdinalIgnoreCase);

    /// <summary>The row's bound, named for the row: the granted folders together hold more than a Vibe disk row copies (D3).</summary>
    public static string TooManyRepositories { get; } =
        $"the granted folders hold more than {MaxRepositories} Git repositories, more than a Vibe disk row copies — grant the project folders themselves";

    private static string TooManyFolders(string root) =>
        $"the folder {root} holds more than {MaxFolders} folders within two levels — grant the project folders themselves";

    public static string NoRepository(string root) =>
        $"the folder {root} holds no Git repository within two levels — Vibe reads copies coai makes through Git";
}
