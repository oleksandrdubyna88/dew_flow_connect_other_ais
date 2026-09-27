using System.Text;

namespace CoaiMcp.Core.Notices;

/// <summary>
/// A PEM private-key block taken out whole, header to footer — the pass that runs before every pattern.
/// </summary>
/// <remarks>
/// <para><b>Why a pass of its own.</b> The five redaction patterns are ONE-LINE shapes, and a private key
/// is base64 on lines of its own — real line breaks in a <c>.pem</c>-like fixture, <c>\n</c> escapes on one
/// line inside a service account's JSON. Nothing named those lines, so a committed
/// <c>service-account.json</c> was served with only its <c>-----BEGIN</c> taken out by the labelled pass
/// and the key body reached the vendor, in the served file and again in every later turn's "served in
/// earlier turns" (found by epic 3's code round, 2026-09-26). Every variant is one shape:
/// <c>-----BEGIN [kind ]PRIVATE KEY[ BLOCK]-----</c> … <c>-----END [kind ]PRIVATE KEY[ BLOCK]-----</c>, the
/// kind being <c>RSA</c>, <c>EC</c>, <c>DSA</c>, <c>OPENSSH</c>, <c>ENCRYPTED</c>, <c>PGP</c> or nothing.</para>
/// <para><b>The contract is <c>src_vs_code/src/privateKeyBlocks.ts</c>, byte for byte</b>, as every pass of
/// <see cref="Redaction"/> is the extension's: both halves redact the notices file, and the parity
/// harness runs both over the same key shapes.</para>
/// <para><b>Procedural and linear, not a pattern.</b> A body between two markers is a lazy or negated
/// loop of tens of kilobytes, and against text made of headers WITHOUT footers every engine searches
/// <c>headers × bound</c> — the .NET side would hit its match ceiling and fail closed, and the
/// TypeScript side, which has no ceiling, would freeze the extension host. Here a header is found by
/// <c>IndexOf</c>, its footer by <c>IndexOf</c> from the furthest point already known to hold none, and a
/// header with no footer in reach consumes the run of key-looking text after it — so every character is
/// examined a bounded number of times whatever the text is shaped like.</para>
/// <para><b>Every line break inside the block is kept</b>, and each line's text becomes one
/// <see cref="Redaction.Redacted"/> after its indentation: a served file's line numbers are what the
/// outline's spans and the reviewer's next request are counted in, and the line count must survive
/// redaction. An escaped one-line block collapses to one placeholder between its markers.</para>
/// <para><b>A header with no footer within <see cref="BodyLimit"/></b> — a notice quoting the first part of a
/// key file, a JSON whose value was cut — loses the run of body-looking characters after it: base64,
/// whitespace, the backslash of an escape, and the <c>Proc-Type:</c>/<c>DEK-Info:</c> lines of an encrypted
/// body. A parser's header constant followed by code loses nothing, because a quote ends the run at once.</para>
/// </remarks>
public static class PrivateKeyBlocks
{
    private const string BeginMark = "-----BEGIN ";
    private const string EndMark = "-----END ";
    private const string PrivateKey = "PRIVATE KEY";
    private const string BlockSuffix = " BLOCK";
    private const string Dashes = "-----";

    /// <summary>What may stand between <c>-----BEGIN </c> and <c>PRIVATE KEY</c>: the kind and its space, or nothing.</summary>
    internal const int KindLimit = 64;

    /// <summary>How far past a header its footer is looked for — an RSA-16384 key is under 13 K characters in PEM, escaped or not.</summary>
    internal const int BodyLimit = 65_536;

    /// <summary>The text with every private-key block's body replaced, line breaks kept.</summary>
    public static string Redact(string text)
    {
        var output = new StringBuilder(text.Length);
        var at = 0;
        var noFooterBefore = 0;
        for (var begin = text.IndexOf(BeginMark, at, StringComparison.Ordinal); begin >= 0; begin = text.IndexOf(BeginMark, at, StringComparison.Ordinal))
        {
            var headerEnd = MarkerEnd(text, begin + BeginMark.Length);
            if (headerEnd < 0)
            {
                output.Append(text, at, begin + 1 - at);
                at = begin + 1;
                continue;
            }

            output.Append(text, at, headerEnd - at);
            var (footer, searchedTo) = FooterAfter(text, headerEnd, noFooterBefore);
            noFooterBefore = searchedTo;
            var bodyEnd = footer >= 0 ? footer : BodyRunEnd(text, headerEnd);
            AppendRedactedLines(output, text, headerEnd, bodyEnd);
            at = footer >= 0 ? AppendFooter(output, text, footer) : bodyEnd;
        }

        output.Append(text, at, text.Length - at);

        return output.ToString();
    }

