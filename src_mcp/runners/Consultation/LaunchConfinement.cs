using CoaiMcp.Core.QuestionConsult;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// How a consultant launch is confined: as the stuck consultant ships today, by a plan the
/// <see cref="ConfinementPlanner"/> made for a question row (PLAN_question_consultant.md, S1) — or, since epoch 3 E2 s2 t3, as a
/// coder's WRITE launch (<see cref="Coder"/>), the only case that may carry <c>acceptEdits</c>.
/// </summary>
/// <remarks>
/// <para>A closed union rather than a defaulted <c>CapabilityGrant</c>, and that is a deviation from
/// the plan's wording recorded in its S1 block: a grant whose default meant "not a capability at all —
/// the adapter's own argv" would have made <c>none</c> mean two things, and <c>none</c> is the one
/// capability a question row is most often given.</para>
/// <para><see cref="AsShipped"/> is the default on every launch, so every existing call site builds
/// today's argv byte for byte — the stuck consultant and its deny lists are NOT moved here (A12: a
/// tails-plan item, a security release of its own). <see cref="Planned"/> composes the planner's
/// fragments and nothing of the adapter's own.</para>
/// </remarks>
public abstract record LaunchConfinement
{
    /// <summary>The adapter's shipped argv: the stuck consultant in the live checkout, resumable.</summary>
    public sealed record Shipped : LaunchConfinement;

    /// <summary>A question row: the plan's fragments, a scratch or root cwd, one shot.</summary>
    public sealed record Planned(Confinement.Planned Plan) : LaunchConfinement;

    /// <summary>
    /// A coder: the one launch that may WRITE — the measured flag set R11 (<c>v2/spikes/RESULTS_coder_write_boundary.md</c>), standing in the job's worktree, which is also its only
    /// added directory, resumable with a handle bound to the job and the worktree (epoch 3 E2 s2 t3).
    /// </summary>
    /// <remarks>
    /// A third case, not a flag on <see cref="Shipped"/> or <see cref="Planned"/>: <c>acceptEdits</c> and <c>--permission-prompts none</c> are composed from this case only, so
    /// no consult or question launch can carry them. <see cref="JobId"/> and <see cref="Worktree"/> are checked by <see cref="CoderLaunches.WhyNot"/>, never trusted.
    /// </remarks>
    public sealed record Coder(string JobId, string Worktree) : LaunchConfinement
    {
        /// <summary>The handle of the turn this one continues; <see cref="CoderHandle.None"/> for the first turn of a job.</summary>
        public CoderHandle Resume { get; init; } = CoderHandle.None;
    }

    public static LaunchConfinement AsShipped { get; } = new Shipped();

    private LaunchConfinement() { }
}
