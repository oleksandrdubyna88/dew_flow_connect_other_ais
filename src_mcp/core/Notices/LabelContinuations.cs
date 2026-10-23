using System.Globalization;
using System.Text;

namespace CoaiMcp.Core.Notices;

/// <summary>
/// A credential label that ENDS a line, and the value that continues on the next — taken out whole, the layout kept.
/// </summary>
/// <remarks>
/// <para><b>Why a pass of its own.</b> The labelled rule is a ONE-LINE shape: its value class excludes every
/// white-space character, a line break included, so <c>"api_key":</c> followed by its value on the next line was
/// served whole — and so was a YAML block scalar's second line, and a folded header's value. Letting the regex
/// joiner cross a line was evaluated and refused (PLAN_redaction_reaches_a_value_on_the_next_line.md, D2): in
/// <c>service:</c> LF <c>  api_key: FAKE</c> the leftmost match would start at the ordinary word <c>service</c>,
/// take <c>api_key:</c> for its value and never see the credential. A regex cannot know a label names a
/// credential and cannot compare indentation; this pass can do both.</para>
/// <para><b>The contract is <c>src_vs_code/src/labelContinuations.ts</c>, byte for byte</b>, as
/// <see cref="PrivateKeyBlocks"/> is <c>privateKeyBlocks.ts</c>: both halves redact the notices file, and the
/// notice road runs this pass BEFORE the strip that glues the lines (<see cref="Redaction"/>, <c>Passes</c>).</para>
/// <para><b>The matching model.</b> At every line break the pass reads the line it has just passed, from its END
/// backwards: at most four trailing spaces, an optional <c>\</c>, an optional YAML block indicator
/// (<c>|</c> or <c>&gt;</c>, a sign, a digit) or an opening quote run, at most 32 spaces, <c>:</c> or <c>=</c>, at most
/// four spaces, an optional quote run, and the label — the LAST 64 label characters, cut on the left to begin at a
/// letter, which is what the regex's leftmost start produces. The label must name a credential
/// (<see cref="CredentialWords.NamesACredential"/>). Five kinds of evidence let it reach the next line, and with
/// none of them the text is untouched:</para>
/// <list type="bullet">
/// <item>the next line is indented DEEPER than the label's line (a tab counts one) — YAML, nested JSON, folded headers;</item>
/// <item>the next line begins, after its indentation, with a quote — pretty JSON with no indent;</item>
/// <item>the label's line ended with <c>\</c> — a shell continuation;</item>
/// <item>the label's line ended with a block indicator — every following line that is BLANK or indented deeper is
/// value, until the first non-blank line that is not, or the end of the text;</item>
/// <item>the label's line ended with a triple quote — every following line is value until the line holding the
/// closing triple quote (kept, its text before the quotes masked), or the end of the text.</item>
/// </list>
/// <para><b>A plain continuation declines when the next line is itself a label assignment</b>: a credential-named
/// CONTAINER (<c>credentials:</c> LF <c>  password: FAKE</c>) must leave its child to the labelled rule, which needs
/// the child's label to mask its value. A plain continuation crosses exactly ONE line break and never a blank line.
/// Inside a block every line is value, assignment-shaped or not.</para>
/// <para><b>What is replaced.</b> On a plain continuation the indentation and an opening quote run are kept and the
/// value TOKEN — the labelled rule's own class and 4 096 bound — becomes <see cref="Redaction.Redacted"/>. A block's
/// lines each become their indentation plus one placeholder; a blank line stays blank. No line break is added or
/// removed, because a served file's spans are counted in lines.</para>
/// <para><b>Linear by construction, with no length bound of its own.</b> One cursor that only advances; a line's
/// end is found once, its indentation counted once per reading, and the label window re-read at a line's end is
/// about a hundred characters. A block ends by its STRUCTURE or at the end of the text — a cap would leave the tail
/// of a longer scalar readable, which is exactly what the plan round found in the first draft — so there is no
/// "bound reached" state to fail closed from, and no regular expression to time out.</para>
/// </remarks>
internal static class LabelContinuations
{
    /// <summary>The longest label: one letter and 63 more, the labelled rule's own bound.</summary>
    internal const int LabelLimit = 64;

