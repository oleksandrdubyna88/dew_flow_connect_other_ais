using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Notices;

/// <summary>
/// One string, made fit to be written down — the server's half of <c>safeText</c>.
/// </summary>
/// <remarks>
/// <para><b>The contract is <c>src_vs_code/src/notifications.ts</c>, byte for byte.</b> Both halves
/// of the product write a notices file the extension reads, and a redactor that disagrees with the
/// other one is a secret on disk on one path and not the other. So this file is a PORT, not a
/// re-design: the same patterns with the same bounds, applied in the same order, cut at the same
/// limits with the same suffix. Where .NET and JavaScript genuinely differ the difference is named
/// at the pattern that carries it, with what was measured and what was done about it.</para>
/// <para><b>Five things were measured, not assumed</b> (2026-09-21, node 24 against .NET 10):
/// <list type="number">
/// <item><c>\b</c> is Unicode-aware here and ASCII in JavaScript without the <c>u</c> flag, so
/// <c>парольsk-…</c>, <c>Tokenßsk-…</c> and a key glued to a combining mark, an ordinal indicator,
/// an Arabic digit or a full-width letter are redacted by the extension and were NOT redacted by a
/// <c>\b</c> port — the dangerous direction. The ASCII boundary the extension applies is spelled
/// as a lookbehind, which is what forces the three patterns that carry it off
/// <c>NonBacktracking</c>.</item>
/// <item><c>\s</c> here contains U+0085 and not U+FEFF; JavaScript's is the reverse. A password
/// carrying a NEL survived a <c>\s</c> port. It is spelled out.</item>
/// <item><c>RegexOptions.IgnoreCase</c> under the invariant culture pairs <c>k</c> with U+212A
/// KELVIN SIGN; JavaScript's <c>i</c> without <c>u</c> pairs only ASCII. The case pairs are spelled
/// out and no pattern uses <c>IgnoreCase</c>.</item>
/// <item><c>NonBacktracking</c> refuses the bearer pattern outright: its three-word alternation
/// times <c>{8,4096}</c> is unrolled into an automaton it sizes at 20 570 nodes against a ceiling of
/// 10 000. The two patterns with a <c>{1,4096}</c> value and no boundary build in under 30 ms, run
/// <c>NonBacktracking</c>, and were measured to produce the same output as the backtracking engine
/// on every one of 395 cases.</item>
/// <item>The remaining differences are in the LINE, not the text — see
/// <see cref="ServerNoticeLine"/>.</item>
/// </list></para>
/// </remarks>
public static partial class Redaction
{
    /// <summary>What a title may be before it is cut. 1000, the extension's deliberate deviation from 200.</summary>
    public const int TitleLimit = 1000;

    /// <summary>What a detail may be before it is cut — exception text, an HTTP body, a stack.</summary>
    public const int DetailLimit = 4096;

    /// <summary>What replaces a secret. Visible, so a reader knows something was taken out rather than missing.</summary>
    public const string Redacted = "[redacted]";

    /// <summary>
    /// Said when a value was cut, AFTER the cut — so a truncated value is longer than its limit.
    /// </summary>
    /// <remarks>
    /// The first character is U+2026 HORIZONTAL ELLIPSIS, one character, not three full stops:
    /// the extension's suffix is <c>'…(truncated)'</c> and a reader that splits the two would be
    /// reading two ledgers. A test pins the code point.
    /// </remarks>
    public const string Truncated = "…(truncated)";

    /// <summary>
    /// JavaScript's <c>\s</c> without the <c>u</c> flag, spelled for .NET.
    /// </summary>
    /// <remarks>
    /// <para>WhiteSpace ∪ LineTerminator: TAB, LF, VT, FF, CR, every <c>Zs</c>, U+2028, U+2029 and
    /// U+FEFF. Measured: .NET's own <c>\s</c> admits U+0085 (NEL) and refuses U+FEFF (ZWNBSP), and
    /// JavaScript does the opposite — so <c>https://u:p‹NEL›@host</c> was redacted by the extension
    /// and not by a port that wrote <c>\s</c>. The one code point no category isolates is written as
    /// a regex escape in a REGULAR string — a <c>\u</c> escape in this repository has twice reached
    /// disk as the raw character, and a raw U+FEFF inside a pattern is invisible to every reader.</para>
    /// </remarks>
    private const string JsSpace = @"\t\n\v\f\r\p{Zs}\p{Zl}\p{Zp}" + "\\uFEFF";

    /// <summary>
    /// JavaScript's <c>\b</c> before a word character: the previous character is not
    /// <c>[A-Za-z0-9_]</c>, or there is none. Every pattern that starts on a letter uses it.
    /// </summary>
    private const string NotAsciiWordBefore = @"(?<![A-Za-z0-9_])";

