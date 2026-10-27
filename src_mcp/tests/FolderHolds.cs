using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CoaiMcp.Tests;

/// <summary>
/// Ways to make an EMPTY folder that something else is using or that cannot be removed — for the arms of S7 of
/// <c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c>: a round that ends must leave such a folder to the sweep, say so, and not fail.
/// </summary>
internal static class FolderHolds
{
    /// <summary>FILE_LIST_DIRECTORY: an open for attributes only takes no part in sharing, so it would not hold the folder.</summary>
    private const uint ListDirectory = 0x1;
    private const uint ShareReadWrite = 0x3;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;

    /// <summary>
    /// Windows: an open handle on the folder that does not share DELETE, so removing it is a sharing violation — the same refusal a folder in use meets.
    /// Unix: the folder's PARENT without write permission, so removing the folder is refused (a process that is privileged ignores the mode, and the arm is skipped by the caller).
    /// </summary>
    public static IDisposable Undeletable(string folder) =>
        OperatingSystem.IsWindows() ? new WindowsHandle(folder) : new ReadOnlyParent(Path.GetDirectoryName(folder) ?? folder);

    /// <summary>
    /// Unix: the folder itself without write permission, so no FILE can be created in it — which is how a write that goes by a temp file and a move is made to fail
    /// (a rename over a read-only FILE succeeds on Unix, so the read-only attribute no longer does it; E3 S5). Windows: nothing, the test's read-only file already refuses the move.
    /// Skip the arm when <see cref="CanMakeOneUndeletable"/> is false: a privileged Unix process ignores the mode.
    /// </summary>
    public static IDisposable Unwritable(string folder) =>
        OperatingSystem.IsWindows() ? new NoHold() : new ReadOnlyParent(folder);

    /// <summary>Whether <see cref="Undeletable"/> works here — a privileged Unix process removes what the mode forbids.</summary>
    public static bool CanMakeOneUndeletable => OperatingSystem.IsWindows() || !Environment.IsPrivilegedProcess;

    /// <summary>
    /// A real second process whose WORKING FOLDER is <paramref name="folder"/>: it has said <c>ready</c> (read from its pipe — an event, never a sleep) and closed all three
    /// pipes, and waits. A descendant of a round that outlives it. Disposing kills it BY ITS PID, never by image name.
    /// </summary>
    public static Descendant StartDescendantIn(string folder)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli"), ["cwd-hold"])
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var launched = Process.Start(start) ?? throw new InvalidOperationException("the descendant did not start");
        var ready = launched.StandardOutput.ReadLine() ?? string.Empty;
        if (!ready.StartsWith("ready ", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"the descendant did not say ready: '{ready}'");
        }

        return new Descendant(launched.Id);
    }

    /// <summary>A started descendant, known by its pid.</summary>
    internal sealed class Descendant(int pid) : IDisposable
    {
        public int Pid => pid;

        public void Dispose()
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill();
                process.WaitForExit();
            }
            catch (ArgumentException)
            {
                // Already gone.
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }
        }
    }

    private sealed class WindowsHandle : IDisposable
    {
        private readonly SafeFileHandle _handle;

        public WindowsHandle(string folder)
        {
            _handle = CreateFileW(folder, ListDirectory, ShareReadWrite, 0, OpenExisting, BackupSemantics, 0);
            if (_handle.IsInvalid)
            {
                throw new IOException($"could not hold {folder}: {Marshal.GetLastWin32Error()}");
            }
        }

        public void Dispose() => _handle.Dispose();

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    }

    private sealed class NoHold : IDisposable
    {
        public void Dispose()
        {
        }
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private sealed class ReadOnlyParent : IDisposable
    {
        private readonly string _parent;
        private readonly UnixFileMode _before;

        public ReadOnlyParent(string parent)
        {
            _parent = parent;
            _before = File.GetUnixFileMode(parent);
            File.SetUnixFileMode(parent, _before & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite));
        }

        public void Dispose() => File.SetUnixFileMode(_parent, _before);
    }
}
