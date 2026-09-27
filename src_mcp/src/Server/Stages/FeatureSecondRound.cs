using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// What the server does with a feature review's SECOND round once the machine admitted it (D23, S3.4):
/// which reviewers a retry asks, what a round's verdict tells the caller about the rounds, and what a
/// second round carries from the first.
/// </summary>
/// <remarks>
/// <para>The admission itself is the machine's (<see cref="RoundMachine.BeginFeatureRound"/>); nothing here
/// decides whether a round runs. This is the round's SHAPE once it does — pure over the session it is
/// handed, so every rule is a unit test, and the engine grows by a line per call rather than a paragraph.</para>
/// <para>Why a retry asks ONLY the reviewers that failed: the ones that answered were paid for and their
/// findings decided at <c>resolve</c>; asking them again buys the same findings twice and hands the caller a
/// second list to decide. So they are named as not asked, with the reason, and their decisions ride on the
/// verdict under <c>carried</c>. When every reviewer failed the retry is everyone, by construction. When none
/// of the failed reviewers is still configured, the retry is everyone too — the gate must not fail open on
/// a roster somebody edited between the rounds.</para>
/// <para>And a failed reviewer that is no longer on the roster — switched off between the rounds — is named
/// too, with why it was not retried. Until 2026-09-26 it was in neither list: the retry kept the failed
/// reviewers still enabled and named the ROSTER's rest, so a round 1 failure whose vendor had been unticked
/// dissolved without a word in the reply or the audit line (found by the epic 3 risk consultation 159f0397).
/// Switching it off is the person's decision, so the retry does not block on it; it says so.</para>
/// </remarks>
internal static class FeatureSecondRound
{
    /// <summary>Whether the round this session is about to run is its second, on a ground.</summary>
    public static bool IsSecondRound(SessionState before) =>
        before.RoundsRunThisStage == 1 && before.SecondRound != SecondRoundGround.None;

    /// <summary>The ground written on a second round's record — empty for any other round.</summary>
    public static string AdmittedBy(SessionState before) =>
        IsSecondRound(before) ? before.SecondRound.ToString() : string.Empty;

    /// <summary>
    /// The retry's roster: the reviewers that FAILED in round 1, the rest named as not asked — or the
    /// work as built, for every round that is not a retry.
    /// </summary>
    public static RoundWork OnlyTheFailed(RoundWork work, PersistedSession session)
    {
        if (!IsSecondRound(session.State) || session.State.SecondRound != SecondRoundGround.ReviewerFailure)
        {
            return work;
        }

        var failed = FailedInRoundOne(session);
        var kept = work.Reviewers.Where(w => failed.Contains(PairOf(w))).ToList();
        var gone = failed
            .Where(f => !work.Reviewers.Any(w => ProviderAndRole.Instance.Equals(PairOf(w), f)))
            .Select(f => new SkippedRole($"{f.Provider}/{f.Role}", "failed in round 1 and is no longer enabled, so it was not retried (D23)"));

        // No failed reviewer is still on the roster: everyone runs, so a roster edited between the rounds
        // cannot turn the retry into a proceed over nobody — and the failures that are gone are still named.
        return kept.Count == 0
            ? work with { NotAsked = [.. work.NotAsked, .. gone] }
            : work with { Reviewers = kept, NotAsked = [.. work.NotAsked, .. AnsweredInRoundOne(work.Reviewers.Except(kept)), .. gone] };
    }

    private static (string Provider, string Role) PairOf(ReviewerWork w) => (w.Invocation.Provider, w.Invocation.Role);

    /// <summary>The reviewers that answered in round 1, named as not asked: their findings were decided and are carried.</summary>
    private static IEnumerable<SkippedRole> AnsweredInRoundOne(IEnumerable<ReviewerWork> answered) =>
        answered.Select(w => new SkippedRole(
            $"{w.Invocation.Provider}/{w.Invocation.Role}",
            "answered in round 1; its findings were decided and are carried, not re-bought (D23)"));

    /// <summary>The (provider, role) pairs the last feature round recorded as failed.</summary>
    private static HashSet<(string Provider, string Role)> FailedInRoundOne(PersistedSession session)
    {
        var last = session.Rounds.LastOrDefault(r => r.Stage == nameof(Stage.FeatureReview) && r.Verdict != RoundRecord.Skipped);
        var pairs = last?.ReviewerStates.Where(s => s.Status == ReviewerState.Failed).Select(s => (s.Provider, s.Role)) ?? [];

        return new HashSet<(string, string)>(pairs, ProviderAndRole.Instance);
    }

