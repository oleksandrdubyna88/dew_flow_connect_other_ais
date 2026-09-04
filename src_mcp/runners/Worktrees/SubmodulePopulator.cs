using CoaiMcp.Runners.Git;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Worktrees;

/// <summary>
/// Fills a round worktree's submodules from the PARENT checkout's own copies.
/// </summary>
/// <remarks>
/// <para>git does not populate submodules in a linked worktree, and in this family a project's
/// written rules ARE a submodule — so every conventions pass was reading an empty directory where
/// the rules were supposed to be, and reporting compliance with a body of rules it had never seen.
/// Measured 2026-09-04 on a consumer whose <c>.claude/rules/</c> holds nothing but the mount.</para>
/// <para><b>From the parent, not from the remote.</b> The parent's object store already holds every
/// commit the reviewed SHA pins, so cloning from it is offline, faster, and immune to a private
/// submodule or an unreachable host. `git worktree add` has no `--recurse-submodules` to do this
/// (git 2.55), and the plain `submodule update --init` goes to the network — this is the same
/// command with its source pointed at the checkout beside it.</para>
/// <para><b>A failure is not a failed round.</b> A round with fewer rules is worse than a round with
/// all of them, and far better than no round at all — so nothing here throws. The absence is not
/// silent either: <see cref="Context.RuleFiles"/> names a declared rules mount that stayed empty, in
/// the prompt, where the reviewer can act on it.</para>
/// </remarks>
internal sealed class SubmodulePopulator(IProcessLauncher launcher)
{
    /// <summary>A local clone that has not finished in a minute is not going to.</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);

    public async Task PopulateAsync(string repoPath, string worktreePath)
    {
        foreach (var mount in GitModules.In(worktreePath))
        {
            await OneAsync(repoPath, worktreePath, mount);
        }
    }

    private async Task OneAsync(string repoPath, string worktreePath, SubmoduleMount mount)
    {
        var source = LocalSource(repoPath, mount);
        if (source.Length == 0)
        {
            return;
        }

        await launcher.RunAsync(new ProcessRequest(
            "git",
            [
                // The mitigation this lifts (CVE-2022-39253) is about a submodule URL the REPOSITORY
                // supplies; the URL below is one we computed and contained. It is set for this one
                // invocation, never for a repository.
                "-c", "protocol.file.allow=always",
                "-c", $"submodule.{mount.Name}.url={source}",
                "submodule", "update", "--init", "--", mount.Path,
            ],
            worktreePath)
        {
            Timeout = Budget,
        });
    }

    /// <summary>
    /// The parent's own working copy of this submodule, or nothing when it cannot serve as one.
    /// </summary>
    /// <remarks>
    /// Two ways it cannot. The path may resolve outside the parent checkout — <c>.gitmodules</c> is
    /// a file inside the repository under review, so its paths are input, not fact. Or the parent
    /// may never have initialised the submodule itself, in which case there is no object store to
    /// clone and the honest answer is to leave the mount empty and let the bundle say so.
    /// </remarks>
    private static string LocalSource(string repoPath, SubmoduleMount mount)
    {
        var root = Path.GetFullPath(repoPath);
        var candidate = Path.GetFullPath(Path.Combine(root, mount.Path));
        var inside = candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        return inside && HasGit(candidate) ? candidate.Replace('\\', '/') : string.Empty;
    }

    /// <summary>A populated submodule carries a <c>.git</c> file; an empty mount carries nothing.</summary>
    private static bool HasGit(string path) =>
        File.Exists(Path.Combine(path, ".git")) || Directory.Exists(Path.Combine(path, ".git"));
}
