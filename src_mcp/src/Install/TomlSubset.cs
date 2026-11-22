using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CoaiMcp.Install;

/// <summary>
/// A strict reader of the TOML a Codex <c>config.toml</c> holds — tables, dotted keys, strings, numbers, booleans, date-times, arrays and inline
/// tables — that answers "this is not TOML" rather than guessing (v2 E3.S6). It exists because the installer must refuse a malformed
/// file BEFORE it starts a vendor command, and re-reading the file with the vendor (<c>codex mcp get</c>) on every run would make every run a vendor call.
/// </summary>
/// <remarks>
/// <para>It reads; it never writes. A value it cannot classify is a refusal (the safe direction): the worst a too-strict reader does is leave a file alone and say why.
/// Tables are kept as <see cref="Dictionary{TKey, TValue}"/>, arrays as <see cref="List{T}"/>, strings as <see cref="string"/>; numbers, booleans and date-times
/// as their source text (nothing here needs their value).</para>
/// </remarks>
internal sealed partial class TomlSubset
{
    private readonly string _text;
    private int _at;

    private TomlSubset(string text) => _text = text;

    /// <summary>One answer: the root table, or why the text is not TOML.</summary>
    internal sealed record Outcome(Dictionary<string, object>? Root, string Error);

    /// <summary>Parses <paramref name="text"/>. Never throws for bad input.</summary>
    internal static Outcome Parse(string text)
    {
        try
        {
            return new Outcome(new TomlSubset(text.TrimStart('﻿')).Document(), string.Empty);
        }
        catch (FormatException e)
        {
            return new Outcome(null, e.Message);
        }
    }

    private Dictionary<string, object> Document()
    {
        var root = new Dictionary<string, object>(StringComparer.Ordinal);
        var defined = new HashSet<string>(StringComparer.Ordinal);
        var current = root;
        while (true)
        {
            SkipBlank();
            if (_at >= _text.Length)
            {
                return root;
            }

            if (_text[_at] == '[')
            {
                current = Header(root, defined);
            }
            else
            {
                KeyValue(current);
            }

            EndOfLine();
        }
    }

    private Dictionary<string, object> Header(Dictionary<string, object> root, HashSet<string> defined)
    {
        var isArray = _at + 1 < _text.Length && _text[_at + 1] == '[';
        _at += isArray ? 2 : 1;
        var path = DottedKey();
        Expect(']');
        if (isArray)
        {
            Expect(']');
        }

        var identity = string.Join('\u0001', path);
        if (!isArray && !defined.Add(identity))
        {
            throw Fault($"the table [{string.Join('.', path)}] is defined twice");
        }

        var table = root;
        for (var index = 0; index < path.Count; index++)
        {
            var last = index == path.Count - 1;
            if (!table.TryGetValue(path[index], out var next))
            {
                next = last && isArray ? new List<object>() : new Dictionary<string, object>(StringComparer.Ordinal);
                table[path[index]] = next;
            }

            if (next is List<object> list)
            {
                if (last)
                {
                    var added = new Dictionary<string, object>(StringComparer.Ordinal);
                    list.Add(added);
                    return added;
                }

                next = list.Count > 0 && list[^1] is Dictionary<string, object> inner ? inner : throw Fault($"'{path[index]}' is not a table");
            }

            table = next as Dictionary<string, object> ?? throw Fault($"'{path[index]}' is not a table");
        }

        return table;
    }

    private void KeyValue(Dictionary<string, object> table)
    {
        var path = DottedKey();
        SkipSpaces();
        Expect('=');
        SkipSpaces();
        var value = Value();
        var target = table;
        for (var index = 0; index < path.Count - 1; index++)
        {
            if (!target.TryGetValue(path[index], out var next))
            {
                next = new Dictionary<string, object>(StringComparer.Ordinal);
                target[path[index]] = next;
            }

            target = next as Dictionary<string, object> ?? throw Fault($"'{path[index]}' is not a table");
        }

        if (!target.TryAdd(path[^1], value))
        {
            throw Fault($"the key '{path[^1]}' is defined twice");
        }
    }

