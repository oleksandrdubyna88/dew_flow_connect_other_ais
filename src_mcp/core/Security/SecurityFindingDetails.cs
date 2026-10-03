using CoaiMcp.Core.Findings;

namespace CoaiMcp.Core.Security;

/// <summary>Optional evidence preserved alongside normalized findings in the history projection.</summary>
public sealed record SecurityFindingDetails(Reproduction? Reproduction, string CapReason, IReadOnlyList<SecuritySighting> AlsoSeenBy)
{
    public AttackEvidence? AttackEvidence { get; init; }

    /// <summary>
    /// Why stored evidence could not be read, or empty. A damaged projection must not read as a finding
    /// that never carried evidence — the round is still read, and this says what was lost.
    /// </summary>
    /// <remarks>Normalised in the accessor: the source generator does not run an initializer for a member
    /// the stored JSON omits, and every projection written before this member omits it.</remarks>
    public string Unreadable
    {
        get => field ?? string.Empty;
        init => field = value ?? string.Empty;
    }

    public static SecurityFindingDetails Of(Finding finding) => new(finding.Reproduction, finding.CapReason,
        finding.AlsoSeenBy.IsDefault ? [] : finding.AlsoSeenBy)
    { AttackEvidence = finding.AttackEvidence };
}
