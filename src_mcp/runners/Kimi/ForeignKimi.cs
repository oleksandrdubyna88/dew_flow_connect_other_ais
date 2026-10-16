namespace CoaiMcp.Runners.Kimi;

/// <summary>One running process, as a read-only listing gives it: nothing here can end it.</summary>
public sealed record ProcessEntry(int Pid, int ParentPid, string Name);

/// <summary>The seam over "which processes are running": the host's listing in the product, a fake list in a test.</summary>
public interface IProcessList
{
    /// <summary>Every live process now, with its parent's id. Read-only; empty when the host cannot say.</summary>
    IReadOnlyList<ProcessEntry> Snapshot();
}

/// <summary>What waiting for the person's own Kimi to end came to: who is still running (empty = the way is clear) and how long was waited.</summary>
public sealed record ForeignKimiWait(IReadOnlyList<ProcessEntry> Still, TimeSpan Waited);

/// <summary>
/// The person's OWN Kimi running beside a coai launch (decision 74 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>, the epic 4 code gate).
/// </summary>
/// <remarks>
/// <para><b>The risk.</b> coai's sign-in locks serialise coai's launches (and anything that inherited the tree hold). They cannot stop a
/// <c>kimi</c> the person started in their own terminal: if a coai review starts near the access token's expiry, both processes refresh the
/// ROTATING refresh token and one is left holding a token the server has already retired — the person is signed out. So before the shim takes the
/// hold and starts its agent it looks, read-only, for a <c>kimi</c> that is not coai's own; while one runs it waits within the turn's deadline,
/// polling, and past the deadline the turn is BUSY (exit 69) with the person's own words. It never ends a process.</para>
/// <para><b>How coai's own is told apart — the parent chain.</b> A <c>kimi</c> is coai's when walking its parents (from one process listing) reaches
/// this shim's own pid, or a process named <c>coai-mcp</c> (the server, or another shim, whose trees the sign-in's tree hold already serialises).
/// Chosen because it needs no elevated rights and no per-process memory: a listing gives each process's parent id to any same-user caller
/// (<c>CreateToolhelp32Snapshot</c>), whereas a process's environment (<c>KIMI_CODE_HOME</c>) or command line means reading its PEB
/// (<c>OpenProcess</c> with <c>PROCESS_VM_READ</c> — refused for protected or elevated processes, so "unreadable" would have to be guessed one way)
/// or WMI (<c>System.Management</c> is not Native-AOT safe, and this binary is). The executable's path cannot tell them apart: both are
/// <c>~/.kimi-code/bin/kimi.exe</c>.</para>
/// <para><b>The failure directions, chosen.</b> A <c>kimi</c> whose parent has ended (an orphan of a crashed server or shim) is counted as the person's:
/// it can still refresh the token, and the tree hold would call it busy anyway. A recycled pid that makes a stranger look like an ancestor is the
/// one way to miss a person's Kimi — rare, and it can only let a turn start, which is today's behaviour. Only the process NAME is trusted for
/// <c>coai-mcp</c>: a framework-dependent server started as <c>dotnet coai-mcp.dll</c> is named <c>dotnet</c>, so its OTHER shims' trees are counted as
/// foreign (the turn waits, then is busy — safe) while this shim's own tree is still recognised by pid.</para>
/// <para><b>The limit, stated.</b> This is a check at the START. A person who starts <c>kimi</c> AFTER coai's agent is already running is not
/// covered — nothing here can stop a process it does not own — and neither is a person's Kimi that refreshes in the seconds between the last poll and
/// the agent's start.</para>
/// </remarks>
public sealed class ForeignKimiWatch(IProcessList processes, int ownPid, TimeSpan poll, Action<TimeSpan> pause)
{
    private const int MostAncestors = 64;

    /// <summary>The names a Kimi runs under on Windows, the only host the shim runs Kimi on (D18).</summary>
    private static readonly string[] KimiNames = ["kimi", "kimi.exe"];

    private static readonly string[] CoaiNames = ["coai-mcp", "coai-mcp.exe"];

    /// <summary>This process, the host's process listing, polled every second, sleeping the thread.</summary>
    public static ForeignKimiWatch Real() => new(HostProcessList.Current, Environment.ProcessId, TimeSpan.FromSeconds(1), Thread.Sleep);

    /// <summary>The <c>kimi</c> processes in <paramref name="all"/> that are not coai's own (see the remarks): pure, over one listing.</summary>
    public static IReadOnlyList<ProcessEntry> Foreign(IReadOnlyList<ProcessEntry> all, int ownPid)
    {
        var byPid = all.GroupBy(entry => entry.Pid).ToDictionary(group => group.Key, group => group.First());

        return [.. all.Where(entry => IsKimi(entry) && !IsCoais(entry, byPid, ownPid))];
    }

    /// <summary>The foreign Kimi's now, and — while one runs — again every <c>poll</c> until none is, up to <paramref name="within"/>.</summary>
    public ForeignKimiWait Wait(TimeSpan within)
    {
        var waited = TimeSpan.Zero;
        var still = Foreign(processes.Snapshot(), ownPid);
        while (still.Count > 0 && waited + poll <= within)
        {
            pause(poll);
            waited += poll;
            still = Foreign(processes.Snapshot(), ownPid);
        }

        return new ForeignKimiWait(still, waited);
    }

    /// <summary>The sentence a busy turn carries: the person's own words, naming the process.</summary>
    public static string Sentence(ProcessEntry running) =>
        $"your own Kimi Code is running (pid {running.Pid}); coai does not start a second Kimi on the same sign-in while it runs, so two refreshes never race — close it or wait";

    private static bool IsKimi(ProcessEntry entry) => KimiNames.Contains(entry.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Walks the parents (bounded, so a listing with a cycle ends): coai's when the chain reaches this shim or a <c>coai-mcp</c>.</summary>
    private static bool IsCoais(ProcessEntry entry, Dictionary<int, ProcessEntry> byPid, int ownPid)
    {
        var parent = entry.ParentPid;
        for (var hop = 0; hop < MostAncestors && byPid.TryGetValue(parent, out var up); hop++)
        {
            if (up.Pid == ownPid || CoaiNames.Contains(up.Name, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            parent = up.ParentPid;
        }

        return parent == ownPid;
    }
}
