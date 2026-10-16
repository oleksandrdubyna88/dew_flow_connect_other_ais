using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CoaiMcp.Runners.Processes;

// Ported in part from wsl_care (MIT, same owner) src_daemon/src/WslCare.Core/Processes/ProcessSignals.cs: `IPidfdCalls` (:85-99) and `LibcPidfdCalls` (:281-332), glibc spelled
// "libc" (the AppendOnlyFile precedent) and not "libc.so.6". NOT ported: wsl_care's `ProcessIdentity(Pid, long StartTicks)`, which clashes by name with this repository's
// `ProcessIdentity(Pid, DateTime StartedUtc)` — the one identity here — and the shell around it (see TreeSignals). NEW here, decided by DL9 of
// todo/PLAN_kimi_and_vibe_on_linux.md (the plan gate for epics 4-6, 2026-10-16, F4): `RawSyscallPidfdCalls` and the `PidfdLayer` that picks between the two and refuses a kernel with no pidfd.

/// <summary>The pidfd calls: a descriptor that PINS one process, a signal sent through it, and a wait for its end. The seam the layer is tested on.</summary>
public interface IPidfdCalls
{
    /// <summary><c>pidfd_open</c>: the descriptor, or <c>-errno</c>.</summary>
    int Open(int pid);

    /// <summary><c>pidfd_send_signal</c>: 0, or the errno.</summary>
    int Signal(int fd, int signal);

    /// <summary><c>poll</c> of the descriptors for readability (a pidfd is readable once its process ended): how many are ready (their <paramref name="ready"/> flags set), 0 when
    /// none within <paramref name="milliseconds"/>, or <c>-errno</c>.</summary>
    int Poll(IReadOnlyList<int> fds, bool[] ready, int milliseconds);

    void Close(int fd);
}

/// <summary>glibc's wrappers (<c>pidfd_open</c>, <c>pidfd_send_signal</c>): glibc 2.36 and later — Ubuntu 24.04 has 2.39, Ubuntu 22.04 has 2.35 and none.</summary>
[SupportedOSPlatform("linux")]
public sealed partial class LibcPidfdCalls : IPidfdCalls
{
    public int Open(int pid)
    {
        var fd = PidfdOpen(pid, 0);

        return fd >= 0 ? fd : -Marshal.GetLastPInvokeError();
    }

    public int Signal(int fd, int signal) => PidfdSendSignal(fd, signal, nint.Zero, 0) == 0 ? 0 : Marshal.GetLastPInvokeError();

    public int Poll(IReadOnlyList<int> fds, bool[] ready, int milliseconds) => PidfdPoll.Poll(fds, ready, milliseconds);

    public void Close(int fd) => PidfdPoll.Close(fd);

    [LibraryImport("libc", EntryPoint = "pidfd_open", SetLastError = true)]
    private static partial int PidfdOpen(int pid, uint flags);

    [LibraryImport("libc", EntryPoint = "pidfd_send_signal", SetLastError = true)]
    private static partial int PidfdSendSignal(int pidfd, int signal, nint info, uint flags);
}

