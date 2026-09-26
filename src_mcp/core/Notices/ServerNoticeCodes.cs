namespace CoaiMcp.Core.Notices;

/// <summary>
/// Every <c>code</c> this server can write — the KEY of a notice, a literal at the call site, never prose.
/// </summary>
/// <remarks>
/// <para>The extension keys repeats on <c>(code, subject)</c> and learned the hard way what keying
/// on a rendered title costs: a title carrying a round number mints a fresh key every round and no
/// threshold is ever reached. So a code is a literal from this list, and the list is what bounds
/// the key space.</para>
/// <para><b>Every string field is redacted, identity fields included</b> — the extension tried an
/// exemption for <c>code</c> and an existing test rejected it. The concern the exemption answered
/// is answered by a GUARD instead: a test runs every code here through the redactor and asserts it
/// comes back unchanged, so a code the redactor would rewrite (one that reads like <c>token=…</c>)
/// fails on the day it is added rather than producing a row that cannot be grouped with its own
/// repeats. <see cref="All"/> exists for that test.</para>
/// </remarks>
public static class ServerNoticeCodes
{
    /// <summary>A refusal returned to the calling AI, from either <c>Error</c> helper or <c>PanelService.Refused</c>.</summary>
    public const string Refused = "refused";

    public const string ReviewerTimedOut = "reviewer-timed-out";

    public const string ReviewerRateLimited = "reviewer-rate-limited";

    /// <summary>A reviewer process that ended with a non-zero exit.</summary>
    public const string ReviewerExit = "reviewer-exit";

    public const string ReviewerNotStarted = "reviewer-not-started";

    /// <summary>A reviewer that answered, in a shape the parser could not read.</summary>
    public const string ReviewerUnparseable = "reviewer-unparseable";

    /// <summary>A setting this build cannot understand — <c>SettingsFile.Unrecognised</c>.</summary>
    public const string UnrecognisedSetting = "unrecognised-setting";

    public const string StorageNote = "storage-note";

    /// <summary>The adoption sentence <c>SettingsFile.Layer</c> hands its caller.</summary>
    public const string SettingsAdopted = "settings-adopted";

    public const string SweptRounds = "swept-rounds";

    public const string SweptConsultations = "swept-consultations";

    public const string KilledReviewers = "killed-reviewers";

    /// <summary>Written by the NEXT start, about a run whose marker was never cleared.</summary>
    public const string UncleanExit = "unclean-exit";

    /// <summary>Written by the run itself, from the catch that wraps the host.</summary>
    public const string Crash = "crash";

    /// <summary>A consultation the cadence owed and nobody could have — the round went ahead (research/PLAN_consult_on_a_cadence.md).</summary>
    public const string CadenceStoodDown = "cadence-stood-down";

    /// <summary>Every code, for the test that asserts the redactor rewrites none of them.</summary>
    public static IReadOnlyList<string> All =>
    [
        Refused,
        ReviewerTimedOut,
        ReviewerRateLimited,
        ReviewerExit,
        ReviewerNotStarted,
        ReviewerUnparseable,
        UnrecognisedSetting,
        StorageNote,
        SettingsAdopted,
        SweptRounds,
        SweptConsultations,
        KilledReviewers,
        UncleanExit,
        Crash,
        CadenceStoodDown,
    ];
}
