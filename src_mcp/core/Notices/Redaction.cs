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

    /// <summary>
    /// How long a BACKTRACKING pattern may search one value before it is given up on.
    /// </summary>
    /// <remarks>
    /// <para>Three of the five patterns could not take <c>NonBacktracking</c> — two need an ASCII
    /// lookbehind and the third also exceeds the engine's node ceiling — so for those three the
    /// bound on the search is a bound somebody has to state. Every repetition in them is already
    /// bounded and nothing nests, and the backtracking cost was measured at 126 ms over fourteen
    /// adversarial 200 KB inputs; a measurement is evidence about the inputs that were tried, and
    /// this is a guarantee about the ones that were not. A reviewer asked for it and was right.</para>
    /// <para>Two seconds is far above anything measured and far below anything a person would call a
    /// hang. It bounds the WHOLE search for one value, not one step of it.</para>
    /// </remarks>
    private const int BacktrackingCeilingMs = 2_000;

    /// <summary>
    /// The ceiling on ONE call, which is what the docstring above used to claim and did not deliver.
    /// </summary>
    /// <remarks>
    /// <para>A reviewer counted what the code actually did: <c>BacktrackingCeilingMs</c> is carried
    /// by three separate patterns and <see cref="Passes"/> runs them one after another, so a value
    /// shaped to drive each of them near its own ceiling blocks for nearly SIX seconds, not two. The
    /// per-pattern ceiling is still there — it is what makes each <c>Replace</c> give up at all —
    /// and this is the bound on the whole call, including the two patterns that run on
    /// <c>NonBacktracking</c> and therefore carry no ceiling of their own.</para>
    /// <para>Checked BETWEEN passes rather than inside one: a pass already stops itself. What was
    /// missing was anything stopping the sequence.</para>
    /// </remarks>
    private static readonly TimeSpan WholeCallCeiling = TimeSpan.FromMilliseconds(BacktrackingCeilingMs);

    // A URL carrying its own credentials: https://someone:password@host
    // NOT NonBacktracking because: `(?<![A-Za-z0-9_])` is a lookbehind. It is there because .NET's
    // `\b` is Unicode-aware — `парольsk-…` and `Tokenß…` were measured to slip past it — and JavaScript's
    // is ASCII. Every quantifier is bounded ({0,15}, {1,256}, {1,256}), nothing nests, nothing refers
    // back: a backtracking search retries at most the bound per start position.
    // The case pairs are spelled ([A-Za-z]) rather than IgnoreCase, which pairs k with U+212A here.
    [GeneratedRegex(UrlAuthorityPattern, RegexOptions.CultureInvariant, BacktrackingCeilingMs)]
    private static partial Regex UrlAuthority();

    // An Authorization header, or anything that spells one out.
    // NOT NonBacktracking because: the lookbehind above, AND `{8,4096}` — NonBacktracking sizes this
    // pattern's automaton at 20 570 nodes and refuses it at construction. The bound is the same one
    // the extension relies on: an explicit upper bound on every repetition, no nesting, no backreference.
    // `[Bb][Ee]…` spells the three words so that JavaScript's ASCII-only `i` is matched exactly;
    // measured, IgnoreCase here would also redact `to‹KELVIN SIGN›en abcdefgh`, which the extension keeps.
    [GeneratedRegex(BearerPattern, RegexOptions.CultureInvariant, BacktrackingCeilingMs)]
    private static partial Regex Bearer();

    // Vendor key shapes that are unmistakable on sight.
    // NOT NonBacktracking because: the lookbehind, for the reason given at UrlAuthority. `{8,512}`
    // alone would have built (measured), so the boundary is the whole reason here.
    [GeneratedRegex(VendorPattern, RegexOptions.CultureInvariant, BacktrackingCeilingMs)]
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
    public static string SafeText(string value, int limit) =>
        WhenRedactionTimesOut(() => Passes(value, limit));

    /// <summary>
    /// FAIL CLOSED: a value whose redaction could not finish is not a value this server may write.
    /// </summary>
    /// <remarks>
    /// <para>The three backtracking patterns carry a match ceiling, and the only way to reach it is
    /// a value shaped to make one of them search for two seconds. What must not happen then is the
    /// tempting thing — returning the value as it arrived, unredacted, because the redactor gave up
    /// on it. A redactor that could not finish does not know whether the text is clean, and text
    /// nobody can vouch for does not go on disk.</para>
    /// <para>It is a method taking a thunk rather than a <c>try</c> inside <see cref="SafeText"/> so
    /// that the branch can be REACHED by a test: a test that had to find an input which actually
    /// times out would be asserting a performance figure, and would stop asserting anything the day
    /// the engine got faster.</para>
    /// <para>The same doctrine the credential list follows: best-effort is right for WRITING a
    /// notice and wrong for deciding what a secret is.</para>
    /// </remarks>
    internal static string WhenRedactionTimesOut(Func<string> redact)
    {
        try
        {
            return redact();
        }
        catch (RegexMatchTimeoutException)
        {
            return Redacted;
        }
    }

    /// <summary>The four passes, in the order the contract fixes, under one deadline.</summary>
    private static string Passes(string value, int limit)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var printable = new string([.. value.Where(IsPrintable)]);
        var withoutParameters = Parameter().Replace(printable, RedactParameter);
        StopIfOverdue(clock);
        var redacted = Secrets.Aggregate(withoutParameters, (text, secret) =>
        {
            StopIfOverdue(clock);

            return secret.Pattern.Replace(text, secret.Replacement);
        });
        StopIfOverdue(clock);
        var named = Labelled().Replace(redacted, RedactLabelled);

        return named.Length <= limit ? named : named[..limit] + Truncated;
    }

    /// <summary>Gives up between passes when the whole call has run out of time.</summary>
    /// <remarks>
    /// It throws the same exception a pattern's own ceiling throws, so there is ONE way this
    /// function fails and one place that answers it — <see cref="WhenRedactionTimesOut"/>, which
    /// fails closed.
    /// </remarks>
    private static void StopIfOverdue(System.Diagnostics.Stopwatch clock)
    {
        if (clock.Elapsed > WholeCallCeiling)
        {
            throw new RegexMatchTimeoutException(
                "the whole redaction", "every pattern in order", WholeCallCeiling);
        }
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
