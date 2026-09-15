using System.Text;

namespace CoaiMcp.Normalizer;

/// <summary>One span of the source and what replaces it.</summary>
/// <param name="Start">An index into the source string, in the units the binding reports.</param>
internal readonly record struct Rewrite(int Start, int End, string Text);

/// <summary>
/// Turning a list of replacements into text.
/// </summary>
/// <remarks>
/// <para><b>The units are the binding's, and that is not a detail.</b> The first version assumed
/// tree-sitter's own UTF-8 byte offsets and spliced a byte array — which was wrong, because this
/// binding hands a .NET <c>string</c> to the parser and reports positions in UTF-16 code units, i.e.
/// in <c>char</c>. Splicing bytes at char offsets cut through the middle of letters and produced
/// skeletons full of word fragments: `ng`, `ld`, `he`. It showed up the moment the property test ran
/// over real files, because this repository's comments are full of em dashes — three bytes each, one
/// char each, and every offset after one was wrong by two.</para>
/// <para>Replacements are applied back to front so the offsets of the ones not yet applied stay true.</para>
/// </remarks>
internal static class Rewriting
{
    /// <summary>Applies <paramref name="rewrites"/> to <paramref name="source"/>.</summary>
    internal static string Apply(string source, List<Rewrite> rewrites)
    {
        rewrites.Sort((a, b) => b.Start.CompareTo(a.Start));

        var output = new StringBuilder(source);
        foreach (var rewrite in rewrites)
        {
            var end = Math.Clamp(rewrite.End, 0, output.Length);
            var start = Math.Clamp(rewrite.Start, 0, end);
            output.Remove(start, end - start);
            output.Insert(start, rewrite.Text);
        }

        return Tidy(output.ToString());
    }

    /// <summary>
    /// Trailing space and empty lines go, so that a comment's absence leaves no shape behind.
    /// </summary>
    /// <remarks>
    /// Without this a method whose comment was removed keeps the blank line the comment sat on, and
    /// two methods differing only by a comment would not normalise alike — which is the one property
    /// the corpus cannot do without.
    /// </remarks>
    private static string Tidy(string text) =>
        string.Join(
            '\n',
            text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n')
                .Select(line => line.TrimEnd())
                .Where(line => line.Length > 0));
}
