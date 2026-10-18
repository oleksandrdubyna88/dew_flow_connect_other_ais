using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Server;

/// <summary>
/// What ONE git call says about a folder: is it inside a work tree, is it the top of one, and which git dir and common dir it has
/// (D9 of <c>todo/PLAN_vibe_reads_a_folder_of_repositories.md</c>). The answer to "is this a linked worktree" is git's, never a
/// reading of the <c>.git</c> file's text: a submodule's top holds the same kind of file, and a submodule inside a linked worktree
/// writes a gitdir that merely CONTAINS <c>/worktrees/</c> (<c>src_vs_code/src/projectIdentity.ts</c> names the trap).
/// </summary>
/// <param name="InsideWorkTree">Git calls the folder inside a work tree — at its top or deeper.</param>
/// <param name="IsTop">The folder IS the top of that work tree: git says there is nothing above it to climb to (<c>--show-cdup</c> empty).</param>
/// <param name="GitDir">The key of git's absolute git dir (<see cref="RepositoryIdentity.KeyOf"/>); empty when git did not say.</param>
/// <param name="CommonDir">The key of git's common dir; empty when git did not say.</param>
internal sealed record RepositoryFacts(bool InsideWorkTree, bool IsTop, string GitDir, string CommonDir)
{
    /// <summary>What git is asked — one call: the work-tree and top questions, the absolute git dir, and (once paths are absolute) the common dir.</summary>
    public static IReadOnlyList<string> Args { get; } =
        ["rev-parse", "--is-inside-work-tree", "--show-cdup", "--absolute-git-dir", "--path-format=absolute", "--git-common-dir"];

    private const int Answers = 4;

    /// <summary>Git did not confirm anything: it failed, was cut short, or the folder is no repository.</summary>
    public static RepositoryFacts None { get; } = new(InsideWorkTree: false, IsTop: false, string.Empty, string.Empty);

    /// <summary>
    /// A checkout whose git dir is not its common dir: the work tree <c>git worktree add</c> made, which has its own git dir under the main
    /// checkout's <c>.git/worktrees</c>. False when either is unknown — an unknown identity is never "linked".
    /// </summary>
    public bool IsLinked => GitDir.Length > 0 && CommonDir.Length > 0 && !string.Equals(GitDir, CommonDir, StringComparison.Ordinal);

    /// <summary>The top of a work tree that is not linked and whose identity is known: a main checkout (or a submodule's), the thing a linked worktree is skipped for.</summary>
    public bool IsMain => IsTop && CommonDir.Length > 0 && !IsLinked;

    /// <summary>Asks git, in <paramref name="folder"/>; a call git did not answer (the budget cut it short) is <see cref="None"/>.</summary>
    public static async Task<RepositoryFacts> AskAsync(IProcessLauncher launcher, string folder, CancellationToken ct)
    {
        var asked = await launcher.RunAsync(new ProcessRequest("git", Args, folder) { Timeout = TimeSpan.FromSeconds(30) }, ct);

        return Parse(asked.ExitCode, asked.StdOut);
    }

    /// <summary>
    /// Reads git's four lines: <c>true</c>, the cdup (empty at the top — the empty line IS the answer, so the lines are never filtered),
    /// the absolute git dir, the common dir. Anything shorter or other is <see cref="None"/>.
    /// </summary>
    internal static RepositoryFacts Parse(int exitCode, string stdOut)
    {
        var lines = stdOut.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        if (exitCode != 0 || lines.Length < Answers || lines[0] != "true")
        {
            return None;
        }

        return new RepositoryFacts(InsideWorkTree: true, IsTop: lines[1].Length == 0, RepositoryIdentity.KeyOf(lines[2]), RepositoryIdentity.KeyOf(lines[3]));
    }
}
