using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CoaiMcp.Runners.Processes;

/// <summary>One live process, named the only way a process can be named safely: its number AND when it started.</summary>
/// <remarks>
/// A pid is reused; a pid whose start time still matches is the process that was recorded. The same identity
/// <see cref="TrackedProcess"/> and the run markers hold, as one value both can be built from.
/// </remarks>
public sealed record ProcessIdentity(int Pid, DateTime StartedUtc);

/// <summary>
/// The processes UNDER a process — every descendant, with its identity — and the confirmation that a killed tree is gone.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> <c>Process.Kill(entireProcessTree: true)</c> walks the tree itself, and that is enough
/// to END a tree; it says nothing afterwards about whether every member died, and it cannot name the members at all. A
/// reader of a shared vendor account (story 6.1a of todo/PLAN_every_round_is_counted.md) must CONFIRM that nothing it
/// started is still holding the account's token set before the account's lease is released — so the members are
/// listed BEFORE the kill, written down, and each one checked by pid and start time after it.</para>
/// <para><b>Platforms are mapped explicitly and an unmeasured one fails closed</b> (common.platform-limits): Linux reads
/// <c>/proc/&lt;pid&gt;/stat</c> for each process's parent; Windows takes a Toolhelp32 snapshot (the process list with
/// parent ids, the API every task manager uses); macOS is NOT supported — <see cref="Supported"/> is false there, and a
/// caller that needs the confirmation refuses to spawn rather than inheriting an unprobed answer. The production host
/// is Linux; the development machines are Windows.</para>
/// <para>The start time is read through <see cref="ProcessTracking.StartedAt"/> and compared with
/// <see cref="OrphanSweep.Same"/>'s slack — one identity rule for every record that names a process here.</para>
/// </remarks>
public static class ProcessTree
{
    /// <summary>How long a confirmation keeps re-killing and re-checking a member that is still there.</summary>
    /// <remarks>
    /// A killed process leaves its table entry for a moment on every platform; two seconds is far past that and far
    /// short of a lease anybody is waiting on. Past it the member is reported as a survivor, and the caller — not this
    /// class — decides what a survivor costs.
    /// </remarks>
    public static readonly TimeSpan ConfirmationBound = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan ConfirmationStep = TimeSpan.FromMilliseconds(100);

    /// <summary>Whether this platform can list a process's descendants — Linux and Windows only.</summary>
    public static bool Supported => OperatingSystem.IsLinux() || OperatingSystem.IsWindows();

    /// <summary>
    /// Every live descendant of <paramref name="pid"/> — children, their children, and so on — each with its start time.
    /// </summary>
    /// <returns>Empty when it has none, when it is gone, or when the platform cannot say (<see cref="Supported"/>).</returns>
    /// <remarks>
    /// A process that exits between the listing and the start-time read is left out: a member that cannot be identified
    /// cannot be confirmed gone either, and listing it would promise a check nobody can make. Called BEFORE the kill — a
    /// killed parent's children are reparented, and a listing after the kill finds nothing of theirs.
    /// </remarks>
    public static IReadOnlyList<ProcessIdentity> Descendants(int pid)
    {
        if (!Supported)
        {
            return [];
        }

        return Walk(pid, OperatingSystem.IsWindows() ? WindowsParents() : LinuxParents(), ProcessTracking.StartedAt);
    }

    /// <summary>The descendants of <paramref name="root"/> in a pid → parent-pid map, each with its start time.</summary>
    /// <remarks>
    /// A table records a parent NUMBER, and a number is reused: a process whose parent died is listed under whoever holds
    /// that number now. A child cannot start before its parent, so an entry that did — beyond the identity slack — is
    /// not a child, and neither is anything under it. A root with no start time is gone, and has nothing to tell apart.
    /// </remarks>
    internal static IReadOnlyList<ProcessIdentity> Walk(int root, IReadOnlyDictionary<int, int> parents, Func<int, DateTime?> startedAt)
    {
        if (startedAt(root) is not { } rootStarted)
        {
            return [];
        }

        var found = new List<ProcessIdentity>();
        var frontier = new Queue<ProcessIdentity>([new ProcessIdentity(root, rootStarted)]);
        while (frontier.TryDequeue(out var parent))
        {
            foreach (var child in ChildrenOf(parent, parents, startedAt))
            {
                frontier.Enqueue(child);
                found.Add(child);
            }
        }

        return found;
    }

