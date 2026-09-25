using System.Text;

namespace CoaiMcp.Api;

/// <summary>
/// Reads an HTTP answer up to a ceiling, and stops there — never buffering the rest.
/// </summary>
/// <remarks>
/// <para><c>ReadAsStringAsync</c> reads the WHOLE body before any cap can run, so an endpoint that ignores
/// the token ceiling, or answers with an error page, decides how much of this process's memory it takes
/// (epic 1's code round, codex). Both api modes read through this instead, over a response obtained with
/// <see cref="HttpCompletionOption.ResponseHeadersRead"/>, so the body is streamed rather than already
/// buffered by the time it is looked at.</para>
/// <para>A declared <c>Content-Length</c> over the ceiling is refused before a byte is read; an undeclared
/// or lying one is refused the moment the count passes it. UTF-8, because every OpenAI-compatible API
/// answers JSON in it.</para>
/// </remarks>
internal static class BoundedBody
{
    /// <summary>The body as text, or null when it is longer than <paramref name="maxBytes"/>.</summary>
    internal static async Task<string?> ReadAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        if (content.Headers.ContentLength is { } declared && declared > maxBytes)
        {
            return null;
        }

        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
