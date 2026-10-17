using System.Diagnostics;
using System.Runtime.Versioning;

namespace CoaiMcp.Runners.Processes;

/// <summary>
/// The Linux end of a tree the shim owns as a CHILD SUBREAPER (DL9 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>): list while the agent lives, write down what was listed BEFORE any
/// signal, end every member by identity through a pidfd, re-list until nothing is left or the bound passes, reap the zombies it listed — and answer with what survived.
/// </summary>
/// <remarks>
/// <para><b>Why not <c>Process.Kill(entireProcessTree)</c>.</b> It cannot reach a child that opened its own session (L-K4: <c>rg</c> survives a pid and a group kill), it finds nothing once the agent
/// has exited and its children were reparented, and it signals by number. Here every member is pinned by a descriptor, checked on the pin and signalled through it (<see cref="TreeSignals"/>).</para>
/// <para><b>Order is the contract:</b> (1) the members are listed while the agent lives — <c>Descendants(shimPid)</c> with the exited entries dropped, plus the agent; (2) <c>onListed</c> — the marker is
/// written BEFORE any signal; (3) <c>SIGKILL</c> through a pidfd per member; (4) every step re-lists <c>Descendants(shimPid)</c> (an orphan born after the first listing is found by its new parent, the shim),
/// reaps the zombies it listed that this process did not start, and ends what is alive again, until nothing is alive or <see cref="ProcessTree.ConfirmationBound"/> passes; (5) stdin closed and the agent's exit
/// awaited with no token — a <c>SIGKILL</c>ed process exits, and .NET must reap it itself; (6) the members alive at the bound are the survivors.</para>
/// <para><b>Reaping is per pid and sparing</b> (<see cref="ProcessReaping"/>): never a pid in <c>startedByUs</c> — the children .NET started and reaps through its own SIGCHLD handling — and never a pid it did
/// not list. A zombie is never a survivor: <see cref="ProcessTree.IsAlive"/> drops an exited entry (the one rule, reused).</para>
/// <para><b>The bound is the descendants'.</b> The agent's own wait is unbounded on purpose (<c>ProcessLauncher.EndItAsync</c>: a cancelled wait returns before the handle is free).</para>
/// </remarks>
[SupportedOSPlatform("linux")]
public static class SupervisedTeardown
{
    /// <param name="agent">The agent the shim started.</param>
    /// <param name="shimPid">This process — the subreaper, whose descendants the agent's whole tree now is.</param>
    /// <param name="startedByUs">Every pid this process started through <c>Process.Start</c> (today only the agent): the drain never takes their status.</param>
    /// <param name="calls">The pidfd road of this host (<see cref="PidfdLayer"/>).</param>
    /// <param name="onListed">Told the members the first listing found, before the first signal. It must not throw: the kill is never skipped.</param>
    public static async Task<TreeEnd> EndAsync(
        Process agent, int shimPid, IReadOnlySet<int> startedByUs, IPidfdCalls calls, Action<IReadOnlyList<ProcessIdentity>> onListed)
    {
        var agentIdentity = IdentityOf(agent);
        var members = Listed(shimPid, agentIdentity);
        onListed(members);
        _ = TreeSignals.Kill(members, calls);

        var survivors = await ConfirmedAsync(shimPid, agentIdentity, startedByUs, calls);

        ProcessLauncher.Close(agent.StandardInput);
        await agent.WaitForExitAsync(CancellationToken.None);

        return new TreeEnd(survivors);
    }

    /// <summary>The agent's identity: its pid and the start the kernel reports — or "now" when that cannot be read a moment after it started, a process that is already gone.</summary>
    private static ProcessIdentity IdentityOf(Process agent) => new(agent.Id, ProcessTracking.StartedAt(agent.Id) ?? DateTime.UtcNow);

    /// <summary>The shim's live descendants plus the agent when it is alive, de-duplicated by pid — an exited entry is dropped by the one rule, <see cref="ProcessTree.IsAlive"/>.</summary>
    private static IReadOnlyList<ProcessIdentity> Listed(int shimPid, ProcessIdentity agent) =>
        [.. AliveOf(ProcessTree.Descendants(shimPid), agent)];

    private static IEnumerable<ProcessIdentity> AliveOf(IEnumerable<ProcessIdentity> descendants, ProcessIdentity agent) =>
        descendants.Append(agent).Where(ProcessTree.IsAlive).DistinctBy(member => member.Pid);

    /// <summary>Step 4: re-list, reap, and end again, until nothing is alive or the bound — the members alive then are the survivors.</summary>
    private static async Task<IReadOnlyList<ProcessIdentity>> ConfirmedAsync(int shimPid, ProcessIdentity agent, IReadOnlySet<int> startedByUs, IPidfdCalls calls)
    {
        var deadline = DateTime.UtcNow + ProcessTree.ConfirmationBound;
        while (true)
        {
            var descendants = ProcessTree.Descendants(shimPid);
            Reap(descendants, startedByUs);
            var alive = AliveOf(descendants, agent).ToList();
            if (alive.Count == 0 || DateTime.UtcNow >= deadline)
            {
                return alive;
            }

            _ = TreeSignals.Kill(alive, calls);
            await Task.Delay(ProcessTree.ConfirmationStep);
        }
    }

    /// <summary>Takes the status of each LISTED descendant in state <c>Z</c> that this process did not start; a pid that is not ours to reap (ECHILD) is no error — the next listing decides.</summary>
    private static void Reap(IReadOnlyList<ProcessIdentity> descendants, IReadOnlySet<int> startedByUs)
    {
        foreach (var pid in ReapSelection.Of(descendants, startedByUs, IsZombie))
        {
            _ = ProcessReaping.Reap(pid);
        }
    }

    private static bool IsZombie(int pid) => ProcStat.ReadFor(pid, ProcReads.Real) is ProcStatRead.Read { Line.State: "Z" };
}

/// <summary>Which listed descendants the drain takes the status of: only an exited (<c>Z</c>) one, and NEVER a pid this process started — .NET reaps those itself, and a status taken from under it can end in the runtime's FailFast.</summary>
internal static class ReapSelection
{
    public static IReadOnlyList<int> Of(IReadOnlyList<ProcessIdentity> descendants, IReadOnlySet<int> startedByUs, Func<int, bool> isZombie) =>
        [.. descendants.Where(member => !startedByUs.Contains(member.Pid) && isZombie(member.Pid)).Select(member => member.Pid)];
}
