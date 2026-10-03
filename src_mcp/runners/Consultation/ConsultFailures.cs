using CoaiMcp.Core.Notices;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// The one reading of a finished consultation launch that produced no advice: which
/// <see cref="ConsultFailure"/> it was.
/// </summary>
/// <remarks>
/// <para>Built from what already reads a vendor's words, never a second copy of them
/// (PLAN_the_consultant_works_on_every_vendor.md, E2.1): the launch's own <see cref="ReviewerOutcome"/>,
/// <see cref="RateLimit.Hopeless"/> for a spent allowance against a throttle, <see cref="VendorDiagnosis"/>
/// for a sentence with a known cure, <c>ReviewerExecutor.Complaint</c> for the stderr line an empty answer
/// left, <see cref="VendorDiagnosis.InstallCure"/> for a CLI that is not there, and the adapter's own
/// <see cref="IConsultantRuntime.DeniedActions"/> for what headless mode refused.</para>
/// <para>Pure: it reads the launch and nothing else, so every row of the table is a unit test.</para>
/// </remarks>
public static class ConsultFailures
{
    /// <summary>
    /// What <paramref name="final"/> — the launch the turn stands on, which answered nothing or ended on
    /// its own failure — says went wrong.
    /// </summary>
    /// <param name="runtime">The runtime's NAME (<c>antigravity</c>, <c>codex</c>…), for the install cure.</param>
    /// <param name="killedAs">
    /// What a KILLED launch was, decided by the caller from the tokens' state: the caller's own token cancelled
    /// → <c>cancelled</c>; the turn's deadline token → <c>deadline</c>; neither → the launch's own timeout. Never
    /// read off the launch: the launcher sets <c>ProcessResult.Cancelled</c> for whatever token it was handed,
    /// and a consultation hands it the LINKED turn token, so a deadline kill reported itself as the caller's
    /// cancellation (epic 2's review, the gate's finding 0).
    /// </param>
    /// <param name="linux">The platform the install cure is for; the tests pass it, the server asks the OS.</param>
    /// <remarks>
    /// Everything taken from what the vendor printed goes through <see cref="Redaction.SafeText"/> before it
    /// becomes part of a failure: the failure is written to the record, the database, the health file and the
    /// caller's answer, and a vendor's stderr can carry a credential as easily as an error.
    /// </remarks>
    public static ConsultFailure Classify(IConsultantRuntime consultant, ReviewerLaunch final, string runtime, ConsultFailure killedAs, bool? linux = null) =>
        final.Terminal switch
        {
            null => Silent(consultant, final),
            _ when final.Process is { } process && consultant.DroppedTheConversation(process) => new ConsultFailure.ConversationDropped(),
            var terminal => Ended(terminal, killedAs, runtime, linux),
        };

    /// <summary>
    /// The evidence worth keeping for a failed launch: its transcript — what it MEANT to say being empty —
    /// or, for a launch that ended on its own failure, both of its streams, labelled.
    /// </summary>
    public static string EvidenceOf(ReviewerLaunch final) =>
        final.Evidence.Length > 0 || final.Process is null
            ? final.Evidence
            : $"--- stdout ---\n{final.Process.StdOut}\n--- stderr ---\n{final.Process.StdErr}";

    /// <summary>A vendor's words, fit to be written down: one line, redacted, bounded.</summary>
    public static string Safe(string said) => Redaction.SafeText(FirstLine(said), SaidCap);

    /// <summary>A clean exit with nothing in it: what the vendor's CLI recorded refusing, or the CLI's own last word.</summary>
    /// <remarks>
    /// <para>The refusal is read by the ADAPTER, in its own vocabulary (<see cref="IConsultantRuntime.SilentFailure"/>) —
    /// agy's permission words used to be read here for every vendor (the whole-branch review, F).</para>
    /// <para>The last word is read from the transcript when the launch kept one, and from the process's own stderr
    /// when it did not (<see cref="EvidenceOf"/>) — an empty answer whose evidence was empty read as having said
    /// nothing, while its stderr said why (epic 2's review, the gate's finding 8).</para>
    /// </remarks>
    private static ConsultFailure Silent(IConsultantRuntime consultant, ReviewerLaunch final) =>
        consultant.SilentFailure(final, new ConsultFailure.Empty(Safe(ReviewerExecutor.Complaint(EvidenceOf(final)))));

    /// <summary>A launch that ended on a failure of its own — the outcome the executor already named.</summary>
    private static ConsultFailure Ended(ReviewerOutcome terminal, ConsultFailure killedAs, string runtime, bool? linux) => terminal switch
    {
        ReviewerOutcome.NotStarted n => new ConsultFailure.CliNotFound(Safe(n.Reason), InstallCureFor(runtime, linux)),
        ReviewerOutcome.TimedOut => killedAs,
        ReviewerOutcome.RateLimited r => RateLimit.Hopeless(r.Reason) ? new ConsultFailure.Quota(Safe(r.Reason)) : new ConsultFailure.RateLimited(Safe(r.Reason)),
        ReviewerOutcome.NonZeroExit e => Exited(e, runtime, linux),
        // An ending that is no exit at all — an answer that would not parse, a launch stood down. None can
        // come out of a consultation's own launch today; if one ever does, it is an answer that did not
        // arrive, said as what it is, and never an invented "exit 0" that reads as success (epic 2's review).
        ReviewerOutcome.Ok or ReviewerOutcome.Unparseable or ReviewerOutcome.StoodDown => new ConsultFailure.Empty(Safe(ReviewerSummaryFactory.Describe(terminal))),
        _ => new ConsultFailure.Empty("the launch ended in a way this product does not name"),
    };

    /// <summary>A non-zero exit: a sentence with a known cure, or the code and what the CLI said.</summary>
    private static ConsultFailure Exited(ReviewerOutcome.NonZeroExit exit, string runtime, bool? linux)
    {
        var said = exit.StdErrTail.Length > 0 ? exit.StdErrTail : exit.FailureReason;

        return VendorDiagnosis.Classify(said) switch
        {
            null => new ConsultFailure.Exit(exit.ExitCode, Safe(said)),
            { Kind: DiagnosisKind.NotOnPath } d => new ConsultFailure.CliNotFound(Safe(said), VendorDiagnosis.InstallCure(runtime, linux) ?? Safe(d.Cure)),
            // The cure is redacted too: the unreachable-engine row carries the endpoint out of the vendor's text.
            var d => new ConsultFailure.VendorRefused(Safe(said), Redaction.SafeText(d.Cure, Redaction.TitleLimit)),
        };
    }

    /// <summary>How to put the CLI on this machine — the vendor's own command, or where to point the row.</summary>
    /// <remarks>Public since a claude whose <c>--help</c> could not even be started is refused with it before any launch (the whole-branch review, L).</remarks>
    public static string InstallCureFor(string runtime, bool? linux = null) =>
        VendorDiagnosis.InstallCure(runtime, linux)
        ?? "set the consultant's executable path in ConnectOtherAIs > Consultant";

    /// <summary>One line of what the CLI said: a cure is read in a sentence, not a stack trace.</summary>
    private static string FirstLine(string said) =>
        said.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

    private const int SaidCap = 240;
}
