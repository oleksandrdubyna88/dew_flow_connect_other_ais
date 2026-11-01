using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Kimi;

/// <summary>
/// The Linux process listing for <see cref="ForeignKimiWatch"/>, read from <c>/proc</c> through <see cref="IProcReads"/> (DL10 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>, A2 §5.5): one <see cref="ProcessEntry"/> per process
/// this account owns that has not exited, with its parent and <c>comm</c> from the one <c>stat</c> reader (<see cref="ProcStat"/>) and its <c>exe</c> basename as the name — or, when that <c>exe</c> is hidden, the basename of
/// <c>argv[0]</c> (DL10, re-decided 2026-10-17).
/// </summary>
/// <remarks>
/// <para><b>The executable is the witness, not <c>comm</c></b> (L-K6): a running <c>kimi acp</c> reads <c>MainThread</c>. A binary a reinstall unlinked is still the same running Kimi, so one trailing <c> (deleted)</c> is stripped before the
/// basename is taken.</para>
/// <para><b>Only this account's processes are judged — by the uids the process's own <c>status</c> writes, never by who owns its <c>/proc/&lt;pid&gt;</c> directory</b> (a non-dumpable process of this account has a root-owned directory, and
/// reading the directory would read a refresher as absent; a <c>status</c> that cannot be read is an unreadable listing, never a skip): another account's <c>exe</c> answers <c>EACCES</c> (everyone's does for <c>/proc/1</c>), and it cannot refresh this person's <c>0700</c> sign-in folder — so it is skipped, not counted
/// unreadable, or every launch would be Busy. <b>A same-account process can still hide its <c>exe</c></b> (<c>PTRACE_MODE_READ_FSCREDS</c>: extra capabilities, or non-dumpable — <c>systemd --user</c> on the ubuntu runner, <c>systemd</c> and
/// <c>(sd-pam)</c> on the owner's WSL2), so the identity is read in this order: (1) the <c>exe</c> basename; (2) when <c>exe</c> is denied, the basename of <c>argv[0]</c> from <c>cmdline</c> — <c>comm</c> is read beside it, and
/// <c>kimi</c> in either is Kimi; (3) only when BOTH are unreadable is the listing <see cref="ProcessListing.Unreadable"/>, naming the pid and what could not be read. A process that left between the
/// reads (<c>ESRCH</c>/<c>ENOENT</c>) is skipped. <i>Residual, stated:</i> a non-dumpable <c>kimi</c> started under another <c>argv[0]</c> is not recognised — the same boundary as a <c>kimi</c> started under another file name;
/// <c>argv[0]</c> is the process's own claim, and the threat here is the person's own <c>kimi</c>, not a process hiding from coai. <i>And why the uid is read from <c>status</c>:</i> the kernel makes
/// <c>/proc/&lt;pid&gt;</c> of a NON-DUMPABLE process owned by <c>root</c>, not by its account, so judging by the directory skipped exactly the processes whose <c>exe</c> is hidden — a refresher read as absent (the E5 risk consultant,
/// 2026-10-17). <c>status</c> is world-readable and says whose the process is, so such a process is judged and falls to the <c>cmdline</c> step above.</para>
/// <para> An exited entry (<c>Z</c>/<c>X</c>) is skipped before its <c>exe</c> is read: a zombie's cannot be, and an unrelated zombie must not
/// make every Kimi launch Busy (S-K13b). A process that left between the listing and the read is skipped (<see cref="ProcStatRead.Gone"/>), never evidence of anything.</para>
/// <para><b>An unreadable or incomplete <c>/proc</c> is never "nobody is running":</b> any failure above makes the whole listing <see cref="ProcessListing.Unreadable"/>, and a listing that does not hold this process's own pid is not a
/// listing of this machine.</para>
/// </remarks>
public sealed class LinuxProcessListing(IProcReads reads, int ownPid) : IProcessList
{
    private const string DeletedSuffix = " (deleted)";

    /// <summary>The real <c>/proc</c>, and this process as the pid the listing must contain.</summary>
    public static LinuxProcessListing Real() => new(ProcReads.Real, Environment.ProcessId);

