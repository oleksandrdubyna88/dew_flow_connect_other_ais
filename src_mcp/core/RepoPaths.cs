namespace CoaiMcp.Core;

/// <summary>
/// Whether a path can name something INSIDE a repository — decided on the string, never on a
/// filesystem.
/// </summary>
/// <remarks>
/// <para>Moved here from <c>GitHistory.IsRepoRelative</c> (runners) when the parser of a reviewer's
/// answer needed the same question: a feature reviewer's <c>sourceRequests</c> name files, and the
/// parser lives in this pure project, which cannot reach the runners. One rule in the common ancestor
/// rather than a second copy of it — the plan named this move for Epic 3 (§4.9, <c>RepoPaths.IsRelative</c>);
/// it happened in S1.3 because the parser came first.</para>
/// <para>Lexically, because the path is read at a RECORDED commit: a file deleted or renamed since
/// has no current path to canonicalise. Refused: empty, a NUL, a leading separator (absolute, or a UNC
/// root), a drive-qualified form, and any <c>..</c> component under either separator. NOT refused:
/// ordinary metacharacters — <c>docs/notes;v2.md</c> and <c>src/[legacy].cs</c> are committed paths.</para>
/// </remarks>
public static class RepoPaths
{
    private static readonly char[] Separators = ['/', '\\'];

    public static bool IsRelative(string path) =>
        path.Length > 0 && !path.Contains('\0') && !IsRooted(path) && !Traverses(path);

    /// <summary>Why <paramref name="path"/> is not repository-relative, or empty when it is.</summary>
    public static string WhyNotRelative(string path) => path switch
    {
        "" => "names no file",
        _ when path.Contains('\0') => "contains a NUL",
        _ when path[0] is '/' or '\\' => "is absolute",
        _ when IsDriveQualified(path) => "names a drive",
        _ when Traverses(path) => "climbs out with '..'",
        _ => string.Empty,
    };

    private static bool IsRooted(string path) => path[0] is '/' or '\\' || IsDriveQualified(path);

    private static bool IsDriveQualified(string path) =>
        path.Length > 1 && char.IsAsciiLetter(path[0]) && path[1] == ':';

    private static bool Traverses(string path) => path.Split(Separators).Contains("..");
}
