using CoaiMcp.Core.Context;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Context;

/// <summary>What never reaches a reviewer, before any budget is spent on it.</summary>
public static class DiffExclusions
{
    /// <summary>On top of whatever the repository's own <c>.gitignore</c> already excludes —
    /// git never diffs untracked files, so ignored build output is out by construction; these
    /// remove the TRACKED noise: lock files, vendored output, minified artefacts.</summary>
    public static readonly IReadOnlyList<string> Default =
    [
        "**/package-lock.json",
        "**/yarn.lock",
        "**/pnpm-lock.yaml",
        "**/Cargo.lock",
        "**/composer.lock",
        "**/Gemfile.lock",
        "**/*.lockb",
        "**/node_modules/**",
        "**/bin/**",
        "**/obj/**",
        "**/dist/**",
        "**/out/**",
        "**/artifacts/**",
        "**/*.min.js",
        "**/*.min.css",
        "**/*.map",
    ];

    /// <summary>Whether ONE path is what these globs exclude — the same table, asked for a file a reviewer requests by name.</summary>
    /// <remarks>
    /// The diff hands the globs to git as <c>:(exclude,glob)</c> pathspecs; the source resolver (plan
    /// §4.9) has no git call to hand them to, so it asks here — one table, two readers, and a lock
    /// file the diff hides is a lock file no reviewer is served. Matched the way git matches a glob
    /// pathspec: <c>**/</c> is any number of directories or none, <c>/**</c> anything beneath, a single
    /// <c>*</c> stays inside one path component; ordinal, as git is on a case-sensitive tree.
    /// </remarks>
    public static bool Excludes(string path) => WhichExcludes(path).Length > 0;

    /// <summary>The first glob that excludes <paramref name="path"/> — for a refusal that names it — or empty.</summary>
    public static string WhichExcludes(string path) =>
        FirstExcluding(path.Replace('\\', '/'), Matchers.Select(one => (one.Glob, (Func<string, bool>)one.Matcher.IsMatch)));

    /// <summary>The first matcher that excludes <paramref name="slashed"/> — a match that could not finish counts as one.</summary>
    internal static string FirstExcluding(string slashed, IEnumerable<(string Glob, Func<string, bool> Matches)> matchers) =>
        matchers.FirstOrDefault(one => WhenMatchTimesOut(() => one.Matches(slashed))).Glob ?? string.Empty;

    /// <summary>
    /// FAIL CLOSED: a path whose exclusion match could not finish is treated as EXCLUDED.
    /// </summary>
    /// <remarks>
    /// <para>These matchers decide what is withheld from a reviewer — a lock file the diff hides is a
    /// lock file no reviewer is served. Each carries a match ceiling (<see cref="MatchTimeoutMs"/>,
    /// SonarCloud S6444), and a path shaped to reach it must not be served because the matcher gave
    /// up on it: a match that could not finish does not know the path is safe to show, and what
    /// nobody can vouch for is withheld. The refusal then names the glob that timed out.</para>
    /// <para>A method taking a thunk, as <c>Redaction.WhenRedactionTimesOut</c> is, so the branch can be
    /// REACHED by a test: one that had to find an input which actually times out would be asserting a
    /// performance figure, and would stop asserting anything the day the engine got faster.</para>
    /// </remarks>
    internal static bool WhenMatchTimesOut(Func<bool> match)
    {
        try
        {
            return match();
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return true;
        }
    }

    /// <summary>How long one glob may search one path — the ceiling every bounded pattern in this product carries.</summary>
    private const int MatchTimeoutMs = 1000;

    internal static readonly (string Glob, System.Text.RegularExpressions.Regex Matcher)[] Matchers =
    [
        .. Default.Select(glob => (glob, new System.Text.RegularExpressions.Regex(
            GlobToRegex(glob), System.Text.RegularExpressions.RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(MatchTimeoutMs)))),
    ];

