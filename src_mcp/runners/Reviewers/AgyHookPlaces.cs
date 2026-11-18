using System.Text.Json.Nodes;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>The place check of the agy hook: where a file reader may read (see <see cref="AntigravityReadOnly.Decide"/>).</summary>
internal static class AgyHookPlaces
{
    /// <summary>Whether the call reads a file inside one of the payload's roots and outside the coai folder; false for anything that does not resolve.</summary>
    internal static bool Allows(JsonObject call, HookSite site)
    {
        var placed = Placed(call, site);

        return placed.Ok && placed.Value.Allows();
    }

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

    /// <summary>
    /// An answer that may be "none": <see cref="Ok"/> says whether <see cref="Value"/> means anything. A path that does not resolve or is not admitted is a
    /// different fact from an empty value (an empty <c>workspacePaths</c> is a valid answer that allows nothing), so the two are different states, never a null.
    /// </summary>
    internal readonly record struct Found<T>(bool Ok, T Value)
    {
        internal static Found<T> Of(T value) => new(true, value);
    }

    private static readonly Found<string> NoText = new(false, string.Empty);

    private static readonly Found<IReadOnlyList<string>> NoRoots = new(false, []);

    private static readonly Found<Places> NoPlaces = new(false, new Places(string.Empty, string.Empty, []));

    /// <summary>What the place check compares, every one of them resolved.</summary>
    private sealed record Places(string Target, string Coai, IReadOnlyList<string> Roots)
    {
        public bool Allows() =>
            !RealPath.IsInside(Coai, Target, DocumentId.Comparison)
            && Roots.Any(root => RealPath.IsInside(root, Target, DocumentId.Comparison));
    }

    /// <summary>The target, the payload's <c>cwd</c>, the coai folder and the roots, resolved in that order; the first that is not resolvable denies the whole payload.</summary>
    private static Found<Places> Placed(JsonObject call, HookSite site)
    {
        var target = Real(PathText(call, site), site);

        return target.Ok && CwdResolves(call, site) ? WithCoaiFolder(call, site, target.Value) : NoPlaces;
    }

    private static Found<Places> WithCoaiFolder(JsonObject call, HookSite site, string target)
    {
        var coai = CoaiFolderOf(site);

        return coai.Ok ? WithRoots(call, site, target, coai.Value) : NoPlaces;
    }

    private static Found<Places> WithRoots(JsonObject call, HookSite site, string target, string coai)
    {
        var roots = Roots(call["workspacePaths"], site);

        return roots.Ok ? Found<Places>.Of(new Places(target, coai, roots.Value)) : NoPlaces;
    }

    /// <summary>The coai folder the hook runs from, resolved the way every other path is; not Ok when the site has none or it does not resolve (then every read is denied, and <see cref="AntigravityReadOnly"/> says so once).</summary>
    internal static Found<string> CoaiFolderOf(HookSite site) => Real(Qualified(site.CoaiFolder), site);

    private static bool CwdResolves(JsonObject call, HookSite site) => Real(Qualified(call["cwd"]), site).Ok;

    /// <summary>
    /// The <c>Path</c> argument, fully qualified and admitted by its SPELLING: not two leading separators (a share or a device where Windows reads it, a path
    /// <see cref="RealPath"/> would fold on Unix before any reader saw it), and where the OS folds <c>.</c> and <c>..</c> as text, no such segment — the spelling
    /// judged is then the spelling opened. Roots, <c>cwd</c> and the coai folder are text from coai and agy, never from the model: they are not asked.
    /// </summary>
    private static Found<string> PathText(JsonObject call, HookSite site)
    {
        var text = OnePath(call);

        return text.Ok && Admitted(text.Value, site) ? text : NoText;
    }

    private static bool Admitted(string text, HookSite site) =>
        !StartsWithTwoSeparators(text) && !(site.FoldsDotsAsText && HasDotSegment(text));

    /// <summary>The ONE property of <c>args</c> whose name is <c>Path</c> without regard to case, and spelled <c>Path</c>.</summary>
    private static Found<string> OnePath(JsonObject call) =>
        call["toolCall"]?["args"] is JsonObject args ? PathOf(args) : NoText;

    private static Found<string> PathOf(JsonObject args) =>
        args.Where(pair => string.Equals(pair.Key, "Path", StringComparison.OrdinalIgnoreCase)).ToList() is [{ Key: "Path", Value: var value }]
            ? Qualified(value)
            : NoText;

    private static Found<string> Qualified(JsonNode? node) => Qualified(AntigravityReadOnly.Text(node));

    private static Found<string> Qualified(string? text) => text is not null && Path.IsPathFullyQualified(text) ? Found<string>.Of(text) : NoText;

    /// <summary>The ONE resolve of the hook: with the guarded reader, and a result counts only when it is local too (a link whose text is a bare share resolves with no look at all).</summary>
    private static Found<string> Real(Found<string> text, HookSite site) => text.Ok ? Resolved(text.Value, site) : NoText;

    private static Found<string> Resolved(string text, HookSite site) =>
        RealPath.Resolve(text, read: Guarded(site.Read, site.IsLocal)) is { Ok: true } real && site.IsLocal(real.Path) ? Found<string>.Of(real.Path) : NoText;

    /// <summary>Every entry of <c>workspacePaths</c>, resolved; an empty array is a valid empty list (it allows nothing), not Ok when it is not an array or ONE element is not a resolvable full path.</summary>
    private static Found<IReadOnlyList<string>> Roots(JsonNode? node, HookSite site) =>
        node is JsonArray entries ? EveryRoot(entries, site) : NoRoots;

    private static Found<IReadOnlyList<string>> EveryRoot(JsonArray entries, HookSite site)
    {
        var roots = entries.Select(entry => Real(Qualified(entry), site)).ToList();

        return roots.All(root => root.Ok) ? Found<IReadOnlyList<string>>.Of([.. roots.Select(root => root.Value)]) : NoRoots;
    }
}
