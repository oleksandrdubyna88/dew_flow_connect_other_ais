using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace CoaiMcp.Runners.Files;

// The same descriptor road as ReadOwned (O_NOFOLLOW | O_NONBLOCK | O_CLOEXEC, the type asked of the OPEN descriptor), without the owner and mode rule: what a host read of an entry that a UNIT can write needs is "no wait, no link, a regular file, a limit" (pass 8, M19).
public static partial class RegularFiles
{
    /// <summary>
    /// The regular file at <paramref name="path"/> opened for a host read of something a unit wrote: never followed if it is a link, never waited on if it is a FIFO, and judged on the open descriptor. Exactly one of the two answers is set: the stream, or why there is none
    /// (<see cref="FileReadResult.Missing"/> when nothing is there). Windows and macOS have no FIFO of this kind here: the entry is looked at without following and opened plainly.
    /// </summary>
    internal static (FileStream? Stream, FileReadResult? Failure) OpenNoFollow(string path)
    {
        try
        {
            return OperatingSystem.IsLinux() ? OpenLinuxNoFollow(path) : OpenPlain(path);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return (null, new FileReadResult.Unreadable(NoStatx));
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return (null, new FileReadResult.Missing());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return (null, new FileReadResult.Unreadable(e.GetType().Name));
        }
    }

    /// <summary>The bytes of the regular file at <paramref name="path"/> by <see cref="OpenNoFollow"/>, at most <paramref name="maxBytes"/> (one more is refused). Never waits on a FIFO and never follows a link.</summary>
    internal static FileReadResult ReadNoFollow(string path, int maxBytes)
    {
        var (stream, failure) = OpenNoFollow(path);
        if (stream is null)
        {
            return failure ?? new FileReadResult.Unreadable("could not be opened");
        }

        using (stream)
        {
            try
            {
                return Capped(stream, maxBytes);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return new FileReadResult.Unreadable(e.GetType().Name);
            }
        }
    }

    [SupportedOSPlatform("linux")]
    private static (FileStream? Stream, FileReadResult? Failure) OpenLinuxNoFollow(string path)
    {
        var fd = Native.Open(path, Native.OpenReadOnlyNonBlocking | Native.NoFollow, 0);
        if (fd < 0)
        {
            return (null, OwnedErrno(Marshal.GetLastPInvokeError()));
        }

        var handle = new SafeFileHandle(fd, ownsHandle: true);
        if (Judged(handle, owner: null) is { Length: > 0 } problem)
        {
            handle.Dispose();

            return (null, new FileReadResult.Unreadable(problem));
        }

        return (new FileStream(handle, FileAccess.Read, bufferSize: 0), null);
    }

    private static (FileStream? Stream, FileReadResult? Failure) OpenPlain(string path) =>
        EntryKindOf(path) switch
        {
            EntryKind.Missing => (null, new FileReadResult.Missing()),
            EntryKind.File => (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 0), null),
            _ => (null, new FileReadResult.Unreadable($"{NotRegular} (a link or a folder)")),
        };

    private static EntryKind EntryKindOf(string path) => RealPath.Lstat(path).Kind;
}
