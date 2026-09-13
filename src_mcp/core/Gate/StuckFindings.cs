using System.Collections.Immutable;
using CoaiMcp.Core.Findings;

namespace CoaiMcp.Core.Gate;

/// <summary>
/// What the caller decided about one finding in an earlier round of this session.
/// </summary>
/// <remarks>
/// Rejections are carried as well as acceptances, and that is not bookkeeping: accepted in round 1,
/// REJECTED in round 2, raised again in round 3 is a disagreement the caller is defending — the
/// <c>re_raised</c> signal — and counting it here would file it as a fix that did not take. The
/// LATEST word about a defect is the one that decides. (codex, story 6's plan round.)
/// </remarks>
public readonly record struct EarlierDecision(int Round, Finding Finding, bool Accepted);

/// <summary>
/// A finding the caller accepted, fixed, and was handed back — the measurable shape of being stuck.
/// </summary>
/// <remarks>
/// <para><b>What this counts and why it is not <c>re_raised</c>.</b> That one is a reviewer raising
/// something over a standing REJECTION: a disagreement the caller is defending, and the more
/// interesting of the two for reading an argument. This is the opposite and the more expensive one —
/// the caller AGREED, changed the code or the plan, and the same defect came back. Nobody is
/// disagreeing; the fix did not take.</para>
/// <para>It is phase 2's instrument. The question the operator put to this feature is whether an
/// automatic consultation should fire when a finding survives two rounds, and that question cannot
/// be answered by an impression of how often it happens. So this counts it, records it on the round
/// and says one sentence in the audit. <b>Nothing is called.</b> A trigger that fired before anybody
/// had read the number would be the same guess with a cost attached.</para>
/// <para>The matching is <see cref="FindingDedup.SameDefect"/> — this product's own rule for "the
/// same defect", already used to merge two vendors' words and to discount a repeat. A second
/// similarity rule here would be a second answer to a question the codebase has answered.</para>
/// </remarks>
public static class StuckFindings
{
    /// <summary>
    /// What survived: the findings themselves, and the rounds their acceptance came from.
    /// </summary>
    /// <remarks>
    /// The LATEST acceptance, when a defect has more than one. Accepted in round 1, rejected in 2,
    /// accepted again in 3 and handed back in 4 is a round-3 story: naming round 1 would point a
    /// person at the acceptance that was superseded rather than the one that was in force.
    /// (local, third code round.)
    /// </remarks>
    public readonly record struct Survivors(
        ImmutableArray<Finding> Findings,
        ImmutableArray<int> Rounds)
    {
        public int Count => Findings.IsDefault ? 0 : Findings.Length;

        /// <summary>
        /// The audit line, or empty when nothing survived.
        /// </summary>
        /// <remarks>
        /// Says what COULD have happened rather than what should: this is a measurement, and a
        /// sentence that instructed anybody would be phase 2 arriving without its evidence.
        /// </remarks>
        public string Sentence => Count == 0
            ? string.Empty
            : $"an automatic consultation could have fired here: {Count} accepted finding(s) from round "
              + $"{string.Join(", ", Rounds)} were raised again";
    }

    /// <summary>
    /// The findings of this round that repeat something the caller already accepted.
    /// </summary>
    /// <param name="thisRound">What this round produced, already merged.</param>
    /// <param name="earlier">
    /// What the caller decided in EARLIER rounds of the same session and stage, oldest first. Same
    /// stage, because a plan-stage remark has no file and a code-stage one usually does — comparing
    /// across them would match on category alone and count coincidences.
    /// </param>
    public static Survivors SurvivedAcceptance(
        IReadOnlyList<Finding> thisRound,
        IReadOnlyList<EarlierDecision> earlier)
    {
        if (thisRound.Count == 0 || earlier.Count == 0)
        {
            return new Survivors([], []);
        }

        var survived = new List<Finding>();
        var rounds = new SortedSet<int>();
        foreach (var finding in thisRound)
        {
            // ONE pass for the one thing the answer needs: the caller's LATEST word about this
            // defect. Materialising the matches per finding allocated a list to read two of its
            // entries. (codex + local, third code round.)
            EarlierDecision? latest = null;
            foreach (var one in earlier.Where(one => FindingDedup.SameDefect(one.Finding, finding)))
            {
                latest = one;
            }

            // That word has to be an acceptance. A defect accepted in round 1 and rejected in round 2
            // is a standing disagreement by the time round 3 raises it — `re_raised`'s signal, not
            // this one. And because it is the latest, the round it NAMES is the acceptance that was in
            // force when the defect came back: round 3 of accept-reject-accept, never the round 1
            // nobody is arguing about any more. (local, third code round.)
            if (latest is not { Accepted: true } word)
            {
                continue;
            }

            // ONE finding, however many earlier rounds accepted something like it: counting per
            // matching round would make a long session look worse than a short one for one defect.
            survived.Add(finding);
            rounds.Add(word.Round);
        }

        return new Survivors([.. survived], [.. rounds]);
    }
}
