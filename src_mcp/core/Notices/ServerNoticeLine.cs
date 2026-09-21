using System.Globalization;
using System.Text;

namespace CoaiMcp.Core.Notices;

/// <summary>
/// The record as one line of JSONL, with the newline — redacted on the way out.
/// </summary>
/// <remarks>
/// <para><b>The bytes are <c>notificationLine</c>'s bytes.</b> The extension reads this file with
/// its own parser, and story 2.4 asserts the persisted bytes against what that parser and
/// serialiser make of them, so "a line the parser accepts" is not the bar — the bar is a line the
/// extension would have WRITTEN from the same record. Three things decide that: the order of the
/// fields, the JSON text of each string, and the text of each number. All three were measured
/// against node 24 on 2026-09-21 and each is explained where it is decided.</para>
///
/// <para><b>Field order — the extension parser's, which is a fixed point of its serialiser.</b>
/// <c>JSON.stringify</c> writes properties in insertion order, so the extension's own records carry
/// the order of whichever constructor built them: <c>noticeRecord</c> puts <c>run, pid</c> straight
/// after <c>code</c>, the storm record puts them after <c>cure</c>, and the <c>NotificationRecord</c>
/// interface declares <c>pid, seq, bound</c> before <c>repo</c>. There is no one "extension order"
/// for a record built by hand. There is exactly one for a record that came THROUGH the extension:
/// <c>parseNotificationLine</c> rebuilds every record as <c>utc, class, source, code</c>, then the
/// thirteen optional strings in its own list order, then <c>pid, seq, bound</c>, then the unknown
/// fields — and <c>notificationLine(parseNotificationLine(line))</c> reproduces that line byte for
/// byte (measured: <c>true</c>). That is the order a server line is held to by the seam leg, and the
/// only order under which the server's line survives the extension's round trip unchanged. So it is
/// the order here: <see cref="Named"/>, in one list, and <c>more</c> after it.</para>
///
/// <para><b>String text — written by hand, because no <c>JavaScriptEncoder</c> can write it.</b>
/// <c>JSON.stringify</c> escapes exactly <c>"</c>, <c>\</c>, the seven short forms and every other
/// code unit below U+0020 (as lowercase <c>\u00xx</c>), plus a LONE surrogate (as lowercase
/// <c>\udxxx</c>); everything else, including U+2028, U+007F, U+FEFF and every astral character, is
/// raw UTF-8. Measured on <see cref="System.Text.Json.Utf8JsonWriter"/>: the default encoder escapes
/// <c>&lt; &gt; &amp; ' + `</c> and every non-ASCII character; <c>UnsafeRelaxedJsonEscaping</c>
/// passes <c>&lt; &gt; &amp; ' +</c>, Cyrillic and umlauts raw but STILL writes an emoji as
/// <c>😀</c>, U+2028/U+2029/U+0085/U+00A0/U+FEFF/U+E000/U+FFFE as uppercase escapes, and
/// turns a lone surrogate into <c>�</c>. An emoji in a reviewer's sentence is ordinary; a lone
/// surrogate is what the 1000-character cut leaves when it lands inside one. Neither encoder is
/// close, so the quoting is ECMA-262's <c>QuoteJSONString</c>, written out in <see cref="Quoted"/>
/// and pinned by a test to the bytes node produced.</para>
///
/// <para><b>Numbers — JavaScript's layout, from .NET's shortest digits.</b> Both runtimes produce the
/// same shortest round-trip digit string; they lay it out differently past 1e21 and below 1e-6
/// (<c>1e+21</c> against <c>1E+21</c>, <c>0.000001</c> against <c>1E-06</c>) and for negative zero
/// (<c>0</c> against <c>-0</c>). <see cref="JsNumber"/> applies ECMA-262's Number::toString rules to
/// the digits .NET gives. Integers are written as integers.</para>
///
/// <para>The redaction happens HERE, at the one road onto the disk, over the record's own entries
/// rather than a list of field names — <c>more</c> first flattened, then every string through
/// <see cref="Redaction.SafeText"/> at its field's limit, numbers untouched.</para>
/// </remarks>
public static class ServerNoticeLine
{
    /// <summary>
    /// The named fields in the one order a line is written — the extension parser's — and how each is read.
    /// </summary>
    private static readonly IReadOnlyList<(string Field, Func<ServerNotice, object?> Value)> Named =
    [
        ("utc", n => n.Utc),
        ("class", n => n.Class),
        ("source", n => n.Source),
        ("code", n => n.Code),
        ("subject", n => n.Subject),
        ("title", n => n.Title),
        ("detail", n => n.Detail),
        ("cure", n => n.Cure),
        ("action", n => n.Action),
        ("offered", n => n.Offered),
        ("answer", n => n.Answer),
        ("run", n => n.Run),
        ("repo", n => n.Repo),
        ("branch", n => n.Branch),
        ("session", n => n.Session),
        ("provider", n => n.Provider),
        ("role", n => n.Role),
        ("pid", n => n.Pid),
        ("seq", n => n.Seq),
        ("bound", n => n.Bound),
    ];

