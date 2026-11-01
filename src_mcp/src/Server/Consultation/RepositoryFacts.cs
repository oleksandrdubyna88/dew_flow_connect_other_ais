using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Server;

/// <summary>Why a <see cref="RepositoryFacts"/> says what it says — a closed set, so a skip names the real cause.</summary>
internal enum GitSaid
{
    /// <summary>Git printed its four lines: the folder is inside a work tree (at its top or deeper).</summary>
    Answered,

    /// <summary>Git failed, did not answer, or was cut short by the budget — or the folder is no repository.</summary>
    Failed,

    /// <summary>Git ran and answered, but not that the folder is inside a work tree (a folder inside a <c>.git</c> directory).</summary>
    NotAWorkTree,
}

/// <summary>
/// What ONE git call says about a folder: is it inside a work tree, is it the top of one, and which git dir and common dir it has
/// (D9 of <c>research/PLAN_vibe_reads_a_folder_of_repositories.md</c>). The answer to "is this a linked worktree" is git's, never a
/// reading of the <c>.git</c> file's text: a submodule's top holds the same kind of file, and a submodule inside a linked worktree
/// writes a gitdir that merely CONTAINS <c>/worktrees/</c> (<c>src_vs_code/src/projectIdentity.ts</c> names the trap).
/// </summary>
/// <param name="InsideWorkTree">Git calls the folder inside a work tree — at its top or deeper.</param>
/// <param name="IsTop">The folder IS the top of that work tree: git says there is nothing above it to climb to (<c>--show-cdup</c> empty).</param>
/// <param name="GitDir">The key of git's absolute git dir (<see cref="RepositoryIdentity.KeyOf"/>); empty when git did not say.</param>
/// <param name="CommonDir">The key of git's common dir; empty when git did not say.</param>
/// <param name="Said">Why the facts are what they are — what a skipped candidate's note says (code gate finding 29).</param>
internal sealed record RepositoryFacts(bool InsideWorkTree, bool IsTop, string GitDir, string CommonDir, GitSaid Said = GitSaid.Answered)
{
    /// <summary>What git is asked — one call: the work-tree and top questions, the absolute git dir, and (once paths are absolute) the common dir.</summary>
    public static IReadOnlyList<string> Args { get; } =
        ["rev-parse", "--is-inside-work-tree", "--show-cdup", "--absolute-git-dir", "--path-format=absolute", "--git-common-dir"];

    private const int Answers = 4;

    /// <summary>Git did not confirm anything: it failed, was cut short, or the folder is no repository.</summary>
    public static RepositoryFacts None { get; } = new(InsideWorkTree: false, IsTop: false, string.Empty, string.Empty, GitSaid.Failed);

    /// <summary>Git ran and answered, but not "inside a work tree".</summary>
    private static RepositoryFacts NotAWorkTree { get; } = None with { Said = GitSaid.NotAWorkTree };

    /// <summary>
    /// A checkout whose git dir is not its common dir: the work tree <c>git worktree add</c> made, which has its own git dir under the main
    /// checkout's <c>.git/worktrees</c>. False when either is unknown — an unknown identity is never "linked".
    /// </summary>
    public bool IsLinked => GitDir.Length > 0 && CommonDir.Length > 0 && !string.Equals(GitDir, CommonDir, StringComparison.Ordinal);

    /// <summary>
    /// The top of a work tree whose git dir is known and is its common dir: a main checkout (or a submodule's), the thing a linked worktree
    /// is skipped for. A git dir git did not say is no identity — a common dir alone never makes a main.
    /// </summary>
    public bool IsMain => IsTop && GitDir.Length > 0 && string.Equals(GitDir, CommonDir, StringComparison.Ordinal);

    /// <summary>
    /// The note of a candidate that is not the top of a work tree of its own, by the cause: git did not answer, git answered that it is no
    /// work tree, or git ran and the folder is inside ANOTHER repository's work tree (a <c>.git</c> folder that is empty or broken).
    /// </summary>
    public string SkippedBecause(string path) => Said switch
    {
        GitSaid.Failed => $"{path} was skipped: git did not confirm it (git failed or gave no answer)",
        GitSaid.NotAWorkTree => $"{path} was skipped: git did not confirm it (git says it is not inside a work tree)",
        _ => $"{path} was skipped: it is inside another repository's work tree, not the top of its own",
    };

    /// <summary>Asks git, in <paramref name="folder"/>; a call git did not answer (the budget cut it short) is <see cref="None"/>.</summary>
    public static async Task<RepositoryFacts> AskAsync(IProcessLauncher launcher, string folder, CancellationToken ct)
    {
        var asked = await launcher.RunAsync(new ProcessRequest("git", Args, folder) { Timeout = TimeSpan.FromSeconds(30) }, ct);

        return Parse(asked.ExitCode, asked.StdOut);
    }

    /// <summary>
    /// Reads git's four lines: <c>true</c>, the cdup (empty at the top — the empty line IS the answer, so the lines are never filtered),
    /// the absolute git dir, the common dir. Anything shorter or other is <see cref="None"/> (git failed) or, when git ran and did not say
    /// <c>true</c>, a not-a-work-tree answer.
    /// </summary>
    internal static RepositoryFacts Parse(int exitCode, string stdOut)
    {
        var lines = stdOut.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        if (exitCode != 0)
        {
            return None;
        }

        if (lines.Length < Answers || lines[0] != "true")
        {
            return NotAWorkTree;
        }

        return new RepositoryFacts(InsideWorkTree: true, IsTop: lines[1].Length == 0, RepositoryIdentity.KeyOf(lines[2]), RepositoryIdentity.KeyOf(lines[3]));
    }
}
