using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Acp;

/// <summary>
/// What taking a profile's hold on its sign-in came to — a closed union, so "somebody else holds it" and "cannot be held at all" are
/// never one answer (E4.3, the shim's side of decision 10 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>).
/// </summary>
public abstract record AcpHolding
{
    private AcpHolding()
    {
    }

    /// <summary>Nothing to hold — the profile has no sign-in of its own to protect.</summary>
    public static AcpHolding Nothing { get; } = new Held(new NoHold(), UnsupervisedTree.Instance);

    /// <summary>Held: dispose it once the agent is over — the shim does, after <see cref="AcpClient.AskAsync"/> has returned.</summary>
    /// <remarks>
    /// <paramref name="Supervisor"/> ends the agent's tree: <see cref="UnsupervisedTree"/> (today's end) unless the profile owns the tree under a subreaper — Kimi on Linux, DL9. The one-argument
    /// form keeps every existing holder as it was.
    /// </remarks>
    public sealed record Held(IDisposable Hold, ITreeSupervisor Supervisor) : AcpHolding
    {
        public Held(IDisposable hold)
            : this(hold, UnsupervisedTree.Instance)
        {
        }
    }

    /// <summary>Another tree holds the sign-in still and did not let go within the time given: the shim's exit 69, with this sentence.</summary>
    public sealed record Busy(string Sentence) : AcpHolding;

    /// <summary>The hold cannot be taken at all (no key, a lock file that cannot be opened): the shim's exit 65, with this sentence.</summary>
    public sealed record Unusable(string Sentence) : AcpHolding;

    private sealed class NoHold : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
