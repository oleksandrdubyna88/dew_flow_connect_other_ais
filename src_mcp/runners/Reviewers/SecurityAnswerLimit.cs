using CoaiMcp.Core.Security;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>Refuse over-budget evidence before it can be counted, merged or persisted as an answer.</summary>
internal static class SecurityAnswerLimit
{
    internal const int MaxFindings = 100;
    internal const int MaxReproductionCharacters = 131072;

    internal static ReviewerOutcome Apply(ReviewerWork work, ReviewerOutcome outcome)
    {
        if (!work.IsSecurity || outcome is not ReviewerOutcome.Ok ok) return outcome;
        var reason = ok.Review.Findings.Length > MaxFindings ? $"security response exceeds {MaxFindings} findings"
            : ok.Review.Findings.Sum(f => Size(f.Reproduction) + (f.AttackEvidence?.Characters ?? 0)) > MaxReproductionCharacters
                ? $"security evidence exceeds {MaxReproductionCharacters} characters per response" : ProtocolRefusal(ok.Review);
        return reason.Length == 0 ? outcome : new ReviewerOutcome.Unparseable(reason, ok.LastTurnUsage)
        { EarlierTurns = ok.EarlierTurns };
    }

    private static string ProtocolRefusal(Core.Findings.NormalisedReview review)
    {
        if (review.Notes.Length > 0 || review.Rejected.Length > 0)
            return "security answers must contain only the declared status and valid findings, without prose notes";
        var expected = review.Findings.Length == 0 ? "SECURE" : "FINDINGS";
        if (review.SecurityStatus != expected) return $"security status must be {expected} for this findings list";
        return review.Findings.Any(f => f.AttackEvidence is not { Complete: true })
            ? "every security finding needs nonempty trigger, mechanism and consequence, at most 8000 characters total"
            : string.Empty;
    }

    private static long Size(Reproduction? reproduction) => reproduction is null ? 0
        : (long)reproduction.Preconditions.Length + reproduction.Steps.Length + reproduction.Expected.Length + reproduction.Actual.Length;
}
