using System.Text;

namespace CoaiMcp.Server.Keys;

/// <summary>
/// Reading a key text from standard input — the ONLY road a text enters the pool by hand (KEY_POOL_SPEC.md section 4): at most 4096 bytes, one line, the line end trimmed. No verb takes a text as an argument,
/// from a named file or from the environment.
/// </summary>
/// <remarks>
/// A refusal says how much was wrong (the byte limit, or that nothing came) and never what was read. A terminal is prompted on stderr and read with no echo by the caller's <c>readHidden</c>.
/// </remarks>
internal static class KeyStdin
{
    internal const int MaxLineBytes = 4096;

    internal const int MaxImportLines = 200;

    internal const int MaxImportLineBytes = 8192;

    internal const int MaxSourceBytes = 1024 * 1024;

    private static readonly UTF8Encoding Strict = new(false, throwOnInvalidBytes: true);

    /// <summary>The first line of <paramref name="input"/>: at most <see cref="MaxLineBytes"/> bytes before its end, trimmed of the line end.</summary>
    public static KeyResult<string> ReadLine(Stream input)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (input.Read(one, 0, 1) == 1 && one[0] != (byte)'\n')
        {
            bytes.Add(one[0]);
            if (bytes.Count > MaxLineBytes)
            {
                return KeyResult<string>.Refused($"the line on stdin is longer than {MaxLineBytes} bytes; nothing was read");
            }
        }

        return Decoded(bytes);
    }

    private static KeyResult<string> Decoded(List<byte> bytes)
    {
        try
        {
            var text = Strict.GetString([.. bytes]).TrimEnd('\r').Trim();

            return text.Length == 0 ? KeyResult<string>.Refused("no text came on stdin: put the key on ONE line") : KeyResult<string>.Of(text);
        }
        catch (DecoderFallbackException)
        {
            return KeyResult<string>.Refused("the line on stdin is not UTF-8 text");
        }
    }

    /// <summary>The whole of <paramref name="input"/> as text, up to <paramref name="maxBytes"/> (one more byte is a refusal).</summary>
    public static KeyResult<string> ReadAll(Stream input, int maxBytes)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > maxBytes)
            {
                return KeyResult<string>.Refused($"stdin is longer than {maxBytes} bytes; nothing was read");
            }
        }

        try
        {
            return KeyResult<string>.Of(Strict.GetString(buffer.ToArray()));
        }
        catch (DecoderFallbackException)
        {
            return KeyResult<string>.Refused("stdin is not UTF-8 text");
        }
    }
}
