using System.Globalization;
using System.Text;

namespace CoaiMcp.Core.Work;

/// <summary>
/// A strict subset of YAML — what <c>v2/_gen/gen-v2.mjs</c> writes and a person can edit: block maps, block lists (a list may sit at the
/// same indent as its parent key), the empty list <c>[]</c>, plain, double-quoted and single-quoted scalars, <c>#</c> comments and one
/// optional leading <c>---</c>. A plain <c>true</c>/<c>false</c> is a boolean, a plain integer is an integer, <c>null</c> and <c>~</c> are null,
/// and everything else plain — and every quoted scalar — is a string.
/// </summary>
/// <remarks>
/// <para><b>Everything else is a named refusal, never a guess:</b> an anchor, alias, tag, flow collection other than <c>[]</c>, block scalar,
/// directive or second document is <c>yaml-forbidden</c>; a duplicate key in one map is <c>yaml-duplicate-key</c>; a file above
/// <see cref="MaxBytes"/> is <c>yaml-too-large</c>; a tab in indentation, a line that is no <c>key: value</c> and a quote that does not close are <c>yaml-syntax</c>.
/// A file of this subset is also valid YAML, so any tool reads what this reader accepts.</para>
/// <para><b>Pure</b>: text in, a value out. The parser holds its state in a private class, one per read, so the reader itself can be shared.</para>
/// </remarks>
public sealed class StrictYamlReader : IYamlReader
{
    /// <summary>The longest file read, in UTF-8 bytes.</summary>
    public const int MaxBytes = 64 * 1024;

    /// <inheritdoc />
    public YamlRead Read(string text) => new YamlParser(text).Run();
}

/// <summary>One parser, one read.</summary>
internal sealed class YamlParser(string text)
{
    private readonly List<YamlLine> _lines = [];
    private int _at;
    private YamlRead.Refused? _refusal;

    internal YamlRead Run()
    {
        var size = Encoding.UTF8.GetByteCount(text);
        if (size > StrictYamlReader.MaxBytes)
        {
            return new YamlRead.Refused(WorkErrorCodes.YamlTooLarge, $"the file is {size} bytes; at most {StrictYamlReader.MaxBytes} are read", 1);
        }

        if (!SplitLines())
        {
            return _refusal!;
        }

        if (_lines.Count == 0)
        {
            return new YamlRead.Parsed(new YamlScalar(1, string.Empty, YamlScalarKind.Null));
        }

        var root = ParseBlock(_lines[0].Indent);
        if (_refusal is not null)
        {
            return _refusal;
        }

        return _at < _lines.Count
            ? Refuse(WorkErrorCodes.YamlSyntax, "unexpected indentation or a second top-level value", _lines[_at].Number)
            : new YamlRead.Parsed(root!);
    }

    // ---------- lines ----------

    private bool SplitLines()
    {
        var source = text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
        var number = 0;
        var marker = false;
        foreach (var raw in source.Split('\n'))
        {
            number++;
            var line = raw.EndsWith('\r') ? raw[..^1] : raw;
            if (line.Contains('\r'))
            {
                return Fail(WorkErrorCodes.YamlSyntax, "a carriage return inside a line", number);
            }

            if (line.Trim().Length == 0)
            {
                continue;
            }

            var lead = line.Length - line.TrimStart(' ', '\t').Length;
            if (line.AsSpan(0, lead).Contains('\t'))
            {
                return Fail(WorkErrorCodes.YamlSyntax, "a tab in the indentation; indent with spaces", number);
            }

            var indent = lead;
            var content = line[indent..].TrimEnd();
            if (content[0] == '#')
            {
                continue;
            }

            if (indent == 0 && DocumentMarker(content, number, ref marker) is { } handled)
            {
                if (!handled)
                {
                    return false;
                }

                continue;
            }

            _lines.Add(new YamlLine(number, indent, content));
        }

        return true;
    }