    /// <summary>The longest value token, the labelled rule's own bound — the tail past it stays "where both halves leave it".</summary>
    internal const int ValueLimit = 4096;

    private const int TrailingGap = 4;
    private const int AfterJoinerGap = 32;
    private const int BeforeJoinerGap = 4;
    private const int QuoteRun = 3;
    private const string TripleDouble = "\"\"\"";
    private const string TripleSingle = "'''";

    /// <summary>One line: where it starts, where its text ends (the break, or the end of the text) and where the next one starts.</summary>
    private readonly record struct Line(int Start, int End, int Next)
    {
        public bool HasBreak(string text) => End < text.Length;
    }

    /// <summary>What the end of a label's line asks for.</summary>
    private enum Opening
    {
        Plain,
        Indicator,
        TripleDouble,
        TripleSingle,
    }

    /// <summary>
    /// What a label's line ends with: the opening, whether a <c>\</c> ended it, and whether the separator was <c>=</c> — a code or
    /// config ASSIGNMENT, which never opens a block (see <see cref="Continue"/>).
    /// </summary>
    private readonly record struct Ending(Opening Opening, bool Backslash, bool Assignment);

    /// <summary>The text with every continued credential value replaced, line breaks kept.</summary>
    public static string Redact(string text)
    {
        if (text.AsSpan().IndexOfAny('\r', '\n') < 0)
        {
            return text;
        }

        var writer = new Writer(text);
        var at = 0;
        while (at < text.Length)
        {
            var line = LineAt(text, at);
            at = line.HasBreak(text) && TryReadEnding(text, line, out var ending)
                ? Continue(writer, text, line, ending)
                : line.Next;
        }

        return writer.Finish();
    }

    /// <summary>Applies the evidence of one label's line and answers where the scan goes on.</summary>
    /// <remarks>
    /// A label whose separator is <c>=</c> is an ASSIGNMENT — <c>WithoutTheKey =</c>, <c>type KeyRead =</c>,
    /// <c>TheRoundsKey = """</c> — and what follows it in code is an expression, not a secret scalar. It never opens a
    /// block: it takes AT MOST the one next line, and only when that line is shaped like a value
    /// (<see cref="IsValueShaped"/>); an opening indicator or triple quote on its line then counts as evidence, since
    /// <c>password = """</c> LF <c>FAKE</c> is the TOML shape. The <c>:</c> labels (YAML, JSON, headers) keep their blocks.
    /// </remarks>
    private static int Continue(Writer writer, string text, Line line, Ending ending)
    {
        switch (ending.Assignment ? Opening.Plain : ending.Opening)
        {
            case Opening.Indicator:
                return MaskIndented(writer, text, line.Next, IndentEnd(text, line.Start, line.End) - line.Start);
            case Opening.TripleDouble:
                return MaskTriple(writer, text, line.Next, TripleDouble);
            case Opening.TripleSingle:
                return MaskTriple(writer, text, line.Next, TripleSingle);
            default:
                return MaskPlain(writer, text, line, ending);
        }
    }

    // ---------- the label's line, read from its end ----------