    /// <summary>A pathspec glob as an anchored regular expression over a <c>/</c>-separated path.</summary>
    internal static string GlobToRegex(string glob) =>
        "^" + System.Text.RegularExpressions.Regex.Escape(glob)
            .Replace(@"\*\*/", "(?:.*/)?", StringComparison.Ordinal)
            .Replace(@"/\*\*", "(?:/.*)?", StringComparison.Ordinal)
            .Replace(@"\*", "[^/]*", StringComparison.Ordinal)
            .Replace(@"\?", "[^/]", StringComparison.Ordinal) + "$";
}

public sealed class ContextException(string operation, string detail)
    : Exception($"git {operation}: {detail}");

/// <summary>Which commit a round's diff was actually taken against, and why that one.</summary>
public enum DiffBase
{
    /// <summary>The common ancestor of the base and the branch — what the branch itself changed.</summary>
    MergeBase,

    /// <summary>No ancestor exists. The diff is between the two tips, so it can show the base's own
    /// commits as deletions; nothing better is available for two unrelated histories.</summary>
    NoCommonAncestor,

    /// <summary>The history is TRUNCATED, so an ancestor cannot be found although one exists. It looks
    /// identical to unrelated histories at the point of asking and deserves a different sentence,
    /// because this one is a checkout that can be deepened rather than a fact about the commits.</summary>
    ShallowHistory,
}

/// <summary>What a code round is a diff OF: the files, and the commit they were compared against.</summary>
/// <remarks>
/// The commit is carried out rather than left implicit inside git, because a round that names what it
/// compared against is a round whose findings can be re-checked afterwards — and a reviewer told the
/// branch deleted three files it never touched has no way to tell that from a real deletion.
/// </remarks>
public sealed record CollectedDiff(IReadOnlyList<FileDiff> Files, string ComparedAgainst, DiffBase Kind);

/// <summary>
/// How much of a change a reviewer would be shown, known before a round is built.
/// </summary>
/// <param name="Shown">Files that survive <see cref="DiffExclusions"/> — what a reviewer would read.</param>
/// <param name="ChangedButExcluded">
/// When <paramref name="Shown"/> is zero, every path the branch changed anyway (all of them excluded);
/// empty otherwise, and empty when the branch changed nothing.
/// </param>
public sealed record ReviewableChange(int Shown, IReadOnlyList<string> ChangedButExcluded)
{
    public bool IsEmpty => Shown == 0;
}

/// <summary>
/// Produces the per-file diffs the pure <see cref="DiffShaper"/> then budgets. All git, no rules:
/// which files gate, what gets elided, what a reviewer reads — none of that is decided here.
/// </summary>
public sealed class ContextAssembler(IProcessLauncher launcher)
{
    /// <summary>
    /// The checkout a path belongs to, or git's own words for why it belongs to none.
    /// </summary>
    /// <remarks>
    /// A consultant runs in the LIVE tree, so the tool takes any path inside it and resolves the top
    /// level here — before anything is snapshotted, locked or launched. A refusal names git's sentence
    /// rather than ours: "not a git repository" is what the person will type into a search box.
    /// </remarks>
    public async Task<(string TopLevel, string Refusal)> TopLevelAsync(string path, CancellationToken ct = default)
    {
        if (!Directory.Exists(path))
        {
            return (string.Empty, $"'{path}' is not a directory on this machine — repoPath must be a path inside the checkout you are working in");
        }

        var result = await launcher.RunAsync(new ProcessRequest("git", ["rev-parse", "--show-toplevel"], path), ct);
        return result.ExitCode == 0
            ? (Path.GetFullPath(result.StdOut.Trim()), string.Empty)
            : (string.Empty, $"'{path}' is not inside a git checkout ({result.StdErr.Trim()}) — repoPath must be a path inside the repository you are working in");
    }

    /// <summary>
    /// The repository a checkout belongs to (<see cref="RepositoryIdentity"/>) — or, where git gives no
    /// answer, the checkout's own path in the same key form: git's refusal is not an identity, and an
    /// empty one would make every such checkout the same repository.
    /// </summary>
    public async Task<string> CommonDirAsync(string repoPath, CancellationToken ct = default)
    {
        var result = await launcher.RunAsync(new ProcessRequest("git", RepositoryIdentity.CommonDirArgs, repoPath), ct);

        return RepositoryIdentity.Normalised(result.ExitCode == 0 && result.StdOut.Trim().Length > 0
            ? result.StdOut
            : Path.GetFullPath(repoPath));
    }

