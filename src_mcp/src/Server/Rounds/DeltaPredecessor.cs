using CoaiMcp.Core.Context;

namespace CoaiMcp.Server;

/// <summary>The round a delta is measured from — its sha — or the ScopeState word saying why there is none.</summary>
internal sealed record Predecessor(string Sha, string State)
{
    public bool IsChosen => State.Length == 0;

    public static Predecessor Of(string sha) => new(sha, string.Empty);

    public static readonly Predecessor FirstRound = new(string.Empty, ScopeState.FirstRound);

    public static readonly Predecessor NoSha = new(string.Empty, ScopeState.NoPredecessorSha);

    /// <summary>A predecessor WAS chosen, but its base cannot be shown to be this round's: the sha is kept (C1's From rule).</summary>
    public static Predecessor Refused(string sha, string state) => new(sha, state);
}

/// <summary>
/// Which earlier round a code round's delta is measured from (E2.S1 §S1.5): the latest done, non-skipped code round of
/// the gate session that carries a sha — and only if it was measured against THIS round's base.
/// </summary>
internal static class DeltaPredecessor
{
    /// <summary>The string a code round's record carries in <c>Stage</c> — <c>Stage.CodeReview.ToString()</c>.</summary>
    private const string CodeStage = nameof(Core.Rounds.Stage.CodeReview);

    /// <param name="earlier">The gate session's trail BEFORE this round, as the work receives it; never re-loaded.</param>
    /// <param name="against">THIS round's resolved base — <c>CollectedDiff.ComparedAgainst</c>, the merge-base.</param>
    public static Predecessor Choose(IReadOnlyList<RoundRecord> earlier, string against)
    {
        var candidates = earlier.Where(IsCandidate).ToList();

        return candidates.Count == 0 ? Predecessor.FirstRound
            : candidates.LastOrDefault(r => r.Sha is { Length: > 0 }) is { } chosen ? SameBase(chosen, against)
            : Predecessor.NoSha;
    }

    /// <summary>
    /// A running or interrupted round never; a plan, document or feature round never; a SKIPPED round never, though it is
    /// written done WITH a sha — it reviewed nothing. A round whose every reviewer failed qualifies: the diff was
    /// collected and sent. Every comparison is ordinal, as the stage is parsed.
    /// </summary>
    private static bool IsCandidate(RoundRecord r) =>
        r.Stage == CodeStage && r.Status == RoundRecord.Done && r.Verdict != RoundRecord.Skipped;

    /// <summary>
    /// The chosen round is held to THIS round's base: a merge of the base between the two rounds, a rebase, or another
    /// base named would put the base's own lines into a head-to-head range and count them as fixer work.
    /// </summary>
    private static Predecessor SameBase(RoundRecord chosen, string against)
    {
        var measured = BaseOf(chosen);

        return measured.Length == 0 ? Predecessor.Refused(chosen.Sha, ScopeState.BaseUnknown)
            : string.Equals(measured, against, StringComparison.OrdinalIgnoreCase) ? Predecessor.Of(chosen.Sha)
            : Predecessor.Refused(chosen.Sha, ScopeState.BaseMoved);
    }

    private static string BaseOf(RoundRecord round) => round.Size?.Whole?.From ?? string.Empty;
}
