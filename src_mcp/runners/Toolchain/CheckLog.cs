using System.Text;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Toolchain;

/// <summary>
/// The log file of one step: its stdout, then its stderr under a separator line, cut at <see cref="CheckLimits.LogBytes"/> bytes. The cut falls on a character boundary, so the file stays valid UTF-8, and a
/// line says it was cut — a digest of a cut log is a digest of a head, and must say so.
/// </summary>
internal static class CheckLog
{
    internal const string StderrSeparator = "\n--- stderr ---\n";

    internal const string CutMarker = "\n[coai: the log is cut here at 20 MiB]\n";

    /// <summary>Writes the log and answers whether it had to be cut (by this cut, or by the launcher's own ceiling on a stream).</summary>
    internal static async Task<bool> WriteAsync(string path, ProcessResult result, string trailer)
    {
        var text = result.StdErr.Length == 0 ? result.StdOut : result.StdOut + StderrSeparator + result.StdErr;
        var bytes = Encoding.UTF8.GetBytes(text);
        var cut = bytes.Length > CheckLimits.LogBytes || result.Truncated;
        var length = bytes.Length > CheckLimits.LogBytes ? CharacterBoundary(bytes, CheckLimits.LogBytes) : bytes.Length;
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await file.WriteAsync(bytes.AsMemory(0, length));
        if (cut)
        {
            await file.WriteAsync(Encoding.UTF8.GetBytes(CutMarker));
        }

        if (trailer.Length > 0)
        {
            await file.WriteAsync(Encoding.UTF8.GetBytes(trailer));
        }

        return cut;
    }

    /// <summary>The largest length at most <paramref name="limit"/> that does not end inside a multi-byte character (UTF-8 continuation bytes are 10xxxxxx).</summary>
    internal static int CharacterBoundary(byte[] bytes, int limit)
    {
        var length = limit;
        while (length > 0 && (bytes[length] & 0xC0) == 0x80)
        {
            length--;
        }

        return length;
    }
}