    /// <summary>Reads <c>label ["'] [ \t]{0,4} [:=] [ \t]{0,32} (indicator | quote run)? \? [ \t]{0,4}</c> backwards from the line's end.</summary>
    private static bool TryReadEnding(string text, Line line, out Ending ending)
    {
        ending = default;
        var floor = line.Start;
        var at = SkipBack(text, line.End, floor, TrailingGap, IsBlank);
        var backslash = at > floor && text[at - 1] == '\\';
        at -= backslash ? 1 : 0;
        var opening = ReadOpening(text, floor, ref at);
        at = SkipBack(text, at, floor, AfterJoinerGap, IsBlank);
        if (at <= floor || text[at - 1] is not (':' or '='))
        {
            return false;
        }

        var assignment = text[at - 1] == '=';
        at = SkipBack(text, at - 1, floor, BeforeJoinerGap, IsBlank);
        at = SkipBack(text, at, floor, QuoteRun, IsQuote);
        if (!NamesACredential(text, floor, at))
        {
            return false;
        }

        ending = new Ending(opening, backslash, assignment);

        return true;
    }

    /// <summary>A block indicator (<c>[|&gt;][-+]?[0-9]?</c>) or an opening quote run at the end; moves <paramref name="at"/> before it.</summary>
    private static Opening ReadOpening(string text, int floor, ref int at)
    {
        var probe = at;
        probe -= probe > floor && char.IsAsciiDigit(text[probe - 1]) ? 1 : 0;
        probe -= probe > floor && text[probe - 1] is '-' or '+' ? 1 : 0;
        if (probe > floor && text[probe - 1] is '|' or '>')
        {
            at = probe - 1;

            return Opening.Indicator;
        }

        if (at <= floor || !IsQuote(text[at - 1]))
        {
            return Opening.Plain;
        }

        var quote = text[at - 1];
        var run = 0;
        while (run < QuoteRun && at - run > floor && text[at - run - 1] == quote)
        {
            run++;
        }

        at -= run;

        return run < QuoteRun ? Opening.Plain : quote == '"' ? Opening.TripleDouble : Opening.TripleSingle;
    }

    /// <summary>Whether the label ending at <paramref name="labelEnd"/> — its last 64 label characters, cut to begin at a letter — names a credential.</summary>
    private static bool NamesACredential(string text, int floor, int labelEnd)
    {
        var start = labelEnd;
        while (start > floor && labelEnd - start < LabelLimit && IsLabelCharacter(text[start - 1]))
        {
            start--;
        }

        while (start < labelEnd && !char.IsAsciiLetter(text[start]))
        {
            start++;
        }

        return start < labelEnd && CredentialWords.NamesACredential(text.Substring(start, labelEnd - start));
    }

    // ---------- the plain continuation ----------

    /// <summary>
    /// Masks the value on the line after a label's line, when there is evidence and the line is not a child assignment —
    /// the value token, or, for a YAML list item, the value after each item's dash. Answers where the scan goes on.
    /// </summary>
    private static int MaskPlain(Writer writer, string text, Line label, Ending ending)
    {
        var next = LineAt(text, label.Next);
        var indentEnd = IndentEnd(text, next.Start, next.End);
        var opened = ending.Assignment && ending.Opening != Opening.Plain;
        if (indentEnd >= next.End
            || !(opened || HasEvidence(text, label, indentEnd - next.Start, text[indentEnd], ending.Backslash))
            || IsAssignment(text, indentEnd, next.End)
            || (ending.Assignment && !IsValueShaped(text, indentEnd, next.End)))
        {
            return label.Next;
        }

        if (!ending.Assignment && TryListValue(text, indentEnd, next.End, out _))
        {
            return MaskListItems(writer, text, next, indentEnd - next.Start);
        }

        MaskValueToken(writer, text, indentEnd, next.End);

        // The continuation line is read again as a label's line of its own: it may end in one.
        return label.Next;
    }

    /// <summary>The quote run (up to three) and then the value token — the labelled rule's own class and bound — starting at <paramref name="from"/>.</summary>
    private static void MaskValueToken(Writer writer, string text, int from, int to)
    {
        var valueStart = from;
        while (valueStart < to && valueStart - from < QuoteRun && IsQuote(text[valueStart]))
        {
            valueStart++;
        }

        var valueEnd = valueStart;
        while (valueEnd < to && valueEnd - valueStart < ValueLimit && IsValueCharacter(text[valueEnd]))
        {
            valueEnd++;
        }

        if (valueEnd > valueStart)
        {
            writer.Mask(valueStart, valueEnd);
        }
    }

