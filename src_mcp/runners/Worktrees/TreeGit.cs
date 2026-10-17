namespace CoaiMcp.Runners.Worktrees;

/// <summary>
/// The one place that decides how git is asked to work in a tree coai MADE — a round tree or a person's
/// review tree.
/// </summary>
/// <remarks>
/// <para>On Windows git refuses to create, read or remove a file whose absolute path passes 259
/// UTF-16 units unless <c>core.longpaths</c> is on, and it is on nowhere by default. A round tree's root
/// is already 82 characters, so a tracked path of only 177 made <c>review_code</c> fail with "Could not
/// reset index file", which names the index while the cause is the path. Nothing about it is
/// confined to the checkout: <c>status</c> reads a clean tree as dirty, <c>worktree remove</c> drops the
/// registration and leaves the files, and a submodule's nested clone fails the same way
/// (<c>research/PLAN_review_trees_survive_long_paths.md</c>, probes P1-P10).</para>
/// <para><b>Per command, never a config write.</b> The flag travels as <c>-c</c> on the git process we
/// start, so no repository, no global and no system file is touched — the person's own git keeps the
/// setting it had. Every launch point that works on one of OUR trees goes through here, which is why a
/// git call added to one of them later inherits the flag instead of needing to remember it.</para>
/// <para><b>Windows only.</b> The key belongs to Git for Windows, and "git elsewhere ignores it" is a claim
/// about every git a Linux or macOS user might run. The argv there stays exactly what it was.</para>
/// </remarks>
internal static class TreeGit
{
    private static readonly string[] LongPaths = ["-c", "core.longpaths=true"];

    /// <summary>The arguments to start git with: <paramref name="args"/>, led by the long-path flag on Windows.</summary>
    internal static IReadOnlyList<string> Argv(IReadOnlyList<string> args) =>
        OperatingSystem.IsWindows() ? [.. LongPaths, .. args] : args;
}
