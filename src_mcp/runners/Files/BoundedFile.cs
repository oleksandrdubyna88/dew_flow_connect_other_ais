using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CoaiMcp.Runners.Files;

/// <summary>What a bounded read of one small file came to — a closed union, so "no file", "a file" and "a file coai refuses to read" are never one answer.</summary>
internal abstract record BoundedRead
{
    private BoundedRead()
    {
    }

    /// <summary>There is no file at the path.</summary>
    public sealed record Absent : BoundedRead;

    /// <summary>The whole file, as UTF-8 text, no larger than the ceiling.</summary>
    public sealed record Text(string Value) : BoundedRead;

    /// <summary>
    /// The file is not read: it is a link, was swapped for another file, is larger than the ceiling, or could not be opened. <paramref name="Failure"/> names what failed, never a resolved path; <paramref name="Path"/> is the path the
    /// CALLER gave. <see cref="Why"/> is both, for a reader that does not name the file itself — a caller that already names it in its own sentence uses <see cref="Failure"/>, so no sentence names the path twice.
    /// </summary>
    public sealed record Refused(string Failure, string Path) : BoundedRead
    {
        /// <summary>The path the caller gave, then the failure.</summary>
        public string Why => $"{Path}: {Failure}";
    }
}

/// <summary>
/// A small coai-written file read WITHOUT trusting its path (the coai code gate, E5): a survivor marker and a lock record live in a folder under the person's data directory, so a link planted there, or a file grown to
/// gigabytes, must not make the reader follow it or fill memory. A link is refused (<c>O_NOFOLLOW</c>'s meaning, by <c>lstat</c> before the open and, on Linux, the opened descriptor compared with the un-followed path
/// after it — .NET has no <c>O_NOFOLLOW</c> flag, and its value differs by architecture); more than <see cref="MaxBytes"/> is refused, never truncated into something that parses; and a failure is said by its type and its
/// code, never by <c>Exception.Message</c>, which carries the resolved path.
/// </summary>
internal static class BoundedFile
{
    /// <summary>The ceiling: a marker or a lock record is a few hundred bytes; 64 KiB is room for a very large tree and nothing more.</summary>
    public const int MaxBytes = 64 * 1024;

    /// <summary>The regular file at <paramref name="path"/>, read whole — or why it is not.</summary>
    public static BoundedRead ReadRegular(string path, int maxBytes = MaxBytes)
    {
        try
        {
            if (new FileInfo(path).LinkTarget is not null)
            {
                return new BoundedRead.Refused("it is a link, and a link is never followed", path);
            }

            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            return IsTheNamedFile(handle, path) ? Read(handle, path, maxBytes) : new BoundedRead.Refused("it was replaced by another file while it was being opened", path);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new BoundedRead.Absent();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new BoundedRead.Refused(Said(e), path);
        }
    }

    /// <summary>A file the caller already holds open (a verified gate), from its start — the same ceiling, the same refusal of what does not fit.</summary>
    public static BoundedRead ReadStream(FileStream stream, int maxBytes = MaxBytes)
    {
        try
        {
            stream.Position = 0;

            return Read(stream.SafeFileHandle, stream.Name, maxBytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new BoundedRead.Refused(Said(e), stream.Name);
        }
    }

    private static bool IsTheNamedFile(SafeFileHandle handle, string path) => !OperatingSystem.IsLinux() || HeldFile.StillTheFile(handle, path);

    private static BoundedRead Read(SafeFileHandle handle, string path, int maxBytes)
    {
        var buffer = new byte[maxBytes + 1];
        var total = 0;
        while (total < buffer.Length && RandomAccess.Read(handle, buffer.AsSpan(total), total) is var read and > 0)
        {
            total += read;
        }

        return total > maxBytes ? new BoundedRead.Refused($"it is larger than {maxBytes} bytes", path) : new BoundedRead.Text(Encoding.UTF8.GetString(buffer, 0, total));
    }

    /// <summary>What failed, by type and code — never <c>Exception.Message</c>, which names the path, and the path may be one a link led to; the path the CALLER gave rides in <see cref="BoundedRead.Refused.Path"/>.</summary>
    private static string Said(Exception e) => $"{e.GetType().Name} (code 0x{e.HResult:X8})";
}
