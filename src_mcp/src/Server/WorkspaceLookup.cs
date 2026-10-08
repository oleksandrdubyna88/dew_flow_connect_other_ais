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

    private readonly LookupLimits _limits = limits ?? LookupLimits.Default;
    private readonly Func<string, string> _follow = followLink ?? DocumentReader.FollowLink;

    public IReadOnlyList<string> Roots { get; } = [.. roots.Select(Path.GetFullPath)];

    public LookupServed Serve(IReadOnlyList<LookupRequest> requests, CancellationToken ct)
    {
        var text = new StringBuilder();
        var served = 0;
        var refused = 0;
        foreach (var request in requests)
        {
            ct.ThrowIfCancellationRequested();
            var (section, ok) = One(request, ct);
            if (Encoding.UTF8.GetByteCount(section) + Encoding.UTF8.GetByteCount(text.ToString()) > _limits.TurnBytes)
            {
                (section, ok) = (NotServed(request, $"this turn's budget of {_limits.TurnBytes / 1024.0:0.#} KB is spent — ask for it again next turn"), false);
            }

            text.Append(section);
            (served, refused) = ok ? (served + 1, refused) : (served, refused + 1);
        }

        return new LookupServed(text.ToString().TrimEnd(), served, refused);
    }

    private (string Section, bool Ok) One(LookupRequest request, CancellationToken ct) =>
        Resolve(request) switch
        {
            Resolution.Refused no => (NotServed(request, no.Why), false),
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

        return Absolute(said) is { } full ? Contained(said, full) : new Resolution.Refused(SeveralRoots(said));
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

        if (DiffExclusions.WhichExcludes(slashed + "/_") is { Length: > 0 } glob)
        {
            return $"'{slashed}' is excluded ({glob}) — dependency and build folders are never shown";
        }

        return CredentialFiles.WhichPattern(slashed) is { Length: > 0 } pattern ? $"'{slashed}' looks like a credential ({pattern})" : string.Empty;
    }

    // ---------- list ----------

    private (string Section, bool Ok) Listed(LookupRequest request, Resolution.Inside at)
    {
        if (!Directory.Exists(at.Path))
        {
            return (NotServed(request, $"there is no folder at '{Slashed(at.Path)}'"), false);
        }

        List<string> entries;
        try
        {
            entries = [.. new DirectoryInfo(at.Path).EnumerateFileSystemInfos()
                .Where(entry => Shown(at.Root, entry))
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Select(Named)
                .Take(_limits.ListEntries + 1)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (NotServed(request, $"'{Slashed(at.Path)}' could not be read: {e.Message}"), false);
        }

        var lines = entries.Take(_limits.ListEntries).ToList();
        if (entries.Count > _limits.ListEntries)
        {
            lines.Add($"(stopped at {_limits.ListEntries} entries — list a narrower folder)");
        }

        return (Section(request, $"in {Slashed(at.Path)}", lines.Count == 0 ? ["(empty)"] : lines), true);
    }

    private static bool Shown(string root, FileSystemInfo entry)
    {
        var relative = Relative(root, entry.FullName) ?? entry.Name;

        return Hidden(relative).Length == 0 && (entry is DirectoryInfo || !DiffExclusions.Excludes(relative.Replace('\\', '/')));
    }

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

    private (string Section, bool Ok) Searched(LookupRequest.Search request, Resolution.Inside at, CancellationToken ct)
    {
        if (!Directory.Exists(at.Path) && !File.Exists(at.Path))
        {
            return (NotServed(request, $"there is no folder or file at '{Slashed(at.Path)}'"), false);
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

        return (Section(request, $"in {Slashed(at.Path)}", Footed(walk)), true);
    }

    private void SearchFolder(string root, string folder, string text, Walk walk, CancellationToken ct)
    {
        var pending = new Stack<string>([folder]);
        while (pending.Count > 0 && !walk.Over())
        {
            ct.ThrowIfCancellationRequested();
            var entries = Entries(pending.Pop(), root, walk);
            foreach (var entry in entries.AsEnumerable().Reverse().OfType<DirectoryInfo>())
            {
                pending.Push(entry.FullName);
            }

            SearchFiles(entries.OfType<FileInfo>(), text, walk);
        }
    }

    /// <summary>A folder's entries a search may walk — never a link — or none, counted as unreadable, when the folder cannot be read.</summary>
    private static List<FileSystemInfo> Entries(string folder, string root, Walk walk)
    {
        try
        {
            return [.. new DirectoryInfo(folder).EnumerateFileSystemInfos()
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

        if (IsBinary(path))
        {
            return;
        }

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
}
