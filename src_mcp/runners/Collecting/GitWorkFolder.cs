using System.Globalization;
using System.Text;
using CoaiMcp.Core;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Work;

namespace CoaiMcp.Runners.Collecting;

/// <summary>A folder opened, or the sentence that says why not.</summary>
public abstract record FolderOpen
{
    private protected FolderOpen()
    {
    }

    public sealed record Opened(GitWorkFolder Folder) : FolderOpen;

    public sealed record Refused(string Sentence) : FolderOpen;
}

/// <summary>
/// The work-plan folder of a COMMIT as an <see cref="IWorkFolder"/> (epoch 3 E3 s3): the plan a round reviews is what git holds at the branch tip, never what the working tree says.
/// </summary>
/// <remarks>
/// <para><b>One listing, then the blobs in batches.</b> <see cref="OpenAsync"/> asks git for every entry under the folder once (<c>ls-tree -r -l -z</c>), refuses a folder of more than <see cref="MaxEntries"/> entries before any blob is
/// asked for, and then reads the <c>.yaml</c> files the validator reads — each one at most <see cref="MaxFileBytes"/> bytes, by the size the LISTING gave, so a larger file is <see cref="WorkFile.TooLarge"/> and is never fetched. Other files
/// the pack needs (the <c>plan</c> and <c>spec</c> files) are asked for by name with <see cref="PreloadAsync"/>.</para>
/// <para><b>Every blob is proved against its id</b> — the SHA-1 of <c>blob &lt;size&gt;\0</c> and the bytes git sent, as the committed-plan reader proves a plan (<see cref="BlobId"/>) — so a capture that is cut, altered or misaligned is refused
/// and names the file, never read as text. A link and a submodule are not followed: a link is neither a folder nor a file, a submodule is an empty folder (what a checkout holds), and nothing inside either is read.</para>
/// <para>The read is a <see cref="WorkFile"/> from memory; the folder never touches the working tree.</para>
/// </remarks>
public sealed class GitWorkFolder : IListedWorkFolder
{
    /// <summary>The most entries (files, links, submodules) a plan folder may hold; more is a refusal.</summary>
    public const int MaxEntries = 2000;

    /// <summary>The largest file read — the YAML cap of the validator.</summary>
    public const int MaxFileBytes = StrictYamlReader.MaxBytes;

    /// <summary>The most bytes of files read for one folder, a bound on memory for the listing's worst case (2 000 files of 64 KiB).</summary>
    public const long MaxReadBytes = 16 * 1024 * 1024;

    private const long BatchBytes = 4 * 1024 * 1024;

    private const int ListingChars = 2 * 1024 * 1024;

    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly GitHistory _git;
    private readonly string _repoPath;
    private readonly Dictionary<string, WorkEntry> _files;
    private readonly Dictionary<string, SortedSet<string>> _folders;
    private readonly Dictionary<string, WorkFile> _loaded = new(StringComparer.Ordinal);

    private GitWorkFolder(GitHistory git, string repoPath, string commit, string folder, IReadOnlyList<TreeEntry> entries)
    {
        (_git, _repoPath, Commit, Folder) = (git, repoPath, commit, folder);
        var prefix = folder + "/";
        var relative = entries.Select(entry => (Entry: entry, Path: entry.Path[prefix.Length..])).ToList();
        _files = relative.Where(item => IsRegular(item.Entry)).ToDictionary(item => item.Path, item => new WorkEntry(item.Path, item.Entry.Oid, item.Entry.Size), StringComparer.Ordinal);
        _folders = Children(relative.Select(item => (item.Path, Directory: item.Entry.Mode == "160000")));
        Files = [.. _files.Values.OrderBy(file => file.Path, StringComparer.Ordinal)];
    }

    /// <summary>The commit the folder is read at.</summary>
    public string Commit { get; }

    /// <summary>The folder's own path from the repository root, as it was opened.</summary>
    public string Folder { get; }

    public IReadOnlyList<WorkEntry> Files { get; }

    /// <summary>The folder at <paramref name="folder"/> (repository-relative) of <paramref name="sha"/>, or why it cannot be read.</summary>
    public static async Task<FolderOpen> OpenAsync(GitHistory git, string repoPath, string sha, string folder, CancellationToken ct)
    {
        if (!GitHistory.IsCommitish(sha))
        {
            return Refuse($"commit {sha} is {sha.Length} hex characters — a feature folder is read at a SHA-1 commit (40 hex)");
        }

        if (RepoPaths.WhyNotRelative(folder) is { Length: > 0 } why)
        {
            return Refuse($"'{folder}' {why} — pass the feature folder as a path from the repository root, such as v2");
        }

        var answer = await git.TreeUnderAsync(repoPath, sha, folder, ListingChars, ct);
        var gate = answer.Truncated && answer is { Ran: true, Ok: true } ? string.Empty : CommittedPlanReader.CaptureGate(answer, $"listing '{folder}' at commit {GitIds.Short(sha)}");
        if (gate.Length > 0)
        {
            return Refuse(gate);
        }

        return answer.Truncated ? Refuse(TooMany(folder, sha, null)) : await ListedAsync(git, repoPath, sha, folder, TreeEntries.Parse(answer.Out), ct);
    }