    private List<string> DottedKey()
    {
        var parts = new List<string>();
        while (true)
        {
            SkipSpaces();
            parts.Add(Key());
            SkipSpaces();
            if (_at < _text.Length && _text[_at] == '.')
            {
                _at++;
                continue;
            }

            return parts;
        }
    }

    private string Key()
    {
        if (_at >= _text.Length)
        {
            throw Fault("a key was expected at the end of the file");
        }

        if (_text[_at] == '"')
        {
            return BasicString();
        }

        if (_text[_at] == '\'')
        {
            return LiteralString();
        }

        var start = _at;
        while (_at < _text.Length && (char.IsAsciiLetterOrDigit(_text[_at]) || _text[_at] is '_' or '-'))
        {
            _at++;
        }

        return _at > start ? _text[start.._at] : throw Fault($"a key was expected at line {LineOf(start)}");
    }

    private object Value()
    {
        if (_at >= _text.Length)
        {
            throw Fault("a value was expected at the end of the file");
        }

        var c = _text[_at];
        if (c == '"')
        {
            return StartsWithAt("\"\"\"") ? MultiLine("\"\"\"", escapes: true) : BasicString();
        }

        if (c == '\'')
        {
            return StartsWithAt("'''") ? MultiLine("'''", escapes: false) : LiteralString();
        }

        return c switch
        {
            '[' => ArrayValue(),
            '{' => InlineTable(),
            _ => Bare(),
        };
    }

    private List<object> ArrayValue()
    {
        _at++;
        var items = new List<object>();
        while (true)
        {
            SkipBlank();
            if (_at < _text.Length && _text[_at] == ']')
            {
                _at++;
                return items;
            }

            items.Add(Value());
            SkipBlank();
            if (_at < _text.Length && _text[_at] == ',')
            {
                _at++;
                continue;
            }

            SkipBlank();
            Expect(']');
            return items;
        }
    }

    private Dictionary<string, object> InlineTable()
    {
        _at++;
        var table = new Dictionary<string, object>(StringComparer.Ordinal);
        SkipSpaces();
        if (_at < _text.Length && _text[_at] == '}')
        {
            _at++;
            return table;
        }

        while (true)
        {
            KeyValue(table);
            SkipSpaces();
            if (_at < _text.Length && _text[_at] == ',')
            {
                _at++;
                continue;
            }

            Expect('}');
            return table;
        }
    }

    private string Bare()
    {
        var start = _at;
        while (_at < _text.Length && !char.IsWhiteSpace(_text[_at]) && _text[_at] is not (',' or ']' or '}' or '#'))
        {
            _at++;
        }

        var token = _text[start.._at];
        if (token is "true" or "false" || NumberLike().IsMatch(token) || DateLike().IsMatch(token))
        {
            return token;
        }

        throw Fault($"'{token}' at line {LineOf(start)} is not a TOML value");
    }

    private string BasicString()
    {
        _at++;
        var builder = new StringBuilder();
        while (true)
        {
            if (_at >= _text.Length || _text[_at] == '\n' || _text[_at] == '\r')
            {
                throw Fault($"a string is not closed on line {LineOf(_at - 1)}");
            }

            var c = _text[_at++];
            if (c == '"')
            {
                return builder.ToString();
            }

            builder.Append(c == '\\' ? Escape() : c.ToString());
        }
    }

    private string LiteralString()
    {
        _at++;
        var start = _at;
        while (_at < _text.Length && _text[_at] is not ('\'' or '\n' or '\r'))
        {
            _at++;
        }

        if (_at >= _text.Length || _text[_at] != '\'')
        {
            throw Fault($"a literal string is not closed on line {LineOf(start)}");
        }

        return _text[start.._at++];
    }

