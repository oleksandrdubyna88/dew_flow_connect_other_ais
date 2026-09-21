namespace CoaiMcp.Runners.Worktrees;

/// <summary>
/// Which submodules a checked-out tree has, and which of them hold files.
/// </summary>
/// <remarks>
/// <para><b>One implementation, because the two sides must agree.</b> Story 3.2a's creator reports
/// the mounts that stayed EMPTY after population; story 3.2b's keeper inspects the mounts that hold
/// something before a removal. They were written separately and a reviewer named the consequence
/// exactly: the creator could populate a mount the removal does not inspect, and files in it would
/// then be treated as clean and deleted. Two answers to "which mounts does this tree have" is the
/// defect; this is the one answer.</para>
/// <para><b>A mount that resolves outside the tree is not a mount of this tree.</b>
/// <c>.gitmodules</c> is a file in the checked-out commit, so its <c>path</c> is as controlled as any
/// committed path is. This product looks at what it made and nothing else.</para>
/// </remarks>
public static class SubmoduleMounts
{
    /// <summary>Every mount <c>.gitmodules</c> declares, keeping only those that stay inside the tree.</summary>
    public static async Task<IReadOnlyList<string>> DeclaredAsync(
        ReviewTreeRoot root, string tree, CancellationToken ct)
    {
        var declared = await root.GitAsync(
            tree, ["config", "-f", ".gitmodules", "--get-regexp", "path"], ReviewTreeRoot.Asking, ct);

        return declared.Ran && declared.Ok
            ? [.. Named(declared.Out).Where(mount => Inside(tree, mount))]
            : [];
    }

    /// <summary>Of those, the ones that actually hold files — an empty mount costs no process.</summary>
    public static async Task<IReadOnlyList<string>> PopulatedAsync(
        ReviewTreeRoot root, string tree, CancellationToken ct) =>
        [.. (await DeclaredAsync(root, tree, ct)).Where(mount => !IsEmpty(tree, mount))];

    /// <summary>Whether a declared mount was left empty — what a freshly made tree reports.</summary>
    public static bool IsEmpty(string tree, string mount)
    {
        var at = Path.Combine(tree, mount);

        return !Directory.Exists(at) || !Directory.EnumerateFileSystemEntries(at).Any();
    }

    /// <summary>The mount paths out of <c>submodule.&lt;name&gt;.path &lt;mount&gt;</c> lines.</summary>
    private static IEnumerable<string> Named(string config) =>
        config.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' ', 2))
            .Where(parts => parts.Length == 2)
            .Select(parts => parts[1].Trim());

    private static bool Inside(string tree, string mount)
    {
        var at = Path.GetFullPath(Path.Combine(tree, mount));
        var below = Path.GetFullPath(tree).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        return at.StartsWith(below, StringComparison.OrdinalIgnoreCase);
    }
}
