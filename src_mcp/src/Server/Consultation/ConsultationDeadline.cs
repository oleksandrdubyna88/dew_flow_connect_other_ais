using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Server;

/// <summary>
/// How long one consultation TURN may run — the launch's own timeout, and a grace for everything the
/// turn does around the launch.
/// </summary>
/// <remarks>
/// <para>Derived, not typed, the way <see cref="RoundBudget"/> derives a round's. The launch is bounded
/// by the reviewer timeout; what remains of a turn — the drain the launcher allows a killed child, the
/// second snapshot of the working tree, the writes — is a fraction of that, and never less than the
/// drain itself. The launch's own deadline therefore fires FIRST, which is the outcome worth keeping: a
/// process the launcher ends on its budget has its handle read off whatever it said, and the turn stays
/// resumable. This deadline is the bound behind it, for a launch that did not end when told to and for a
/// git that hangs afterwards.</para>
/// <para>It is the runtime bound; the SWEEP's abandonment threshold is the operator's idle window
/// (<c>COAI_CONSULT_IDLE_MINUTES</c>), which this deadline sits well inside — so an <c>asking</c> record
/// idle past that window, with nobody holding its repository's lock, was left behind by a turn that did
/// not settle, whether or not the pid it names is alive (2026-09-26). Before that the sweep asked only
/// about the pid, and a record naming the server's own live pid was kept for ever.</para>
/// </remarks>
public static class ConsultationDeadline
{
    /// <summary>The least grace a turn gets after its launch: the drain the launcher grants a killed child.</summary>
    public static readonly TimeSpan MinimumGrace = ProcessRequest.DefaultDrainGrace;

    /// <summary>What a turn may spend around its launch — a quarter of the launch's own budget, at least the minimum.</summary>
    public static TimeSpan GraceFor(TimeSpan reviewerTimeout)
    {
        var quarter = RoundBudget.Expressible(reviewerTimeout) / 4;

        return quarter > MinimumGrace ? quarter : MinimumGrace;
    }

    /// <summary>The turn's whole deadline — the launch's budget plus its grace, inside what a timer can hold.</summary>
    public static TimeSpan For(TimeSpan reviewerTimeout) =>
        RoundBudget.Expressible(RoundBudget.Expressible(reviewerTimeout) + GraceFor(reviewerTimeout));
}
