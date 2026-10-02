using System.Globalization;
using System.Text;

namespace CoaiMcp.Core.QuestionConsult;

/// <summary>
/// A text as a MODEL reads it, which is what the question consultant's checks must read too (S4b item 4): Unicode
/// format characters removed — zero-width spaces and joiners, bidi controls, the soft hyphen, tag characters, every
/// code point of category <c>Cf</c> — and compatibility forms folded the way NFKC folds them, so <c>Ｃ：＼</c> is
/// <c>C:\</c> and a full-width <c>ｓｋ-</c> is the key prefix it looks like.
/// </summary>
/// <remarks>
/// <para>A check that reads the raw text is beaten by one invisible character: <c>sk-</c>, a zero-width space, then
/// the key, is no key to a pattern and the same key to the model that receives it. Reading the folded text closes
/// that; the text that travels is never rewritten — a web question carrying a format character is REFUSED
/// (<see cref="WebQuestionSanitiser"/>), and a context is only ever inspected.</para>
/// <para><b>The fold is ours, not <see cref="string.Normalize(NormalizationForm)"/>.</b> Measured 2026-10-02 on .NET 10
/// with <c>InvariantGlobalization</c> — which every binary here is built with (<c>Directory.Build.props</c>):
/// <c>Normalize(FormKC)</c> returns its input UNCHANGED for <c>Ｃ：＼</c>, <c>ｓｋ-</c>, a no-break space, <c>ﬁ</c> and
/// <c>…</c> — no exception, no folding. So the compatibility mappings that reach ASCII are carried here, from the
/// Unicode data: every space separator to a space, the full-width ASCII block, the small form variants, the
/// mathematical letters and digits, the super- and subscript digits, the Latin ligatures and the dot leaders. A
/// compatibility character outside those is left as it is — it cannot spell an ASCII path, host or key.</para>
/// <para>An ill-formed UTF-16 sequence (a lone surrogate) reads as U+FFFD — <see cref="string.EnumerateRunes"/>'s own
/// rule — because a lone half has nothing to fold and must not throw out of a check.</para>
/// </remarks>
public static class TextAsRead
{
    /// <summary>The text with every format character removed and its compatibility forms folded.</summary>
    public static string Normalised(string text) =>
        string.Concat(text.EnumerateRunes().Where(rune => !IsFormat(rune)).Select(Folded));

    /// <summary>How many format characters (<c>Cf</c>) the text carries — zero-width, bidi, soft hyphen, tags.</summary>
    public static int FormatCharacters(string text) => text.EnumerateRunes().Count(IsFormat);

    private static bool IsFormat(Rune rune) => Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format;

    /// <summary>One code point as NFKC writes it, for the blocks that fold to ASCII; itself otherwise.</summary>
    private static string Folded(Rune rune) => rune.Value switch
    {
        >= FullWidthFirst and <= FullWidthLast => ((char)(rune.Value - FullWidthOffset)).ToString(),
        >= MathLettersFirst and <= MathLettersLast => MathLetter(rune.Value - MathLettersFirst),
        >= MathDigitsFirst and <= MathDigitsLast => ((char)('0' + ((rune.Value - MathDigitsFirst) % 10))).ToString(),
        _ => Rune.GetUnicodeCategory(rune) == UnicodeCategory.SpaceSeparator ? " " : Single(rune),
    };

    private static string Single(Rune rune) => Singles.TryGetValue(rune.Value, out var folded) ? folded : rune.ToString();

    /// <summary>The mathematical alphabets: thirteen runs of A–Z then a–z from U+1D400 (a reserved hole folds like its neighbours — it is never written).</summary>
    private static string MathLetter(int offset)
    {
        var within = offset % 52;

        return ((char)(within < 26 ? 'A' + within : 'a' + (within - 26))).ToString();
    }

    /// <summary>U+FF01–U+FF5E, FULLWIDTH EXCLAMATION MARK to FULLWIDTH TILDE: each is its ASCII character plus U+FEE0.</summary>
    private const int FullWidthFirst = 0xFF01;
    private const int FullWidthLast = 0xFF5E;
    private const int FullWidthOffset = 0xFEE0;

    /// <summary>MATHEMATICAL BOLD CAPITAL A to MATHEMATICAL MONOSPACE SMALL Z.</summary>
    private const int MathLettersFirst = 0x1D400;
    private const int MathLettersLast = 0x1D6A3;

    /// <summary>MATHEMATICAL BOLD DIGIT ZERO to MATHEMATICAL MONOSPACE DIGIT NINE: five runs of 0–9.</summary>
    private const int MathDigitsFirst = 0x1D7CE;
    private const int MathDigitsLast = 0x1D7FF;

    /// <summary>The single code points NFKC folds to ASCII outside the runs above (UnicodeData.txt's compatibility decompositions).</summary>
    private static readonly Dictionary<int, string> Singles = new()
    {
        // Small form variants.
        [0xFE50] = ",",
        [0xFE52] = ".",
        [0xFE54] = ";",
        [0xFE55] = ":",
        [0xFE56] = "?",
        [0xFE57] = "!",
        [0xFE59] = "(",
        [0xFE5A] = ")",
        [0xFE5B] = "{",
        [0xFE5C] = "}",
        [0xFE5F] = "#",
        [0xFE60] = "&",
        [0xFE61] = "*",
        [0xFE62] = "+",
        [0xFE63] = "-",
        [0xFE64] = "<",
        [0xFE65] = ">",
        [0xFE66] = "=",
        [0xFE68] = "\\",
        [0xFE69] = "$",
        [0xFE6A] = "%",
        [0xFE6B] = "@",
        // Super- and subscript digits and signs.
        [0x00B2] = "2",
        [0x00B3] = "3",
        [0x00B9] = "1",
        [0x2070] = "0",
        [0x2071] = "i",
        [0x2074] = "4",
        [0x2075] = "5",
        [0x2076] = "6",
        [0x2077] = "7",
        [0x2078] = "8",
        [0x2079] = "9",
        [0x207A] = "+",
        [0x2080] = "0",
        [0x2081] = "1",
        [0x2082] = "2",
        [0x2083] = "3",
        [0x2084] = "4",
        [0x2085] = "5",
        [0x2086] = "6",
        [0x2087] = "7",
        [0x2088] = "8",
        [0x2089] = "9",
        [0x208A] = "+",
        // Latin ligatures.
        [0xFB00] = "ff",
        [0xFB01] = "fi",
        [0xFB02] = "fl",
        [0xFB03] = "ffi",
        [0xFB04] = "ffl",
        [0xFB05] = "st",
        [0xFB06] = "st",
        // Dot leaders.
        [0x2024] = ".",
        [0x2025] = "..",
        [0x2026] = "...",
    };
}
