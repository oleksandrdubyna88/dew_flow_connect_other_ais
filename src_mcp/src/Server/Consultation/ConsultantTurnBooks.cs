using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// The bookkeeping of a finished consultation turn, ONE copy for a consultation and a consultant check: whether it
/// answered, where a failed launch's transcript is kept, and the ledger row it writes.
/// </summary>
/// <remarks>
/// Epic 4's code round: <see cref="ConsultantCheckTurn"/> had grown its own copy of each of the three, beside
/// <see cref="ConsultationService"/>'s — the shape the reuse rule names as the defect, because the next change to one
/// (a redaction, a retention, a new ledger column) would reach only one of them. Both call these now.
/// </remarks>
internal static class ConsultantTurnBooks
{
    /// <summary>The turn ANSWERED: its last launch ended cleanly with something in it.</summary>
    public static bool Answered(ReviewerLaunch final) =>
        final.Terminal is null && !string.IsNullOrWhiteSpace(final.Answer);

    /// <summary>
    /// The failure, with where its launch's transcript was kept — redacted, bounded and atomic through
    /// <see cref="EvidenceFile"/>, in the consultations' own evidence directory and under its retention — or unchanged
    /// when there was nothing to keep.
    /// </summary>
    /// <param name="fileStem">The kept file's name before its time stamp — the vendor for a consultation, <c>check-</c> and the vendor for a check.</param>
    /// <param name="failed">Told when the transcript could not be kept; the failure then carries no evidence path.</param>
    public static ConsultFailure Kept(string dataDir, string fileStem, ReviewerLaunch final, ConsultFailure failure, Action<Exception> failed) =>
        ConsultFailures.EvidenceOf(final) is { Length: > 0 } evidence
            ? failure with
            {
                Evidence = EvidenceFile.Keep(
                    ConsultationRetention.EvidenceDirectory(dataDir),
                    $"{fileStem}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.txt",
                    Core.Notices.Redaction.SafeSource(evidence),
                    failed),
            }
            : failure;

    /// <summary>The turn's ledger row — kind <c>consult</c>, stage <c>Consultation</c>, under <paramref name="role"/>.</summary>
    /// <remarks>
    /// The WHOLE usage reaches the line — a usage the vendor never reported as <c>usage not captured</c>, not a free 0/0
    /// (the whole-branch review, C) — for a consultation (<c>consult</c>) and a check (<c>consult-check</c>) alike.
    /// </remarks>
    public static void Billed(UsageLedger ledger, ProviderSettings row, string model, string role, string outcome, TimeSpan elapsed, Usage usage) =>
        ledger.RecordJob(string.Empty, row.Provider, model, role, outcome, elapsed, usage, UsageKinds.Consult, stage: "Consultation");
}
