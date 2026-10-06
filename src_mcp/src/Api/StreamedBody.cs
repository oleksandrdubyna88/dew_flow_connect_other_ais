using System.Text;
using CoaiMcp.Core.Api;

namespace CoaiMcp.Api;

/// <summary>Why the reading of a stream stopped.</summary>
internal enum StreamStop
{
    /// <summary>The body ended — with <c>[DONE]</c> or without it; the outcome says which.</summary>
    Ended,

    /// <summary>More answer than the ceiling allows; read no further.</summary>
    Capped,

    /// <summary>One line longer than any chunk is: not a stream this reader will buffer.</summary>
    LineTooLong,

    /// <summary>The connection dropped after the headers.</summary>
    Dropped,

    /// <summary>The deadline passed mid-stream.</summary>
    TimedOut,
}

/// <summary>What was read, and why the reading stopped.</summary>
internal sealed record StreamRead(StreamOutcome Outcome, StreamStop Stop);

/// <summary>
/// Reads a streamed answer off an HTTP body (todo/PLAN_api_streaming.md) — the streaming counterpart of
/// <see cref="BoundedBody"/>, and like it never buffering past a ceiling.
/// </summary>
/// <remarks>
/// <para><b>The ANSWER is capped, not the stream.</b> Every chunk wraps a token or two in a few hundred bytes of JSON, so
/// a 65,536-token answer is about 19 MB of stream: a cap on the raw bytes would refuse exactly the long answers a stream
/// exists for. One line has its own ceiling, so a body with no line ends is refused rather than held.</para>
/// <para>UTF-8 is decoded as a stream — a character split between two network reads arrives whole. The deadline's
/// token rides on every read. A drop and a timeout keep what had arrived, so the shim can say how far it got.</para>
/// </remarks>
internal static class StreamedBody
{
    private const int ReadChars = 8192;

    /// <summary>The stream, read until it ends, drops, times out, or passes a ceiling.</summary>
    /// <param name="maxAnswerChars">The most answer to take.</param>
    /// <param name="maxLineChars">The longest one line may be.</param>
    internal static async Task<StreamRead> ReadAsync(HttpContent content, long maxAnswerChars, int maxLineChars, CancellationToken token)
    {
        var lines = new LineReader(new SseEvents(), new StreamAssembler(), maxAnswerChars, maxLineChars);
        try
        {
            await using var body = await content.ReadAsStreamAsync(token);
            using var reader = new StreamReader(body, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false);

            var stop = await lines.ReadAsync(reader, token);

            return new StreamRead(lines.Outcome(), stop);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return new StreamRead(lines.Outcome(), StreamStop.TimedOut);
        }
        catch (IOException)
        {
            return new StreamRead(lines.Outcome(), StreamStop.Dropped);
        }
    }

    /// <summary>Characters in, lines out, payloads into the assembler — and the ceilings kept.</summary>
    private sealed class LineReader(SseEvents events, StreamAssembler assembler, long maxAnswerChars, int maxLineChars)
    {
        private readonly StringBuilder _line = new();

        public StreamOutcome Outcome()
        {
            if (events.Finish() is { } last)
            {
                assembler.Add(last);
            }

            return assembler.Finish(events.Done);
        }

        public async Task<StreamStop> ReadAsync(StreamReader reader, CancellationToken token)
        {
            var buffer = new char[ReadChars];
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
            {
                if (Take(buffer.AsSpan(0, read)) is { } stopped)
                {
                    return stopped;
                }
            }

            return _line.Length > 0 ? LineEnded() ?? StreamStop.Ended : StreamStop.Ended;
        }

        private StreamStop? Take(ReadOnlySpan<char> chars)
        {
            foreach (var c in chars)
            {
                if (c != '\n')
                {
                    _line.Append(c);
                    if (_line.Length > maxLineChars)
                    {
                        return StreamStop.LineTooLong;
                    }
                }
                else if (LineEnded() is { } stopped)
                {
                    return stopped;
                }
            }

            return null;
        }

        private StreamStop? LineEnded()
        {
            var payload = events.Add(_line.ToString());
            _line.Clear();
            if (payload is not null)
            {
                assembler.Add(payload);
            }

            return assembler.ContentChars > maxAnswerChars ? StreamStop.Capped : null;
        }
    }
}
