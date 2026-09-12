using System.Text;

namespace CoaiMcp.Core.Context;

/// <summary>
/// An untracked file as a diff a reviewer can read — a synthetic "new file" hunk — so the pure
/// <see cref="DiffShaper"/> budgets it exactly like a tracked change.
/// </summary>
/// <remarks>
/// <para>Built in C# rather than through <c>git diff --no-index /dev/null &lt;path&gt;</c>: that form exits 1
/// on every hit and its <c>/dev/null</c> is a platform question. The git plumbing the consultant path
/// touches is <c>ls-files</c> and <c>diff HEAD</c>, and nothing here touches the index.</para>
/// <para>The two ceilings answer the plan round's Major on untracked size: a file over
/// <see cref="InlineCap"/> is NAMED with its size and not shown, and a file with a NUL in its first
/// eight kilobytes is a binary and is named the way the shaper already names binaries.</para>
/// </remarks>
public static class UntrackedDiff
{
    public const int InlineCap = 16 * 1024;

    private const int BinaryProbe = 8 * 1024;

    public static FileDiff For(string path, ReadOnlySpan<byte> bytes)
    {
        if (LooksBinary(bytes))
        {
            return new FileDiff(path, string.Empty, IsBinary: true, BinaryBytes: bytes.Length);
        }

        if (bytes.Length > InlineCap)
        {
            return TooBig(path, bytes.Length);
        }

        return new FileDiff(path, Hunk(path, Encoding.UTF8.GetString(bytes)));
    }

    /// <summary>
    /// A file too large to show, named with its size — decided from the LENGTH alone.
    /// </summary>
    /// <remarks>
    /// Public so the collector can answer without reading the bytes at all: the cap bounds what the
    /// consultant sees, and it must bound what is allocated to decide that too.
    /// </remarks>
    public static FileDiff TooBig(string path, long bytes) =>
        new(path, $"new file (untracked): {path} — {bytes} bytes, not shown (over {InlineCap} bytes)\n");

    private static bool LooksBinary(ReadOnlySpan<byte> bytes) =>
        bytes[..Math.Min(bytes.Length, BinaryProbe)].IndexOf((byte)0) >= 0;

    private static string Hunk(string path, string text)
    {
        var lines = text.Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            lines = lines[..^1];
        }

        var hunk = new StringBuilder();
        hunk.Append("diff --git a/").Append(path).Append(" b/").Append(path).Append('\n');
        hunk.Append("new file mode 100644\n--- /dev/null\n+++ b/").Append(path).Append('\n');
        hunk.Append("@@ -0,0 +1,").Append(lines.Length).Append(" @@\n");
        foreach (var line in lines)
        {
            hunk.Append('+').Append(line.TrimEnd('\r')).Append('\n');
        }

        return hunk.ToString();
    }
}
