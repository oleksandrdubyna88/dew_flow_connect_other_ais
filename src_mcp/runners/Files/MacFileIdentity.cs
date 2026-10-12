using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CoaiMcp.Runners.Files;

/// <summary>
/// Whether an opened handle and a path are the SAME file on macOS — the device and inode <c>fstat</c> reports for the
/// handle against those <c>lstat</c> reports for the path, which is the one question about an opened file macOS answers
/// without naming it (<see cref="HandleRead"/> says why it cannot be named there).
/// </summary>
/// <remarks>
/// <para>The cadence consultation of epics 4-5 (2026-10-12): the macOS fallback walked the path for links again after
/// the open, so a link swapped in for the open and the directory put back before the walk passed it — and the outside
/// file's bytes were served. A file opened through such a link is a different inode from the one the restored path
/// names, so this check refuses it, whatever the path looks like by then.</para>
/// <para>The 64-bit-inode <c>struct stat</c>: <c>st_dev</c> (int32) at 0, <c>st_ino</c> (uint64) at 8, 144 bytes in all.
/// arm64 has only that layout under the plain names; x64 reaches it through the <c>$INODE64</c> entry points. Anything
/// that cannot be asked — a missing library or entry point, a failed call — is not known to be the same file.</para>
/// </remarks>
internal static partial class MacFileIdentity
{
    private const string LibSystem = "/usr/lib/libSystem.B.dylib";

    /// <summary>Room past the 144 bytes of <c>struct stat</c>, so a layout that ever grows cannot overrun the buffer.</summary>
    private const int StatBytes = 256;

    public static bool SameEntry(SafeFileHandle handle, string path)
    {
        var opened = OfHandle(handle);
        var named = OfPath(path);

        return opened.Known && named.Known && opened == named;
    }

    private readonly record struct Identity(bool Known, int Device, ulong Inode);

    private static unsafe Identity OfHandle(SafeFileHandle handle)
    {
        try
        {
            var stat = stackalloc byte[StatBytes];
            var fd = (int)handle.DangerousGetHandle();
            var result = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? FStat(fd, stat) : FStatInode64(fd, stat);

            return result == 0 ? Of(stat) : default;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return default;
        }
    }

    private static unsafe Identity OfPath(string path)
    {
        try
        {
            var stat = stackalloc byte[StatBytes];
            var result = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? LStat(path, stat) : LStatInode64(path, stat);

            return result == 0 ? Of(stat) : default;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return default;
        }
    }

    private static unsafe Identity Of(byte* stat) => new(true, *(int*)stat, *(ulong*)(stat + 8));

    [LibraryImport(LibSystem, EntryPoint = "fstat", SetLastError = true)]
    private static unsafe partial int FStat(int fd, byte* stat);

    [LibraryImport(LibSystem, EntryPoint = "fstat$INODE64", SetLastError = true)]
    private static unsafe partial int FStatInode64(int fd, byte* stat);

    [LibraryImport(LibSystem, EntryPoint = "lstat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static unsafe partial int LStat(string path, byte* stat);

    [LibraryImport(LibSystem, EntryPoint = "lstat$INODE64", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static unsafe partial int LStatInode64(string path, byte* stat);
}