    /// <summary>Null when the line is no marker or directive; true when it is the one allowed leading marker (skip it); false after a refusal.</summary>
    private bool? DocumentMarker(string content, int number, ref bool seen)
    {
        if (content[0] == '%')
        {
            return Fail(WorkErrorCodes.YamlForbidden, "a directive (%...) is not read", number);
        }

        var bare = StripComment(content);
        if (bare == "...")
        {
            return Fail(WorkErrorCodes.YamlForbidden, "a document end marker (...) is not read; one document per file", number);
        }

        if (bare != "---")
        {
            return content.StartsWith("---", StringComparison.Ordinal) && content.Length > 3 && content[3] is ' ' or '\t'
                ? Fail(WorkErrorCodes.YamlForbidden, "content after a document marker is not read", number)
                : null;
        }

        if (seen || _lines.Count > 0)
        {
            return Fail(WorkErrorCodes.YamlForbidden, "a second document (---) is not read; one document per file", number);
        }

        seen = true;

        return true;
    }

    // ---------- blocks ----------

    private YamlNode? ParseBlock(int indent)
    {
        var line = _lines[_at];
        if (line.Indent != indent)
        {
            return FailNull(WorkErrorCodes.YamlSyntax, "wrong indentation", line.Number);
        }

        return IsListItem(line.Text) ? ParseList(indent) : ParseMap(indent);
    }

    private static bool IsListItem(string text) => text == "-" || text.StartsWith("- ", StringComparison.Ordinal);

    private YamlNode? ParseMap(int indent)
    {
        var entries = new List<YamlEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var first = _lines[_at].Number;
        while (_at < _lines.Count && _lines[_at].Indent >= indent)
        {
            var line = _lines[_at];
            if (line.Indent > indent)
            {
                return FailNull(WorkErrorCodes.YamlSyntax, "unexpected indentation", line.Number);
            }

            if (IsListItem(line.Text))
            {
                return FailNull(WorkErrorCodes.YamlSyntax, "a list item where a key is expected", line.Number);
            }

            if (SplitKey(line) is not { } split)
            {
                return null;
            }

            if (!seen.Add(split.Key))
            {
                return FailNull(WorkErrorCodes.YamlDuplicateKey, $"the key '{split.Key}' appears twice in one map", line.Number);
            }

            _at++;
            var value = ParseValue(split.Value, line, indent, allowSameIndentList: true);
            if (value is null)
            {
                return null;
            }

            entries.Add(new YamlEntry(split.Key, line.Number, value));
        }

        return new YamlMap(first, entries);
    }

    private YamlNode? ParseList(int indent)
    {
        var items = new List<YamlNode>();
        var first = _lines[_at].Number;
        while (_at < _lines.Count && _lines[_at].Indent == indent && IsListItem(_lines[_at].Text))
        {
            var line = _lines[_at];
            var rest = line.Text.Length > 1 ? line.Text[2..] : string.Empty;
            var item = ParseListItem(line, indent, rest.TrimStart(), rest.Length - rest.TrimStart().Length + 2);
            if (item is null)
            {
                return null;
            }

            items.Add(item);
        }

        if (_at < _lines.Count && _lines[_at].Indent > indent)
        {
            return FailNull(WorkErrorCodes.YamlSyntax, "unexpected indentation after a list item", _lines[_at].Number);
        }

        return new YamlList(first, items);
    }

    private YamlNode? ParseListItem(YamlLine line, int indent, string rest, int offset)
    {
        if (rest.Length == 0 || rest[0] == '#')
        {
            _at++;

            return ParseValue(string.Empty, line, indent, allowSameIndentList: false);
        }

        if (IsListItem(rest))
        {
            return FailNull(WorkErrorCodes.YamlForbidden, "a list inside a list is not read", line.Number);
        }

        if (LooksLikeEntry(rest))
        {
            // `- key: value` opens a map whose later keys sit under the first key.
            _lines[_at] = new YamlLine(line.Number, indent + offset, rest);

            return ParseMap(indent + offset);
        }

        _at++;

        return ParseInline(rest, line.Number);
    }

