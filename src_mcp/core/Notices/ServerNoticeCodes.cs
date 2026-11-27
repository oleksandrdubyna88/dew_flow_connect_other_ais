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

    /// <summary>
    /// A reviewer the CALLER's token killed — the round ended under it, or the person stopped the round — never one the
    /// vendor was too slow for (<see cref="ReviewerTimedOut"/>). Its own code since story 3.2 of
    /// research/PLAN_every_round_is_counted.md: until then the executor read a cancellation as a timeout, and the notice sent a
    /// person to look at a vendor that was never slow. Still written down, because the launch may well have been billed.
    /// </summary>
    public const string ReviewerCancelled = "reviewer-cancelled";
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

    /// <summary>
    /// The v2 migration released a held gate whose card a person had ANSWERED: v1 never applied the answer and v2 never waits, so the session was converted to
    /// good_enough. Subject is the CARD id — written once per card (E1.S4, <c>v2/epoch-1/e1-gate-simplify/stories/s4-migrate-v2/SPEC.md</c> §5.4).
    /// </summary>
    public const string V2ConvertedAnsweredHold = "v2-converted-answered-hold";

    /// <summary>
    /// A role job whose runner still holds its lease but has shown no heartbeat for two minutes — stopped or hung, not dead (epoch 3 E1 s1, spec A). Subject is the job id; one per stall.
    /// </summary>
    public const string RoleJobStalled = "role-job-stalled";

    /// <summary>A role job whose lease nobody holds any more and that never reached a terminal state: closed as <c>lost</c>, its recorded child stopped (epoch 3 E1 s1). Subject is the job id; one per job.</summary>
    public const string RoleJobLost = "role-job-lost";

    /// <summary>
    /// A role job moved on to its next model after a stop (limit, refusal or crash) — one per switch, subject <c>&lt;jobId&gt;/&lt;n&gt;</c> (epoch 3 E1 s3, spec A). It names the vendor the work went to, because the next model may belong to
    /// another vendor and the repository text (also of a security task) goes with it (spec D, risk 5).
    /// </summary>
    public const string RoleModelSwitched = "role-model-switched";

    /// <summary>A role job ended <c>failed</c> — every model of its route stopped, or a stop that allows no failover (<c>tree-changed</c>, <c>output-invalid</c>), or its own timeout. Subject is the job id; one per job (epoch 3 E1 s3).</summary>
    public const string RoleJobFailed = "role-job-failed";

    /// <summary>
    /// A claude key of the pool was refused by the vendor and is now <c>dead</c> (epoch 3 E2 s7, KEY_POOL_SPEC.md section 5). Subject is the key id; ONE per key. It names the label and the id — never the key.
    /// </summary>
    public const string ClaudeKeyDead = "claude-key-dead";

    /// <summary>The exact text of a pool key was found in what its own launch printed or in the coder's changed files: the key is <c>dead (exposed)</c> and must be revoked at the vendor. Subject is the key id; ONE per key.</summary>
    public const string ClaudeKeyExposed = "claude-key-exposed";

    /// <summary>Every claude key of the pool is disabled, dead or cooling: a pick found none. Subject is <c>pool</c>; at most one in 30 minutes.</summary>
    public const string ClaudeKeysExhausted = "claude-keys-exhausted";

    /// <summary>
    /// The claude key pool of this device holds no key at all, and a role job that needs one was refused or its claude models were skipped (epoch 3 E2 s7, KEY_POOL_SPEC.md section 6). Subject is the UTC date, so one per day coalesces on the page.
    /// </summary>
    public const string ClaudeKeysEmpty = "claude-keys-empty";

    /// <summary>
    /// A claude role job ran on the PERSON'S OWN sign-in because the device switch <c>COAI_ROLE_CLAUDE_PERSON_SIGN_IN</c> is set and the pool holds no key (epoch 3 E2 s7). Subject is the UTC date, so one per day coalesces on the page.
    /// </summary>
    public const string ClaudePersonSignInUsed = "claude-person-sign-in-used";

    /// <summary>
    /// The instruction step of a coder job printed text that looks like a credential (epoch 3 E3, owner decision of 2026-11-26): the job went on and the output reached the prompt as it was, and this records the finding. Subject is the job id; the title and detail
    /// name the class of the secret and the line, NEVER the value. Read by the extension's notifications like every notice; a later screen may show the findings in a tab of their own.
    /// </summary>
    public const string InstructionsSecret = "instructions-secret";

    /// <summary>
    /// The real sign-in file of a vendor (codex, agy) was not what it was when a coder job began: its sha256 or its modification time differ at the job's end (epoch 3 E3 s7 t6 d4). coai opens that file for reading only and gives the unit a copy, so something else changed it — usually the person's own CLI.
    /// Subject is the job id; the notice names the vendor and says the two times, never a hash of a secret as a secret and never any text of the file.
    /// </summary>
    public const string SignInFileChanged = "sign-in-file-changed";

    /// <summary>
    /// The preflight of a codex coder found a tool that is not in the known set of 11 (epoch 3 E3 s2 t5; <c>SPEC_CHANGES.md</c> Q3 item 5): the job was refused before the coder started. ONE notice for the job. Subject is the job id. It names the vendor, the model as asked and as resolved, the installed CLI release and the names — a name only when it is
    /// 1 to 64 characters of letters, digits, underscore, dot and hyphen (any other name is said as a name that cannot be shown, with its length), at most 5, then the count. The tab that shows it is epoch 4 <c>e4-local-pages</c>.
    /// </summary>
    public const string CoderToolUnknown = "coder-tool-unknown";

    /// <summary>Every code, for the test that asserts the redactor rewrites none of them.</summary>
    public static IReadOnlyList<string> All =>
    [
        Refused,
        ReviewerTimedOut,
        ReviewerCancelled,
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
        V2ConvertedAnsweredHold,
        RoleJobStalled,
        RoleJobLost,
        RoleModelSwitched,
        RoleJobFailed,
        ClaudeKeyDead,
        ClaudeKeyExposed,
        ClaudeKeysExhausted,
        ClaudeKeysEmpty,
        ClaudePersonSignInUsed,
        InstructionsSecret,
        SignInFileChanged,
        CoderToolUnknown,
    ];
}
