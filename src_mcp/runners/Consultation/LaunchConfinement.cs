using CoaiMcp.Core.QuestionConsult;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// How a consultant launch is confined: as the stuck consultant ships today, or by a plan the
/// <see cref="ConfinementPlanner"/> made for a question row (PLAN_question_consultant.md, S1).
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

    public static LaunchConfinement AsShipped { get; } = new Shipped();

    private LaunchConfinement() { }
}