    /// <summary>The index after <c>[kind ]PRIVATE KEY[ BLOCK]-----</c> starting at <paramref name="kindStart"/>, or -1.</summary>
    private static int MarkerEnd(string text, int kindStart)
    {
        // Looked for within the kind's bound, never to the end of the text: a header that opens
        // nothing must cost the same however much text follows it.
        var window = Math.Min(KindLimit + PrivateKey.Length, text.Length - kindStart);
        var key = window < PrivateKey.Length ? -1 : text.IndexOf(PrivateKey, kindStart, window, StringComparison.Ordinal);
        if (key < 0 || !IsKind(text, kindStart, key))
        {
            return -1;
        }

        var afterKey = key + PrivateKey.Length;
        var afterSuffix = string.CompareOrdinal(text, afterKey, BlockSuffix, 0, BlockSuffix.Length) == 0 ? afterKey + BlockSuffix.Length : afterKey;

        return string.CompareOrdinal(text, afterSuffix, Dashes, 0, Dashes.Length) == 0 ? afterSuffix + Dashes.Length : -1;
    }

    private static bool IsKind(string text, int from, int to)
    {
        for (var i = from; i < to; i++)
        {
            if (!(char.IsAsciiLetterUpper(text[i]) || char.IsAsciiDigit(text[i]) || text[i] == ' '))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The first footer starting within <see cref="BodyLimit"/> of <paramref name="from"/>, and the furthest
    /// index now known to have no footer before it — so the search across many headers stays linear.
    /// </summary>
    private static (int Footer, int SearchedTo) FooterAfter(string text, int from, int noFooterBefore)
    {
        var limit = from + BodyLimit;
        var at = Math.Max(from, noFooterBefore);
        while (at <= limit && at < text.Length)
        {
            var candidate = text.IndexOf(EndMark, at, StringComparison.Ordinal);
            if (candidate < 0 || candidate > limit)
            {
                return (-1, candidate < 0 ? text.Length : candidate);
            }

            if (MarkerEnd(text, candidate + EndMark.Length) >= 0)
            {
                return (candidate, candidate);
            }

            at = candidate + 1;
        }

        return (-1, at);
    }

    /// <summary>Where the run of key-looking text after a footerless header ends, within <see cref="BodyLimit"/>.</summary>
    private static int BodyRunEnd(string text, int from)
    {
        var limit = Math.Min(text.Length, from + BodyLimit);
        var at = from;
        while (at < limit && IsBodyCharacter(text[at]))
        {
            at++;
        }

        return at;
    }

    /// <summary>Base64, the whitespace between lines, the backslash of an escape, and what an encrypted body's own header lines are made of.</summary>
    private static bool IsBodyCharacter(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '=' or ' ' or '\t' or '\r' or '\n' or '\\' or ':' or ',' or '-';

    /// <summary>Every line of the block: its indentation kept, the rest one placeholder, every CR and LF as it was.</summary>
    private static void AppendRedactedLines(StringBuilder output, string text, int from, int to)
    {
        var at = from;
        while (at < to)
        {
            var lineEnd = LineEnd(text, at, to);
            var indent = at;
            while (indent < lineEnd && text[indent] is ' ' or '\t')
            {
                indent++;
            }

            output.Append(text, at, indent - at);
            if (indent < lineEnd)
            {
                output.Append(Redaction.Redacted);
            }

            at = lineEnd;
            while (at < to && text[at] is '\r' or '\n')
            {
                output.Append(text[at++]);
            }
        }
    }

    private static int LineEnd(string text, int from, int to)
    {
        var at = from;
        while (at < to && text[at] is not ('\r' or '\n'))
        {
            at++;
        }

        return at;
    }

    /// <summary>Copies the footer through its closing dashes and answers the index after it.</summary>
    private static int AppendFooter(StringBuilder output, string text, int footer)
    {
        var end = MarkerEnd(text, footer + EndMark.Length);
        output.Append(text, footer, end - footer);

        return end;
    }
}
