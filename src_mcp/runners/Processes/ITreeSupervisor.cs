using System.Diagnostics;

namespace CoaiMcp.Runners.Processes;

/// <summary>How a tree ended: the members still alive when the confirmation's bound passed — empty when the whole tree is confirmed gone.</summary>
public sealed record TreeEnd(IReadOnlyList<ProcessIdentity> Survivors);

/// <summary>
/// Who ends an ACP agent's tree, and what it writes down while it does (DL9 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>): the transport asks it to end the agent instead of killing
/// the tree itself, so a Linux Kimi turn is ended under a subreaper, by identity, with a survivor marker — and every other profile and host keeps today's end.
/// </summary>
public interface ITreeSupervisor
{
    /// <summary>The agent was just started. Never throws: a record that cannot be written is the supervisor's to hold and to say, not a reason to stop the turn.</summary>
    void Started(ProcessIdentity agent);

    /// <summary>
    /// The agent was just started but its start time cannot be read, so no identity can be made for it — and none is invented (a made-up start time disagrees with the real one on every later read, and the owner or member
    /// it names then reads as Gone). Only its pid is told, so the drain never reaps it from under .NET; the marker stays as it was (<c>spawning</c>, which is blocking while a kimi runs). Never throws.
    /// </summary>
    void StartedUnidentified(int pid);

    /// <summary>Ends the agent and everything under it, confirms it, and answers with the survivors. The agent is still alive when this is called (the caller's graceful step has run); stdin is closed here.</summary>
    Task<TreeEnd> EndAsync(Process agent);
}

/// <summary>Today's end, byte for byte: the tree killed through the agent's own handle while it lives, stdin closed, the exit awaited — and nothing listed, nothing recorded, no survivors.</summary>
/// <remarks>Windows, and every profile without a home of its own (<see cref="CoaiMcp.Runners.Acp.AcpHolding.Nothing"/>).</remarks>
public sealed class UnsupervisedTree : ITreeSupervisor
{
    private UnsupervisedTree()
    {
    }

    /// <summary>The one instance.</summary>
    public static UnsupervisedTree Instance { get; } = new();

    public void Started(ProcessIdentity agent)
    {
    }

    public void StartedUnidentified(int pid)
    {
    }

    public async Task<TreeEnd> EndAsync(Process agent)
    {
        await ProcessLauncher.EndTreeFirstAsync(agent);

        return new TreeEnd([]);
    }
}