    /// <summary>The direct children of <paramref name="parent"/>: listed under its number AND started no earlier than it.</summary>
    private static IEnumerable<ProcessIdentity> ChildrenOf(
        ProcessIdentity parent, IReadOnlyDictionary<int, int> parents, Func<int, DateTime?> startedAt) =>
        parents.Where(entry => entry.Value == parent.Pid)
            .Select(entry => (Pid: entry.Key, Started: startedAt(entry.Key)))
            .Where(child => child.Started is { } started && BornAfter(started, parent.StartedUtc))
            .Select(child => new ProcessIdentity(child.Pid, child.Started!.Value));

    private static bool BornAfter(DateTime child, DateTime parent) => child >= parent || OrphanSweep.Same(child, parent);

    /// <summary>
    /// Every live descendant of the process <paramref name="identity"/> names — or none when that pid is somebody else's
    /// now, whose children are nothing of ours.
    /// </summary>
    public static IReadOnlyList<ProcessIdentity> DescendantsOf(ProcessIdentity identity) =>
        IsAlive(identity) ? Descendants(identity.Pid) : [];

    /// <summary>True while the process with that pid is the one <paramref name="identity"/> names and has not exited.</summary>
    /// <remarks>An exited process its parent has not reaped yet — a Linux zombie — is NOT alive; see <see cref="Exited"/>.</remarks>
    public static bool IsAlive(ProcessIdentity identity) =>
        ProcessTracking.StartedAt(identity.Pid) is { } started && OrphanSweep.Same(started, identity.StartedUtc) && !Exited(identity.Pid);