    private static bool HasEvidence(string text, Line label, int nextIndent, char first, bool backslash) =>
        backslash || IsQuote(first) || nextIndent > IndentEnd(text, label.Start, label.End) - label.Start;

    /// <summary>
    /// Whether a line (after its indentation) reads as ONE value, not as code: an optional quote, a single token with no white
    /// space, quote or bracket inside — so never <c>new X(…)</c>, <c>| { … }</c>, <c>[a, b]</c>, <c>(expr)</c>, a heredoc or a
    /// raw-string opener — an optional closing quote, and at most a trailing <c>,</c> or <c>;</c>.
    /// </summary>
    private static bool IsValueShaped(string text, int from, int to)
    {
        var end = SkipBackBlanks(text, from, to);
        end -= end > from && text[end - 1] is ',' or ';' ? 1 : 0;
        var start = from + (from < end && IsQuote(text[from]) ? 1 : 0);
        end -= end > start && IsQuote(text[end - 1]) ? 1 : 0;
        if (end <= start || text[start] is '|' or '<')
        {
            return false;
        }

        for (var at = start; at < end; at++)
        {
            if (IsJsSpace(text[at]) || IsQuote(text[at]) || text[at] is '(' or ')' or '[' or ']' or '{' or '}')
            {
                return false;
            }
        }

        return true;
    }

    private static int SkipBackBlanks(string text, int floor, int to)
    {
        var end = to;
        while (end > floor && IsBlank(text[end - 1]))
        {
            end--;
        }

        return end;
    }

    /// <summary>A YAML list item: a dash, then white space, then something. <paramref name="valueFrom"/> is where the item's value starts.</summary>
    private static bool TryListValue(string text, int indentEnd, int to, out int valueFrom)
    {
        valueFrom = indentEnd + 1;
        if (indentEnd + 1 >= to || text[indentEnd] != '-' || !IsBlank(text[indentEnd + 1]))
        {
            return false;
        }

        valueFrom = IndentEnd(text, indentEnd + 1, to);

        return valueFrom < to;
    }

    /// <summary>
    /// A YAML list under a credential label: every consecutive item of the first item's indentation has the value after its
    /// dash replaced and the dash kept; an item that is itself an assignment, a line that is not an item and a deeper or shallower
    /// line end the list. Answers the first line not consumed.
    /// </summary>
    private static int MaskListItems(Writer writer, string text, Line first, int itemIndent)
    {
        var line = first;
        while (true)
        {
            var indentEnd = IndentEnd(text, line.Start, line.End);
            if (indentEnd - line.Start != itemIndent || !TryListValue(text, indentEnd, line.End, out var valueFrom) || IsAssignment(text, valueFrom, line.End))
            {
                return line.Start;
            }

            MaskValueToken(writer, text, valueFrom, line.End);
            if (!line.HasBreak(text))
            {
                return line.Next;
            }

            line = LineAt(text, line.Next);
        }
    }

    /// <summary>Whether a line (after its indentation) is itself <c>["']? label ["']{0,3} [ \t]{0,4} [:=]</c> — and not base64 padding.</summary>
    private static bool IsAssignment(string text, int from, int to)
    {
        var at = from;
        at += at < to && IsQuote(text[at]) ? 1 : 0;
        if (at >= to || !char.IsAsciiLetter(text[at]))
        {
            return false;
        }

        var labelEnd = at;
        while (labelEnd < to && labelEnd - at < LabelLimit && IsLabelCharacter(text[labelEnd]))
        {
            labelEnd++;
        }

        if (labelEnd < to && IsLabelCharacter(text[labelEnd]))
        {
            return false;
        }

        at = labelEnd;
        while (at < to && at - labelEnd < QuoteRun && IsQuote(text[at]))
        {
            at++;
        }

        var gap = at;
        while (at < to && at - gap < BeforeJoinerGap && IsBlank(text[at]))
        {
            at++;
        }

        return at < to && text[at] is ':' or '=' && !(text[at] == '=' && at + 1 < to && text[at + 1] == '=');
    }

