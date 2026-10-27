using System.Diagnostics;
using System.Runtime.Versioning;

namespace CoaiMcp.Runners.Processes;

/// <summary>
/// The Linux end of a tree the shim owns as a CHILD SUBREAPER (DL9 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>): list while the agent lives, write down what was listed BEFORE any
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
    /// <param name="startedByUs">Every pid this process started through <c>Process.Start</c> (today only the agent). They are still KILLED like every other descendant, while alive — only their STATUS is never taken by the drain (a zombie among them is left for .NET to reap), so a child that exited by itself keeps its own exit code and one that was alive at the first listing ends with 137.</param>
    /// <param name="calls">The pidfd road of this host (<see cref="PidfdLayer"/>).</param>
    /// <param name="onListed">Told the members the first listing found, before the first signal. It must not throw: the kill is never skipped.</param>
    public static async Task<TreeEnd> EndAsync(
        Process agent, int shimPid, IReadOnlySet<int> startedByUs, IPidfdCalls calls, Action<IReadOnlyList<ProcessIdentity>> onListed)
    {
        var members = Listed(shimPid, AgentIdentity(agent));
        onListed(members);
        _ = Ended(members, calls);

        var survivors = await TeardownRounds.ConfirmedAsync(members, Rounds(shimPid, startedByUs, calls));

        ProcessLauncher.Close(agent.StandardInput);
        await agent.WaitForExitAsync(CancellationToken.None);

        return new TreeEnd(survivors);
    }

    /// <summary>This host's confirmation: the process table, the zombie drain, the three-state liveness, the pidfd kill, a step of <see cref="ProcessTree.ConfirmationStep"/> and the bound of <see cref="ProcessTree.ConfirmationBound"/>.</summary>
    private static RoundSeams Rounds(int shimPid, IReadOnlySet<int> startedByUs, IPidfdCalls calls)
    {
        var deadline = DateTime.UtcNow + ProcessTree.ConfirmationBound;

        return new RoundSeams(
            () => ProcessTree.Descendants(shimPid),
            descendants => Reap(descendants, startedByUs),
            LivenessCheck.Of,
            members => Ended(members, calls),
            () => Task.Delay(ProcessTree.ConfirmationStep),
            () => DateTime.UtcNow >= deadline);
    }

    /// <summary>Ends <paramref name="members"/> by identity through the pidfd road, each pinned with the EXACT start ticks of its own <c>/proc</c> line (read now, one comparison on the pinned process) rather than the one-second slack of a start time.</summary>
    private static IReadOnlyList<Signalled> Ended(IReadOnlyList<ProcessIdentity> members, IPidfdCalls calls) =>
        TreeSignals.Kill([.. members.Select(member => PinnedMember.Of(member, ProcReads.Real))], calls);

    /// <summary>
    /// The agent's identity from a REAL read of its start time — or none. An agent whose start time cannot be read is never given a made-up one: it is not a listed member, its exit is awaited by the caller (no bound),
    /// and the marker names it only if the descendants listing can identify it.
    /// </summary>
    private static IReadOnlyList<ProcessIdentity> AgentIdentity(Process agent) =>
        ProcessTracking.StartedAt(agent.Id) is { } started ? [new ProcessIdentity(agent.Id, started)] : [];

    /// <summary>The shim's live descendants plus the agent when it is identified, kept unless provably gone: an exited entry is Gone by the one rule (<see cref="LivenessCheck"/>), an entry that cannot be checked stays.</summary>
    private static IReadOnlyList<ProcessIdentity> Listed(int shimPid, IReadOnlyList<ProcessIdentity> agent) =>
        TeardownRounds.Kept([.. ProcessTree.Descendants(shimPid), .. agent], LivenessCheck.Of);

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