/// <summary>
/// The raw system calls, for a libc with no wrappers: <c>syscall(434, pid, 0)</c> and <c>syscall(424, fd, signal, NULL, 0)</c> — the same numbers on x86-64 and arm64 (both use the
/// generic table), present in every kernel from 5.3.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed partial class RawSyscallPidfdCalls : IPidfdCalls
{
    /// <summary><c>pidfd_open</c>.</summary>
    public const int SyscallOpen = 434;

    /// <summary><c>pidfd_send_signal</c>.</summary>
    public const int SyscallSendSignal = 424;

    public int Open(int pid)
    {
        var fd = Syscall(SyscallOpen, pid, 0, 0, 0);

        return fd >= 0 ? (int)fd : -Marshal.GetLastPInvokeError();
    }

    public int Signal(int fd, int signal) => Syscall(SyscallSendSignal, fd, signal, 0, 0) == 0 ? 0 : Marshal.GetLastPInvokeError();

    public int Poll(IReadOnlyList<int> fds, bool[] ready, int milliseconds) => PidfdPoll.Poll(fds, ready, milliseconds);

    public void Close(int fd) => PidfdPoll.Close(fd);

    [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static partial nint Syscall(nint number, nint first, nint second, nint third, nint fourth);
}

/// <summary>The two calls both roads share: <c>poll</c> and <c>close</c>.</summary>
[SupportedOSPlatform("linux")]
internal static partial class PidfdPoll
{
    private const short PollIn = 1;

    public static int Poll(IReadOnlyList<int> fds, bool[] ready, int milliseconds)
    {
        var entries = fds.Select(fd => new PollFd { Fd = fd, Events = PollIn }).ToArray();
        var count = PollNative(entries, (nuint)entries.Length, milliseconds);
        if (count < 0)
        {
            return -Marshal.GetLastPInvokeError();
        }

        for (var i = 0; i < entries.Length; i++)
        {
            ready[i] = entries[i].Revents != 0;
        }

        return count;
    }

    public static void Close(int fd) => _ = CloseNative(fd);

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static partial int PollNative([In, Out] PollFd[] fds, nuint count, int timeoutMilliseconds);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int CloseNative(int fd);
}

/// <summary>Which road ends a process by identity on this host — a closed union, so "this kernel cannot" always carries the sentence to show.</summary>
public abstract record PidfdSupport
{
    private PidfdSupport()
    {
    }

    /// <summary>glibc 2.36 or later: libc's own <c>pidfd_open</c> and <c>pidfd_send_signal</c>.</summary>
    public sealed record Wrapper(IPidfdCalls Calls) : PidfdSupport;

    /// <summary>No libc wrappers: <c>syscall(434)</c> and <c>syscall(424)</c> — every kernel from 5.3.</summary>
    public sealed record Raw(IPidfdCalls Calls) : PidfdSupport;

    /// <summary>The kernel has no pidfd (the call answered <c>ENOSYS</c>): the launch is refused with this reason before anything starts.</summary>
    public sealed record Missing(string Reason) : PidfdSupport;
}

/// <summary>The pidfd layer, resolved once per process: the wrapper where libc has it, the raw syscall where it does not, a refusal where the kernel has neither — never a kill by number.</summary>
public static class PidfdLayer
{
    /// <summary><c>ENOSYS</c>.</summary>
    private const int NoSystemCall = 38;

    /// <summary>The refusal for a kernel before 5.3 (DL9; the wording is the plan's).</summary>
    public const string NoPidfdSentence =
        "this kernel has no pidfd (`pidfd_open` answered ENOSYS; Linux 5.3 or later is needed), so coai cannot end Kimi's process tree by identity and does not launch it";

    private static readonly AsyncLocal<PidfdSupport?> Overridden = new();

    private static readonly Lazy<PidfdSupport> Resolved = new(ResolveThisHost);

    /// <summary>This process's pidfd layer: resolved once on Linux (the probe is one <c>pidfd_open</c> of this process); a test's override per async flow.</summary>
    public static PidfdSupport Current => Overridden.Value ?? Resolved.Value;

    /// <summary>Empty when Kimi may be launched on this kernel; else <see cref="NoPidfdSentence"/>. Off Linux: empty — pidfd is a Linux question.</summary>
    public static string Refusal() =>
        Overridden.Value is null && !OperatingSystem.IsLinux() ? string.Empty : Current is PidfdSupport.Missing missing ? missing.Reason : string.Empty;

    /// <summary>TEST ONLY — per async flow (the <c>VibePathBudget.Using</c> shape): force "no wrapper" and/or a fake <see cref="IPidfdCalls"/>; the probe runs on the fake.</summary>
    internal static IDisposable Using(bool wrapperPresent, IPidfdCalls calls)
    {
        var before = Overridden.Value;
        Overridden.Value = Probed(wrapperPresent, calls);

        return new Restore(() => Overridden.Value = before);
    }

    /// <summary>TEST ONLY — force the road, over the REAL calls of that road (Linux): "no wrapper" is the raw syscall against the real kernel.</summary>
    [SupportedOSPlatform("linux")]
    internal static IDisposable Using(bool wrapperPresent) => Using(wrapperPresent, wrapperPresent ? new LibcPidfdCalls() : new RawSyscallPidfdCalls());

    private static PidfdSupport ResolveThisHost()
    {
        if (!OperatingSystem.IsLinux())
        {
            return new PidfdSupport.Missing("pidfd is a Linux call");
        }

        var wrappers = WrapperPresent();

        return Probed(wrappers, wrappers ? new LibcPidfdCalls() : new RawSyscallPidfdCalls());
    }

    /// <summary>Whether libc exports BOTH wrappers — looked up by name, which works under Native AOT; the imports themselves stay "libc".</summary>
    [SupportedOSPlatform("linux")]
    private static bool WrapperPresent() =>
        NativeLibrary.TryLoad("libc.so.6", out var libc) && NativeLibrary.TryGetExport(libc, "pidfd_open", out _) && NativeLibrary.TryGetExport(libc, "pidfd_send_signal", out _);

    /// <summary>The road on <paramref name="calls"/>, unless the kernel answers <c>ENOSYS</c> to pinning this very process; any other answer (a descriptor, closed at once, or another errno) keeps it.</summary>
    private static PidfdSupport Probed(bool wrapperPresent, IPidfdCalls calls)
    {
        var fd = calls.Open(Environment.ProcessId);
        if (fd >= 0)
        {
            calls.Close(fd);
        }

        return -fd == NoSystemCall ? new PidfdSupport.Missing(NoPidfdSentence) : wrapperPresent ? new PidfdSupport.Wrapper(calls) : new PidfdSupport.Raw(calls);
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}
