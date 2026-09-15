namespace CoaiMcp.Core.Collecting;

/// <summary>Whether a repository path is one that can have a history at all.</summary>
/// <remarks>
/// <para><b>30 % of raw candidates live in scratch directories</b> — measured, 231 of 771: Claude's
/// own temp working folders, throwaway worktrees, a `toDelete`. None of them has a fix commit to find,
/// a quarter of them no longer exist, and running the whole pipeline over them to discover that is
/// work nobody needs done.</para>
/// <para>A guard rather than a discovery, therefore, and a pure one so it is a unit test rather than
/// a directory somebody has to create.</para>
/// </remarks>
public static class CandidatePath
{
    /// <summary>Path fragments that mean "this was never meant to last".</summary>
    /// <remarks>
    /// <para>Matched on a path normalised to forward slashes and lower case, because the same repository is
    /// recorded three ways in the live database — `D:\rsd\…`, `d:\rsd\…` and `D:/rsd/…` — and a check
    /// that treated those as different strings would let two of the three through.</para>
    /// <para><b>Named precisely, not by "somewhere under a temp folder".</b> The first version matched
    /// <c>/appdata/local/temp/</c> as well, which is true of every scratchpad in the measurement AND
    /// of any repository a person or a test happens to put there — it refused this suite's own
    /// fixtures before git was asked anything. A guard that cannot be tested is a guard nobody can
    /// trust, and the breadth bought nothing: a scratch checkout that is not named here still skips,
    /// for the honest reason that its path is gone or its commit is unreachable.</para>
    /// </remarks>
    private static readonly string[] Transient =
    [
        "/temp/claude/",
        "/todelete",
    ];

    /// <summary>The path as a comparison can use it: forward slashes, lower case, no trailing slash.</summary>
    public static string Canonical(string path) =>
        path.Replace('\\', '/').ToLowerInvariant().TrimEnd('/');

    /// <summary>Whether this path is a scratch directory rather than somebody's repository.</summary>
    public static bool IsTransient(string path)
    {
        var canonical = Canonical(path);

        return Array.Exists(Transient, fragment => canonical.Contains(fragment, StringComparison.Ordinal));
    }
}
