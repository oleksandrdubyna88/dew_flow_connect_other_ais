using System.Buffers;
using System.Text;
using System.Text.Json;

namespace CoaiMcp.Core.Api;

/// <summary>How a stream ended: with an answer, with a failure it reported, or cut off before either.</summary>
public enum StreamEnd
{
    /// <summary><c>[DONE]</c>, or a final <c>finish_reason</c>: the completion is read as any other.</summary>
    Answered,

    /// <summary>An <c>error</c> chunk, or a <c>finish_reason</c> that reports a failure — inside an HTTP 200.</summary>
    Failed,

    /// <summary>Neither <c>[DONE]</c> nor a <c>finish_reason</c>: the stream stopped before the answer finished.</summary>
    Broken,
}

/// <summary>
/// What a stream amounted to.
/// </summary>
/// <param name="End">How it ended.</param>
/// <param name="Completion">One completion JSON in the non-streaming shape, for <see cref="CompletionReader"/>.</param>
/// <param name="ErrorText">The failure the stream reported, bounded; empty unless <paramref name="End"/> is Failed.</param>
/// <param name="ContentChars">How much answer arrived.</param>
/// <param name="ReasoningChars">How much reasoning arrived — counted, never kept whole.</param>
public sealed record StreamOutcome(StreamEnd End, string Completion, string ErrorText, long ContentChars, long ReasoningChars);

/// <summary>
/// The chunks of a streamed completion assembled into the ONE answer shape the non-streaming path reads
/// (todo/PLAN_api_streaming.md, S4): a streamed and an unstreamed answer are judged by the same
/// <see cref="CompletionReader"/>, so they cannot be judged differently.
/// </summary>
/// <remarks>
/// <para>Only the first choice (<c>index</c> 0) is read. Its <c>delta.content</c> is joined; its reasoning —
/// <c>reasoning_content</c> (Alibaba, DeepSeek, Z.ai) or <c>reasoning</c> (OpenRouter, Ollama) — is COUNTED and
/// kept only as a short quote for a note, never in the content and never whole: nothing reads it, and a long think
/// is tens of megabytes of stream.</para>
/// <para>The last non-null <c>usage</c> wins wherever it came — a last <c>choices: []</c> chunk, every chunk, beside
/// the <c>finish_reason</c> — and the last non-null <c>finish_reason</c>, which settles a reason sent twice. A
/// failure (S5) wins over everything: an <c>error</c> chunk, or a reason of <see cref="FailedReasons"/>, even after a
/// <c>stop</c>. A chunk that is not a JSON object is passed over.</para>
/// </remarks>
public sealed class StreamAssembler
{
    /// <summary>The <c>finish_reason</c> values vendors use to report a failure inside a stream.</summary>
    public static readonly IReadOnlyList<string> FailedReasons = ["error", "network_error", "sensitive", "insufficient_system_resource", "aborted"];

    private const int QuoteLimit = 300;
    private const int ErrorLimit = 600;

    private readonly StringBuilder _content = new();
    private readonly StringBuilder _reasoningQuote = new();
    private long _reasoningChars;
    private string? _usage;
    private string _finish = string.Empty;
    private string _error = string.Empty;
    private string _id = string.Empty;
    private string _model = string.Empty;

    /// <summary>One <c>data</c> payload.</summary>
    public void Add(string payload)
    {
        try
        {
            using var chunk = JsonDocument.Parse(payload);
            if (chunk.RootElement.ValueKind == JsonValueKind.Object)
            {
                Read(chunk.RootElement);
            }
        }
        catch (JsonException)
        {
            // A payload that is not JSON is no part of the answer; the stream goes on.
        }
    }

    /// <summary>The stream as a whole, once its body has ended.</summary>
    /// <param name="sawDone">Whether <c>data: [DONE]</c> arrived (<see cref="SseEvents.Done"/>).</param>
    public StreamOutcome Finish(bool sawDone) => new(EndOf(sawDone), Completion(), _error, _content.Length, _reasoningChars);

