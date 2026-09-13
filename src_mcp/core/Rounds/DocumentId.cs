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
/// <para>Pure, and it takes both paths ALREADY RESOLVED — absolute, with every symlink followed.
/// Doing that resolution is the impure half of the same rule and lives with the filesystem in
/// <c>DocumentReader</c>; this half is the comparison, so the containment rule itself is a unit
/// test.</para>
/// </remarks>
public static class DocumentId
{
    /// <summary>
    /// How THIS machine's filesystem compares two paths.
    /// </summary>
    /// <remarks>
    /// <para><b>Both sides were lower-cased, and two reviewers found the same hole in it.</b> On
    /// Linux <c>/tmp/repo</c> and <c>/tmp/Repo</c> are two different directories, so lower-casing
    /// made a SIBLING look contained: <c>/tmp/Repo/secrets.md</c> passed the check as
    /// <c>repo/secrets.md</c> and was read and sent to three vendors. The same normalisation also
    /// merged two genuinely distinct files into one session on a case-sensitive checkout.</para>
    /// <para>The filesystem's own rule is the only correct one, and it is a property of the platform
    /// rather than a preference of this product.</para>
    /// </remarks>
    public static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// The repo-relative identity of <paramref name="fullPath"/>, or empty when it is not inside
    /// <paramref name="repoRoot"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>The comparison is against the root plus a SEPARATOR, never a bare prefix.</b>
    /// <c>/repo-secrets/x.md</c> starts with <c>/repo</c> and is not in it — which is the whole
    /// difference between a containment check and a string that looks like one.</para>
    /// <para>The identity keeps the path's OWN case. Lower-casing it would make two files that
    /// differ only in case into one session on a filesystem where they are two files.</para>
    /// </remarks>
    public static string Of(string repoRoot, string fullPath)
    {
        var root = Normalised(repoRoot);
        var full = Normalised(fullPath);

        return full.StartsWith($"{root}/", Comparison) ? full[(root.Length + 1)..] : string.Empty;
    }

    /// <summary>
    /// The same identity, folded the way <see cref="Comparison"/> compares it — for KEYING only.
    /// </summary>
    /// <remarks>
    /// <para><b>The identity keeps its spelling and the KEY does not, and the two are different
    /// jobs.</b> On Windows <c>docs/spec.md</c> and <c>DOCS\SPEC.MD</c> are one file, so a person who
    /// typed the second after reviewing the first would get a second session for a document already
    /// under review — <c>status</c> would miss the open round and a duplicate review would start.
    /// codex found it in the gap between the comparison this file had just been given and the key
    /// that was still a raw string.</para>
    /// <para>Folding the DISPLAY spelling too would be the opposite mistake, and the round before
    /// this one was about exactly that: on Linux those are two files and merging them loses one.</para>
    /// </remarks>
    public static string KeyOf(string identity) =>
        OperatingSystem.IsWindows() ? identity.ToLowerInvariant() : identity;

    /// <summary>One spelling for a path: forward slashes, no trailing one. The case is the disk's.</summary>
    private static string Normalised(string path) => path.Replace('\\', '/').TrimEnd('/');
}
