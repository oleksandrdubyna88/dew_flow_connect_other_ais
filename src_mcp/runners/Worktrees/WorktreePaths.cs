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
/// </remarks>
public static class WorktreePaths
{
    /// <summary>Whether two paths name the same place once links are resolved.</summary>
    public static bool Same(string one, string other) =>
        string.Equals(Key(one), Key(other), StringComparison.OrdinalIgnoreCase);

    /// <summary>The path with every existing link on it resolved, component by component.</summary>
    public static string Real(string path)
    {
        var full = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(full);
        if (parent is null)
        {
            return full;
        }

        return Resolved(Path.Combine(Real(parent), Path.GetFileName(full)));
    }

    private static string Key(string path) => Real(path).Replace('\\', '/').TrimEnd('/');

    /// <summary>The target of a link, itself resolved — or the path, when it is not a link.</summary>
    private static string Resolved(string candidate)
    {
        FileSystemInfo info = Directory.Exists(candidate) ? new DirectoryInfo(candidate) : new FileInfo(candidate);
        if (info.LinkTarget is null)
        {
            return candidate;
        }

        var target = Target(info);

        return target.Length > 0 ? Real(target) : candidate;
    }

    /// <summary>
    /// The final target, or empty for a link that cannot be followed — a cycle or a dangling target,
    /// which git could not have resolved either, so the spelling is all there is to compare.
    /// </summary>
    private static string Target(FileSystemInfo link)
    {
        try
        {
            return link.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }
}