    /// <summary>The commit and branch the working tree stands on — for the record and the prompt.</summary>
    public async Task<(string Sha, string Branch)> HeadAsync(string repoPath, CancellationToken ct = default)
    {
        var sha = await launcher.RunAsync(new ProcessRequest("git", ["rev-parse", "HEAD"], repoPath), ct);
        var branch = await launcher.RunAsync(new ProcessRequest("git", ["branch", "--show-current"], repoPath), ct);

        return (
            sha.ExitCode == 0 ? sha.StdOut.Trim() : string.Empty,
            branch.ExitCode == 0 && branch.StdOut.Trim().Length > 0 ? branch.StdOut.Trim() : "(detached)");
    }

    /// <summary>
    /// What is uncommitted in the LIVE tree: every change against HEAD, and every untracked file
    /// that is not ignored, as the same <see cref="FileDiff"/> list a review is shaped from.
    /// </summary>
    /// <remarks>
    /// <para>The consultant sees the tree, not the agent's story — so this is collected by the SERVER
    /// and never handed in by the caller. <c>diff HEAD</c> covers staged and unstaged alike; untracked
    /// files come from <c>ls-files --others --exclude-standard</c> and are rendered by the pure
    /// <see cref="UntrackedDiff"/>, which names a binary or an oversized file rather than inlining it.</para>
    /// <para>Nothing here touches the index — no <c>git add -N</c> — because the consultant is
    /// read-only and so must the collection be.</para>
    /// </remarks>
    public async Task<IReadOnlyList<FileDiff>> CollectWorkingTreeAsync(
        string repoPath, IReadOnlyList<string>? exclusions = null, CancellationToken ct = default)
    {
        var excludes = (exclusions ?? DiffExclusions.Default).Select(e => $":(exclude,glob){e}").ToArray();

        var numstat = await Git(repoPath, ct, ["diff", "--numstat", "-z", "HEAD", "--", ".", .. excludes]);
        var changes = NumstatReader.Read(numstat).ToList();
        var perFile = DiffSplitter.ByFile(await TrackedDiffAsync(repoPath, changes, ct));
        var files = new List<FileDiff>(Tracked(repoPath, changes, perFile));

        var untracked = await Git(repoPath, ct, ["ls-files", "--others", "--exclude-standard", "-z", "--", ".", .. excludes]);
        foreach (var path in untracked.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            files.Add(Untracked(repoPath, path));
        }

        return files;
    }

    /// <summary>
    /// The whole text diff in ONE git process, or nothing when every change is binary.
    /// </summary>
    /// <remarks>
    /// One process rather than one per file. A review's collector can afford the per-file loop — it
    /// runs inside a round somebody expects to take minutes — but a consultation blocks an agent that
    /// is already stuck, and on Windows a process start is 30-50 ms: a fifty-file refactor paid two to
    /// three seconds before the consultant was even launched. (gemini Blocking + codex, code round.)
    /// The text is split on git's own <c>diff --git</c> boundaries, which is what the per-file calls
    /// produced anyway.
    /// </remarks>
    private async Task<string> TrackedDiffAsync(string repoPath, IReadOnlyList<NumstatChange> changes, CancellationToken ct) =>
        changes.Any(c => !c.IsBinary)
            ? await Git(repoPath, ct, ["diff", "HEAD", "--", .. changes.Where(c => !c.IsBinary).SelectMany(c => c.Pathspecs)])
            : string.Empty;

    /// <summary>Each tracked change paired with its own piece of that diff — a binary is NAMED, never inlined.</summary>
    private static IEnumerable<FileDiff> Tracked(
        string repoPath, IReadOnlyList<NumstatChange> changes, IReadOnlyDictionary<string, string> perFile) =>
        changes.Select(change => change.IsBinary
            ? new FileDiff(change.Path, string.Empty, IsBinary: true, BinaryBytes: SizeOnDisk(repoPath, change.Path))
            : new FileDiff(change.Path, perFile.TryGetValue(change.Path, out var text) ? text : string.Empty));

