using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CoaiMcp.Runners.Processes;

/// <summary>What waiting for ONE child of this process came to — a closed union, so "not mine to reap" is never read as "gone".</summary>
public abstract record Reaped
{
    private Reaped()
    {
    }

    /// <summary>The child had exited and its status was taken: its table entry is gone. <paramref name="Status"/> is <c>si_status</c> — the exit code, or the signal that ended it.</summary>
    public sealed record Yes(int Status) : Reaped;

    /// <summary>A child of this process that has not exited yet.</summary>
    public sealed record NotYet : Reaped;

    /// <summary>Not a child of this process, or already reaped (<c>ECHILD</c> — by the runtime, or by nobody): not an error; the next re-listing decides.</summary>
    public sealed record NotOurs(int Errno) : Reaped;
}

/// <summary>
/// Takes the exit status of ONE orphan that was reparented to this subreaper — <c>waitid(P_PID, pid, …, WEXITED | WNOHANG)</c> — so its zombie leaves <c>/proc</c> (DL9, the plan gate
/// for epics 4–6, F11 + F15).
/// </summary>
/// <remarks>
/// Per pid, NEVER <c>waitpid(-1)</c>: .NET reaps the children it started through its own SIGCHLD handling (<c>ProcessWaitState.Unix.cs</c>), and a status taken from a child the runtime still tracks can
/// end in the runtime's <c>FailFast</c>, not only in a wrong exit code. The caller passes only pids it LISTED in state <c>Z</c> that it did not start itself.
/// </remarks>
[SupportedOSPlatform("linux")]
internal static partial class ProcessReaping
{
    private const int PidType = 1; // P_PID
    private const int WaitExited = 4; // WEXITED
    private const int WaitNoHang = 1; // WNOHANG
    private const int SiginfoBytes = 128;
    private const int SiPidOffset = 16;
    private const int SiStatusOffset = 24;

    /// <summary>Yes(status) when <c>si_pid</c> is this pid; NotYet when the call answered 0 and <c>si_pid</c> is 0; NotOurs(errno) for -1. Never throws.</summary>
    public static Reaped Reap(int pid)
    {
        var info = new byte[SiginfoBytes];
        if (Waitid(PidType, pid, info, WaitExited | WaitNoHang) != 0)
        {
            return new Reaped.NotOurs(Marshal.GetLastPInvokeError());
        }

        return BitConverter.ToInt32(info, SiPidOffset) == pid ? new Reaped.Yes(BitConverter.ToInt32(info, SiStatusOffset)) : new Reaped.NotYet();
    }

    [LibraryImport("libc", EntryPoint = "waitid", SetLastError = true)]
    private static partial int Waitid(int idtype, int id, [Out] byte[] info, int options);
}
