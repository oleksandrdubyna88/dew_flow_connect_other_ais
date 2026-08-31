using System.Collections.Immutable;
using System.Text;

namespace CoaiMcp.Core.Context;

/// <summary>One file's part of the diff, as the runners collected it.</summary>
public sealed record FileDiff(string Path, string Text, bool IsBinary = false, long BinaryBytes = 0);

public sealed record ElidedFile(string Path, int Bytes);

/// <summary>The diff a reviewer actually sees, with an honest account of what it does not.</summary>
public sealed record ShapedDiff(string Text, ImmutableArray<ElidedFile> Elided, int TotalFiles)
{
    public bool WasElided => !Elided.IsEmpty;
}

/// <summary>
/// The diff is the reviewers' entire world at the code stage — its shape decides the quality of
/// the review, and a context window spent on a lock file is a finding not made.
/// </summary>
/// <remarks>
/// Binaries are named with their size, never inlined. Text files ride whole, in diff order, until
/// the byte cap; past it a file is elided ENTIRE and listed with its size — a reviewer must know
/// its view was partial instead of assuming it was whole, and half a diff hunk is worse than none.
/// </remarks>
public static class DiffShaper
{
    public const int DefaultMaxBytes = 192 * 1024;

    public static ShapedDiff Shape(IReadOnlyList<FileDiff> files, int maxBytes = DefaultMaxBytes)
    {
        var text = new StringBuilder();
        var elided = ImmutableArray.CreateBuilder<ElidedFile>();
        var budget = maxBytes;

        foreach (var file in files.Where(f => f.IsBinary))
        {
            text.AppendLine($"Binary file changed: {file.Path} ({file.BinaryBytes} bytes) — content not shown.");
        }

        foreach (var file in files.Where(f => !f.IsBinary))
        {
            var bytes = Encoding.UTF8.GetByteCount(file.Text);
            if (bytes <= budget)
            {
                text.Append(file.Text);
                if (!file.Text.EndsWith('\n'))
                {
                    text.AppendLine();
                }

                budget -= bytes;
            }
            else
            {
                elided.Add(new ElidedFile(file.Path, bytes));
            }
        }

        if (elided.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("## Diff over budget — the following changed files are NOT shown (your view is partial):");
            foreach (var e in elided)
            {
                text.AppendLine($"- {e.Path} ({e.Bytes} bytes of diff elided)");
            }
        }

        return new ShapedDiff(text.ToString(), elided.ToImmutable(), files.Count);
    }
}
