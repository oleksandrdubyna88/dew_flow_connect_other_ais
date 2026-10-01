using CoaiMcp.Core.Findings;

namespace CoaiMcp.Core.Security;

/// <summary>Optional evidence preserved alongside normalized findings in the history projection.</summary>
public sealed record SecurityFindingDetails(Reproduction? Reproduction, string CapReason, IReadOnlyList<SecuritySighting> AlsoSeenBy)
{
    public AttackEvidence? AttackEvidence { get; init; }
    public static SecurityFindingDetails Of(Finding finding) => new(finding.Reproduction, finding.CapReason,
        finding.AlsoSeenBy.IsDefault ? [] : finding.AlsoSeenBy)
    { AttackEvidence = finding.AttackEvidence };
}