    private static async Task<FolderOpen> ListedAsync(GitHistory git, string repoPath, string sha, string folder, TreeListing listing, CancellationToken ct)
    {
        if (Shape(listing, folder, sha) is { Length: > 0 } wrong)
        {
            return Refuse(wrong);
        }

        var opened = new GitWorkFolder(git, repoPath, sha, folder, listing.Entries);
        var missed = await opened.PreloadAsync(opened.Files.Where(file => file.Path.EndsWith(".yaml", StringComparison.Ordinal)).Select(file => file.Path), ct);

        return missed.Length > 0 ? Refuse(missed) : new FolderOpen.Opened(opened);
    }

    /// <summary>The refusal for a listing that is not a readable folder, or empty: a malformed record, nothing there, the folder itself a file or a link or a submodule, or too many entries.</summary>
    private static string Shape(TreeListing listing, string folder, string sha)
    {
        var sha12 = GitIds.Short(sha);
        if (listing.Malformed.Length > 0)
        {
            return $"git ls-tree answered a record this reader cannot parse for '{folder}' at commit {sha12} ('{listing.Malformed}') — re-run, and if it repeats report it with the git version";
        }

        if (listing.Entries.Count == 0)
        {
            return $"'{folder}' is not in commit {sha12} — commit the plan folder on this branch first, or pass the path exactly as committed (case and separators count)";
        }

        if (listing.Entries.Count > MaxEntries)
        {
            return TooMany(folder, sha, listing.Entries.Count);
        }

        return listing.Entries.Any(entry => entry.Path == folder) || listing.Entries.Any(entry => !entry.Path.StartsWith(folder + "/", StringComparison.Ordinal))
            ? $"'{folder}' is not a folder at commit {sha12} (it is a file, a link or a submodule, or git listed another path) — pass the folder that holds feature.yaml"
            : string.Empty;
    }

    private static string TooMany(string folder, string sha, int? count) =>
        $"'{folder}' holds {(count is { } n ? n.ToString(CultureInfo.InvariantCulture) : "more than " + MaxEntries.ToString(CultureInfo.InvariantCulture))} entries at commit {GitIds.Short(sha)}, over the {MaxEntries.ToString(CultureInfo.InvariantCulture)} a plan folder may hold "
        + "— a work plan is small; move what is not the plan out of the feature folder. Nothing was read.";

    /// <summary>
    /// Reads these files (paths from the folder) so <see cref="Read"/> can answer them: only regular files of at most <see cref="MaxFileBytes"/> bytes, once each. Answers the refusal sentence, or empty when all were read and proved.
    /// </summary>
    public async Task<string> PreloadAsync(IEnumerable<string> relatives, CancellationToken ct)
    {
        var wanted = relatives.Distinct(StringComparer.Ordinal).Where(path => _files.TryGetValue(path, out var file) && file.Size <= MaxFileBytes && !_loaded.ContainsKey(path)).Select(path => _files[path]).ToList();
        if (wanted.Sum(file => file.Size) is var total && total > MaxReadBytes)
        {
            return $"the files to read under '{Folder}' add up to {total.ToString(CultureInfo.InvariantCulture)} bytes, over the {MaxReadBytes.ToString(CultureInfo.InvariantCulture)} read for one folder — the plan folder is too large; nothing was read";
        }

        foreach (var batch in Batches(wanted))
        {
            if (await ReadBatchAsync(batch, ct) is { Length: > 0 } refusal)
            {
                return refusal;
            }
        }

        return string.Empty;
    }

    private static List<List<WorkEntry>> Batches(IReadOnlyList<WorkEntry> wanted)
    {
        var batches = new List<List<WorkEntry>>();
        long size = BatchBytes;
        foreach (var file in wanted)
        {
            if (size + file.Size > BatchBytes)
            {
                batches.Add([]);
                size = 0;
            }

            batches[^1].Add(file);
            size += file.Size;
        }

        return batches;
    }

    private async Task<string> ReadBatchAsync(List<WorkEntry> batch, CancellationToken ct)
    {
        var oids = batch.Select(file => file.Blob).Distinct(StringComparer.Ordinal).ToList();
        var answer = await _git.BlobsAsync(_repoPath, oids, (int)(BatchBytes + (oids.Count * 128L)), ct);
        var gate = CommittedPlanReader.CaptureGate(answer, $"reading {oids.Count} file(s) of '{Folder}' at commit {GitIds.Short(Commit)}");
        if (gate.Length > 0)
        {
            return gate;
        }

        var proved = Proved(Encoding.UTF8.GetBytes(answer.Out), oids, batch);

        return proved.Refusal.Length > 0 ? proved.Refusal : Stored(batch, proved.Files);
    }

