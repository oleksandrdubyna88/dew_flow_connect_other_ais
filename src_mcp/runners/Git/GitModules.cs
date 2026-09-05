namespace CoaiMcp.Runners.Git;

/// <summary>One submodule a repository declares: the name git knows it by, and where it mounts.</summary>
/// <remarks>
/// The two are separate on purpose. They happen to be equal in this family (<c>.gitmodules</c> here
/// names the section after the path), but git does not require it, and the URL override that keeps a
/// round offline is keyed by the NAME — get that wrong and git quietly falls back to the remote,
/// which is the one outcome this exists to avoid.
/// </remarks>
public sealed record SubmoduleMount(string Name, string Path);

/// <summary>
/// The submodules a repository declares, read from its own <c>.gitmodules</c>.
/// </summary>
/// <remarks>
/// <para>Parsed rather than asked of git, because both callers already hold a path and neither
/// wants a process launch: the rule collector runs on temp directories that are not repositories at
/// all, and the worktree populator is on the round's critical path.</para>
/// <para><b>A declared path is untrusted input.</b> It comes from a file inside the repository under
/// review, so a mount is dropped here unless it is relative and free of traversal — anything else is
/// a path that resolves outside the checkout, and the whole point of the source override is that the
/// source is one we chose.</para>
/// </remarks>
public static class GitModules
{
    public const string FileName = ".gitmodules";

    private const string HeaderStart = "[submodule \"";

    /// <summary>What <paramref name="repoPath"/> declares, or nothing when it declares nothing.</summary>
    public static IReadOnlyList<SubmoduleMount> In(string repoPath)
    {
        var file = System.IO.Path.Combine(repoPath, FileName);

        return Read(file) is { } text ? Parse(text) : [];
    }

    /// <summary>Every safe <c>(name, path)</c> pair in a <c>.gitmodules</c> body.</summary>
    public static IReadOnlyList<SubmoduleMount> Parse(string text)
    {
        var mounts = new List<SubmoduleMount>();
        var name = string.Empty;

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            name = NameOf(trimmed, name);
            Collect(mounts, name, PathOf(trimmed));
        }

        return mounts;
    }

    /// <summary>
    /// A name that can be spelt into a <c>git -c submodule.&lt;name&gt;.url=…</c> key and mean only that.
    /// </summary>
    /// <remarks>
    /// The section name is as untrusted as the path, and it lands in a CONFIG KEY rather than a
    /// value: a name carrying <c>=</c>, a newline or a bracket is a name that can set a key nobody
    /// asked for. Ordinary git names — including this family's, which are paths — pass; anything
    /// else drops the mount rather than being escaped, because a mount we cannot address is one
    /// whose URL override would silently miss and send the round to the network.
    /// </remarks>
    public static bool IsSafeMountName(string name) =>
        name.Length > 0 && name.All(IsOrdinaryNameChar);

    /// <summary>Relative, and made of ordinary segments — nothing that can resolve out of the tree.</summary>
    public static bool IsSafeMountPath(string path)
    {
        var normalised = Normalise(path);

        return normalised.Length > 0
            && !System.IO.Path.IsPathRooted(normalised)
            && !RootedSomewhereElse(normalised)
            && normalised.Split('/').All(IsOrdinarySegment);
    }

    /// <summary>
    /// Absolute where it was WRITTEN, which is not necessarily where it is read.
    /// </summary>
    /// <remarks>
    /// <see cref="System.IO.Path.IsPathRooted(string)"/> answers for the platform running it, and a
    /// review runs on whichever machine the round is on: <c>C:/Windows</c> is rooted on Windows and
    /// an ordinary relative directory called <c>C:</c> on Linux, where CI runs. A
    /// <c>.gitmodules</c> is a file inside the repository under review, so the safe reading is the
    /// stricter one on every platform — nobody mounts a submodule at a directory named <c>C:</c>.
    /// Caught by CI on Linux, where the Windows-path case of this rule's own test went green.
    /// </remarks>
    private static bool RootedSomewhereElse(string path) =>
        path.StartsWith('/') || (path.Length > 1 && path[1] == ':');

    /// <summary>Forward slashes and no trailing separator, so paths compare as text.</summary>
    public static string Normalise(string path) => path.Trim().Replace('\\', '/').TrimEnd('/');

    private static bool IsOrdinarySegment(string segment) =>
        segment.Length > 0 && segment != "." && segment != "..";

    private static bool IsOrdinaryNameChar(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '/';

    private static void Collect(List<SubmoduleMount> mounts, string name, string path)
    {
        if (IsSafeMountName(name) && IsSafeMountPath(path))
        {
            mounts.Add(new SubmoduleMount(name, Normalise(path)));
        }
    }

    /// <summary>The section this line opens, or the one still open. Any other section closes it.</summary>
    private static string NameOf(string line, string current)
    {
        if (!line.StartsWith('['))
        {
            return current;
        }

        if (!line.StartsWith(HeaderStart, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var end = line.LastIndexOf('"');

        return end > HeaderStart.Length ? line[HeaderStart.Length..end] : string.Empty;
    }

    private static string PathOf(string line)
    {
        var equals = line.IndexOf('=');
        if (equals < 0)
        {
            return string.Empty;
        }

        return line[..equals].Trim().Equals("path", StringComparison.OrdinalIgnoreCase)
            ? line[(equals + 1)..].Trim()
            : string.Empty;
    }

    /// <summary>A file that cannot be read declares nothing; it is never a round that fails.</summary>
    private static string? Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
