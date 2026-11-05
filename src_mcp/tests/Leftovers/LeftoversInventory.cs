using System.Text;
using System.Text.Json;

namespace CoaiMcp.Tests.Leftovers;

/// <summary>
/// The expected-exemption inventory (E5 S6, <c>todo/PLAN_test_leftovers_E5_S6_brief.md</c> § 7.3): <c>shared/leftovers-exemptions.json</c> lists every inline exemption marker of this repository with its
/// SITE's line and the hash of the site's trimmed line. A new marker, a removed one, a moved line or a changed site is a visible diff there, and <see cref="Differences"/> is empty only when the tree and the file agree.
/// </summary>
/// <remarks>
/// <para>The marker is on the line above its site, or on the site's own line when code stands before the comment. The hash is never computed in C#: JavaScript's <c>trim</c> and C#'s <c>Trim</c> differ on
/// U+FEFF and U+0085, so the shipped <c>siteSha256</c> answers (<see cref="LeftoversBaseline.SiteHashes"/>). The Node half of the same comparison is <c>testLeftovers.test.mjs</c> (N3).</para>
/// </remarks>
internal static class LeftoversInventory
{
    internal const string RelativePath = "shared/leftovers-exemptions.json";

    private static readonly string[] CommentOpeners = ["//", "#", "--", "/*", "*", "<!--"];

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    internal sealed record Row(string File, int Line, string Sha256, string Class, IReadOnlyList<string> Guards, string? Proof);

    internal sealed record Pair(string File, string TestFile, string Why);

    internal sealed record Document(string About, IReadOnlyList<Row> Rows, IReadOnlyList<Pair> NamedBy);

    /// <summary>One marker in the tree: the file, the SITE's line and the class it names.</summary>
    internal sealed record Marker(string File, int Line, string Class);

    internal static Document Read(string root) =>
        JsonSerializer.Deserialize<Document>(File.ReadAllText(Path.Combine(root, "shared", "leftovers-exemptions.json"), Encoding.UTF8), Json)
        ?? throw new InvalidDataException(RelativePath + " is empty");

    /// <summary>Every valid marker the C# guard reads, with its site's line: the discovered files, in the guard's own order.</summary>
    internal static IReadOnlyList<Marker> MarkersOnDisk(string root)
    {
        var found = new List<Marker>();
        foreach (var source in GuardFiles.Discover(root))
        {
            var lines = GuardText.SplitLines(File.ReadAllText(source.Absolute, Encoding.UTF8));
            var exemptions = GuardExemptions.Read(source.Relative, lines, out _);
            foreach (var (cls, markerLines) in exemptions)
            {
                found.AddRange(markerLines.Select(number => new Marker(source.Relative, SiteOf(lines[number - 1], number), cls)));
            }
        }

        return [.. found.OrderBy(m => m.File, StringComparer.Ordinal).ThenBy(m => m.Line).ThenBy(m => m.Class, StringComparer.Ordinal)];
    }

    /// <summary>The site of a marker on line <paramref name="number"/>: the next line, or this one when code stands before the comment.</summary>
    internal static int SiteOf(string markerLine, int number)
    {
        var at = markerLine.IndexOf(LeftoversGuard.CamelWord + "-exempt:", StringComparison.Ordinal);
        var before = at < 0 ? string.Empty : markerLine[..at].TrimEnd();
        var opener = CommentOpeners.OrderByDescending(o => o.Length).FirstOrDefault(o => before.EndsWith(o, StringComparison.Ordinal));
        var code = opener is null ? before : before[..^opener.Length];

        return string.IsNullOrWhiteSpace(code) ? number + 1 : number;
    }

    /// <summary>
    /// One line per difference between the tree and the inventory's rows that name the <c>csharp</c> guard: <c>in the tree, not in the inventory</c>, <c>in the inventory, not in the tree</c>, or
    /// <c>the site changed under its marker</c>. Empty when they agree.
    /// </summary>
    internal static IReadOnlyList<string> Differences(string root, string? mountRoot = null, string node = "node")
    {
        var rows = Read(root).Rows.Where(row => row.Guards.Contains("csharp")).ToList();
        var markers = MarkersOnDisk(root);
        var hashes = LeftoversBaseline.SiteHashes(root, mountRoot ?? root, [.. markers.Select(m => (m.File, m.Line))], node);
        var inTree = markers.Select((marker, index) => (Key: KeyOf(marker.File, marker.Line, marker.Class), Hash: hashes[index])).ToDictionary(x => x.Key, x => x.Hash, StringComparer.Ordinal);
        var inRows = rows.ToDictionary(row => KeyOf(row.File, row.Line, row.Class), StringComparer.Ordinal);

        return
        [
            .. inTree.Keys.Where(key => !inRows.ContainsKey(key)).Select(key => $"in the tree, not in the inventory: {key}"),
            .. inRows.Keys.Where(key => !inTree.ContainsKey(key)).Select(key => $"in the inventory, not in the tree: {key}"),
            .. inRows.Where(pair => inTree.TryGetValue(pair.Key, out var hash) && hash != pair.Value.Sha256)
                .Select(pair => $"the site changed under its marker: {pair.Key} — the inventory holds {pair.Value.Sha256}, the line hashes to {inTree[pair.Key]}"),
        ];
    }

    private static string KeyOf(string file, int line, string cls) => $"{file}:{line}:{cls}";
}
