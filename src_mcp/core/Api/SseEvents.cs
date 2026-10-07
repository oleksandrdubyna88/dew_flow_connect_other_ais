namespace CoaiMcp.Core.Api;

/// <summary>
/// Server-sent events, one line at a time, turned into the <c>data</c> payloads a streamed completion is made of
/// (research/PLAN_api_streaming.md). Pure: the caller reads the lines; this decides what they mean.
/// </summary>
/// <remarks>
/// <para>The SSE rules every vendor's stream was read against (research/RESULTS_api_streaming_vendors.md): a blank
/// line ends an event; a line starting with <c>:</c> is a comment — <c>: keep-alive</c> (DeepSeek),
/// <c>: OPENROUTER PROCESSING</c> — and so are <c>event:</c>, <c>id:</c> and <c>retry:</c> here, because every vendor
/// carries its error in <c>data:</c>; <c>data:</c> takes one optional space; several <c>data:</c> lines of one event
/// are one payload, joined by a line feed; <c>data: [DONE]</c> ends the stream, and nothing after it is read.</para>
/// <para>A line may arrive with its carriage return still on it (<c>\r\n</c> split at the <c>\n</c>); it is taken off
/// here, so the caller can split on either.</para>
/// </remarks>
public sealed class SseEvents
{
    /// <summary>The payload that ends a stream in every OpenAI-compatible API.</summary>
    public const string DoneMarker = "[DONE]";

    private const string DataField = "data:";

    private readonly List<string> _data = [];

    /// <summary>Whether <c>data: [DONE]</c> arrived — the stream's own word that it is complete.</summary>
    public bool Done { get; private set; }

    /// <summary>
    /// The characters of the event still open — its <c>data:</c> lines, held until a blank line closes it. Each line
    /// is under the reader's ceiling; this is what keeps the EVENT under it too (the code round of
    /// research/PLAN_api_streaming.md: an endpoint could otherwise fill the child's memory below every per-line limit).
    /// </summary>
    public long PendingChars { get; private set; }

    /// <summary>One line, without its line feed. Answers the payload this line completes, or null.</summary>
    public string? Add(string line)
    {
        if (Done)
        {
            return null;
        }
        var text = line.EndsWith('\r') ? line[..^1] : line;

        return text.Length == 0 ? Dispatch() : Collect(text);
    }

    /// <summary>A non-blank line: a <c>data:</c> value is held for its event; anything else is a comment.</summary>
    private string? Collect(string text)
    {
        if (text.StartsWith(DataField, StringComparison.Ordinal))
        {
            var value = ValueOf(text);
            _data.Add(value);
            PendingChars += value.Length;
        }

        return null;
    }

    /// <summary>The payload still open when the body ended without the blank line that would have closed it.</summary>
    public string? Finish() => Done ? null : Dispatch();

    private string? Dispatch()
    {
        if (_data.Count == 0)
        {
            return null;
        }
        var payload = string.Join('\n', _data);
        _data.Clear();
        PendingChars = 0;
        Done = payload == DoneMarker;

        return Done ? null : payload;
    }

    /// <summary>The field's value: what follows <c>data:</c>, less the one space the format allows.</summary>
    private static string ValueOf(string line)
    {
        var value = line[DataField.Length..];

        return value.StartsWith(' ') ? value[1..] : value;
    }
}
