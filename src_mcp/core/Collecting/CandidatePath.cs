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
    /// <remarks>
    /// <para>Stored WITHOUT delimiters, because the delimiters belong to the matcher: these are
    /// component names, and <see cref="IsTransient"/> is what decides that a component is bounded by
    /// slashes on both sides. Written the other way — <c>"/temp/claude/"</c> against a bare
    /// <c>"/todelete"</c> — the data had to carry a rule about its own matching that only one of the
    /// two entries obeyed, and the method needed a conditional to paper over the difference.</para>
    /// <para>They are also not paths this program ever opens. Nothing here is created, read or
    /// written; they are needles in a <c>Contains</c> over a candidate's path, and the whole purpose
    /// is to REFUSE what matches.</para>
    /// </remarks>
    private static readonly string[] Transient =
    [
        "temp/claude",
        "todelete",
    ];

    /// <summary>The path as a comparison can use it: forward slashes, lower case, no trailing slash.</summary>
    public static string Canonical(string path) =>
        path.Replace('\\', '/').ToLowerInvariant().TrimEnd('/');

    /// <summary>Whether this path is a scratch directory rather than somebody's repository.</summary>
    /// <remarks>
    /// Each name is matched as a whole path COMPONENT: `todelete` must not claim
    /// `todelete_benchmarks/repo` or `dev/todelete-fixtures`, which are ordinary directories whose
    /// names merely begin the same way. Bounding BOTH ends with a slash is what says so, and the
    /// path is padded at both ends so a name can match first or last. Caught twice — once accepted
    /// and not done, once found again by the same two reviewers.
    /// </remarks>
    public static bool IsTransient(string path)
    {
        var bounded = "/" + Canonical(path) + "/";

        return Array.Exists(
            Transient,
            name => bounded.Contains("/" + name + "/", StringComparison.Ordinal));
    }
}
