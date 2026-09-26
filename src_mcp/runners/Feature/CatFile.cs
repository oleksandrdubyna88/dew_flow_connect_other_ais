using System.Globalization;
using System.Text;
using CoaiMcp.Runners.Context;

namespace CoaiMcp.Runners.Feature;

/// <summary>What <c>git cat-file --batch-check</c> said about one object name.</summary>
/// <param name="Oid">The object id, or empty when the name resolved to nothing.</param>
/// <param name="Type"><c>blob</c>, <c>commit</c> (a submodule), <c>tree</c> — or empty when missing.</param>
/// <param name="Size">Its size in bytes, or -1 when missing.</param>
public sealed record GitObject(string Oid, string Type, long Size)
{
    public static readonly GitObject Missing = new(string.Empty, string.Empty, -1);

    public bool Exists => Oid.Length > 0;

    public bool IsBlob => Type == "blob";
}

/// <summary>One blob's text as <c>git cat-file --batch</c> returned it.</summary>
/// <param name="Aligned">
/// False when the text did not re-encode to the size git declared — bytes that are not UTF-8 were
/// replaced on the way in, so the text is not the file and is not outlined.
/// </param>
public sealed record BlobText(string Text, bool Aligned);

/// <summary>
/// The two answers of <c>git cat-file</c> the feature outline reads, parsed — pure, so every shape is
/// tested without a repository.
/// </summary>
/// <remarks>
/// <para><b>Why the batch is parsed by the NEXT header rather than trusted by byte count alone.</b> The
/// launcher hands stdout over as UTF-8 text, and git declares each blob's size in BYTES. A valid UTF-8
/// file re-encodes to exactly that many bytes, and then the declared size ends the blob exactly where
/// git's separating newline is. A file that is not UTF-8 decodes with replacement characters and no
/// longer measures what git said — so the parser falls back to the header it KNOWS comes next (the
/// object id and size it asked for, in the order it asked), and reports that blob as not aligned
/// instead of mis-slicing every blob after it.</para>
/// </remarks>
public static class CatFile
{
    /// <summary>Reads <c>--batch-check</c> output: one line per requested name, in request order.</summary>
    /// <exception cref="ContextException">When git answered a different number of lines than it was asked.</exception>
    public static IReadOnlyList<GitObject> ParseCheck(string output, int requested)
    {
        var lines = output.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines.Count == requested
            ? [.. lines.Select(Object)]
            : throw new ContextException("cat-file --batch-check", $"answered {lines.Count} line(s) for {requested} name(s)");
    }

    /// <summary>
    /// A found object is exactly three fields: a hex id, a type, a size. Anything else — <c>missing</c>,
    /// <c>ambiguous</c> — echoes the name that was asked, which may contain spaces, and is not an object.
    /// </summary>
    private static GitObject Object(string line)
    {
        var parts = line.Split(' ');

        return parts.Length == 3 && IsHex(parts[0]) && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var size)
            ? new GitObject(parts[0], parts[1], size)
            : GitObject.Missing;
    }

    /// <summary>Reads <c>--batch</c> output for exactly the blobs asked for, in that order.</summary>
    /// <exception cref="ContextException">When an expected header is not where it must be.</exception>
    public static IReadOnlyList<BlobText> ParseBatch(string output, IReadOnlyList<GitObject> expected)
    {
        var blobs = new List<BlobText>(expected.Count);
        var at = 0;
        for (var i = 0; i < expected.Count; i++)
        {
            var header = Header(expected[i]);
            if (string.CompareOrdinal(output, at, header, 0, header.Length) != 0)
            {
                throw new ContextException("cat-file --batch", $"expected '{header.TrimEnd()}' at object {i + 1} of {expected.Count}");
            }

            var start = at + header.Length;
            var end = ByteEnd(output, start, expected[i].Size);
            var aligned = end >= 0 && end < output.Length && output[end] == '\n';
            end = aligned ? end : Resync(output, start, i + 1 < expected.Count ? Header(expected[i + 1]) : string.Empty);
            blobs.Add(new BlobText(output[start..end], aligned));
            at = end + 1;
        }

        return blobs;
    }

    private static string Header(GitObject blob) => $"{blob.Oid} blob {blob.Size}\n";

    /// <summary>The character index where <paramref name="bytes"/> UTF-8 bytes from <paramref name="start"/> end, or -1.</summary>
    private static int ByteEnd(string text, int start, long bytes)
    {
        var (at, used) = (start, 0L);
        while (used < bytes && at < text.Length)
        {
            Rune.DecodeFromUtf16(text.AsSpan(at), out var rune, out var consumed);
            used += rune.Utf8SequenceLength;
            at += consumed;
        }

        return used == bytes ? at : -1;
    }

    /// <summary>Where a blob that did not measure true ends: before the next header, or before the final newline.</summary>
    private static int Resync(string text, int start, string nextHeader)
    {
        var end = nextHeader.Length > 0
            ? text.IndexOf("\n" + nextHeader, start, StringComparison.Ordinal)
            : text.Length > start && text[^1] == '\n' ? text.Length - 1 : -1;

        return end >= start
            ? end
            : throw new ContextException("cat-file --batch", "a blob's end could not be found — the output is not what was asked for");
    }

    private static bool IsHex(string value) => value.Length is 40 or 64 && value.All(Uri.IsHexDigit);
}