    /// <summary>Every JSON name the record owns — what a <c>more</c> key may not be.</summary>
    internal static readonly IReadOnlySet<string> NamedFields =
        new HashSet<string>(Named.Select(n => n.Field), StringComparer.Ordinal);

    /// <summary>The JSON names in the order they are written, for the test that pins it.</summary>
    internal static IReadOnlyList<string> Order => [.. Named.Select(n => n.Field)];

    private static readonly IReadOnlyDictionary<string, object> NoMore = new Dictionary<string, object>();

    /// <summary>The line, redacted, ending in <c>\n</c>.</summary>
    public static string Of(ServerNotice notice)
    {
        var line = new StringBuilder(256).Append('{');
        var separator = "";
        foreach (var (field, value) in Entries(notice))
        {
            var text = Text(field, value);
            if (Vanished(field, text))
            {
                continue;
            }

            line.Append(separator).Append(Quoted(field)).Append(':').Append(text);
            separator = ",";
        }

        return line.Append('}').Append('\n').ToString();
    }

    /// <summary>The JSON text of a string with nothing left in it.</summary>
    private const string Nothing = "\"\"";

    /// <summary>
    /// Whether a NAMED optional field redacted away to nothing, and is therefore absent.
    /// </summary>
    /// <remarks>
    /// <para>Presence is decided AFTER redaction, which the first version did not do: a field whose
    /// value was empty when the record was built is skipped by <see cref="Entries"/>, but one that
    /// HAD characters and lost all of them — <c>Title</c> holding a single control character — came
    /// out as <c>"title":""</c>. The extension's parser drops an empty optional on the way back in,
    /// so that line is not a fixed point of its own parser: a record read and written again is not
    /// the record that arrived. Measured on the parity harness, which said exactly that. (CodeRabbit,
    /// on the pull request.)</para>
    /// <para>The four required fields cannot reach here empty —
    /// <c>ServerNotice.RequiredAfterRedaction</c> refuses such a record at construction — and
    /// <c>more</c> keeps whatever it has, because the extension's own serialiser does and the two
    /// must answer alike.</para>
    /// </remarks>
    private static bool Vanished(string field, string text) =>
        text == Nothing && NamedFields.Contains(field);

    /// <summary>The named fields that are present, in order, then <c>more</c> flattened after them.</summary>
    private static IEnumerable<(string Field, object Value)> Entries(ServerNotice notice)
    {
        foreach (var (field, read) in Named)
        {
            if (read(notice) is { } value and not "")
            {
                yield return (field, value);
            }
        }

        foreach (var (field, value) in notice.More ?? NoMore)
        {
            yield return (field, value);
        }
    }

    /// <summary>One value as JSON text: a string cleaned at its field's limit and quoted, a number as JavaScript would write it.</summary>
    private static string Text(string field, object value) =>
        value switch
        {
            string text => Quoted(Redaction.SafeText(text, Redaction.LimitFor(field))),
            int whole => whole.ToString(CultureInfo.InvariantCulture),
            long whole => whole.ToString(CultureInfo.InvariantCulture),
            double real => JsNumber(real),
            _ => throw new InvalidOperationException(
                $"{field} is a {value.GetType().Name}, which ServerNotice.More was built to refuse"),
        };