    private const string UrlAuthorityPattern =
        NotAsciiWordBefore + @"([A-Za-z][A-Za-z0-9+.-]{0,15}://)[^" + JsSpace + @"/@:]{1,256}:[^" + JsSpace + @"/@]{1,256}@";

    private const string BearerPattern =
        NotAsciiWordBefore + @"([Bb][Ee][Aa][Rr][Ee][Rr]|[Bb][Aa][Ss][Ii][Cc]|[Tt][Oo][Kk][Ee][Nn])[ \t]+[A-Za-z0-9._~+/=-]{8,4096}";

    private const string VendorPattern =
        NotAsciiWordBefore + @"(sk-|ghp_|gho_|ghu_|ghs_|github_pat_|xox[baprs]-)[A-Za-z0-9._-]{8,512}";

    private const string ParameterPattern =
        @"([?&#])([^" + JsSpace + @"=&#]{1,64})=([^" + JsSpace + @"&#]{1,4096})";

    private const string LabelledPattern =
        @"([A-Za-z][A-Za-z0-9_.-]{0,63})(""?[ \t]{0,4}[:=][ \t]{0,4}""?)([^" + JsSpace + @"""',;)}]{1,4096})";

    /// <summary>Why the ASCII word boundary keeps three patterns off <c>NonBacktracking</c>.</summary>
    private const string BoundaryReason =
        "NOT NonBacktracking because: the ASCII word boundary JavaScript applies is a lookbehind, "
        + "which NonBacktracking does not support; .NET's \\b is Unicode-aware and was measured to miss "
        + "a key glued to Cyrillic, ß, a combining mark or a full-width letter. Bounded instead by every "
        + "quantifier carrying an explicit upper bound, no nesting and no backreference: a backtracking "
        + "search retries at most the bound per start position, measured at 130 ms or less over 200 KB "
        + "of adversarial input.";

    /// <summary>Why the bearer pattern would be off it even without the boundary.</summary>
    private const string AutomatonReason =
        "NOT NonBacktracking because: it unrolls this pattern's three-word alternation times {8,4096} "
        + "into an automaton it sizes at 20 570 nodes against its ceiling of 10 000 and refuses it at "
        + "construction (measured) — and the ASCII boundary is a lookbehind besides. Bounded instead by "
        + "every quantifier carrying an explicit upper bound, no nesting and no backreference, measured "
        + "at 130 ms or less over 200 KB of adversarial input.";

    /// <summary>The two patterns that need neither a boundary nor the refused loop carry no reason: they are <c>NonBacktracking</c>.</summary>
    private const string NoReason = "";

    // A URL carrying its own credentials: https://someone:password@host
    // NOT NonBacktracking because: `(?<![A-Za-z0-9_])` is a lookbehind. It is there because .NET's
    // `\b` is Unicode-aware — `парольsk-…` and `Tokenß…` were measured to slip past it — and JavaScript's
    // is ASCII. Every quantifier is bounded ({0,15}, {1,256}, {1,256}), nothing nests, nothing refers
    // back: a backtracking search retries at most the bound per start position.
    // The case pairs are spelled ([A-Za-z]) rather than IgnoreCase, which pairs k with U+212A here.
    [GeneratedRegex(UrlAuthorityPattern, RegexOptions.CultureInvariant)]
    private static partial Regex UrlAuthority();

    // An Authorization header, or anything that spells one out.
    // NOT NonBacktracking because: the lookbehind above, AND `{8,4096}` — NonBacktracking sizes this
    // pattern's automaton at 20 570 nodes and refuses it at construction. The bound is the same one
    // the extension relies on: an explicit upper bound on every repetition, no nesting, no backreference.
    // `[Bb][Ee]…` spells the three words so that JavaScript's ASCII-only `i` is matched exactly;
    // measured, IgnoreCase here would also redact `to‹KELVIN SIGN›en abcdefgh`, which the extension keeps.
    [GeneratedRegex(BearerPattern, RegexOptions.CultureInvariant)]
    private static partial Regex Bearer();

    // Vendor key shapes that are unmistakable on sight.
    // NOT NonBacktracking because: the lookbehind, for the reason given at UrlAuthority. `{8,512}`
    // alone would have built (measured), so the boundary is the whole reason here.
    [GeneratedRegex(VendorPattern, RegexOptions.CultureInvariant)]
    private static partial Regex Vendor();

    // A query or fragment parameter, so its NAME can be judged before its value is kept.
    // NonBacktracking: no boundary, and a `{1,4096}` value on its own builds in 28 ms (measured) —
    // it is the bearer pattern's alternation in front of its loop that breaks the ceiling, not the
    // loop. Measured to give the backtracking engine's exact output on every corpus case.
    [GeneratedRegex(ParameterPattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Parameter();

    // A credential NAMED and then given, anywhere in ordinary text. Applied LAST — see SafeText.
    // NonBacktracking, as at Parameter (builds in 1 ms). The optional quotes are matched without a
    // backreference on purpose, exactly as the extension does: `"?` on each side, so a JSON body
    // redacts as "password": "[redacted]" with the quotes kept — and a backreference is a construct
    // NonBacktracking would have refused, so the extension's own promise is what makes this engine
    // available here at all.
    [GeneratedRegex(LabelledPattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Labelled();

    /// <summary>One redaction pattern and its standing on <c>NonBacktracking</c>, for the test that audits the list.</summary>
    internal sealed record Pattern(string Name, Regex Regex, string WhyNotNonBacktracking);

    /// <summary>
    /// Every pattern that takes a secret out of a string, in the order it runs. A pattern is either
    /// <c>NonBacktracking</c> or carries a reason beginning <c>NOT NonBacktracking because:</c>.
    /// </summary>
    internal static IReadOnlyList<Pattern> Patterns =>
    [
        new("parameter", Parameter(), NoReason),
        new("url-authority", UrlAuthority(), BoundaryReason),
        new("bearer", Bearer(), AutomatonReason),
        new("vendor-key", Vendor(), BoundaryReason),
        new("labelled", Labelled(), NoReason),
    ];

    /// <summary>The three shapes recognisable on sight, in the extension's array order, with their replacements.</summary>
    /// <remarks>
    /// <c>${1}</c> rather than <c>$1</c>: the extension writes <c>$1</c>, and .NET reads the same
    /// two characters the same way here — but .NET also reads <c>$1</c> followed by a digit as
    /// <c>$1x</c> and <c>$</c> before <c>&amp;</c>, <c>'</c>, <c>`</c>, <c>+</c> and <c>_</c> as
    /// something else, so the braces say exactly one thing.
    /// </remarks>
    private static readonly IReadOnlyList<(Regex Pattern, string Replacement)> Secrets =
    [
        (UrlAuthority(), "${1}" + Redacted + "@"),
        (Bearer(), "${1} " + Redacted),
        (Vendor(), "${1}" + Redacted),
    ];

    /// <summary>
    /// Whether a character may be written down at all: code point <c>&gt;= 32</c> and <c>!= 127</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Numbers, not a literal and not a character class.</b> The extension's own comment
    /// records why: spelling the class put a real NUL byte into a source file twice, and git stopped
    /// treating the file as text. <c>CredentialWords.cs</c> in this very folder carries a raw NUL in a
    /// character literal today, for the same reason. No escape, no class, no literal: the numbers.</para>
    /// <para>Decided per UTF-16 code unit where the extension decides per code point, and the answers
    /// are the same: every character outside the surrogate range is its own code point, and both
    /// halves of a surrogate pair are <c>&gt;= 0xD800</c>, so a pair is kept whole here exactly as the
    /// extension keeps its code point, and a lone surrogate is kept on both sides too.</para>
    /// </remarks>
    internal static bool IsPrintable(char unit) => unit >= 32 && unit != 127;

    /// <summary>Which limit a field gets. Everything that is not the long one is held to the short one.</summary>
    public static int LimitFor(string field) => field == "detail" ? DetailLimit : TitleLimit;

    /// <summary>
    /// One string, made fit to be written down. The ORDER is the contract.
    /// </summary>
    /// <remarks>
    /// <para>Control characters go first: they are not an attack anybody is expecting, they are how
    /// a corrupted value produces a line no parser can read back. Then a parameter whose NAME reads
    /// as a credential; then the three shapes recognisable on sight, in array order; then a credential
    /// named and given in ordinary text — LAST, so that an <c>Authorization: Bearer …</c> has already
    /// lost its value by the time this one sees the word. Then the cut, and the suffix after the cut.</para>
    /// <para>Applied to EVERY string field of a record — the line iterates the record's entries
    /// rather than a list of names, so a field added next year is redacted without anybody
    /// remembering to add it.</para>
    /// </remarks>
    public static string SafeText(string value, int limit)
    {
        var printable = new string([.. value.Where(IsPrintable)]);
        var withoutParameters = Parameter().Replace(printable, RedactParameter);
        var redacted = Secrets.Aggregate(withoutParameters, (text, secret) => secret.Pattern.Replace(text, secret.Replacement));
        var named = Labelled().Replace(redacted, RedactLabelled);

        return named.Length <= limit ? named : named[..limit] + Truncated;
    }

    private static string RedactParameter(Match parameter) =>
        CredentialWords.NamesACredential(parameter.Groups[2].Value)
            ? parameter.Groups[1].Value + parameter.Groups[2].Value + "=" + Redacted
            : parameter.Value;

    private static string RedactLabelled(Match labelled) =>
        CredentialWords.NamesACredential(labelled.Groups[1].Value)
            ? labelled.Groups[1].Value + labelled.Groups[2].Value + Redacted
            : labelled.Value;
}
