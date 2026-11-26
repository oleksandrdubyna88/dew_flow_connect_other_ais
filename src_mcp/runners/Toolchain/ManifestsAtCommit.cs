using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Collecting;

namespace CoaiMcp.Runners.Toolchain;

/// <summary>
/// The manifests of ONE commit, read from the objects of the MAIN repository (epoch 3 E4 s2 t2; SPEC §A1, §A3): one listing of the tree (<see cref="GitHistory.TreeAtAsync"/>), the files of <see cref="ToolchainDefaults"/> selected from it by <see cref="ManifestSearch"/>, and their texts read by
/// object id. <b>Nothing of a working tree is read</b> — not the coder's worktree, not the main checkout's files: the commit is the whole input, so an untracked or ignored file changes nothing and the same commit gives the same answer.
/// </summary>
/// <remarks>
/// <para><b>It is a security boundary.</b> What a repository's manifests say decides which commands coai runs on a coder's work, and the head commit is the coder's. So: the main repository's path is the only one asked (never the worktree's <c>.git</c> pointer, which a coder can rewrite); a link is a
/// refusal and is never read; a submodule is never entered and is named; every limit is a refusal with the number and the limit, never a cut list; a git that does not answer, and a commit that is not there, are refusals, never an empty set — an empty set is "this commit has no manifest", which is an answer that
/// leads to <c>not_configured</c>, and must never be what a failure looks like.</para>
/// </remarks>
public sealed class ManifestsAtCommit(GitHistory git)
{
    /// <summary>The listing's ceiling in characters: the stream holds characters, a listing is ASCII except in paths, so 32 MiB of bytes is at most that many characters.</summary>
    private const int ListingChars = (int)ManifestLimits.MaxListingBytes + 1;

    /// <summary>Reads the manifests of <paramref name="sha"/> from the repository at <paramref name="repoPath"/> (the main checkout, a real repository path).</summary>
    public async Task<ManifestRead> ReadAsync(string repoPath, string sha, ToolchainDefaults table, CancellationToken ct = default)
    {
        if (!GitHistory.IsCommitish(sha))
        {
            return new ManifestRead.Refused($"{PathSpelling.Spell(ShortOf(sha))} is not a commit id (forty hex characters), so no manifest was looked for");
        }

        var has = await git.HasCommitAsync(repoPath, sha, ct);
        if (!has.Ran)
        {
            return new ManifestRead.Refused($"git did not answer whether commit {sha} exists, so no manifest was looked for");
        }

        if (!has.Ok)
        {
            return new ManifestRead.Refused($"commit {sha} is not in the repository, so no manifest was looked for");
        }

        var tree = await git.TreeAtAsync(repoPath, sha, ListingChars, ct);
        if (!tree.Ran)
        {
            return new ManifestRead.Refused($"git did not answer the listing of commit {sha}{(tree.Refusal.Length > 0 ? ": " + tree.Refusal : string.Empty)}, so no manifest was looked for");
        }

        if (!tree.Ok)
        {
            return new ManifestRead.Refused($"git could not list the tree of commit {sha}, so no manifest was looked for");
        }

        if (tree.Truncated || tree.Out.Length > ManifestLimits.MaxListingBytes)
        {
            return new ManifestRead.Refused($"the listing of the tree of commit {sha} is over the limit of {ManifestLimits.MaxListingBytes} bytes; name the commands in {ToolchainTables.FilePath}");
        }

        var selection = ManifestSearch.Select(TreeEntries.Parse(tree.Out), table);
        if (selection is ManifestSelection.Refused refused)
        {
            return new ManifestRead.Refused(refused.Sentence);
        }

        var selected = (ManifestSelection.Selected)selection;
        var files = new List<ManifestFile>();
        foreach (var candidate in selected.Candidates)
        {
            if (candidate.Kind == ManifestKind.Present)
            {
                files.Add(new ManifestFile(candidate.Path, candidate.Stack, candidate.Kind, candidate.Oid, candidate.Size, string.Empty));

                continue;
            }

            var blob = await git.BlobAsync(repoPath, candidate.Oid, ct);
            if (!blob.Ran || !blob.Ok)
            {
                return new ManifestRead.Refused($"git could not read {PathSpelling.Spell(candidate.Path, candidate.Oid)} of commit {sha}, so no manifest was decided");
            }

            if (blob.Truncated)
            {
                return new ManifestRead.Refused($"{PathSpelling.Spell(candidate.Path, candidate.Oid)} is larger than the reader's ceiling, so no manifest was decided; name the commands in {ToolchainTables.FilePath}");
            }

            files.Add(new ManifestFile(candidate.Path, candidate.Stack, candidate.Kind, candidate.Oid, candidate.Size, blob.Out));
        }

        return new ManifestRead.Found(new ManifestSet(sha, files, selected.Notes));
    }

    private static string ShortOf(string text) => text.Length > 40 ? text[..40] + "..." : text;
}
