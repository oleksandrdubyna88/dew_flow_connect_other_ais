using System.Diagnostics;
using System.Text;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Feature;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Context;

namespace CoaiMcp.Server;

/// <summary>
/// coai's own read-only list and search over a consultant's granted roots (todo/PLAN_agy_searches_through_coai.md §3) —
/// served to an antigravity consultant, which in <c>--mode plan</c> can only <c>view_file</c> a path it already knows.
/// </summary>
/// <remarks>
/// <para><b>Contained the document reader's way.</b> A requested path is resolved component by component with every
/// link followed (<see cref="DocumentReader.Canonical"/>) and must land inside a granted root (<see cref="DocumentId.Of"/>);
/// <c>..</c>, a link or junction leading out, and a path in no granted root are refused by name. While a search WALKS, it
/// never follows a link at all: a reparse point is skipped, so nothing reached through one is read.</para>
/// <para><b>What it never shows</b>: a credential-shaped name (<see cref="CredentialFiles"/>), <c>.git</c>, what the diff
/// excludes (<see cref="DiffExclusions"/> — dependency and build folders, lock files), and the contents of a binary file.
/// Every line it returns passes <see cref="Redaction.SafeSource"/>.</para>
/// <para><b>Bounded, and says so.</b> Every cap in <see cref="LookupLimits"/> that bites is written into the result — a
/// model told "stopped at 50 hits — narrow the folder" can do something about it; one handed a silently short list cannot.</para>
/// <para>It reads the working tree, never git: a granted root is usually a folder of projects, not a checkout. Reads
/// only; it opens nothing for writing.</para>
/// </remarks>
public sealed class WorkspaceLookup(IReadOnlyList<string> roots, LookupLimits? limits = null, Func<string, string>? followLink = null)
    : IWorkspaceLookup
{
    private const string GitFolder = ".git";
    private const int BinarySniffBytes = SourceBudget.BinarySniffChars;

    /// <summary>Below this many bytes left in the turn no result can be sent, so none is read (code round).</summary>
    private const int SmallestSection = 256;

    private readonly LookupLimits _limits = limits ?? LookupLimits.Default;
    private readonly Func<string, string> _follow = followLink ?? DocumentReader.FollowLink;

    public IReadOnlyList<string> Roots { get; } = [.. roots.Select(Path.GetFullPath)];

    public LookupServed Serve(IReadOnlyList<LookupRequest> requests, CancellationToken ct)
    {
        var text = new StringBuilder();
        var (served, refused, used) = (0, 0, 0);
        var block = Stopwatch.StartNew();
        foreach (var request in requests)
        {
            ct.ThrowIfCancellationRequested();
            var answered = Bounded(request, served + refused == 0, block.Elapsed, used, ct);
            text.Append(answered.Section);
            used += Encoding.UTF8.GetByteCount(answered.Section);
            (served, refused) = answered.Served ? (served + 1, refused) : (served, refused + 1);
        }

        return new LookupServed(text.ToString().TrimEnd(), served, refused);
    }

    /// <summary>What one request rendered, and whether it was served — a result, or the sentence saying why not.</summary>
    private sealed record Answered(string Section, bool Served)
    {
        public static Answered Not(LookupRequest request, string why) => new(NotServed(request, why), false);
    }

    /// <summary>
    /// One request inside the block's limits: past the block's time (the first request always runs) or with no room left
    /// for any result it is not read at all; a result too large for what is left is not sent. Each says so.
    /// </summary>
    private Answered Bounded(LookupRequest request, bool first, TimeSpan elapsed, int used, CancellationToken ct)
    {
        if (!first && elapsed >= _limits.BlockTime)
        {
            return Answered.Not(request, $"this block's time limit of {_limits.BlockTime.TotalSeconds:0.#} s is spent — ask for it again next turn");
        }

        if (_limits.TurnBytes - used < SmallestSection)
        {
            return Answered.Not(request, $"this turn's budget of {Kb(_limits.TurnBytes)} is spent — ask for it again next turn");
        }

        var answered = One(request, ct);

        return used + Encoding.UTF8.GetByteCount(answered.Section) <= _limits.TurnBytes
            ? answered
            : Answered.Not(request, $"its result does not fit what is left of this turn's budget of {Kb(_limits.TurnBytes)} — narrow it, or ask again next turn");
    }

    private Answered One(LookupRequest request, CancellationToken ct) =>
        Resolve(request) switch
        {
            Resolution.Refused no => Answered.Not(request, no.Why),
            Resolution.Inside at when request is LookupRequest.List => Listed(request, at),
            Resolution.Inside at => Searched((LookupRequest.Search)request, at, ct),
            _ => throw new InvalidOperationException("the union is closed"),
        };

    // ---------- where a request points ----------

    private abstract record Resolution
    {
        public sealed record Inside(string Root, string Path) : Resolution;

        public sealed record Refused(string Why) : Resolution;
    }

    private Resolution Resolve(LookupRequest request)
    {
        var said = request switch
        {
            LookupRequest.List list => list.Path,
            LookupRequest.Search search => search.Path,
            _ => string.Empty,
        };

        try
        {
            return Absolute(said) is { } full ? Contained(said, full) : new Resolution.Refused(SeveralRoots(said));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A model-written path the file system cannot hold (a NUL, a malformed name) is one line refused, never the
            // turn lost (code round, codex + gemini).
            return new Resolution.Refused($"'{said.Replace('\0', '?')}' is not a valid path: {e.Message}");
        }
    }

    /// <summary>The path made absolute — against THE root when there is exactly one; null when it would be a guess.</summary>
    private string? Absolute(string said)
    {
        if (Path.IsPathRooted(said))
        {
            return said;
        }

        return Roots.Count == 1 ? Path.GetFullPath(said.Length == 0 ? "." : said, Roots[0]) : null;
    }

    private string SeveralRoots(string said) =>
        (said.Length == 0 ? "name the folder to search" : $"'{said}' is relative and there are several granted roots")
        + $" — write the absolute path, inside one of: {string.Join(", ", Roots)}";

    private Resolution Contained(string said, string full)
    {
        var resolved = DocumentReader.Canonical(full, full, _follow);
        foreach (var root in Roots)
        {
            var canonicalRoot = DocumentReader.CanonicalRoot(root, _follow);
            var relative = Relative(canonicalRoot, resolved);
            if (relative is not null)
            {
                return Hidden(relative) is { Length: > 0 } why ? new Resolution.Refused(why) : new Resolution.Inside(canonicalRoot, resolved);
            }
        }

        var target = string.Equals(Path.GetFullPath(full), resolved, DocumentId.Comparison) ? string.Empty : $" (it resolves to '{resolved}')";

        return new Resolution.Refused($"'{said}' is outside the granted roots{target}: {string.Join(", ", Roots)}");
    }

    /// <summary>The path relative to the root — empty for the root itself — or null when it is not inside it.</summary>
    private static string? Relative(string root, string resolved) =>
        string.Equals(Path.TrimEndingDirectorySeparator(root), Path.TrimEndingDirectorySeparator(resolved), DocumentId.Comparison)
            ? string.Empty
            : DocumentId.Of(root, resolved) is { Length: > 0 } inside ? inside : null;

    /// <summary>Why a path inside a root is still never shown — empty when it may be.</summary>
    private static string Hidden(string relative)
    {
        var slashed = relative.Replace('\\', '/');
        if (slashed.Split('/').Contains(GitFolder, StringComparer.OrdinalIgnoreCase))
        {
            return $"'{slashed}' is inside .git, which is never shown";
        }

        if (Excluded(slashed) is { Length: > 0 } glob)
        {
            return $"'{slashed}' is excluded ({glob}) — dependency and build folders and lock files are never shown";
        }

        return CredentialFiles.WhichPattern(slashed) is { Length: > 0 } pattern ? $"'{slashed}' looks like a credential ({pattern})" : string.Empty;
    }

    /// <summary>The glob that hides this path as a file (a lock file named directly) or as a folder — empty when none does.</summary>
    private static string Excluded(string slashed) =>
        DiffExclusions.WhichExcludes(slashed) is { Length: > 0 } asFile ? asFile : DiffExclusions.WhichExcludes(slashed + "/_");

    // ---------- list ----------

    private Answered Listed(LookupRequest request, Resolution.Inside at)
    {
        if (!Directory.Exists(at.Path))
        {
            return Answered.Not(request, $"there is no folder at '{Slashed(at.Path)}'");
        }

        List<string> entries;
        try
        {
            // Stops at the cap BEFORE sorting: a folder of a million entries is not read whole to show two hundred (code
            // round, codex) — which two hundred is then the file system's order, and the cut is said.
            entries = [.. new DirectoryInfo(at.Path).EnumerateFileSystemInfos()
                .Where(entry => Shown(at.Root, entry))
                .Take(_limits.ListEntries + 1)
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Select(Named)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Answered.Not(request, $"'{Slashed(at.Path)}' could not be read: {e.Message}");
        }

        var lines = entries.Take(_limits.ListEntries).ToList();
        if (entries.Count > _limits.ListEntries)
        {
            lines.Add($"(stopped at {_limits.ListEntries} entries — list a narrower folder)");
        }

        return new Answered(Section(request, $"in {Slashed(at.Path)}", lines.Count == 0 ? ["(empty)"] : lines), true);
    }

    private static bool Shown(string root, FileSystemInfo entry) => Hidden(Relative(root, entry.FullName) ?? entry.Name).Length == 0;

    private static string Named(FileSystemInfo entry) =>
        entry.Attributes.HasFlag(FileAttributes.ReparsePoint) ? $"{entry.Name}@ (a link — not followed)"
        : entry is DirectoryInfo ? entry.Name + "/"
        : entry.Name;

    // ---------- search ----------

    private sealed class Walk(LookupLimits limits)
    {
        public List<string> Hits { get; } = [];
        public int Files { get; set; }
        public int TooLarge { get; set; }
        public int Unreadable { get; set; }
        public string Stopped { get; set; } = string.Empty;
        public Stopwatch Clock { get; } = Stopwatch.StartNew();

        public bool Over() =>
            Stopped.Length > 0 || Clock.Elapsed >= limits.WalkTime && Stop($"stopped at the time limit of {limits.WalkTime.TotalSeconds:0.#} s");

        public bool Stop(string why)
        {
            Stopped = why;

            return true;
        }
    }

    private Answered Searched(LookupRequest.Search request, Resolution.Inside at, CancellationToken ct)
    {
        if (!Directory.Exists(at.Path) && !File.Exists(at.Path))
        {
            return Answered.Not(request, $"there is no folder or file at '{Slashed(at.Path)}'");
        }

        var walk = new Walk(_limits);
        if (File.Exists(at.Path))
        {
            SearchFile(at.Path, request.Text, walk);
        }
        else
        {
            SearchFolder(at.Root, at.Path, request.Text, walk, ct);
        }

        return new Answered(Section(request, $"in {Slashed(at.Path)}", Footed(walk)), true);
    }

    private void SearchFolder(string root, string folder, string text, Walk walk, CancellationToken ct)
    {
        var pending = new Stack<string>([folder]);
        while (pending.Count > 0 && !walk.Over())
        {
            ct.ThrowIfCancellationRequested();
            var entries = Entries(pending.Pop(), root, walk, ct);
            foreach (var entry in entries.AsEnumerable().Reverse().OfType<DirectoryInfo>())
            {
                pending.Push(entry.FullName);
            }

            SearchFiles(entries.OfType<FileInfo>(), text, walk);
        }
    }

    /// <summary>A folder's entries a search may walk — never a link — or none, counted as unreadable, when the folder cannot be read.</summary>
    /// <remarks>Read entry by entry with the walk's limits and the caller's token asked at each one, so one enormous folder
    /// cannot outlast the time limit or a cancel before a single file is searched (code round, codex).</remarks>
    private List<FileSystemInfo> Entries(string folder, string root, Walk walk, CancellationToken ct)
    {
        try
        {
            return [.. new DirectoryInfo(folder).EnumerateFileSystemInfos()
                .TakeWhile(_ => !ct.IsCancellationRequested && !walk.Over())
                .Where(entry => !entry.Attributes.HasFlag(FileAttributes.ReparsePoint) && Shown(root, entry))
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            walk.Unreadable++;
            return [];
        }
    }

    private void SearchFiles(IEnumerable<FileInfo> files, string text, Walk walk)
    {
        foreach (var file in files)
        {
            if (walk.Over() || walk.Files >= _limits.WalkFiles && walk.Stop($"stopped after {_limits.WalkFiles} file{Plural(_limits.WalkFiles)} — search a narrower folder"))
            {
                return;
            }

            SearchFile(file.FullName, text, walk);
        }
    }

    private void SearchFile(string path, string text, Walk walk)
    {
        walk.Files++;
        try
        {
            SearchReadable(path, text, walk);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A locked or forbidden file is one file not searched — said in the footer, never the whole turn lost.
            walk.Unreadable++;
        }
    }

    private void SearchReadable(string path, string text, Walk walk)
    {
        if (new FileInfo(path).Length > _limits.FileBytes)
        {
            walk.TooLarge++;
            return;
        }

        if (!IsBinary(path))
        {
            SearchLines(path, text, walk);
        }
    }

    private void SearchLines(string path, string text, Walk walk)
    {
        var number = 0;
        foreach (var line in File.ReadLines(path))
        {
            number++;
            if (line.Contains(text, StringComparison.OrdinalIgnoreCase) && !Hit(path, number, line, walk))
            {
                return;
            }
        }
    }

    /// <summary>Adds one hit; false once the hit cap is reached (and the walk is stopped, saying so).</summary>
    private bool Hit(string path, int number, string line, Walk walk)
    {
        if (walk.Hits.Count >= _limits.SearchHits)
        {
            walk.Stop($"stopped at {_limits.SearchHits} hit{Plural(_limits.SearchHits)} — search a narrower folder or a longer text");
            return false;
        }

        var cut = line.Length > _limits.LineChars ? line[.._limits.LineChars] + " …" : line;
        walk.Hits.Add($"{Slashed(path)}:{number}: {Redaction.SafeSource(cut.Trim())}");

        return true;
    }

    private IReadOnlyList<string> Footed(Walk walk)
    {
        var lines = new List<string>(walk.Hits.Count == 0 ? ["(no match)"] : walk.Hits);
        if (walk.TooLarge > 0)
        {
            lines.Add($"({walk.TooLarge} file{Plural(walk.TooLarge)} over the size limit of {Size(_limits.FileBytes)} not searched)");
        }

        if (walk.Unreadable > 0)
        {
            lines.Add($"({walk.Unreadable} file{Plural(walk.Unreadable)} or folder{Plural(walk.Unreadable)} could not be read)");
        }

        if (walk.Stopped.Length > 0)
        {
            lines.Add($"({walk.Stopped})");
        }

        return lines;
    }

    private static bool IsBinary(string path)
    {
        using var stream = File.OpenRead(path);
        var head = new byte[BinarySniffBytes];
        var read = stream.Read(head, 0, head.Length);

        return Array.IndexOf(head, (byte)0, 0, read) >= 0;
    }

    // ---------- rendering ----------

    private static string Section(LookupRequest request, string where, IReadOnlyList<string> lines) =>
        $"### {request.Line}\n{where}\n{string.Join("\n", lines)}\n\n";

    private static string NotServed(LookupRequest request, string why) => $"### {request.Line}\nnot served: {why}\n\n";

    private static string Slashed(string path) => path.Replace('\\', '/');

    private static string Plural(int count) => count == 1 ? string.Empty : "s";

    private static string Size(long bytes) => bytes < 1024 ? $"{bytes} bytes" : $"{bytes / 1024} KB";

    private static string Kb(int bytes) => $"{bytes / 1024.0:0.#} KB";
}
