namespace CoaiMcp.Runners.Worktrees;

/// <summary>
/// How coai's own git commands run on a coder worktree: the flags that keep the repository's config and the tree's files from running code, the one working folder, the environment that cannot move git, and the commands that are never made
/// (epoch 3 E2 s2 t4 d5; <c>v2/spikes/RESULTS_coder_write_boundary_judgement.md</c> must-change 3).
/// </summary>
/// <remarks>
/// <para><b>Why.</b> A coder's files are untrusted input to coai's git. Git runs code named in config (<c>core.hooksPath</c>, a filter, <c>core.fsmonitor</c>) and the tree can hold <c>.gitattributes</c>, <c>.gitmodules</c>, <c>.githooks/</c> and <c>.husky/</c>. This
/// repository's own config names none of those keys, so there is no path today; the rule is for every repository a coder job runs on.</para>
/// <para><b>The flags come FIRST, and no caller option can follow them into place.</b> Git lets a later <c>-c</c> override an earlier one, so a command whose first words are an option that sets config, moves git (<c>-C</c>, <c>--git-dir</c>, <c>--work-tree</c>,
/// <c>--exec-path</c>) or names a namespace is refused, and so is every <c>submodule</c> command — <c>git submodule update</c> in a coder tree would clone, check out and run a checkout hook for whatever the coder wrote into <c>.gitmodules</c>.
/// Only the two read-only global options the existing readers use are allowed before the verb.</para>
/// </remarks>
internal static class CoderGit
{
    private static readonly string[] Flags = ["-c", "core.hooksPath=/dev/null", "-c", "core.fsmonitor=false"];

    /// <summary>The only options the readers of this product put before a verb. Anything else that starts with a dash is refused.</summary>
    private static readonly string[] AllowedBeforeTheVerb = ["--no-optional-locks", "--literal-pathspecs"];

    /// <summary>The names git reads to decide which repository, index and objects it works on. Taken out of the environment of every coder-tree command.</summary>
    private static readonly string[] Moving =
        ["GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES"];

    /// <summary>The environment changes: each name in <see cref="Moving"/> removed.</summary>
    internal static IReadOnlyDictionary<string, string?> Environment { get; } = Moving.ToDictionary(name => name, string?(_) => null, StringComparer.Ordinal);

    /// <summary>The hardening flags, then the arguments (with the Windows long-path flag where it belongs).</summary>
    internal static IReadOnlyList<string> Argv(IReadOnlyList<string> args) => [.. Flags, .. TreeGit.Argv(args)];

    /// <summary>The sentence that says why this command is not made, or empty: the pointer is compared with what was written down, the verb is not <c>submodule</c>, and no option moves git or sets config.</summary>
    internal static string Refusal(CoderTree tree, IReadOnlyList<string> args)
    {
        var pointer = tree.Verify();

        return pointer.Length > 0 ? pointer : Verb(args);
    }

    private static string Verb(IReadOnlyList<string> args)
    {
        var verb = args.SkipWhile(argument => AllowedBeforeTheVerb.Contains(argument, StringComparer.Ordinal)).FirstOrDefault() ?? string.Empty;

        return verb.Length == 0 || verb.StartsWith('-')
            ? $"A git command in a coder worktree starts with its verb; '{verb}' would set config or move git, so it was not run."
            : string.Equals(verb, "submodule", StringComparison.Ordinal)
                ? "git submodule is never run in a coder worktree: it would clone and check out whatever the coder wrote into .gitmodules."
                : string.Empty;
    }
}
