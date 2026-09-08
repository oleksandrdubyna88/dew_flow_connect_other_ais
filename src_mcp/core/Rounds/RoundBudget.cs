namespace CoaiMcp.Core.Rounds;

/// <summary>
/// How long a whole round may take, derived from what a round actually does.
/// </summary>
/// <remarks>
/// <para>A reviewer is bounded by <c>reviewerTimeoutMinutes</c>. A ROUND is not, and the round is
/// what a person watches — so a round can legitimately run for a long time while every reviewer
/// inside it behaves. The operator asked for a bound on it after a round passed ten minutes.</para>
/// <para><b>The default is computed, not typed, and that is the whole point.</b> A bound below what
/// a healthy round takes would cancel reviewers mid-answer, lose their findings and produce a
/// verdict nobody could explain — a defect that would read exactly like a bug in the gate. A round
/// runs <c>vendors × roles</c> reviewers through one machine-wide cap, so it takes as many WAVES as
/// that division needs, and each wave can legitimately last a whole reviewer timeout. At the shipped
/// defaults — three vendors, four code roles, a cap of three, ten minutes each — that is four waves:
/// forty minutes of entirely healthy work.</para>
/// <para>What this does NOT model: a Team server's queue, where a job waits behind other people's
/// work. That waiting is real wall-clock to the person watching, and it is not this machine's to
/// predict — which is why the number this produces is a DEFAULT somebody can raise rather than a
/// limit the product enforces on their behalf.</para>
/// </remarks>
public static class RoundBudget
{
    /// <summary>The deadline a round of this shape earns, never less than one reviewer's own.</summary>
    /// <param name="reviewerTimeout">What ONE reviewer is allowed, launches and repairs included.</param>
    /// <param name="reviewers">How many will run — vendors times the roles this stage schedules.</param>
    /// <param name="concurrency">The machine-wide cap they queue on.</param>
    public static TimeSpan For(TimeSpan reviewerTimeout, int reviewers, int concurrency)
    {
        // A configured zero is a configuration mistake, not a way to make a round instantaneous or
        // infinite — the same reading `BoundedScheduler` gives its own caps.
        var waves = Math.Max(1, (int)Math.Ceiling(Math.Max(1, reviewers) / (double)Math.Max(1, concurrency)));
        var derived = reviewerTimeout * waves;

        // Capped, because a derivation with no ceiling is a way to build a round nobody is waiting
        // for any more: an hour per reviewer at a concurrency of one and twenty reviewers is twenty
        // hours, and the person who set those numbers did not ask for a day-long round. A reviewer
        // on the plan round called it a zombie, which is the right word — it holds a worktree and a
        // session while nobody watches. The ceiling is generous on purpose: it bounds the absurd
        // without arguing with the merely patient.
        var capped = derived > Ceiling ? Ceiling : derived;

        // The FLOOR wins where the two bounds meet. A reviewer allowed longer than the ceiling is a
        // deliberate setting, and capping under it would cancel that reviewer before its first
        // attempt — the one outcome neither bound exists for. Found by a test whose assertion was
        // weaker than its own docstring: it asserted "at least the ceiling" and passed while the
        // floor lost.
        return capped < reviewerTimeout ? reviewerTimeout : capped;
    }

    /// <summary>The longest a DERIVED budget may reach. An explicit setting is the operator's own.</summary>
    public static readonly TimeSpan Ceiling = TimeSpan.FromHours(8);
}