    /// <summary>
    /// ECMA-262 <c>QuoteJSONString</c>: a surrogate pair passes whole, a lone surrogate and every
    /// code unit below U+0020 are escaped in lowercase hex, <c>"</c> and <c>\</c> are escaped, and
    /// nothing else is.
    /// </summary>
    internal static string Quoted(string text)
    {
        var quoted = new StringBuilder(text.Length + 2).Append('"');
        // A `while`, because the step is not always one: a surrogate PAIR is written whole and
        // advances by two. Writing that as a `for` whose body reassigns its own stop variable is
        // what Sonar reports, and it is right — a reader has to find the hidden assignment before
        // the loop means anything.
        var at = 0;
        while (at < text.Length)
        {
            at = AppendUnit(quoted, text, at) + 1;
        }

        return quoted.Append('"').ToString();
    }

    /// <summary>Appends the unit at <paramref name="at"/> — with its partner when it opens a pair — and says where it stopped.</summary>
    private static int AppendUnit(StringBuilder into, string text, int at)
    {
        var unit = text[at];
        if (char.IsHighSurrogate(unit) && at + 1 < text.Length && char.IsLowSurrogate(text[at + 1]))
        {
            into.Append(unit).Append(text[at + 1]);

            return at + 1;
        }

        into.Append(Escaped(unit));

        return at;
    }

    private static string Escaped(char unit) =>
        unit switch
        {
            '"' => "\\\"",
            '\\' => "\\\\",
            '\b' => "\\b",
            '\f' => "\\f",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            _ when unit < ' ' || char.IsSurrogate(unit) => "\\u" + ((int)unit).ToString("x4", CultureInfo.InvariantCulture),
            _ => unit.ToString(),
        };

    /// <summary>
    /// A finite double as <c>JSON.stringify</c> writes it: ECMA-262 Number::toString over the
    /// shortest round-trip digits.
    /// </summary>
    /// <remarks>
    /// With <c>s</c> the digits (no leading or trailing zeros), <c>k</c> their count and <c>n</c>
    /// the decimal exponent such that the value is <c>s × 10^(n−k)</c>: plain digits with zeros
    /// when <c>k ≤ n ≤ 21</c>, a decimal point inside the digits when <c>0 &lt; n ≤ 21</c>, a
    /// <c>0.000…</c> prefix when <c>−6 &lt; n ≤ 0</c>, and <c>d.ddde±x</c> otherwise. Negative zero
    /// is <c>0</c>.
    /// </remarks>
    internal static string JsNumber(double value)
    {
        if (value == 0)
        {
            return "0";
        }

        var (digits, point) = Digits(Math.Abs(value).ToString("R", CultureInfo.InvariantCulture));
        var laid = Layout(digits, point);

        return value < 0 ? "-" + laid : laid;
    }

    /// <summary>The digit string and the decimal exponent out of .NET's <c>R</c> text (<c>1.234E-06</c>, <c>0.0001</c>, <c>37308</c>).</summary>
    private static (string Digits, int Point) Digits(string round)
    {
        var e = round.IndexOf('E');
        var exponent = e < 0 ? 0 : int.Parse(round[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var mantissa = e < 0 ? round : round[..e];
        var dot = mantissa.IndexOf('.');
        var integer = dot < 0 ? mantissa : mantissa[..dot];
        var raw = integer + (dot < 0 ? "" : mantissa[(dot + 1)..]);
        var significant = raw.TrimStart('0');

        return (significant.TrimEnd('0'), integer.Length + exponent - (raw.Length - significant.Length));
    }

    private static string Layout(string s, int n) =>
        n switch
        {
            _ when s.Length <= n && n <= 21 => s + new string('0', n - s.Length),
            > 0 and <= 21 => s[..n] + "." + s[n..],
            > -6 and <= 0 => "0." + new string('0', -n) + s,
            _ => Exponential(s, n - 1),
        };

    private static string Exponential(string s, int e) =>
        (s.Length == 1 ? s : s[..1] + "." + s[1..]) + "e" + (e < 0 ? "-" : "+") + Math.Abs(e).ToString(CultureInfo.InvariantCulture);
}