    // ---------- the blocks ----------

    /// <summary>A block scalar: every following line that is blank or deeper than the label's line is value. Answers where the block ended.</summary>
    private static int MaskIndented(Writer writer, string text, int from, int labelIndent)
    {
        var at = from;
        while (at < text.Length)
        {
            var line = LineAt(text, at);
            var indentEnd = IndentEnd(text, line.Start, line.End);
            if (indentEnd < line.End && indentEnd - line.Start <= labelIndent)
            {
                return at;
            }

            if (indentEnd < line.End)
            {
                writer.Mask(indentEnd, line.End);
            }

            at = line.Next;
        }

        return text.Length;
    }

    /// <summary>A triple-quoted value: every following line is value up to the line that holds the closing quotes. Answers that line, to be read as an ordinary one.</summary>
    private static int MaskTriple(Writer writer, string text, int from, string quotes)
    {
        var at = from;
        while (at < text.Length)
        {
            var line = LineAt(text, at);
            var close = text.IndexOf(quotes, line.Start, line.End - line.Start, StringComparison.Ordinal);
            var maskEnd = close >= 0 ? close : line.End;
            var indentEnd = IndentEnd(text, line.Start, maskEnd);
            if (indentEnd < maskEnd)
            {
                writer.Mask(indentEnd, maskEnd);
            }

            if (close >= 0)
            {
                return line.Start;
            }

            at = line.Next;
        }

        return text.Length;
    }

    // ---------- lines and characters ----------

    private static Line LineAt(string text, int start)
    {
        var found = text.AsSpan(start).IndexOfAny('\r', '\n');
        var end = found < 0 ? text.Length : start + found;
        var next = end;
        if (end < text.Length)
        {
            next = text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? end + 2 : end + 1;
        }

        return new Line(start, end, next);
    }

    private static int IndentEnd(string text, int from, int to)
    {
        var at = from;
        while (at < to && IsBlank(text[at]))
        {
            at++;
        }

        return at;
    }

    private static int SkipBack(string text, int from, int floor, int limit, Func<char, bool> accepts)
    {
        var at = from;
        while (at > floor && from - at < limit && accepts(text[at - 1]))
        {
            at--;
        }

        return at;
    }

    private static bool IsBlank(char c) => c is ' ' or '\t';

    private static bool IsQuote(char c) => c is '"' or '\'';

    private static bool IsLabelCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-';

    /// <summary>The labelled rule's value class: not white space (JavaScript's <c>\s</c>), and none of <c>"',;)}</c>.</summary>
    private static bool IsValueCharacter(char c) => !IsJsSpace(c) && c is not ('"' or '\'' or ',' or ';' or ')' or '}');

    /// <summary>JavaScript's <c>\s</c>, as <see cref="Redaction"/> spells it: TAB LF VT FF CR, every <c>Zs</c>, U+2028, U+2029 and U+FEFF (by number).</summary>
    private static bool IsJsSpace(char c) =>
        c is '\t' or '\n' or '\v' or '\f' or '\r' || c == (char)0xFEFF
        || char.GetUnicodeCategory(c) is UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;

    /// <summary>Copies the text through, with ranges replaced by one placeholder each; the cursor of the replacements only advances.</summary>
    private sealed class Writer(string text)
    {
        private readonly StringBuilder _output = new(text.Length);
        private int _written;
        private bool _changed;

        public void Mask(int from, int to)
        {
            _output.Append(text, _written, from - _written).Append(Redaction.Redacted);
            _written = to;
            _changed = true;
        }

        public string Finish() => !_changed
            ? text
            : _output.Append(text, _written, text.Length - _written).ToString();
    }
}
