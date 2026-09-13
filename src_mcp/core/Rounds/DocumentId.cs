namespace CoaiMcp.Core.Rounds;

/// <summary>
/// What makes two rounds be about the same document: its place in the repository, spelled one way.
/// </summary>
/// <remarks>
/// <para><b>Identity, never content.</b> The first draft of plan 4 identified a document by the
/// SHA-256 of its text, and three vendors independently found the same consequence: edit the
/// document between rounds and the identity changes, so round 2 opens a new session and round 1 is
/// orphaned unresolved — which makes the <c>resolve</c>-then-repeat loop the tool promises
/// impossible. A path survives every edit, which is exactly the property the loop needs. The content
/// hash is still taken; it is the per-round artifact snapshot, which is a different job.</para>
/// <para>Pure, and it takes both paths ALREADY RESOLVED. Following symlinks and refusing what
/// resolves outside the repository is the impure half of the same rule and lives with the
/// filesystem; this half is the normalisation, so the containment rule itself is a unit test.</para>
/// </remarks>
public static class DocumentId
{
    /// <summary>
    /// The repo-relative identity of <paramref name="fullPath"/>, or empty when it is not inside
    /// <paramref name="repoRoot"/>.
    /// </summary>
    /// <remarks>
    /// <b>The comparison is against the root plus a SEPARATOR, never a bare prefix.</b>
    /// <c>/repo-secrets/x.md</c> starts with <c>/repo</c> and is not in it — which is the whole
    /// difference between a containment check and a string that looks like one.
    /// </remarks>
    public static string Of(string repoRoot, string fullPath)
    {
        var root = Normalised(repoRoot);
        var full = Normalised(fullPath);

        return full.StartsWith($"{root}/", StringComparison.Ordinal) ? full[(root.Length + 1)..] : string.Empty;
    }

    /// <summary>
    /// One spelling for a path: forward slashes, no trailing one, lower case.
    /// </summary>
    /// <remarks>
    /// Lower-cased for the same reason <see cref="SessionKey"/> lower-cases a repository path: the
    /// two filesystems this runs on disagree about case, and a session that splits in two because
    /// somebody typed <c>Docs/</c> once is a session nobody can find. It costs the ability to review
    /// two documents in one directory whose names differ only in case, which is not a thing anybody
    /// does on purpose.
    /// </remarks>
    private static string Normalised(string path) =>
        path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
}
