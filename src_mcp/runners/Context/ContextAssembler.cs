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
/// Produces the per-file diffs the pure <see cref="DiffShaper"/> then budgets. All git, no rules:
/// which files gate, what gets elided, what a reviewer reads — none of that is decided here.
/// </summary>
public sealed class ContextAssembler(IProcessLauncher launcher)
{
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
    /// </remarks>
    private async Task<(string Against, DiffBase Kind)> ComparisonBase(
        string repoPath, string baseRef, string sha, CancellationToken ct)
    {
        var found = await launcher.RunAsync(
            new ProcessRequest("git", ["merge-base", baseRef, sha], repoPath), ct);
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
            new ProcessRequest("git", ["rev-parse", "--is-shallow-repository"], repoPath), ct);
        var truncated = shallow.ExitCode == 0
            && shallow.StdOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        var kind = truncated ? DiffBase.ShallowHistory : DiffBase.NoCommonAncestor;

        var pinned = await launcher.RunAsync(
            new ProcessRequest("git", ["rev-parse", $"{baseRef}^{{commit}}"], repoPath), ct);
        var commit = pinned.ExitCode == 0 ? pinned.StdOut.Trim() : string.Empty;

        return IsCommit(commit)
            ? (commit, kind)
            // Nothing names a commit here, so there is nothing to diff and nothing to pin. Refused
            // rather than handed to git: a "ref" that begins with a dash is an option, and this is
            // the one place a caller's own string would have reached a command line.
            : throw new ContextException("rev-parse", $"{baseRef} names no commit in this repository");
    }

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