    /// <summary>
    /// One untracked file, read only as far as it can be used.
    /// </summary>
    /// <remarks>
    /// The LENGTH is asked first. Reading the whole file and then discarding it against the 16 KB cap
    /// allocates a gigabyte for a stray database dump somebody forgot to ignore — the cap exists to
    /// bound what the consultant SEES, and it has to bound what we read as well. (gemini + codex,
    /// code round.)
    /// </remarks>
    private static FileDiff Untracked(string repoPath, string path)
    {
        var full = Path.Combine(repoPath, path);
        try
        {
            var info = new FileInfo(full);

            // A LINK is named, never followed. `git ls-files --others` lists untracked symlinks, and
            // both `Length` and `ReadAllBytes` resolve them — so a link planted in the checkout puts
            // a file from ANYWHERE on the machine into the prompt this server sends to a third-party
            // vendor. The consultant is promised the working tree; a path whose bytes live outside it
            // is not the working tree, whatever it spells. Named rather than dropped, because a link
            // somebody added is part of what changed. (CodeRabbit, on the pull request, as a
            // path-traversal finding.)
            if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return new FileDiff(path, $"new file (untracked): {path} — a link, not followed\n");
            }

            var length = info.Length;

            return length > UntrackedDiff.InlineCap
                ? UntrackedDiff.TooBig(path, length)
                : UntrackedDiff.For(path, File.ReadAllBytes(full));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Listed a moment ago and unreadable now: named, so the consultant knows it exists.
            return new FileDiff(path, $"new file (untracked): {path} — could not be read ({e.Message})\n");
        }
    }

    private static long SizeOnDisk(string repoPath, string path)
    {
        var full = Path.Combine(repoPath, path);
        return File.Exists(full) ? new FileInfo(full).Length : 0;
    }

    public async Task<CollectedDiff> CollectAsync(
        string repoPath,
        string baseRef,
        string sha,
        IReadOnlyList<string>? exclusions = null,
        CancellationToken ct = default)
    {
        var excludes = (exclusions ?? DiffExclusions.Default).Select(e => $":(exclude,glob){e}").ToArray();
        // The whole of this fix. `{base}..{sha}` diffs two ENDPOINTS, so every commit the base has
        // and the branch does not arrives INVERTED — as a deletion the branch performed. Measured on
        // 2026-09-08: 17 files and 1616 deletions sent where the branch's own change was 9 and 12,
        // and two vendors independently filed it as Blocking. Resolved once, and used for all three
        // git calls below — `cat-file` takes a rev rather than a range, so a three-dot form could not
        // have reached it.
        var (against, kind) = await ComparisonBase(repoPath, baseRef, sha, ct);

        // --numstat: "added deleted path", binaries as "- - path". One call decides binary-ness
        // and file order; each text file then rides its own diff so elision stays whole-file.
        //
        // `-z` is not a detail. Without it the third column is not a PATH: a rename arrives as
        // `src/{old.cs => new.cs}` and a non-ASCII name C-quoted with escapes, and handing either
        // back to git as a pathspec matches nothing — so the file reached the reviewer named, with an
        // empty diff under it and no error anywhere. See NumstatReader, which is where the shape and
        // the measurement are written down.
        var numstat = await Git(repoPath, ct, ["diff", "--numstat", "-z", $"{against}..{sha}", "--", ".", .. excludes]);

        var files = new List<FileDiff>();
        foreach (var change in NumstatReader.Read(numstat))
        {
            files.Add(change.IsBinary
                ? new FileDiff(
                    change.Path,
                    string.Empty,
                    IsBinary: true,
                    BinaryBytes: await BlobSize(repoPath, sha, against, change.Path, ct))
                // BOTH names when it moved, so git prints the rename rather than an add beside a
                // delete — the pathspecs are the change's own, not a path this method reassembles.
                : new FileDiff(
                    change.Path,
                    await Git(repoPath, ct, ["diff", $"{against}..{sha}", "--", .. change.Pathspecs])));
        }

        return new CollectedDiff(files, against, kind);
    }

    /// <summary>
    /// Whether a round over <paramref name="sha"/> would have anything to show a reviewer — asked
    /// BEFORE any worktree or launch, from two numstats and nothing else.
    /// </summary>
    /// <returns>
    /// How many files a reviewer would see, and — only when that is none — every path the branch
    /// changed at all, so a refusal can name the lock files it was made of. An unresolvable
    /// <paramref name="baseRef"/> throws <see cref="ContextException"/> naming it, exactly as
    /// <see cref="CollectAsync"/> would: that is a wrong argument, never an empty change.
    /// </returns>
    public async Task<ReviewableChange> ReviewableAsync(
        string repoPath,
        string baseRef,
        string sha,
        IReadOnlyList<string>? exclusions = null,
        CancellationToken ct = default)
    {
        var excludes = (exclusions ?? DiffExclusions.Default).Select(e => $":(exclude,glob){e}").ToArray();
        var (against, _) = await ComparisonBase(repoPath, baseRef, sha, ct);

        var shown = NumstatReader.Read(
            await Git(repoPath, ct, ["diff", "--numstat", "-z", $"{against}..{sha}", "--", ".", .. excludes])).Count();
        if (shown > 0)
        {
            return new ReviewableChange(shown, []);
        }

        var all = NumstatReader.Read(await Git(repoPath, ct, ["diff", "--numstat", "-z", $"{against}..{sha}"]));

        return new ReviewableChange(0, [.. all.Select(c => c.Path)]);
    }

    /// <summary>
    /// What is uncommitted in the checkout at <paramref name="repoPath"/> — but only when that
    /// checkout stands on <paramref name="sha"/>, the commit being reviewed.
    /// </summary>
    /// <remarks>
    /// A tree on another commit says nothing about this one, and naming its files would be a wrong
    /// sentence; the answer is then empty rather than guessed (plan round, codex). Tracked changes
    /// against HEAD and untracked files that are not ignored — the same two sources the consultant
    /// reads, and like it this never touches the index.
    /// </remarks>
    public async Task<Uncommitted> UncommittedAsync(string repoPath, string sha, CancellationToken ct = default)
    {
        var (head, _) = await HeadAsync(repoPath, ct);
        if (!head.Equals(sha, StringComparison.OrdinalIgnoreCase))
        {
            return Uncommitted.None;
        }

        var tracked = await launcher.RunAsync(
            new ProcessRequest("git", ["diff", "--name-only", "-z", "HEAD"], repoPath), ct);
        var untracked = await launcher.RunAsync(
            new ProcessRequest("git", ["ls-files", "--others", "--exclude-standard", "-z"], repoPath), ct);

        // A call that failed is an UNREAD checkout, never a clean one — the two used to be the same
        // empty list, and a round over committed work then passed without naming what it had not seen.
        return tracked.ExitCode != 0 || untracked.ExitCode != 0
            ? new Uncommitted([], Reason(tracked.ExitCode != 0 ? tracked : untracked))
            : new Uncommitted([.. Paths(tracked).Concat(Paths(untracked)).Distinct(StringComparer.Ordinal)], string.Empty);

        static IEnumerable<string> Paths(ProcessResult result) =>
            result.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        static string Reason(ProcessResult failed) =>
            failed.StdErr.Trim() is { Length: > 0 } said ? said : $"git exited {failed.ExitCode}";
    }

    /// <summary>An object id and nothing else — never a ref, never anything git could read as a flag.</summary>
    /// <remarks>
    /// Every value that reaches a `{x}..{sha}` range or a `{x}:{path}` argument passes through here.
    /// A revision beginning with `-` is a command-line OPTION to git, not a revision, and
    /// `--output=…` is one that writes a file. The values are hex ids by construction — `merge-base`
    /// and `rev-parse` both answer with one — so this asserts what is already true rather than
    /// repairing anything, which is the only kind of check worth having on a path like this.
    /// </remarks>
    private static bool IsCommit(string? value) =>
        value is { Length: >= 7 and <= 64 } && value.All(Uri.IsHexDigit);

    /// <summary>The COMMIT to compare against, and what kind of answer it is.</summary>
    /// <remarks>
    /// <para>A failure here is never fatal: a review taken against the wrong base is worth more than
    /// no review, so the two-dot form remains as the fallback — named, so the round says which one it
    /// used rather than leaving somebody to wonder at the deletions.</para>
    /// <para>But the fallback resolves the ref to a commit before using it, and that is not tidiness.
    /// `origin/main` is read three times — the numstat, each file's diff, and a binary's old side —
    /// and another session advancing it between two of them produces a review of two different
    /// snapshots, which nobody could reproduce afterwards from a log naming only the ref. Pinning it
    /// is the same lesson as the one this whole change is about. (codex, the code round, three times.)</para>
    /// <para>Public since the feature review (plan §4.7): its outline builder compares <c>base..head</c>
    /// against the same commit a code round would, through this one road rather than a second
    /// merge-base of its own.</para>
    /// </remarks>
    /// <param name="timeout">
    /// The deadline each process here runs under. Absent, the launcher's own default — what every code
    /// round has always run it with; the feature outline builder passes its 60 s, so no git process of
    /// that build stands outside the deadline it promises.
    /// </param>
    public async Task<(string Against, DiffBase Kind)> ComparisonBase(
        string repoPath, string baseRef, string sha, CancellationToken ct, TimeSpan? timeout = null)
    {
        var found = await launcher.RunAsync(
            Bounded(new ProcessRequest("git", ["merge-base", baseRef, sha], repoPath), timeout), ct);
        // One commit per LINE where a criss-cross history has several, and the first is the one git
        // itself would pick. Interpolating two of them makes a range out of two commits and a
        // newline, which is not a range at all. (gemini, the plan round.)
        var first = found.ExitCode == 0
            ? found.StdOut
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault()
            : null;
        if (IsCommit(first))
        {
            return (first!, DiffBase.MergeBase);
        }

        // A truncated checkout looks exactly like unrelated history from here, and the two want
        // different sentences: one is a fact about the commits, the other is a clone somebody can
        // deepen. Asked only on the path that already failed, so an ordinary round pays nothing.
        var shallow = await launcher.RunAsync(
            Bounded(new ProcessRequest("git", ["rev-parse", "--is-shallow-repository"], repoPath), timeout), ct);
        var truncated = shallow.ExitCode == 0
            && shallow.StdOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        var kind = truncated ? DiffBase.ShallowHistory : DiffBase.NoCommonAncestor;

        var pinned = await launcher.RunAsync(
            Bounded(new ProcessRequest("git", ["rev-parse", $"{baseRef}^{{commit}}"], repoPath), timeout), ct);
        var commit = pinned.ExitCode == 0 ? pinned.StdOut.Trim() : string.Empty;

        return IsCommit(commit)
            ? (commit, kind)
            // Nothing names a commit here, so there is nothing to diff and nothing to pin. Refused
            // rather than handed to git: a "ref" that begins with a dash is an option, and this is
            // the one place a caller's own string would have reached a command line.
            : throw new ContextException("rev-parse", $"{baseRef} names no commit in this repository");
    }

    /// <summary>The request under the caller's deadline when it named one; under the launcher's own otherwise.</summary>
    private static ProcessRequest Bounded(ProcessRequest request, TimeSpan? timeout) =>
        timeout is { } deadline ? request with { Timeout = deadline } : request;

    private async Task<long> BlobSize(string repoPath, string sha, string baseRef, string path, CancellationToken ct)
    {
        foreach (var rev in (string[])[sha, baseRef])
        {
            var result = await launcher.RunAsync(
                new ProcessRequest("git", ["cat-file", "-s", $"{rev}:{path}"], repoPath), ct);
            if (result.ExitCode == 0 && long.TryParse(result.StdOut.Trim(), out var size))
            {
                return size;
            }
        }

        return 0; // deleted on both sides of a rename chain — the name still reaches the reviewer
    }

    private async Task<string> Git(string repoPath, CancellationToken ct, string[] args)
    {
        var result = await launcher.RunAsync(new ProcessRequest("git", args, repoPath), ct);
        return result.ExitCode == 0
            ? result.StdOut
            : throw new ContextException(args[0], result.StdErr.Trim());
    }
}