    // ---------- keys and values ----------

    private (string Key, string Value)? SplitKey(YamlLine line)
    {
        var text = line.Text;
        if (ReservedKind(text[0]) is { Length: > 0 } what)
        {
            Fail(WorkErrorCodes.YamlForbidden, $"{what} at the start of a key is not read", line.Number);

            return null;
        }

        if (text[0] is '"' or '\'' or '[' or '{')
        {
            Fail(WorkErrorCodes.YamlSyntax, "a key must be a plain word", line.Number);

            return null;
        }

        var colon = KeyColon(text);
        if (colon < 0)
        {
            Fail(WorkErrorCodes.YamlSyntax, $"expected 'key: value', found '{Shorten(text)}'", line.Number);

            return null;
        }

        var key = text[..colon].TrimEnd();
        if (key.Length == 0)
        {
            Fail(WorkErrorCodes.YamlSyntax, "an empty key", line.Number);

            return null;
        }

        return (key, text[(colon + 1)..].TrimStart());
    }

    /// <summary>The index of the colon that ends a key (followed by a space or the end), or -1.</summary>
    private static int KeyColon(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ':' && (i == text.Length - 1 || text[i + 1] == ' '))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool LooksLikeEntry(string rest) => "\"'[{&*!|>#%@`?".IndexOf(rest[0]) < 0 && KeyColon(rest) >= 0;

    private YamlNode? ParseValue(string rest, YamlLine owner, int ownerIndent, bool allowSameIndentList)
    {
        if (rest.Length > 0 && rest[0] != '#')
        {
            return ParseInline(rest, owner.Number);
        }

        if (_at >= _lines.Count)
        {
            return new YamlScalar(owner.Number, string.Empty, YamlScalarKind.Null);
        }

        var next = _lines[_at];
        if (next.Indent > ownerIndent)
        {
            return ParseBlock(next.Indent);
        }

        return allowSameIndentList && next.Indent == ownerIndent && IsListItem(next.Text)
            ? ParseList(ownerIndent)
            : new YamlScalar(owner.Number, string.Empty, YamlScalarKind.Null);
    }

    private YamlNode? ParseInline(string rest, int number)
    {
        if (ReservedKind(rest[0]) is { Length: > 0 } what)
        {
            return FailNull(WorkErrorCodes.YamlForbidden, $"{what} at the start of a value is not read; quote the text if it is plain text", number);
        }

        if (IsListItem(rest))
        {
            return FailNull(WorkErrorCodes.YamlSyntax, "a list item cannot follow a key on the same line", number);
        }

        if (rest[0] is '[' or '{')
        {
            return StripComment(rest) == "[]"
                ? new YamlList(number, [])
                : FailNull(WorkErrorCodes.YamlForbidden, "a flow collection is not read; only [] is, write a block list", number);
        }

        if (rest[0] is '"' or '\'')
        {
            return ParseQuoted(rest, number);
        }

        return ParsePlain(rest, number);
    }

    /// <summary>What an anchor, alias, tag, block scalar or reserved indicator at the start of a key or value is called; empty for any other character.</summary>
    private static string ReservedKind(char first) => first switch
    {
        '&' => "an anchor (&)",
        '*' => "an alias (*)",
        '!' => "a tag (!)",
        '|' or '>' => "a block scalar (| or >)",
        '?' => "a complex key (?)",
        '@' or '`' => $"the reserved indicator {first}",
        _ => string.Empty,
    };

    // ---------- scalars ----------

    private YamlNode? ParsePlain(string rest, int number)
    {
        var plain = StripComment(rest);
        if (plain.EndsWith(':') || plain.Contains(": ", StringComparison.Ordinal))
        {
            return FailNull(WorkErrorCodes.YamlSyntax, $"a ':' inside a plain value; quote it ('{Shorten(plain)}')", number);
        }

        return new YamlScalar(number, plain, KindOf(plain));
    }