    private string MultiLine(string quote, bool escapes)
    {
        _at += 3;
        if (_at < _text.Length && _text[_at] == '\r')
        {
            _at++;
        }

        if (_at < _text.Length && _text[_at] == '\n')
        {
            _at++;
        }

        var builder = new StringBuilder();
        while (true)
        {
            if (_at >= _text.Length)
            {
                throw Fault("a multi-line string is not closed");
            }

            if (StartsWithAt(quote))
            {
                _at += 3;
                for (var extra = 0; extra < 2 && _at < _text.Length && _text[_at] == quote[0]; extra++)
                {
                    builder.Append(quote[0]);
                    _at++;
                }

                return builder.ToString();
            }

            var c = _text[_at++];
            builder.Append(escapes && c == '\\' ? EscapeInMultiLine() : c.ToString());
        }
    }

    private string EscapeInMultiLine()
    {
        if (_at < _text.Length && (_text[_at] == '\n' || _text[_at] == '\r' || _text[_at] == ' ' || _text[_at] == '\t'))
        {
            while (_at < _text.Length && char.IsWhiteSpace(_text[_at]))
            {
                _at++;
            }

            return string.Empty;
        }

        return Escape();
    }

    private string Escape()
    {
        if (_at >= _text.Length)
        {
            throw Fault("a string ends in a backslash");
        }

        var c = _text[_at++];
        switch (c)
        {
            case 'b': return "\b";
            case 't': return "\t";
            case 'n': return "\n";
            case 'f': return "\f";
            case 'r': return "\r";
            case '"': return "\"";
            case '\\': return "\\";
            case 'u': return Unicode(4);
            case 'U': return Unicode(8);
            default: throw Fault($"'\\{c}' is not a TOML escape (line {LineOf(_at - 1)})");
        }
    }

    private string Unicode(int digits)
    {
        if (_at + digits > _text.Length || !int.TryParse(_text.AsSpan(_at, digits), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code) || code is > 0x10FFFF or (>= 0xD800 and <= 0xDFFF))
        {
            throw Fault($"a unicode escape is not valid on line {LineOf(_at)}");
        }

        _at += digits;

        return char.ConvertFromUtf32(code);
    }

    private void SkipSpaces()
    {
        while (_at < _text.Length && _text[_at] is ' ' or '\t')
        {
            _at++;
        }
    }

    /// <summary>Blank space, newlines and comments — between statements and between array items.</summary>
    private void SkipBlank()
    {
        while (_at < _text.Length)
        {
            if (char.IsWhiteSpace(_text[_at]))
            {
                _at++;
            }
            else if (_text[_at] == '#')
            {
                SkipComment();
            }
            else
            {
                return;
            }
        }
    }

    private void SkipComment()
    {
        while (_at < _text.Length && _text[_at] is not ('\n' or '\r'))
        {
            _at++;
        }
    }

    /// <summary>After a statement only spaces and a comment may stand before the line ends.</summary>
    private void EndOfLine()
    {
        SkipSpaces();
        if (_at < _text.Length && _text[_at] == '#')
        {
            SkipComment();
        }

        if (_at < _text.Length && _text[_at] is not ('\n' or '\r'))
        {
            throw Fault($"unexpected '{_text[_at]}' on line {LineOf(_at)}");
        }
    }

    private void Expect(char c)
    {
        if (_at >= _text.Length || _text[_at] != c)
        {
            throw Fault($"'{c}' was expected on line {LineOf(Math.Min(_at, _text.Length - 1))}");
        }

        _at++;
    }

    private bool StartsWithAt(string prefix) => string.CompareOrdinal(_text, _at, prefix, 0, prefix.Length) == 0;

    private int LineOf(int position) => 1 + _text.Take(Math.Max(0, Math.Min(position, _text.Length))).Count(c => c == '\n');

    private static FormatException Fault(string message) => new(message);

    [GeneratedRegex(@"^[+-]?(0x[0-9A-Fa-f_]+|0o[0-7_]+|0b[01_]+|inf|nan|\d[\d_]*(\.\d[\d_]*)?([eE][+-]?\d[\d_]*)?)$")]
    private static partial Regex NumberLike();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}([Tt ]\d{2}:\d{2}(:\d{2}(\.\d+)?)?([Zz]|[+-]\d{2}:\d{2})?)?$|^\d{2}:\d{2}(:\d{2}(\.\d+)?)?$")]
    private static partial Regex DateLike();
}
