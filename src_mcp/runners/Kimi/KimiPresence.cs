namespace CoaiMcp.Runners.Kimi;

/// <summary>Whether a <c>kimi</c> runs on this machine, as the survivor rules need it — a closed union, so "nobody" is only ever said over a listing that could be READ.</summary>
public abstract record KimiPresence
{
    private KimiPresence()
    {
    }

    /// <summary>The listing was read and holds no <c>kimi</c> at all.</summary>
    public sealed record NoKimi : KimiPresence;

    /// <summary>These pids are <c>kimi</c> processes.</summary>
    public sealed record Running(IReadOnlyList<int> Pids) : KimiPresence;

    /// <summary>The listing could not be read: not evidence that no Kimi runs.</summary>
    public sealed record Unreadable(string Reason) : KimiPresence;

    /// <summary>
    /// The presence in the host’s listing. This is the ONE place that reads <see cref="IProcessList"/>’s shape: an <see cref="ProcessListing.Unreadable"/> listing is "cannot tell", and so is a
    /// <see cref="ProcessListing.Listed"/> one with nothing in it (a real machine’s list always holds at least this process) — fail closed, never "nobody".
    /// </summary>
    public static KimiPresence Of(IProcessList listing) => listing.Snapshot() switch
    {
        ProcessListing.Listed { Processes.Count: > 0 } listed => Judged(listed.Processes),
        ProcessListing.Listed => new Unreadable("the host’s process list is empty"),
        ProcessListing.Unreadable unreadable => new Unreadable(unreadable.Reason),
        _ => new Unreadable("a listing this check has no word for"),
    };

    private static KimiPresence Judged(IReadOnlyList<ProcessEntry> processes)
    {
        var pids = processes.Where(ForeignKimiWatch.IsKimi).Select(entry => entry.Pid).ToList();

        return pids.Count == 0 ? new NoKimi() : new Running(pids);
    }
}