    /// <summary>Kills the process <paramref name="identity"/> names — its whole tree — if it is still that process.</summary>
    /// <returns>False when the kill was refused or the process is not ours any more; true when the signal was sent.</returns>
    /// <remarks>
    /// The identity is checked FIRST, under the same rule <see cref="OrphanSweep"/> applies: a reused number is somebody
    /// else's process, and "kill every codex" is a product that terminates its user's terminal session.
    /// </remarks>
    public static bool Terminate(ProcessIdentity identity)
    {
        if (!IsAlive(identity))
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(identity.Pid);
            process.Kill(entireProcessTree: true);

            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException or UnauthorizedAccessException)
        {
            // Exited between the check and the kill, or not ours to signal. Both are answered by the confirmation below:
            // a process that is still there afterwards is a survivor, whatever this call could or could not do.
            return false;
        }
    }

    /// <summary>
    /// Waits, up to <see cref="ConfirmationBound"/>, for every member to be gone — re-killing any that is still there —
    /// and answers with the ones that survived.
    /// </summary>
    /// <returns>Empty when the whole tree is confirmed gone; otherwise the members still alive at the bound.</returns>
    public static async Task<IReadOnlyList<ProcessIdentity>> ConfirmGoneAsync(IReadOnlyList<ProcessIdentity> members)
    {
        var deadline = DateTime.UtcNow + ConfirmationBound;
        var alive = members.Where(IsAlive).ToList();
        while (alive.Count > 0 && DateTime.UtcNow < deadline)
        {
            foreach (var member in alive)
            {
                Terminate(member);
            }

            await Task.Delay(ConfirmationStep);
            alive = alive.Where(IsAlive).ToList();
        }

        return alive;
    }

    /// <summary>Every process on this Linux box, as pid → parent pid, read from <c>/proc</c>.</summary>
    private static Dictionary<int, int> LinuxParents()
    {
        var parents = new Dictionary<int, int>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var pid))
            {
                continue;
            }

            if (ParentOf(Path.Combine(directory, "stat")) is { } parent)
            {
                parents[pid] = parent;
            }
        }

        return parents;
    }

    /// <summary>The parent pid in one <c>/proc/&lt;pid&gt;/stat</c> line — the field after the state, which follows the bracketed name.</summary>
    private static int? ParentOf(string statPath) =>
        StatFields(statPath) is [_, var field, ..] && int.TryParse(field, out var parent) ? parent : null;

    /// <summary>
    /// True when the Linux process <paramref name="pid"/> has exited and only its table entry is left — a zombie
    /// (<c>Z</c>) or one being removed (<c>X</c>).
    /// </summary>
    /// <remarks>
    /// A killed process keeps its <c>/proc</c> entry, start time included, until its parent reaps it; under a parent that
    /// never reaps, that is for ever. It holds no file, socket or token any more, so for "is anything of the reader
    /// still running" it is gone. Always false off Linux, where the start-time read already fails for an exited process.
    /// </remarks>
    private static bool Exited(int pid) =>
        OperatingSystem.IsLinux() && StatFields($"/proc/{pid}/stat") is [var state, ..] && ExitedEntry(state, ThreadsOf(pid), pid);

    /// <summary>Whether a process table entry in <paramref name="state"/>, whose thread group lists <paramref name="threads"/>, has exited.</summary>
    /// <remarks>
    /// The thread-group leader reads <c>Z</c> once its OWN thread has exited, while the group's other threads still run
    /// and still hold what the process held — so an entry has exited only when no thread but the leader is left.
    /// </remarks>
    internal static bool ExitedEntry(string state, IReadOnlyCollection<int> threads, int pid) =>
        state is "Z" or "X" && threads.All(thread => thread == pid);

    /// <summary>The thread ids of <paramref name="pid"/>'s group, from <c>/proc/&lt;pid&gt;/task</c>.</summary>
    /// <returns><c>[-1]</c> when the listing cannot be read — a thread nobody can rule out, so the entry is not called exited.</returns>
    private static int[] ThreadsOf(int pid)
    {
        try
        {
            return [.. Directory.EnumerateDirectories($"/proc/{pid}/task").Select(path => int.TryParse(Path.GetFileName(path), out var thread) ? thread : -1)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [-1];
        }
    }

    /// <summary>The fields of one <c>/proc/&lt;pid&gt;/stat</c> line AFTER the bracketed name: state, parent pid, …</summary>
    /// <remarks>The name may itself hold spaces and brackets, so the fields are counted from the LAST closing bracket.</remarks>
    /// <returns>Empty when the line is unreadable — the process exited between the listing and this read.</returns>
    private static string[] StatFields(string statPath)
    {
        try
        {
            var stat = File.ReadAllText(statPath);
            var afterName = stat.LastIndexOf(')');

            return afterName < 0 ? [] : stat[(afterName + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A process that exited between the directory listing and this read. Nothing to say about it.
            return [];
        }
    }

    private const uint SnapshotOfProcesses = 0x00000002;

    /// <summary>Every process on this Windows box, as pid → parent pid, from one Toolhelp32 snapshot.</summary>
    private static Dictionary<int, int> WindowsParents()
    {
        var parents = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(SnapshotOfProcesses, 0);
        if (snapshot == nint.Zero || snapshot == -1)
        {
            return parents;
        }

        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (Process32FirstW(snapshot, ref entry) == 0)
            {
                return parents;
            }

            do
            {
                parents[(int)entry.ProcessId] = (int)entry.ParentProcessId;
            }
            while (Process32NextW(snapshot, ref entry) != 0);
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return parents;
    }

    /// <summary><c>PROCESSENTRY32W</c>, laid out as the kernel defines it; only the two ids are read.</summary>
    /// <remarks>
    /// Every field is blittable — the <c>BOOL</c> answers are <c>int</c>s and the name buffer is an inline array of
    /// UTF-16 units — so these are classic <c>DllImport</c>s with no marshalling stub, no unsafe code and nothing for
    /// the AOT analyser to object to. The library's own name is the one thing that reaches the kernel.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nuint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        public ExeFile ExeFile;
    }

    /// <summary>The entry's <c>MAX_PATH</c> name buffer, as the 16-bit units it is made of.</summary>
    [InlineArray(260)]
    private struct ExeFile
    {
        private ushort _first;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int Process32FirstW(nint snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int Process32NextW(nint snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int CloseHandle(nint handle);
}
