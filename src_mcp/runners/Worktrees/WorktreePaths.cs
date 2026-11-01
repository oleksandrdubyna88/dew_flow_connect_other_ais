using CoaiMcp.Runners.Files;

namespace CoaiMcp.Runners.Worktrees;

/// <summary>
/// A worktree path the way git records one: absolute, and with every link on the way resolved.
/// </summary>
/// <remarks>
/// <para><c>git worktree add</c> stores the REAL path, so <c>git worktree list</c> answers
/// <c>/private/var/folders/…</c> for a tree made under <c>/var/folders/…</c> — which is every macOS temp
/// directory — and the target of a junction on Windows. <see cref="Path.GetFullPath(string)"/> only
/// normalises spelling and cannot see a link, so comparing with it never found our own tree behind
/// one: a live tree listed as unregistered, and a deleted one could not be made again. Found by the
/// <c>mcp-v0.31.0</c> release build on <c>osx-arm64</c>.</para>
/// <para>A path that does not exist yet — a tree whose directory was deleted — resolves as far as it
/// exists, with the rest appended: that is exactly the part git resolved when the tree was made.</para>
/// <para><b>Resolved by <see cref="RealPath"/>, root-down</b> (2026-11-01). This used to resolve each component by
/// <c>ResolveLinkTarget(...).FullName</c> — the directories inside a link's target were never resolved and a <c>..</c> in link
/// text was folded as a string, so <c>dd → root/l3/../x</c> with <c>l3</c> a link to <c>outside/deep</c> compared as
/// <c>root/x</c> where git says <c>outside/x</c>; and a loop's <see cref="IOException"/> was swallowed into "the spelling is all
/// there is to compare". A path that cannot be resolved is now REPORTED: <see cref="Real"/> throws <see cref="IOException"/>, and
/// <see cref="Same"/> — a predicate, which answers for every input — says <c>false</c> for it, because a place this process
/// cannot resolve is not a place it can say it is looking at.</para>
/// <para><b>Not only worktrees: every place a path git ANSWERED meets a path somebody SPELLED.</b>
/// <c>git rev-parse --show-toplevel</c> resolves links the same way, so the feature stage's top-level
/// check (<c>FeatureRefs</c>) and the gate history's repository match (<c>GateHistoryRules</c>, where a
/// consultation records git's answer and a review is asked with the caller's spelling) compare through
/// here too. The pull request for epic 2 of the feature review found the first on the macOS job — 23
/// tests refused a repository as being inside itself — and the second by sweeping for the same shape.</para>
/// </remarks>
public static class WorktreePaths
{
    /// <summary>Whether two paths name the same place once links are resolved.</summary>
    /// <remarks>
    /// A predicate answers for every input it accepts: a path this machine cannot resolve at all — a NUL
    /// in it, a loop of links, a link chain over the budget, a record written by a server on another operating
    /// system — is not the place being asked about, so it answers <c>false</c> ("cannot compare") rather than
    /// throwing at a caller that asked a yes-or-no question. It is never <c>true</c> for two spellings it could not resolve.
    /// </remarks>
    public static bool Same(string one, string other)
    {
        var first = RealPath.Resolve(one);
        var second = first.Ok ? RealPath.Resolve(other) : first;

        return first.Ok && second.Ok
            && string.Equals(Key(first.Path), Key(second.Path), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The path with every link on it resolved, root-down — what <c>realpath</c> answers.</summary>
    /// <exception cref="IOException">The path cannot be resolved (a loop, too many links, a NUL, a <c>..</c> after a missing name); the message says which.</exception>
    public static string Real(string path)
    {
        var real = RealPath.Resolve(path);

        return real.Ok ? real.Path : throw new IOException(real.Why);
    }

    private static string Key(string path) => path.Replace('\\', '/').TrimEnd('/');
}
