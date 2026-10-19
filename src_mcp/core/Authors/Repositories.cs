namespace CoaiMcp.Core.Authors;

/// <summary>
/// Which repository a path is in — the ONE containment rule, moved here from <c>AgentLog</c> and not copied (E4.S1; reuse-first move 2).
/// </summary>
/// <remarks>
/// <para>Nothing here reads the disk or a working directory, so nothing is resolved: a relative path, or one with a <c>.</c> or
/// <c>..</c> segment, is in NO repository — which costs evidence and never produces a wrong attribution. No translation between a
/// Windows and a WSL spelling is guessed; the caller passes the universe in the spelling the transcript uses.</para>
/// </remarks>
public static class Repositories
{
    /// <summary>Backslashes folded to <c>/</c>, trailing separators dropped.</summary>
    public static string Normalise(string path) => path.Replace('\\', '/').TrimEnd('/');

    /// <summary>Whether two paths name the same root: equal under <see cref="Normalise"/> and <c>OrdinalIgnoreCase</c>.</summary>
    public static bool SameRoot(string left, string right) =>
        Normalise(left).Equals(Normalise(right), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether <paramref name="path"/> is <paramref name="root"/> or under it. The separator is part of the test, so <c>/repo-two</c>
    /// is not inside <c>/repo</c>; case is ignored, ordinally. Either side empty is false.
    /// </summary>
    public static bool Contains(string root, string path)
    {
        if (root.Length == 0 || path.Length == 0)
        {
            return false;
        }

        var (where, repo) = (Normalise(path), Normalise(root));

        return where.Equals(repo, StringComparison.OrdinalIgnoreCase)
            || where.StartsWith(repo + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The LONGEST root of <paramref name="universe"/> that equals or contains <paramref name="path"/>, or empty. Longest wins, so a
    /// worktree nested in another checkout is the inner one. Two roots equal under <see cref="Normalise"/> and
    /// <c>OrdinalIgnoreCase</c> are ONE root, and the first in ordinal order is returned, so a tie never depends on input order.
    /// </summary>
    public static string RepositoryOf(string path, IReadOnlyList<string> universe) =>
        IsPlace(path)
            ? universe.Where(IsPlace).Where(root => Contains(root, path)).OrderByDescending(root => Normalise(root).Length).ThenBy(root => root, StringComparer.Ordinal).FirstOrDefault() ?? string.Empty
            : string.Empty;

    /// <summary>Absolute, and free of dot segments: the only paths membership is decided for.</summary>
    private static bool IsPlace(string path) => IsAbsolute(Normalise(path)) && !HasDotSegment(Normalise(path));

    private static bool IsAbsolute(string normalised) => normalised.StartsWith('/') || IsDrivePath(normalised);

    private static bool IsDrivePath(string normalised) =>
        normalised.Length >= 2 && char.IsAsciiLetter(normalised[0]) && normalised[1] == ':' && IsDriveRoot(normalised);

    /// <summary>A drive letter and colon end the path or are followed by a separator: <c>C:</c> and <c>C:/x</c>, never <c>C:x</c>.</summary>
    private static bool IsDriveRoot(string normalised) => normalised.Length == 2 || normalised[2] == '/';

    private static bool HasDotSegment(string normalised) => normalised.Split('/').Any(segment => segment is "." or "..");
}