    private string Stored(List<WorkEntry> batch, Dictionary<string, WorkFile> byOid)
    {
        foreach (var file in batch)
        {
            _loaded[file.Path] = byOid[file.Blob];
        }

        return string.Empty;
    }

    /// <summary>Walks the batch answer record by record; each object is cut by its declared size and proved against its id before it is decoded.</summary>
    private (Dictionary<string, WorkFile> Files, string Refusal) Proved(byte[] bytes, List<string> oids, List<WorkEntry> batch)
    {
        var byOid = new Dictionary<string, WorkFile>(StringComparer.Ordinal);
        var at = 0;
        foreach (var oid in oids)
        {
            var path = batch.First(file => file.Blob == oid).Path;
            var record = Record(bytes, ref at, oid);
            if (record is null)
            {
                return ([], $"git cat-file --batch answered something this reader cannot parse for '{Folder}/{path}' (blob {GitIds.Short(oid)}) — nothing was reviewed; re-run, and if it repeats report it with the git version");
            }

            if (BlobId.Sha1Of(record) != oid)
            {
                return ([], $"the capture of '{Folder}/{path}' (blob {GitIds.Short(oid)}) differs from the blob — nothing was reviewed. If the file is not UTF-8 save it as UTF-8 and commit it; otherwise re-run, and if it repeats report it");
            }

            byOid[oid] = Decoded(record);
        }

        return (byOid, string.Empty);
    }

    /// <summary>One record: <c>&lt;oid&gt; blob &lt;size&gt;\n</c>, the bytes and a newline. Null when the head is not that or the bytes run past the end.</summary>
    private static byte[]? Record(byte[] bytes, ref int at, string oid)
    {
        var end = Array.IndexOf(bytes, (byte)'\n', at);
        if (end < 0 || end - at > 128)
        {
            return null;
        }

        var head = Encoding.ASCII.GetString(bytes, at, end - at).Split(' ');
        if (head is not [var id, "blob", var text] || id != oid || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var size) || end + 1 + size > bytes.Length)
        {
            return null;
        }

        var body = bytes.AsSpan(end + 1, size).ToArray();
        at = end + 1 + size + 1;

        return body;
    }

    private static WorkFile Decoded(byte[] body)
    {
        try
        {
            return new WorkFile.Text(Strict.GetString(body));
        }
        catch (DecoderFallbackException)
        {
            return new WorkFile.NotUtf8();
        }
    }

    public IReadOnlyList<string> Folders(string relative) =>
        _folders.TryGetValue(relative.TrimEnd('/'), out var names) ? [.. names] : [];

    /// <summary>The file at <paramref name="relative"/>. A link, a submodule and a path that is not there are <see cref="WorkFile.Missing"/>; a file over <paramref name="maxBytes"/> is <see cref="WorkFile.TooLarge"/> by the listing's size, unread.</summary>
    /// <exception cref="InvalidOperationException">The file is in the commit and within the size, but was never preloaded — a caller's mistake, not a state of the repository.</exception>
    public WorkFile Read(string relative, int maxBytes)
    {
        if (!_files.TryGetValue(relative, out var file))
        {
            return new WorkFile.Missing();
        }

        if (file.Size > maxBytes)
        {
            return new WorkFile.TooLarge(file.Size);
        }

        return _loaded.TryGetValue(relative, out var read) ? read : throw new InvalidOperationException($"'{relative}' was not preloaded: ask for it with PreloadAsync before it is read");
    }

    private static bool IsRegular(TreeEntry entry) => entry.Type == "blob" && entry.Mode is "100644" or "100755";

    /// <summary>The folders of a listing: every directory on the way to a path, and a submodule (an empty folder in a checkout). A directory is known by its path from the feature folder; the folder itself is the empty path.</summary>
    private static Dictionary<string, SortedSet<string>> Children(IEnumerable<(string Path, bool Directory)> entries)
    {
        var children = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var (path, directory) in entries)
        {
            var parts = path.Split('/');
            for (var depth = 0; depth < parts.Length - (directory ? 0 : 1); depth++)
            {
                var parent = string.Join('/', parts.Take(depth));
                if (!children.TryGetValue(parent, out var names))
                {
                    children[parent] = names = new SortedSet<string>(StringComparer.Ordinal);
                }

                names.Add(parts[depth]);
            }
        }

        return children;
    }

    private static FolderOpen Refuse(string sentence) => new FolderOpen.Refused(sentence);
}