    private StreamEnd EndOf(bool sawDone)
    {
        if (_error.Length > 0)
        {
            return StreamEnd.Failed;
        }

        return sawDone || _finish.Length > 0 ? StreamEnd.Answered : StreamEnd.Broken;
    }

    private void Read(JsonElement chunk)
    {
        _id = TextOf(chunk, "id") ?? _id;
        _model = TextOf(chunk, "model") ?? _model;
        RememberError(chunk);
        if (chunk.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            _usage = usage.GetRawText();
        }
        if (FirstChoice(chunk) is { } choice)
        {
            ReadChoice(choice);
        }
    }

    private void RememberError(JsonElement chunk)
    {
        if (_error.Length == 0 && chunk.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
        {
            _error = Bounded(error.ValueKind == JsonValueKind.Object && TextOf(error, "message") is { } message ? $"{message} ({error.GetRawText()})" : error.GetRawText());
        }
    }

    private void ReadChoice(JsonElement choice)
    {
        if (choice.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
        {
            _content.Append(TextOf(delta, "content"));
            CountReasoning(TextOf(delta, "reasoning_content") ?? TextOf(delta, "reasoning"));
        }
        if (TextOf(choice, "finish_reason") is { Length: > 0 } reason)
        {
            Finished(reason);
        }
    }

    private void Finished(string reason)
    {
        if (!FailedReasons.Contains(reason))
        {
            _finish = reason;
        }
        else if (_error.Length == 0)
        {
            _error = $"the stream ended with finish_reason \"{reason}\"";
        }
    }

    private void CountReasoning(string? text)
    {
        if (text is null)
        {
            return;
        }
        _reasoningChars += text.Length;
        _reasoningQuote.Append(text.AsSpan(0, Math.Min(text.Length, Math.Max(0, QuoteLimit - _reasoningQuote.Length))));
    }

    /// <summary>The choice whose <c>index</c> is 0 — or the first object when the vendor gave no index.</summary>
    private static JsonElement? FirstChoice(JsonElement chunk)
    {
        if (!chunk.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        foreach (var choice in choices.EnumerateArray().Where((one) => one.ValueKind == JsonValueKind.Object))
        {
            return IndexOf(choice) == 0 ? choice : null;
        }

        return null;
    }

    private static long IndexOf(JsonElement choice) =>
        choice.TryGetProperty("index", out var index) && index.ValueKind == JsonValueKind.Number && index.TryGetInt64(out var value) ? value : 0;

    private static string? TextOf(JsonElement element, string field) =>
        element.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Bounded(string text) => text.Length <= ErrorLimit ? text : $"{text[..ErrorLimit]}…";

    /// <summary>The non-streaming completion this stream amounts to — written by hand: the binary is Native AOT.</summary>
    private string Completion()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("id", _id);
            json.WriteString("model", _model);
            json.WriteStartArray("choices");
            WriteChoice(json);
            json.WriteEndArray();
            if (_usage is not null)
            {
                json.WritePropertyName("usage");
                json.WriteRawValue(_usage);
            }
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private void WriteChoice(Utf8JsonWriter json)
    {
        json.WriteStartObject();
        json.WriteNumber("index", 0);
        json.WriteStartObject("message");
        json.WriteString("role", "assistant");
        WriteTextOrNull(json, "content", _content);
        WriteTextOrNull(json, "reasoning_content", _reasoningQuote);
        json.WriteEndObject();
        if (_finish.Length > 0)
        {
            json.WriteString("finish_reason", _finish);
        }
        else
        {
            json.WriteNull("finish_reason");
        }
        json.WriteEndObject();
    }

    private static void WriteTextOrNull(Utf8JsonWriter json, string name, StringBuilder text)
    {
        if (text.Length > 0)
        {
            json.WriteString(name, text.ToString());
        }
        else
        {
            json.WriteNull(name);
        }
    }
}
