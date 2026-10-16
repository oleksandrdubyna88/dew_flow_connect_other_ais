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

    private static readonly string[] KimiNames = ["kimi", "kimi.exe"];

    /// <summary>
    /// The presence in the host's listing. This is the ONE place that reads <see cref="IProcessList"/>'s shape: today a listing is a list, and an EMPTY one is read as unreadable (a real
    /// machine's list always holds at least this process, so an empty one means "the host cannot say" — fail closed). When the listing becomes a closed union (DL10, A2) this method's body is
    /// the only thing that changes.
    /// </summary>
    public static KimiPresence Of(IProcessList listing)
    {
        var processes = listing.Snapshot();
        if (processes.Count == 0)
        {
            return new Unreadable("the host's process list is empty or could not be read");
        }

        var pids = processes.Where(entry => KimiNames.Contains(entry.Name, StringComparer.OrdinalIgnoreCase)).Select(entry => entry.Pid).ToList();

        return pids.Count == 0 ? new NoKimi() : new Running(pids);
    }
}
