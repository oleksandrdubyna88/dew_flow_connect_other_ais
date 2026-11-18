using System.Text.Json.Nodes;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>The place check of the agy hook: where a file reader may read (see <see cref="AntigravityReadOnly.Decide"/>).</summary>
internal static class AgyHookPlaces
{
    /// <summary>Whether the call reads a file inside one of the payload's roots and outside the coai folder; false for anything that does not resolve.</summary>
    internal static bool Allows(JsonObject call, HookSite site) =>
        Placed(call, site) is { } places && places.Allows();

    /// <summary>What the guard says when it refuses a look: a constant, with no part of the path.</summary>
    internal const string NotLocal = "coai: the agy hook looks only at local paths";

    /// <summary>
    /// <paramref name="inner"/> behind a question: for each path it is handed the guard asks <paramref name="isLocal"/> FIRST, and a path that is
    /// not local is refused before any look — an <see cref="IOException"/>, which <see cref="RealPath"/> turns into a failed resolve. Looking at a
    /// share is what offers the user's credentials to its host.
    /// </summary>
    internal static EntryReader Guarded(EntryReader inner, Func<string, bool> isLocal) =>
        path => isLocal(path) ? inner(path) : throw new IOException(NotLocal);

    /// <summary>Whether the text starts with two separators of either kind, in either order: the spelling of a share or a device on Windows, and of a path <see cref="RealPath"/> would fold on Unix.</summary>
    internal static bool StartsWithTwoSeparators(string text) => text.Length >= 2 && IsSeparator(text[0]) && IsSeparator(text[1]);

    /// <summary>
    /// Whether the text has a segment that is exactly <c>.</c> or <c>..</c>, splitting on both separators. Text only: it does not ask which OS it runs on
    /// (<see cref="HookSite.FoldsDotsAsText"/> says whether it is consulted). Measured on Windows: only an exact <c>..</c> leaves its folder; <c>.. </c>,
    /// <c>...</c> and <c>..x</c> stay names.
    /// </summary>
    internal static bool HasDotSegment(string text) => text.Split('/', '\\').Any(segment => segment is "." or "..");

    private static bool IsSeparator(char c) => c is '/' or '\\';

    /// <summary>What the place check compares, every one of them resolved.</summary>
    private sealed record Places(string Target, string Coai, IReadOnlyList<string> Roots)
    {
        public bool Allows() =>
            !RealPath.IsInside(Coai, Target, DocumentId.Comparison)
            && Roots.Any(root => RealPath.IsInside(root, Target, DocumentId.Comparison));
    }

    private static Places? Placed(JsonObject call, HookSite site) =>
        Real(PathText(call, site), site) is { } target
        && Real(Qualified(call["cwd"]), site) is not null
        && Real(Qualified(site.CoaiFolder), site) is { } coai
        && Roots(call["workspacePaths"], site) is { } roots
            ? new Places(target, coai, roots)
            : null;

    /// <summary>
    /// The <c>Path</c> argument, fully qualified and admitted by its SPELLING: not two leading separators (a share or a device where Windows reads it, a path
    /// <see cref="RealPath"/> would fold on Unix before any reader saw it), and where the OS folds <c>.</c> and <c>..</c> as text, no such segment — the spelling
    /// judged is then the spelling opened. Roots, <c>cwd</c> and the coai folder are text from coai and agy, never from the model: they are not asked.
    /// </summary>
    private static string? PathText(JsonObject call, HookSite site) =>
        OnePath(call) is { } text && Admitted(text, site) ? text : null;

    private static bool Admitted(string text, HookSite site) =>
        !StartsWithTwoSeparators(text) && !(site.FoldsDotsAsText && HasDotSegment(text));

    /// <summary>The ONE property of <c>args</c> whose name is <c>Path</c> without regard to case, and spelled <c>Path</c>.</summary>
    private static string? OnePath(JsonObject call) =>
        call["toolCall"]?["args"] is JsonObject args
        && args.Where(pair => string.Equals(pair.Key, "Path", StringComparison.OrdinalIgnoreCase)).ToList() is [{ Key: "Path", Value: var value }]
            ? Qualified(value)
            : null;

    private static string? Qualified(JsonNode? node) => Qualified(AntigravityReadOnly.Text(node));

    private static string? Qualified(string? text) => text is not null && Path.IsPathFullyQualified(text) ? text : null;

    /// <summary>The ONE resolve of the hook: with the guarded reader, and a result counts only when it is local too (a link whose text is a bare share resolves with no look at all).</summary>
    private static string? Real(string? text, HookSite site) =>
        text is not null && RealPath.Resolve(text, read: Guarded(site.Read, site.IsLocal)) is { Ok: true } real && site.IsLocal(real.Path) ? real.Path : null;

    /// <summary>Every entry of <c>workspacePaths</c>, resolved; null when it is not an array or ONE element is not a resolvable full path.</summary>
    private static IReadOnlyList<string>? Roots(JsonNode? node, HookSite site)
    {
        if (node is not JsonArray entries)
        {
            return null;
        }

        var roots = entries.Select(entry => Real(Qualified(entry), site)).ToList();

        return roots.All(root => root is not null) ? roots.OfType<string>().ToList() : null;
    }
}
