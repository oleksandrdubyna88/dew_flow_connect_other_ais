using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CoaiMcp.Install;

/// <summary>A member of a JSON object: its name, where its name token starts, and its value.</summary>
internal sealed record JsonMember(string Name, int Start, JsonSpan Value);

/// <summary>
/// One JSON value with the BYTE span it occupies in the text it was read from — the reason it exists: a file another tool owns (Claude's <c>settings.json</c>) is changed by
/// splicing text into it at these spans, so every byte the installer did not mean to touch (indentation, escapes, number spellings) stays as its owner wrote it.
/// </summary>
internal sealed class JsonSpan
{
    internal JsonTokenType Type { get; init; }

    /// <summary>The first byte of the value (an opening brace or bracket, or a quote).</summary>
    internal int Start { get; init; }

    /// <summary>One past the last byte.</summary>
    internal int End { get; set; }

    /// <summary>A string's decoded text.</summary>
    internal string? Text { get; init; }

    internal List<JsonMember> Members { get; } = [];

    internal List<JsonSpan> Items { get; } = [];

    internal bool IsObject => Type == JsonTokenType.StartObject;

    internal bool IsArray => Type == JsonTokenType.StartArray;

    internal bool IsString => Type == JsonTokenType.String;

    internal JsonSpan? Get(string name) => Members.FirstOrDefault(member => member.Name == name)?.Value;

    /// <summary>Reads <paramref name="bytes"/> (no byte-order mark) as ONE JSON value. Comments and trailing commas are not JSON and are refused, as Claude's own reader does.</summary>
    /// <exception cref="JsonException">The text is not one JSON value.</exception>
    internal static JsonSpan Parse(ReadOnlySpan<byte> bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false, MaxDepth = 64 });
        if (!reader.Read())
        {
            throw new JsonException("the text is empty");
        }

        var root = ReadValue(ref reader);
        if (reader.Read())
        {
            throw new JsonException("there is more than one value");
        }

        return root;
    }

    private static JsonSpan ReadValue(ref Utf8JsonReader reader)
    {
        var start = (int)reader.TokenStartIndex;
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                var objectSpan = new JsonSpan { Type = JsonTokenType.StartObject, Start = start };
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var nameStart = (int)reader.TokenStartIndex;
                    var name = reader.GetString() ?? string.Empty;
                    reader.Read();
                    objectSpan.Members.Add(new JsonMember(name, nameStart, ReadValue(ref reader)));
                }

                objectSpan.End = (int)reader.TokenStartIndex + 1;

                return objectSpan;
            case JsonTokenType.StartArray:
                var arraySpan = new JsonSpan { Type = JsonTokenType.StartArray, Start = start };
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    arraySpan.Items.Add(ReadValue(ref reader));
                }

                arraySpan.End = (int)reader.TokenStartIndex + 1;

                return arraySpan;
            case JsonTokenType.String:
                return new JsonSpan { Type = JsonTokenType.String, Start = start, End = (int)reader.BytesConsumed, Text = reader.GetString() };
            default:
                return new JsonSpan { Type = reader.TokenType, Start = start, End = (int)reader.BytesConsumed };
        }
    }
}

/// <summary>
/// Changes to a JSON text made by splicing at <see cref="JsonSpan"/> positions: a new first member or item, a replaced string, a removed member or item. Each returns the new bytes;
/// the caller parses again before the next change, so no span is ever used after the text moved under it.
/// </summary>
internal static class JsonSplice
{
    private static readonly JsonWriterOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>A JSON string literal, written with the relaxed encoder so <c>&lt;</c> and a non-ASCII letter stay as typed.</summary>
    internal static string StringLiteral(string value)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, Compact))
        {
            writer.WriteStringValue(value);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>The value at <paramref name="span"/> replaced by <paramref name="literal"/>.</summary>
    internal static byte[] Replace(byte[] text, JsonSpan span, string literal) => Splice(text, span.Start, span.End, literal);

    /// <summary>
    /// <paramref name="snippet"/> (a value for an array, <c>"name": value</c> for an object; lines joined by <c>\n</c>) put FIRST in <paramref name="container"/>. In a container written over several
    /// lines it is whole new lines after the opening bracket — the lines already there are not touched; in one written on one line it is spliced in compact.
    /// </summary>
    internal static byte[] InsertFirst(byte[] text, JsonSpan container, string snippet)
    {
        var eol = Eol(text);
        var empty = container.IsArray ? container.Items.Count == 0 : container.Members.Count == 0;
        var firstStart = container.IsArray ? (container.Items.Count > 0 ? container.Items[0].Start : -1) : (container.Members.Count > 0 ? container.Members[0].Start : -1);
        var multiline = Contains(text, container.Start, empty ? container.End : firstStart, (byte)'\n');
        var baseIndent = IndentOfLineAt(text, container.Start);
        var indent = !empty && multiline ? IndentOfLineAt(text, firstStart) : baseIndent + "  ";
        if (!multiline)
        {
            var one = string.Join(string.Empty, snippet.Split('\n').Select(line => line.Trim()));

            return Splice(text, container.Start + 1, empty ? container.End - 1 : container.Start + 1, empty ? one : one + ",");
        }

        var lines = string.Join(eol, snippet.Split('\n').Select(line => indent + line));
        var open = container.Start + 1;

        return empty
            ? Splice(text, open, container.End - 1, eol + lines + eol + baseIndent)
            : Splice(text, open, open, eol + lines + ",");
    }

    /// <summary>The item or member whose text starts at <paramref name="start"/> and ends at <paramref name="end"/>, taken out with the comma that joined it to its neighbour.</summary>
    internal static byte[] Remove(byte[] text, JsonSpan container, int index)
    {
        var count = container.IsArray ? container.Items.Count : container.Members.Count;
        (int Start, int End) Of(int at) => container.IsArray
            ? (container.Items[at].Start, container.Items[at].End)
            : (container.Members[at].Start, container.Members[at].Value.End);
        var (start, end) = Of(index);
        if (count == 1)
        {
            return Splice(text, container.Start + 1, container.End - 1, string.Empty);
        }

        return index + 1 < count
            ? Splice(text, start, Of(index + 1).Start, string.Empty)
            : Splice(text, Of(index - 1).End, end, string.Empty);
    }

    private static byte[] Splice(byte[] text, int from, int to, string replacement)
    {
        var inserted = Encoding.UTF8.GetBytes(replacement);
        var result = new byte[text.Length - (to - from) + inserted.Length];
        Buffer.BlockCopy(text, 0, result, 0, from);
        Buffer.BlockCopy(inserted, 0, result, from, inserted.Length);
        Buffer.BlockCopy(text, to, result, from + inserted.Length, text.Length - to);

        return result;
    }

    private static string Eol(byte[] text)
    {
        var at = Array.IndexOf(text, (byte)'\n');

        return at > 0 && text[at - 1] == (byte)'\r' ? "\r\n" : "\n";
    }

    private static bool Contains(byte[] text, int from, int to, byte value) => Array.IndexOf(text, value, from, Math.Max(0, to - from)) >= 0;

    /// <summary>The whitespace that starts the line the byte at <paramref name="position"/> is on.</summary>
    private static string IndentOfLineAt(byte[] text, int position)
    {
        var start = position;
        while (start > 0 && text[start - 1] != (byte)'\n')
        {
            start--;
        }

        var end = start;
        while (end < text.Length && text[end] is (byte)' ' or (byte)'\t')
        {
            end++;
        }

        return Encoding.UTF8.GetString(text, start, end - start);
    }
}
