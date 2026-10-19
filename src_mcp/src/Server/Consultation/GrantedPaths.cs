using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Worktrees;

namespace CoaiMcp.Server;

/// <summary>
/// The paths of the folders a one-shot place copies, and the link rule that guards them (D5 of
/// <c>todo/PLAN_vibe_reads_a_folder_of_repositories.md</c>): a copy made through a link would be a copy of whatever the link points at.
/// </summary>
internal static class GrantedPaths
{
    /// <summary>The comparer a set of this machine's paths needs.</summary>
    public static StringComparer Comparer { get; } = StringComparer.FromComparison(RepositoryIdentity.PathComparison);

    /// <summary>The full path without a trailing separator — the one spelling a granted folder is checked, asked about and walked by.</summary>
    public static string Keyed(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static string LinkSentence(string link, string granted) =>
        $"{link} is a link, and the granted folder {granted} is reached through it — grant the folder it points at instead";

    public static string NotAWorkTree(string source) =>
        $"the folder {source} is not inside a Git work tree — this runtime reads a copy coai makes through Git (D6 of todo/PLAN_vibe_and_kimi_runtimes.md)";

    /// <summary>
    /// The first link on the way from <paramref name="source"/> to its granted root — or, for a work-tree root granted directly
    /// (<paramref name="linkRoot"/> empty), up to its work tree's top; empty when none is a link. Folders above a work tree are the
    /// machine's own layout (a relocated profile; macOS's <c>/var</c>) and are not asked.
    /// </summary>
    public static string LinkAbove(string source, string linkRoot) =>
        linkRoot.Length == 0 ? LinkUpToTheWorkTree(source) : LinkUpTo(source, linkRoot);

    /// <summary>The first folder from <paramref name="repository"/> up to and INCLUDING the granted <paramref name="root"/> that is a link, or empty (D5).</summary>
    public static string LinkUpTo(string repository, string root) =>
        UpTo(Path.GetFullPath(repository), Keyed(root)).FirstOrDefault(OwnedPaths.IsLinkIfThere) ?? string.Empty;

    private static IEnumerable<string> UpTo(string folder, string top)
    {
        for (var at = folder; at is not null; at = Path.GetDirectoryName(at))
        {
            yield return at;
            if (string.Equals(Keyed(at), top, RepositoryIdentity.PathComparison))
            {
                yield break;
            }
        }
    }

    /// <summary>The first folder from <paramref name="source"/> up to its work tree's top that is a link, or empty.</summary>
    private static string LinkUpToTheWorkTree(string source) =>
        UpToTheWorkTree(Path.GetFullPath(source)).FirstOrDefault(OwnedPaths.IsLinkIfThere) ?? string.Empty;

    /// <summary>The folder and each one above it, up to and including the first that holds a <c>.git</c>.</summary>
    private static IEnumerable<string> UpToTheWorkTree(string folder)
    {
        for (var at = folder; at is not null; at = Path.GetDirectoryName(at))
        {
            yield return at;
            if (GitMarker.HasGit(at))
            {
                yield break;
            }
        }
    }
}