    private static YamlScalarKind KindOf(string plain)
    {
        if (plain is "true" or "false")
        {
            return YamlScalarKind.Boolean;
        }

        if (plain is "null" or "~")
        {
            return YamlScalarKind.Null;
        }

        return IsInteger(plain) ? YamlScalarKind.Integer : YamlScalarKind.String;
    }

    private static bool IsInteger(string plain)
    {
        var digits = plain.StartsWith('-') ? plain[1..] : plain;

        return digits.Length > 0
            && digits.All(char.IsAsciiDigit)
            && (digits.Length == 1 || digits[0] != '0')
            && long.TryParse(plain, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
    }

    /// <summary>The text up to a comment: a <c>#</c> at the start or after a space.</summary>
    private static string StripComment(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '#' && (i == 0 || value[i - 1] is ' ' or '\t'))
            {
                return value[..i].TrimEnd();
            }
        }

        return value.TrimEnd();
    }

    private YamlNode? ParseQuoted(string rest, int number)
    {
        var quote = rest[0];
        var value = new StringBuilder();
        var i = 1;
        for (; i < rest.Length; i++)
        {
            var c = rest[i];
            if (c == quote && quote == '\'' && i + 1 < rest.Length && rest[i + 1] == '\'')
            {
                value.Append('\'');
                i++;
            }
            else if (c == quote)
            {
                break;
            }
            else if (c == '\\' && quote == '"')
            {
                if (!Escape(rest, ref i, value, number))
                {
                    return null;
                }
            }
            else
            {
                value.Append(c);
            }
        }

        if (i >= rest.Length)
        {
            return FailNull(WorkErrorCodes.YamlSyntax, "a quoted value that does not end on its line", number);
        }

        var after = rest[(i + 1)..].Trim();

        return after.Length == 0 || after[0] == '#'
            ? new YamlScalar(number, value.ToString(), YamlScalarKind.String)
            : FailNull(WorkErrorCodes.YamlSyntax, $"text after a closing quote: '{Shorten(after)}'", number);
    }

    private bool Escape(string rest, ref int i, StringBuilder value, int number)
    {
        if (i + 1 >= rest.Length)
        {
            return Fail(WorkErrorCodes.YamlSyntax, "a backslash at the end of a quoted value", number);
        }

        var code = rest[++i];
        switch (code)
        {
            case '"' or '\\' or '/':
                value.Append(code);
                return true;
            case 'b':
                value.Append('\b');
                return true;
            case 'f':
                value.Append('\f');
                return true;
            case 'n':
                value.Append('\n');
                return true;
            case 'r':
                value.Append('\r');
                return true;
            case 't':
                value.Append('\t');
                return true;
            case 'u' when i + 4 < rest.Length && ushort.TryParse(rest.AsSpan(i + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var unit):
                value.Append((char)unit);
                i += 4;
                return true;
            default:
                return Fail(WorkErrorCodes.YamlSyntax, $"the escape \\{code} is not one of \\\" \\\\ \\/ \\b \\f \\n \\r \\t \\uXXXX", number);
        }
    }

    // ---------- refusals ----------

    /// <summary>Records the first refusal and answers false, so a caller can return it as a bool.</summary>
    private bool Fail(string code, string message, int line)
    {
        _refusal ??= new YamlRead.Refused(code, $"line {line}: {message}", line);

        return false;
    }

    private YamlNode? FailNull(string code, string message, int line)
    {
        Fail(code, message, line);

        return null;
    }

    private YamlRead.Refused Refuse(string code, string message, int line)
    {
        Fail(code, message, line);

        return _refusal!;
    }

    private static string Shorten(string value) => value.Length <= 40 ? value : value[..40] + "...";
}

/// <summary>One content line: its number, its indent in spaces and its text without the indent (comments of a whole line are already gone).</summary>
internal readonly record struct YamlLine(int Number, int Indent, string Text);
