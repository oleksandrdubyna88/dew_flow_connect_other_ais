using System.Collections.Immutable;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// What the reviewers that LANDED before a withdrawal found, and whether any of it gates — the rule that keeps a cancel from discarding an
/// adverse finding (G4 of todo/PLAN_a_cancelled_review_stops_its_round.md, revised 2026-10-22; §12 of its S2 spec).
/// </summary>
/// <remarks>
/// <para>A withdrawal never judges a partial panel, but it never throws away a landed finding either: otherwise the agent a gate is meant to
/// stop could cancel past an adverse reviewer and run the round again. EVERY landed finding is kept; when ANY of them gates, the withdrawal
/// holds the gate for a person.</para>
/// <para>"Gates" is the completed path's own count, not a second definition: the findings are merged as the completed path merges them
/// (<see cref="SecurityRound.Merge"/>) and counted by <see cref="GateRule.Evaluate(ImmutableArray{Finding}, ImmutableArray{PriorRejection}, Func{string, int})"/>,
/// so a blocking or major finding counts and a standing rejection (rejected before with a reason, raised again with the same argument) does not.
/// The per-role THRESHOLD is not consulted: it decides whether a panel passes, and a withdrawal judges no panel — one counted gating finding is
/// enough to ask a person.</para>
/// </remarks>
/// <param name="Findings">Every landed finding, merged as the completed path merges them — gating or not.</param>
/// <param name="Gating">How many of them count toward the gate; zero means the withdrawal stays free.</param>
/// <param name="GatingProviders">The vendors whose answer carried a counted gating finding, in the order they appear.</param>
public sealed record KeptFindings(ImmutableArray<Finding> Findings, int Gating, IReadOnlyList<string> GatingProviders)
{
    /// <summary>Nothing landed.</summary>
    public static KeptFindings None { get; } = new([], 0, []);

    /// <summary>
    /// The words every held withdrawal's note ends with — by a landed finding or by the count of withdrawals in a row. The extension tells a held
    /// cancelled round from a free one by them (and by the record's gating count), so the sentence is spelled once, here.
    /// </summary>
    public const string HeldForAPerson = "held for a person";

    /// <summary>
    /// Which of <see cref="Findings"/> (by position) count toward the gate — the same decision as <see cref="Gating"/>, standing rejections
    /// excluded, kept per finding so the record can tell a reader which ones gate without it re-deriving the rule from severity.
    /// </summary>
    public IReadOnlyList<int> GatingOrdinals { get; init; } = [];

    /// <summary>Whether the withdrawal must hold the gate for a person.</summary>
    public bool HoldsTheGate => Gating > 0;

    public static KeptFindings From(
        IReadOnlyList<(ReviewerInvocation Invocation, ReviewerOutcome Outcome)> results,
        IReadOnlyList<ReviewerWork> work,
        ImmutableArray<PriorRejection> rejections)
    {
        var landed = results.Select(r => (r.Invocation, Outcome: Landed(r.Outcome))).ToArray();
        var merged = SecurityRound.Merge(landed, work);

        var counted = Counted(merged, rejections);

        return new(
            merged,
            counted.GatingCount,
            [.. counted.Gating.SelectMany(f => f.Providers).Distinct()])
        {
            GatingOrdinals = [.. Enumerable.Range(0, merged.Length).Where(i => counted.Gating.Any(g => ReferenceEquals(g, merged[i])))],
        };
    }

    /// <summary>
    /// What a reviewer LANDED: its answer, or — when the round's token cut a later turn of its conversation — the last turn that answered
    /// (<see cref="ReviewerOutcome.LastAnswered"/>). A later turn that failed on its own terms is not cut and lands nothing, as on the completed path.
    /// </summary>
    private static ReviewerOutcome Landed(ReviewerOutcome outcome) =>
        outcome is { EndedByRoundToken: true, LastAnswered: { } answered } ? answered : outcome;

    /// <summary>The sentence the cancelled record carries: the free withdrawal's own words, or why a person was asked.</summary>
    public string NoteFor(string cause) =>
        (HoldsTheGate, cause == EndCauses.HostStopped) switch
        {
            (true, true) => $"the host stopped with a gating finding from {string.Join(", ", GatingProviders)} — {HeldForAPerson}",
            (true, false) => $"withdrawn with a gating finding from {string.Join(", ", GatingProviders)} — {HeldForAPerson}",
            (false, true) => "the host stopped while the round ran",
            (false, false) => "the caller withdrew the call while the round ran",
        };

    // Any threshold would do: the count does not depend on it, and a withdrawal has no panel to pass.
    private static GateResult Counted(ImmutableArray<Finding> merged, ImmutableArray<PriorRejection> rejections) =>
        GateRule.Evaluate(merged, rejections, _ => int.MaxValue);
}
