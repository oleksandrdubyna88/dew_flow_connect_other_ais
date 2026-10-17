namespace CoaiMcp.Runners.Worktrees;

/// <summary>
/// What a failed round-tree <c>worktree add</c> says, read from git's own stderr.
/// </summary>
/// <remarks>
/// <para>Git reports a path it could not write on a line of its own — <c>error: unable to create file
/// &lt;rel&gt;: &lt;reason&gt;</c> or <c>fatal: cannot create directory at '&lt;rel&gt;': &lt;reason&gt;</c> — and
/// then closes with a <c>fatal:</c> line about the INDEX ("Could not reset index file to revision
/// 'HEAD'"). Keeping only that last line is what made a path longer than Windows allows read like a
/// corrupt repository (probes P2, P3, P11 of <c>todo/PLAN_review_trees_survive_long_paths.md</c>).</para>
/// <para>The reason is the text after the LAST <c>": "</c> on the line: git's reasons hold none, and a
/// Windows file name cannot hold a colon. Lengths are <see cref="string.Length"/> — UTF-16 units, which is
/// what Windows counts (P5), never bytes.</para>
/// </remarks>
internal static class WorktreeFailure
{
    /// <summary>
    /// The longest absolute FILE path Windows accepts without long-path support: 259 UTF-16 units were
    /// created, 260 were refused (probe P1, P2).
    /// </summary>
    internal const int WindowsMaxPathUnits = 259;

    /// <summary>
    /// The longest absolute FOLDER path Windows accepts without long-path support — 12 units short of a
    /// file's: git for Windows made a 247-unit folder and refused 248 (probed 2026-10-17, git 2.55), the
    /// same 247 <c>research/PLAN_vibe_live_round_fixes.md</c> measured with Python.
    /// </summary>
    internal const int WindowsMaxFolderUnits = 247;

    private const string TooLong = "Filename too long";

    private static readonly (string Prefix, bool IsFolder)[] PathLines =
    [
        ("error: unable to create file ", false),
        ("fatal: cannot create directory at ", true),
    ];

    /// <summary>
    /// The detail of the <see cref="WorktreeException"/> for a failed add of <paramref name="treePath"/>:
    /// the path git could not write, its length and git's reason — or, when git named no path, exactly what
    /// <see cref="WorktreeManager.FatalLine"/> has always said.
    /// </summary>
    internal static string Sentence(string stderr, string treePath)
    {
        var verdict = WorktreeManager.FatalLine(stderr);
        var unwritable = stderr.Split('\n', StringSplitOptions.TrimEntries)
            .SelectMany(Unwritable.From)
            .Take(1)
            .ToList();

        return unwritable.Count == 0
            ? verdict
            : $"{unwritable[0].Describe(treePath)}{Advice(unwritable[0])} git: {verdict}";
    }

    /// <summary>
    /// What an operator can DO about it — only for the reason a shorter root actually cures, and held against
    /// the limit of the KIND of path git could not make: a folder stops sooner than a file.
    /// </summary>
    private static string Advice(Unwritable what) =>
        what.Reason != TooLong
            ? string.Empty
            : OperatingSystem.IsWindows()
                ? $" Windows allows a {what.Kind} path of {what.WindowsLimit} characters without long-path support; "
                  + "coai already passed core.longpaths=true, so this git did not honour it — set "
                  + "COAI_ROUND_WORKTREES to a shorter local folder, or shorten the path."
                : " Set COAI_ROUND_WORKTREES to a shorter local folder, or shorten the path.";

    /// <summary>One path git could not write, relative to the tree, the reason it gave, and whether it was a folder.</summary>
    private sealed record Unwritable(string Relative, string Reason, bool IsFolder)
    {
        public string Kind => IsFolder ? "folder" : "file";

        public int WindowsLimit => IsFolder ? WindowsMaxFolderUnits : WindowsMaxPathUnits;

        /// <summary>The record for a stderr line, or none when the line names no path.</summary>
        public static IEnumerable<Unwritable> From(string line) =>
            PathLines
                .Where(known => line.StartsWith(known.Prefix, StringComparison.Ordinal))
                .Take(1)
                .SelectMany(known => Split(line[known.Prefix.Length..], known.IsFolder));

        public string Describe(string treePath)
        {
            var path = Path.Combine(treePath, OperatingSystem.IsWindows() ? Relative.Replace('/', '\\') : Relative);

            return $"could not write '{path}' ({path.Length} characters): {Reason}.";
        }

        /// <summary><c>&lt;rel&gt;: &lt;reason&gt;</c> split at the LAST <c>": "</c>, or none when there is no reason.</summary>
        private static IEnumerable<Unwritable> Split(string rest, bool isFolder)
        {
            var split = rest.LastIndexOf(": ", StringComparison.Ordinal);

            return split > 0
                ? [new Unwritable(Unquoted(rest[..split]), rest[(split + 2)..].Trim(), isFolder)]
                : [];
        }

        private static string Unquoted(string text) =>
            text.Length > 1 && text[0] == '\'' && text[^1] == '\'' ? text[1..^1] : text;
    }
}