    public ProcessListing Snapshot()
    {
        try
        {
            return Walked([.. reads.Pids()], reads.OwnUid());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new ProcessListing.Unreadable($"/proc could not be listed ({e.GetType().Name})");
        }
    }

    /// <summary>Judges the processes in order and stops at the FIRST one that cannot be judged: the listing is unreadable whatever the rest say, and judging them costs a read each.</summary>
    private ProcessListing Walked(List<int> pids, uint own)
    {
        var found = new List<ProcessEntry>();
        foreach (var verdict in pids.Select(pid => Judged(pid, own)))
        {
            if (verdict is Verdict.Failed failed)
            {
                return new ProcessListing.Unreadable(failed.Reason);
            }

            if (verdict is Verdict.Found entry)
            {
                found.Add(entry.Entry);
            }
        }

        return Complete(found);
    }

    private ProcessListing Complete(IReadOnlyList<ProcessEntry> found) =>
        found.Any(entry => entry.Pid == ownPid)
            ? new ProcessListing.Listed(found)
            : new ProcessListing.Unreadable("the listing does not hold this process's own pid, so it is not a listing of this machine");

    /// <summary>One <c>/proc/&lt;pid&gt;</c>: skipped, found, or the reason the listing cannot be trusted.</summary>
    private Verdict Judged(int pid, uint own)
    {
        try
        {
            return ProcStatus.RunsAs(reads.StatusText(pid), own) switch
            {
                true => Described(pid),
                false => new Verdict.Skipped(),
                _ => new Verdict.Failed($"/proc/{pid}/status: no Uid line, so whose process it is cannot be told"),
            };
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new Verdict.Skipped();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Verdict.Failed($"/proc/{pid}/status: {e.Message}");
        }
    }

    private Verdict Described(int pid) => ProcStat.ReadFor(pid, reads) switch
    {
        ProcStatRead.Read read => ProcessTree.ExitedEntry(read.Line.State, reads.Threads(pid), pid) ? new Verdict.Skipped() : Executable(pid, read.Line),
        ProcStatRead.Unreadable unreadable => new Verdict.Failed($"/proc/{pid}/stat: {unreadable.Reason}"),
        _ => new Verdict.Skipped(),
    };

    private Verdict Executable(int pid, ProcStatLine line)
    {
        try
        {
            return new Verdict.Found(new ProcessEntry(pid, line.ParentPid, BaseName(reads.ExeTarget(pid)), line.Comm));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ArgvZero(pid, line, e.Message);
        }
    }

    /// <summary>The <c>exe</c> is hidden (<paramref name="exeReason"/>): the process's own claim, <c>argv[0]</c>, names it. Gone is skipped; only a <c>cmdline</c> that is hidden too is a failure.</summary>
    private Verdict ArgvZero(int pid, ProcStatLine line, string exeReason)
    {
        try
        {
            return new Verdict.Found(new ProcessEntry(pid, line.ParentPid, Path.GetFileName(FirstArgument(reads.Cmdline(pid))), line.Comm));
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new Verdict.Skipped();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Verdict.Failed($"pid {pid} can be identified neither by /proc/{pid}/exe ({exeReason}) nor by /proc/{pid}/cmdline ({e.Message})");
        }
    }

    /// <summary>The first NUL-ended argument of a <c>cmdline</c>; empty for none (a kernel thread, or a process that blanked its own).</summary>
    private static string FirstArgument(string cmdline) => cmdline.Split('\0', 2)[0];

    /// <summary>The file name of an <c>exe</c> target, after ONE trailing <c> (deleted)</c> is taken off; empty for no executable (a kernel thread).</summary>
    private static string BaseName(string target) =>
        Path.GetFileName(target.EndsWith(DeletedSuffix, StringComparison.Ordinal) ? target[..^DeletedSuffix.Length] : target);

    /// <summary>What judging one process came to.</summary>
    private abstract record Verdict
    {
        private Verdict()
        {
        }

        public sealed record Skipped : Verdict;

        public sealed record Found(ProcessEntry Entry) : Verdict;

        public sealed record Failed(string Reason) : Verdict;
    }
}
