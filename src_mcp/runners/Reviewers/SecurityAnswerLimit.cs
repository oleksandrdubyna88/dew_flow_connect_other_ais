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
        var reason = Refusal(ok.Review);
        return reason.Length == 0 ? outcome : new ReviewerOutcome.Unparseable(reason, ok.LastTurnUsage)
        { EarlierTurns = ok.EarlierTurns };
    }

    /// <summary>The first rule the answer breaks, in this order — size, then prose, status, severity and evidence — or empty.</summary>
    private static string Refusal(Core.Findings.NormalisedReview review)
    {
        if (review.Findings.Length > MaxFindings) return $"security response exceeds {MaxFindings} findings";
        if (review.Findings.Sum(EvidenceCharacters) > MaxReproductionCharacters)
            return $"security evidence exceeds {MaxReproductionCharacters} characters per response";
        return ProtocolRefusal(review);
    }

    private static long EvidenceCharacters(Core.Findings.Finding finding) =>
        Size(finding.Reproduction) + (finding.AttackEvidence?.Characters ?? 0);

    private static string ProtocolRefusal(Core.Findings.NormalisedReview review)
    {
        if (review.Notes.Length > 0 || review.Rejected.Length > 0)
            return "security answers must contain only the declared status and valid findings, without prose notes";
        var status = StatusRefusal(review);
        return status.Length > 0 ? status : FindingRefusal(review);
    }

    private static string StatusRefusal(Core.Findings.NormalisedReview review)
    {
        var expected = review.Findings.Length == 0 ? "SECURE" : "FINDINGS";
        return review.SecurityStatus == expected ? string.Empty : $"security status must be {expected} for this findings list";
    }

    private static string FindingRefusal(Core.Findings.NormalisedReview review)
    {
        if (review.Findings.Any(f => f.Severity == Core.Findings.Severity.Nit))
            return "security findings must use blocking (CRITICAL), major (HIGH) or minor (MEDIUM); Low/Info are not accepted";
        return review.Findings.Any(f => f.AttackEvidence is not { Complete: true })
            ? "every security finding needs nonempty trigger, mechanism and consequence, at most 8000 characters total"
            : string.Empty;
    }

    private static long Size(Reproduction? reproduction) => reproduction is null ? 0
        : (long)reproduction.Preconditions.Length + reproduction.Steps.Length + reproduction.Expected.Length + reproduction.Actual.Length;
}