    /// <summary>
    /// What a feature round's verdict tells the caller: the D23 rule in the words of THIS round's case, and
    /// round 1's decisions carried on a second round.
    /// </summary>
    /// <param name="after">The state the round completed to — its ground for a second round, if any.</param>
    /// <param name="carried">Round 1's decisions, when this was round 2; empty otherwise.</param>
    public static ReviewAnswer Apply(ReviewAnswer answer, RoundVerdict verdict, ReviewerSummary reviewers, SessionState after, IReadOnlyList<CarriedDecision> carried)
    {
        var d23 = Instruction(verdict, reviewers, after);
        var instruction = d23.Length > 0 ? d23 : answer.Instruction;
        var carriedClause = carried.Count == 0
            ? string.Empty
            : $" Round 1's {carried.Count} finding(s) ride in `carried` as they were decided; do not decide them again.";

        return answer with
        {
            Instruction = instruction + carriedClause,
            Carried = carried.Count == 0 ? null : carried,
        };
    }

    /// <summary>The D23 sentence for this verdict, or empty where the engine's own instruction already says it all (doctrine §4: never null).</summary>
    private static string Instruction(RoundVerdict verdict, ReviewerSummary reviewers, SessionState after) => verdict switch
    {
        RoundVerdict.Proceed =>
            "The feature gate passed. Record a decision for EVERY finding via resolve (rejections need reasons); the review "
            + "closes on resolve. " + OneRoundUnless,
        RoundVerdict.GoodEnough g =>
            $"{g.Gate.GatingCount} finding(s) gate and none is blocking: one round is this review's budget for them (D23). "
            + "READ them and apply the ones that are true and useful — as new pull requests, since the epics have merged — "
            + "reject the rest with reasons via resolve, and say in your summary what you took and what you declined; the "
            + "review closes on resolve. " + OneRoundUnless,
        RoundVerdict.Revise when after.SecondRound == SecondRoundGround.BlockingFinding =>
            "A blocking finding came back, so a second round runs (D23) — the last: a blocking finding or a reviewer "
            + "failure in it calls a person. Resolve every finding with accept/reject + reason, fix the accepted ones as new "
            + "pull requests, then call review_feature again with again: true over the new head.",
        RoundVerdict.Revise when after.SecondRound == SecondRoundGround.ReviewerFailure =>
            $"{reviewers.Asked - reviewers.Answered} of {reviewers.Asked} reviewer(s) failed, so a retry runs (D23) — the second "
            + "and last round: it asks only the reviewers that failed, needs no new head, and a retry that fails again calls a "
            + "person. Resolve the findings of the reviewers that answered first (their decisions are carried into the retry's "
            + "verdict, not re-bought), then call review_feature again.",
        _ => string.Empty,
    };

    private const string OneRoundUnless =
        "A second round runs only for a reviewer failure, a blocking finding, or when the person asks for one — "
        + "at most two rounds, ever (D23).";

    /// <summary>
    /// Why an ADMITTED second round with nobody left to review it is refused rather than skipped: the
    /// skip's own reason, said as the block it is.
    /// </summary>
    /// <remarks>
    /// A round 1 skip is D1's ordinary state — nobody ticked, nothing owed, the trail says so and does not
    /// block. A round 2 admitted on a ground is OWED: a reviewer failed, a blocking finding stands, or the
    /// person asked. Until 2026-09-26 unticking every feature reviewer between the rounds — or passing
    /// fewer epics than the D17 line — turned that owed round into a <c>skipped</c> row that "did not
    /// block", which is the gate bypassed by a settings edit (the gate's finding #25). Nothing is recorded;
    /// the ground stands until a reviewer runs the round or a person decides.
    /// </remarks>
    public static string NobodyForAnAdmittedRound(SessionState before, string skipReason) =>
        $"the feature review's second round was admitted ({Ground(before.SecondRound)}) and this call cannot run it: "
        + $"{skipReason}. An admitted round is owed and this BLOCKS the release — it is not a skip. Tick a vendor for "
        + "the feature review and call review_feature again, or ask the person with ask_human.";

    private static string Ground(SecondRoundGround ground) => ground switch
    {
        SecondRoundGround.ReviewerFailure => "a reviewer failed in round 1",
        SecondRoundGround.BlockingFinding => "round 1 found a blocking finding",
        SecondRoundGround.PersonAsked => "the person asked for it",
        _ => "on no ground",
    };

    /// <summary>
    /// What <c>resolve</c> keeps for the second round: round 1's decisions, when the review stays open for
    /// one — nothing otherwise.
    /// </summary>
    public static List<CarriedDecision> CarriedFrom(SessionState before, SessionState after, IEnumerable<Decision> decisions) =>
        before.Stage == Stage.FeatureReview && after.Stage == Stage.FeatureReview && after.RoundsRunThisStage == 1
            ? [.. decisions.Select(CarriedDecision.From)]
            : [];

    /// <summary>Provider and role compared as the trail spells them — case does not make a second reviewer.</summary>
    private sealed class ProviderAndRole : IEqualityComparer<(string Provider, string Role)>
    {
        public static readonly ProviderAndRole Instance = new();

        public bool Equals((string Provider, string Role) x, (string Provider, string Role) y) =>
            string.Equals(x.Provider, y.Provider, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Role, y.Role, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Provider, string Role) pair) =>
            HashCode.Combine(pair.Provider.ToUpperInvariant(), pair.Role.ToUpperInvariant());
    }
}
