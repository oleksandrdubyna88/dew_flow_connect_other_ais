using CoaiMcp.Core.Rounds;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// The five ways one reviewer ends, and the success. Never a silent zero: a round that ran with
/// four of six reviewers says so by name.
/// </summary>
/// <remarks>
/// <b>"A sixth cannot appear silently" was written here and it was not true.</b> A C# switch over a
/// class hierarchy is not exhaustive-checked: adding a sealed subtype below compiles, and every
/// switch over these then either throws at runtime or folds the new ending into a default arm. What
/// makes the claim true is a TEST — <c>ReviewerNotices.ByType</c> is a map with no default, and
/// <c>TheReviewerFailuresAreWrittenDownTests</c> compares its keys against the sealed subtypes
/// reflection finds in this assembly, so a sixth is red on the day it is added. (codex, on story
/// 2.3's plan round.)
/// </remarks>
public abstract record ReviewerOutcome
{
    /// <summary>
    /// The turns of this reviewer's conversation BEFORE the one this outcome describes, each with what it
    /// consumed — empty for the single-turn reviewer every stage but one runs (S3.2, plan §4.9).
    /// </summary>
    /// <remarks>
    /// On the BASE, not on <see cref="Ok"/> alone, because the whole point is the failures: a turn-2
    /// timeout, a cancellation during turn 2, an unparseable turn 3 each end the reviewer, and the money
    /// turn 1 cost is real whatever came after. The ledger writes one line per entry here and the round
    /// total adds them; <c>Usage</c> on an outcome that has one stays the LAST turn's alone.
    /// </remarks>
    public IReadOnlyList<TurnUsage> EarlierTurns { get; init; } = [];

    /// <summary>What every earlier turn consumed, added up — <see cref="Usage.None"/> for a single turn.</summary>
    public Usage EarlierUsage => EarlierTurns.Aggregate(Usage.None, (total, turn) => total.Add(turn.Usage));

    /// <summary>
    /// What THIS turn's earlier launches consumed before the ending this outcome describes: the first
    /// attempt that completed, was billed, and answered something malformed — when the repair that
    /// followed it then failed on its own launch instead of answering.
    /// </summary>
    /// <remarks>
    /// <para>On the base, like <see cref="EarlierTurns"/>, because the outcomes that need it are the
    /// FAILURES: a repair that timed out, exited non-zero, was rate limited or never started is returned
    /// as its launch produced it, and until 2026-09-26 that returned it without the first attempt's
    /// usage — a completed, billed process filed as free, on every stage and on every turn of the
    /// feature reviewer's conversation. <see cref="Ok"/> and <see cref="Unparseable"/> fold both launches
    /// into their own <c>Usage</c> already, so this stays <see cref="Usage.None"/> on them.</para>
    /// <para>Also what the rate-limit ladder's EARLIER steps spent, folded forward by the scheduler: a
    /// step whose repair was rate limited still paid for its malformed first launch.</para>
    /// </remarks>
    public Usage EarlierLaunches { get; init; } = Usage.None;

    /// <summary>
    /// Every launch of the outcome's own — last — turn, one part each, when it took more than one: a repair, a
    /// follow-up, a step of the rate-limit ladder. Empty for a turn of one launch.
    /// </summary>
    /// <remarks>
    /// The LEDGER's, not the round's (story 3.3 of todo/PLAN_every_round_is_counted.md): the ledger writes one line per
    /// part, while <see cref="LastTurnUsage"/> and <see cref="TotalUsage"/> keep the sums every other reader adds — so
    /// the parts' usage adds up to <see cref="LastTurnUsage"/> exactly, or <see cref="LaunchLines.Of"/> ignores them.
    /// </remarks>
    public IReadOnlyList<LaunchPart> Launches { get; init; } = [];

    /// <summary>
    /// What the launch control said the person should know about a launch it ALLOWED — empty for nearly every reviewer.
    /// Runtime-neutral: a CLI version nobody measured is the first case (Vibe, the owner's decision of 2026-10-13: "warn,
    /// not refuse"), and any adapter's launch control can say one (<see cref="LaunchTurn.Warning"/>).
    /// </summary>
    /// <remarks>
    /// On the BASE, because it is about the launch and not about how it ended: a warned launch that then failed still ran
    /// on a version nobody measured, and the person reading the failure is owed that too.
    /// </remarks>
    public string Warning { get; init; } = string.Empty;

    /// <summary>The CLI release the launch control asked before the first launch (<see cref="LaunchTurn.CliVersion"/>) — what the ledger line names; empty when none was asked.</summary>
    public string CliVersion { get; init; } = string.Empty;

    /// <summary>What the launch this outcome describes reported: an answer's, an unparseable one's; nothing for a run that never finished.</summary>
    protected virtual Usage OwnUsage => Usage.None;

    /// <summary>
    /// What the outcome's own — last — turn consumed, every launch of it: the ledger's terminal line, and
    /// the one place the question "what did this turn cost" is answered for every kind of outcome.
    /// </summary>
    public Usage LastTurnUsage => EarlierLaunches.Add(OwnUsage);

    /// <summary>The whole conversation's usage: every earlier turn and the last, every launch of each.</summary>
    public Usage TotalUsage => EarlierUsage.Add(LastTurnUsage);

    /// <param name="Usage">What the vendor said the LAST turn consumed. Zeroes mean it said nothing.</param>
    /// <param name="Turns">How many turns the conversation took — one for every reviewer but a feature reviewer that asked for source.</param>
    /// <param name="Served">What the conversation served and refused, one line per follow-up turn, for the audit and the reviewer's note; empty for a single turn.</param>
    /// <param name="Evidence">
    /// Where this reviewer's raw answer was kept, or empty for the ordinary case where it was not.
    /// <para>Only a review with NO findings is kept. A reviewer that answers `{"findings": []}` has
    /// succeeded by every measure the round can take — an `ok` outcome, its tokens counted — and on
    /// 2026-09-08 eight of them did that on a diff a ninth reviewer found eleven things in. Nothing
    /// was kept, so the question "did the model answer emptily, or was it handed a prompt that
    /// deserved an empty answer" had no artefact behind it at all.</para>
    /// <para>It travels ON the outcome rather than being logged where it is written, because the
    /// sentence that needs it is the reviewer's own audit line — which is written by the caller,
    /// from the outcome, and is where somebody chasing a silent round is already reading.</para>
    /// </param>
    public sealed record Ok(NormalisedReview Review, bool Repaired, Usage Usage, string Evidence = "", int Turns = 1, string Served = "")
        : ReviewerOutcome
    {
        public Ok(NormalisedReview Review, bool Repaired) : this(Review, Repaired, Usage.None) { }

        protected override Usage OwnUsage => Usage;
        public string InputCoverage { get; init; } = string.Empty;

        /// <summary>What a Team server said it did not apply of this reviewer's launch (E2.5); empty for every other.</summary>
        public string NotApplied { get; init; } = string.Empty;
    }

    /// <param name="FailureReason">
    /// What the vendor said about its own failure somewhere OTHER than stderr, or empty.
    /// </param>
    /// <remarks>
    /// <para>A second field rather than folding it into <paramref name="StdErrTail"/>, because the
    /// two are not equally trustworthy and the sentence must prefer the one that announces: stderr
    /// is a vendor writing an error, while this is whatever its adapter could recover. See
    /// <c>IReviewerRuntime.WhyItFailed</c>.</para>
    /// <para>Named for what it HOLDS rather than where it came from. It was <c>StdOutTail</c> for
    /// one round, and that name was wrong twice: it is a parsed sentence rather than a tail of a
    /// stream, and the next adapter to implement this may read a file it named rather than stdout
    /// at all. Raised by gemini on the code round, twice from different angles.</para>
    /// </remarks>
    public sealed record NonZeroExit(int ExitCode, string StdErrTail, string FailureReason = "") : ReviewerOutcome
    {
        /// <summary>
        /// What the failed launch consumed anyway — the third failure that costs money, beside an
        /// unparseable answer and a failed later turn.
        /// </summary>
        /// <remarks>
        /// Measured 2026-09-26: the api shim exits 70 on a reasoning-only completion (Qwen3.8-max: 53,092
        /// prompt tokens, 16,382 reasoning tokens, no content) and on an answer cut at the token ceiling,
        /// AFTER the vendor billed the call — and the launch answered <see cref="Usage.None"/> for every
        /// non-zero exit without asking the adapter, so the money stopped at stdout. Read through the same
        /// adapter route as an answered launch; a process that reported nothing is still <see cref="Usage.None"/>.
        /// </remarks>
        public Usage Usage { get; init; } = Usage.None;

        protected override Usage OwnUsage => Usage;
    }

    /// <summary>
    /// The launch was killed — on the reviewer's own deadline, or by a cancellation the launcher reports
    /// the same way (<c>ProcessResult.Cancelled</c>).
    /// </summary>
    public sealed record TimedOut : ReviewerOutcome
    {
        /// <summary>
        /// What the killed launch consumed: <see cref="Usage.Unknown"/>, because a process killed before it
        /// answered never reported its usage — and the vendor may well have read and billed the request.
        /// </summary>
        /// <remarks>
        /// Unknown rather than zero is the plan round's accepted finding on the API-vendor calibration
        /// branch: <see cref="Usage.None"/> here wrote a killed turn down as <c>0</c> tokens, which a priced
        /// row then read as $0. <see cref="Usage.None"/> only for a turn that was never LAUNCHED — the
        /// conversation's cap was already spent (<c>TurnLoop</c>) — which is the one TimedOut that
        /// consumed nothing, as a fact.
        /// </remarks>
        public Usage Usage { get; init; } = Usage.Unknown;

        protected override Usage OwnUsage => Usage;
    }

    /// <summary>
    /// The launch was killed because the CALLER's token fired — a job somebody withdrew, a round that ended, a Team
    /// server that stopped it — and never because the reviewer ran out of its own deadline, which is
    /// <see cref="TimedOut"/>.
    /// </summary>
    /// <remarks>
    /// <para>Its own ending since story 3.2 of todo/PLAN_every_round_is_counted.md. <c>ProcessLauncher</c> has told the
    /// two apart since <c>ProcessResult.Cancelled</c> — a budget that ran out and a caller that withdrew arrive as the
    /// same kill and mean opposite things — and the executor read only <see cref="TimedOut"/>, so a withdrawal the
    /// Team server's client gave up on after 8.4 s was written down as the vendor's timeout, and a person reading the
    /// ledger was sent to look at a vendor that was never slow. The deployed server's own line says so:
    /// <c>src_server/tests/fixtures/usage/timed-out-withdrawal.jsonl</c>.</para>
    /// <para>What it consumed is <see cref="Usage.Unknown"/>, for <see cref="TimedOut"/>'s reason: a process killed
    /// before it answered never reported, and the vendor may well have read and billed the request. WHY the caller
    /// fired — withdrawn, abandoned, the server's backstop, the host stopping — is not this type's to know: the token
    /// does not say, and only the store that fired it does (<c>JobStore</c> on the Team server, <c>JobRecord.EndCause</c>).</para>
    /// </remarks>
    public sealed record Cancelled : ReviewerOutcome
    {
        public Usage Usage { get; init; } = Usage.Unknown;

        protected override Usage OwnUsage => Usage;
    }

    /// <param name="Usage">
    /// What the run consumed anyway. An unparseable answer is the one FAILURE whose process
    /// completed and reported its usage, and dropping it under-reported a round by roughly half
    /// whenever a reviewer fell over — measured: two failed reviewers ran 107 and 128 seconds
    /// beside a sibling that cost 210k input tokens, and the round recorded them as free.
    /// </param>
    public sealed record Unparseable(string Reason, Usage Usage) : ReviewerOutcome
    {
        public Unparseable(string Reason) : this(Reason, Usage.None) { }

        protected override Usage OwnUsage => Usage;
    }

    /// <summary>
    /// Distinct from a timeout in the log AND the result — they demand different cures.
    /// </summary>
    /// <param name="Reason">
    /// The vendor's own words. Carried because a bare "rate limited" repeats the mistake that a
    /// bare "exit 1" made: a per-minute throttle a retry clears and a DAILY quota that no retry
    /// can clear read identically, and only one of them is worth waiting for.
    /// </param>
    /// <param name="Attempts">
    /// How many launches it actually took before this was the answer — one when the limit was
    /// hopeless from the first word, up to one more than the ladder has steps.
    /// </param>
    /// <remarks>
    /// It travels ON the outcome because the summary is built from outcomes alone, and the number
    /// is known only inside the scheduler's retry loop. Without it the round said "after one retry"
    /// however many launches there had been — a sentence that was true when there was one step and
    /// became a confident wrong number the moment there were four.
    /// </remarks>
    public sealed record RateLimited(string Reason = "", int Attempts = 1) : ReviewerOutcome;

    /// <summary>
    /// The process never ran: the CLI is not installed, or the configured path is wrong. Its own
    /// outcome because it is neither a failure of the model nor of the network — found when a
    /// test pointed one provider at a missing binary and the exception took the whole ROUND down
    /// instead of one reviewer.
    /// </summary>
    public sealed record NotStarted(string Reason) : ReviewerOutcome;

    /// <summary>
    /// A local reviewer the round chose NOT to launch, because the cloud reviewers had all answered and
    /// found almost nothing — issue #485, behind <c>COAI_STOP_LOCAL_WHEN_QUIET</c>. A decision, not a
    /// failure: the summary lists it as not asked, and it cost nothing.
    /// </summary>
    public sealed record StoodDown(string Reason) : ReviewerOutcome;

    private ReviewerOutcome() { }
}

/// <summary>
/// Recognising "the vendor could not serve this right now, try again" in a CLI's output — the one
/// failure worth a second attempt. Pure, so it is a table test.
/// </summary>
public static class RateLimit
{
    /// <summary>
    /// The phrases vendors actually use, all of them observed rather than imagined:
    /// <list type="bullet">
    /// <item>Codex says "You've hit your usage limit" — never "rate limit", never "429", which is
    /// why a quota exhaustion was first misreported as a plain non-zero exit and never retried.</item>
    /// <item>Gemini answers <c>503 UNAVAILABLE</c> "This model is currently experiencing high
    /// demand" — transient by its own description, and so exactly what one retry is for.</item>
    /// <item>xAI answers HTTP 500 <c>{"code":"internal","error":"Auth context expired."}</c> — measured
    /// 2026-09-27 on 8 of 20 grok-4.7 feature reviews, mid-generation, with nothing on our side
    /// correlating; the api shim reports it as a transient (exit 75) and this phrase is what lets the
    /// ladder retry it, bounded by what is left of the turn's deadline inside the twenty-minute cap.</item>
    /// <item>Kimi Code on a spent API-key account answers <c>provider.api_error: 429 Your account … is suspended due to
    /// insufficient balance</c> — measured 2026-10-11. Its 429 has no status label and no "too many", so the code pattern
    /// below does not take it; this phrase does, and <see cref="Spent"/> names it hopeless.</item>
    /// </list>
    /// </summary>
    private static readonly string[] Phrases =
        ["rate limit", "usage limit", "quota", "unavailable", "high demand", "auth context expired", "insufficient balance"];

    /// <summary>
    /// The status codes, matched as CODES rather than as three digits anywhere in the output.
    /// </summary>
    /// <remarks>
    /// <para><b>`429` and `503` used to be in the phrase list above</b>, matched as bare substrings
    /// of stdout and stderr — and a Cloudflare ray id is hexadecimal, a token count is a number, and
    /// a duration in milliseconds is a number. Measured 2026-09-03: a codex reviewer handed
    /// <c>unexpected status 404 Not Found ... cf-ray: a3…</c> was reported to the person as
    /// <i>"rate limited (after one retry)"</i>, so they were told to wait for a quota that was never
    /// the problem, and the reviewer was retried against a route that answers 404.</para>
    /// <para>A code counts when something says it IS a status — `HTTP 429`, `status: 503`,
    /// `code 429` — or when the reason phrase follows it, which is how the CLIs actually print one:
    /// `429 Too Many Requests`, `503 UNAVAILABLE`, `503 Service Unavailable`.</para>
    /// </remarks>
    /// <remarks>
    /// <para>The alternatives are exactly the shapes in the table below and nothing else. The first
    /// version of this regex also took <c>code</c>, <c>status_code</c> and <c>error</c> as labels
    /// and a bare <c>rate</c> as a reason — none of them observed from a vendor, and the same class
    /// of guess as the bare <c>429</c> it replaced. Raised in this change's own code round: a rule
    /// that says every code is observed is not kept by a pattern that accepts four more.</para>
    /// </remarks>
    private static readonly System.Text.RegularExpressions.Regex StatusCode = new(
        @"(?:\bhttps?\s*[:/]?\s*(?:429|503)\b)"
            + @"|(?:\bstatus\b\W{0,4}(?:429|503)\b)"
            + @"|(?:\b429\b\s*[:\-]?\s*too\s+many)"
            + @"|(?:\b503\b\s*[:\-]?\s*(?:service\s+)?unavailable)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static bool Hit(ProcessResult result) =>
        result.ExitCode != 0 && (Marked(result.StdErr) || Marked(result.StdOut));

    private static bool Marked(string text) =>
        Phrases.Any(p => Contains(text, p)) || StatusCode.IsMatch(text);

    /// <summary>
    /// An allowance that has been SPENT — not a momentary refusal. Every phrase observed.
    /// </summary>
    /// <remarks>
    /// <para>The distinction is not how big the number is, it is what kind of thing ran out: a plan's
    /// allowance, bought back or waiting for a window measured in hours, against a refusal that
    /// clears while you stand there.</para>
    /// <para><b>Nothing goes in this list that has not been read off a real vendor answer.</b> The
    /// first draft of it also had <i>weekly limit</i>, <i>upgrade</i>, <i>purchase</i>,
    /// <i>resets at</i> and <i>try again at</i> — all reasoned from what a vendor might say, which is
    /// the same guess that once matched a bare <c>429</c> against a Cloudflare ray id. Two of them
    /// were worse than useless: "rate limit reached; upgrade to the paid plan" is a sales footer on a
    /// throttle that clears, and refusing to wait it out would lose a reviewer that would have
    /// answered. (The plan round, two vendors independently.)</para>
    /// </remarks>
    internal static readonly string[] Spent = ["daily", "exhausted", "hit your usage limit", "insufficient balance"];

    /// <summary>
    /// Whether waiting is pointless: a SPENT allowance, not a per-minute throttle.
    /// </summary>
    /// <remarks>
    /// <para>Measured cost of not asking, twice over. Gemini answered "You have exhausted your daily
    /// quota on this model", the scheduler waited its backoff and launched a second doomed reviewer,
    /// and that round took 157 seconds instead of 19. Then on 2026-09-09 codex answered "You've hit
    /// your usage limit. Upgrade to Pro … to purchase", which contained neither of the two words this
    /// knew — so four reviewers of one code round climbed the whole ladder at 204.4, 212.6, 209.9 and
    /// 240.8 seconds, and a plan round spent 213 of its 213 seconds on one reviewer waiting.</para>
    /// <para>The retry exists for "this model is currently experiencing high demand" and for "please
    /// try again in 1.2s", which clear in seconds. What it must never be spent on is an allowance
    /// that clears at midnight in somebody else's timezone, or when somebody pays.</para>
    /// </remarks>
    public static bool Hopeless(string reason) =>
        Spent.Any(p => Contains(reason, p));

    /// <summary>
    /// The line that says WHICH limit was hit, so a person can tell a per-minute throttle from a
    /// spent allowance — and so <see cref="Hopeless"/> has something to read.
    /// </summary>
    /// <remarks>
    /// <para><b>A terminal line wins over a merely marked one.</b> A vendor that prints
    /// <c>429 Too Many Requests</c> and then says the allowance is gone had the first line read as
    /// its reason, so the scheduler was told a limit that cannot clear was worth waiting for — and
    /// the person was shown the less useful of the two sentences. Two passes, the terminal one
    /// first.</para>
    /// <para>Both passes are over MARKED lines only, and that is deliberate rather than an oversight:
    /// every phrase in <see cref="Spent"/> is itself matched by <see cref="Phrases"/> — <i>quota</i>
    /// covers the first two in every observed sample and <i>usage limit</i> the third — so a spent
    /// line that is not marked cannot occur for anything seen. Scanning unmarked lines was asked for
    /// on the plan round and declined: it is what would let a sales footer beside an unrelated error
    /// reach this decision. (The two findings answer each other; the one with an observed failure
    /// behind it wins.)</para>
    /// </remarks>
    public static string Reason(ProcessResult result)
    {
        var marked = (result.StdErr + '\n' + result.StdOut)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(Marked)
            .ToArray();

        return marked.FirstOrDefault(Hopeless) ?? marked.FirstOrDefault() ?? string.Empty;
    }

    private static bool Contains(string text, string needle) =>
        text.Contains(needle, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Runs ONE reviewer to a <see cref="ReviewerOutcome"/>: launch → classify the exit → read the
/// answer (codex: the <c>-o</c> file; gemini: stdout through <see cref="GeminiPayload"/>) →
/// parse. An unparseable answer gets exactly one repair launch; a second failure is a named
/// outcome, never a retry loop.
/// </summary>
public sealed class ReviewerExecutor(
    IProcessLauncher launcher,
    string? keepUnparseableIn = null,
    string? keepEmptyIn = null,
    Action<string>? note = null)
{
    private const int StdErrTail = 400;

    /// <summary>
    /// How much of a failing CLI's stderr travels with its exit code — in WHOLE LINES.
    /// </summary>
    /// <remarks>
    /// <para>It used to be the last <see cref="StdErrTail"/> characters, full stop, and the cut
    /// landed wherever it landed. Measured on a real gate, 2026-09-08:
    /// <c>local/Conventions FAILED after 290.0s: exit 69: l Qwen3.5-35B-A3B-Q5_vk128:latest,
    /// pid 52068)</c> — a sentence that begins mid-word, because the reason-picker takes the first
    /// line of this tail and the first line of this tail was half of one.</para>
    /// <para><b>Whole lines by CONSTRUCTION, not by a flag.</b> The plan proposed telling the picker
    /// that the tail had been truncated so it could drop the first line; all three plan reviewers
    /// found the same two holes in that, from three directions. A 400-character cut can land exactly
    /// on a newline, and dropping the first line then discards a complete diagnostic; and a stderr
    /// with no newline inside the budget has no first line to drop, so dropping it leaves a real
    /// failure reported as blank. Trimming here — where the whole stderr is in hand and the boundary
    /// is knowable — costs the picker no new parameter and leaves it no case to get wrong.</para>
    /// <para>A line longer than the whole budget is ONE reason, and it is kept whole up to
    /// <see cref="VendorReason.SafetyCap"/> — never cut at the budget. It used to keep only its opening
    /// <see cref="StdErrTail"/> characters, which were then the most the ledger and the log could ever say about it
    /// (story 3.3 of todo/PLAN_every_round_is_counted.md); a summary still shortens what it shows.</para>
    /// </remarks>
    public static string TailOf(string stdErr)
    {
        var written = stdErr.Trim();
        if (written.Length <= StdErrTail)
        {
            return written;
        }

        var kept = WholeLinesFrom(written);

        return kept.Length > 0 ? kept : OpeningOfLastLine(written);
    }

    /// <summary>As many complete trailing lines as the budget holds, and never half of one.</summary>
    private static string WholeLinesFrom(string written)
    {
        var cut = written[^StdErrTail..];

        // The cut begins a line only when what it followed was a newline. Anything else means it
        // landed inside one, and everything up to the next break is a fragment.
        if (written[^(StdErrTail + 1)] == '\n')
        {
            return cut.Trim();
        }

        var firstBreak = cut.IndexOf('\n');

        return firstBreak < 0 ? string.Empty : cut[(firstBreak + 1)..].Trim();
    }

    /// <summary>One diagnostic longer than the whole budget: the whole of it, to the safety cap.</summary>
    private static string OpeningOfLastLine(string written)
    {
        var lastBreak = written.LastIndexOf('\n');
        var line = (lastBreak < 0 ? written : written[(lastBreak + 1)..]).Trim();

        return VendorReason.Capped(line);
    }

    /// <summary>
    /// Where an answer that would not parse is KEPT, so the next person can read what the vendor
    /// actually said.
    /// </summary>
    /// <remarks>
    /// The raw text is PASSED IN rather than remembered on this instance. One executor serves the
    /// whole fan-out — six reviewers at once — so an instance field would hand a failed reviewer
    /// whichever answer happened to finish last. Both vendors caught that in the round that
    /// reviewed this file.
    /// </remarks>
    /// <remarks>
    /// The first real code round lost a reviewer to "unparseable: still not the schema's JSON
    /// after one repair attempt" and the evidence was gone — the same answer replayed by hand
    /// afterwards succeeded, so the sentence named a symptom nobody could chase. An unparseable
    /// answer is the one case where the raw text is the whole story.
    /// </remarks>
    private string? Keep(ReviewerInvocation invocation, string? raw) =>
        KeepIn(keepUnparseableIn, invocation, raw);

    /// <summary>
    /// One reviewer's raw answer, on disk, under whichever directory the question belongs to.
    /// </summary>
    /// <remarks>
    /// <para><b>The name identifies the author.</b> `provider-Role-timestamp` is unique within a
    /// round by construction — one reviewer per provider and role — which matters because a person
    /// reading a silent round has twelve files and needs the one belonging to codex/Architecture.
    /// Nothing inside the file says who wrote it.</para>
    /// <para><b>Written to a sibling and renamed.</b> `File.WriteAllText` truncates first and writes
    /// second, so a process killed between those steps leaves a file that exists and holds nothing —
    /// which, in THIS directory, reads exactly like a vendor that answered with nothing. That is the
    /// one confusion the evidence exists to prevent. `RemoteRuntime.Claim` documents the same trap
    /// and the same cure; it is not shared code because that one also retries a `Replace` against a
    /// reader holding the destination, and here the destination is a name nothing else knows.</para>
    /// </remarks>
    private string? KeepIn(string? directory, ReviewerInvocation invocation, string? raw) =>
        directory is null || raw is null
            ? null
            // Bounded, atomic and never fatal — `EvidenceFile`, which the consultation's evidence uses too.
            // The note goes to the caller because this class has no logger of its own and should not grow one.
            : Kept(EvidenceFile.Keep(
                directory,
                $"{FileName.Safe(invocation.Provider)}-{FileName.Safe(invocation.Role)}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.txt",
                raw,
                e => note?.Invoke($"the answer could not be kept in {directory}: {e.Message}")));

    /// <summary>The kept path, or null where this class has always said "nothing was kept".</summary>
    private static string? Kept(string path) => path.Length > 0 ? path : null;

    /// <summary>
    /// Why there is no second launch to make, or null when there is one.
    /// </summary>
    /// <remarks>
    /// <para>Two questions with one answer — nobody configured a repair, or the deadline is spent —
    /// and they were two `if` blocks returning nearly the same object until the code round counted
    /// `RunAsync`'s branches. Asking once puts the two REASONS beside each other, which is where
    /// they belong: they differ only in the sentence a person reads.</para>
    /// <para>A spent deadline matters more than it looks. `ProcessLauncher` calls
    /// `CancelAfter(request.Timeout)`, so a repair handed zero would start a process and cancel it
    /// in the same breath, then report a timeout describing the repair rather than the answer that
    /// needed one — hiding the diagnosis somebody is actually looking for.</para>
    /// </remarks>
    /// <summary>
    /// A reviewer that answered — and, when it answered with NOTHING, the file that says so.
    /// </summary>
    /// <remarks>
    /// <para>A review with no findings is a success by every measure the round can take: an `ok`
    /// outcome, its tokens counted, its seconds recorded. On 2026-09-08 eight remote reviewers
    /// returned `{"findings": []}` in four to twenty-five seconds each, on a diff the local
    /// reviewer found eleven things in — and there was nothing to read afterwards, because the raw
    /// text of a parsed answer is dropped. Only a PARSE failure reached `unparseable/`.</para>
    /// <para>Kept in its own directory rather than beside those, because "it said nothing" and
    /// "it said something I could not read" are different questions and a person chasing one must
    /// not wade through the other.</para>
    /// <para>An answer WITH findings is not kept. A directory that also collected the healthy case
    /// would be a directory whose name lies, and the one file that matters would be one of
    /// hundreds.</para>
    /// </remarks>
    private ReviewerOutcome.Ok Answered(
        NormalisedReview review,
        bool Repaired,
        Usage usage,
        ReviewerInvocation invocation,
        string? raw) =>
        new(review,
            Repaired,
            usage,
            review.Findings.IsEmpty ? KeepIn(keepEmptyIn, invocation, raw) ?? string.Empty : string.Empty);

    /// <summary>
    /// The second launch: the adapter's own continuation of the first when it has one, else the repair.
    /// </summary>
    /// <remarks>
    /// Issue #504: a reviewer whose shell command was auto-denied ended its turn with nothing, and a fresh
    /// repair asked the same question into the same denial. The adapter that can CONTINUE the conversation
    /// knows it; this only asks. Still ONE second launch, inside the same remaining deadline — and it says
    /// which it is, because the ledger writes each launch as its own line (story 3.3).
    /// </remarks>
    private static (ReviewerInvocation? Launch, string Attempt) SecondLaunch(ReviewerInvocation first, ReviewerInvocation? repair, string transcript) =>
        first.Adapter?.FollowUp(first, transcript) is { } followUp ? (followUp, LaunchAttempts.FollowUp) : (repair, LaunchAttempts.Repair);

    private static string? NoRepairToRun(ReviewerInvocation? repair, TimeSpan left) => repair switch
    {
        null => "and no repair was configured",
        _ when left <= TimeSpan.Zero => "and the reviewer's deadline was spent before it could be repaired",
        _ => null,
    };

    /// <remarks>
    /// The outcome carries the FIRST launch's warning (<see cref="ReviewerOutcome.Warning"/>), however the reviewer ended:
    /// the launch control says it once per launch, and a repair or a follow-up runs on the same CLI.
    /// </remarks>
    public async Task<ReviewerOutcome> RunAsync(
        ReviewerInvocation invocation,
        ReviewerInvocation? repair = null,
        CancellationToken ct = default)
    {
        // The reviewer's deadline bounds the REVIEWER, and this method makes up to two launches.
        // The clock starts here so the second one gets what the first left, exactly as the
        // rate-limit ladder already does — same helper, same arithmetic.
        var spent = System.Diagnostics.Stopwatch.StartNew();
        var first = await RunOnceAsync(invocation, ct);

        return (await ConcludedAsync(invocation, repair, first, spent, ct)) with { Warning = first.Warning, CliVersion = first.CliVersion };
    }

    /// <summary>The reviewer's outcome once its first launch has run: that launch's own, or after its one repair.</summary>
    private async Task<ReviewerOutcome> ConcludedAsync(
        ReviewerInvocation invocation, ReviewerInvocation? repair, OneLaunch first, System.Diagnostics.Stopwatch spent, CancellationToken ct)
    {
        var budget = invocation.Request.Timeout;
        var (outcome, review, usage, answer, evidence, notApplied, _) = first;
        if (outcome is not null)
        {
            return outcome;
        }

        if (review is { } parsed)
        {
            return Answered(parsed, Repaired: false, usage, invocation, answer) with { NotApplied = notApplied };
        }

        // Measured in the ledger before it was fixed: one reviewer at 668.8 s against a ten-minute
        // deadline, reporting `ok`. `reviewerTimeoutMinutes` bounded each LAUNCH, so a reviewer that
        // needed a repair could take twice what it was given — and the operator who reported "the
        // limit did not work again" was reading a real number.
        var left = RetryLadder.Remaining(spent.Elapsed, budget);
        var next = SecondLaunch(invocation, repair, evidence);
        if (NoRepairToRun(next.Launch, left) is { } why)
        {
            return new ReviewerOutcome.Unparseable(
                Because(Said(answer, Complaint(evidence)), why, Keep(invocation, evidence)), usage);
        }

        // The LESSER of the two, not whichever was computed last. The scheduler may already have
        // shortened this repair on a retry, and overwriting that with the executor's own remainder
        // would hand it back time the ladder had taken away. Raised on the code round.
        // Stamped with what it IS (story 4.1 of todo/PLAN_every_round_is_counted.md): an adapter that repeats the
        // attempt on the wire reads it at launch, and it is the word the ledger's line below carries.
        var started = spent.Elapsed;
        var launch = next.Launch! with { Attempt = next.Attempt };
        var second = await RunOnceAsync(RetryLadder.Lesser(left, launch), ct);
        var after = AfterTheRepair(launch, (usage, answer, evidence), second);

        // Two paid launches, each its own ledger line (story 3.3 of todo/PLAN_every_round_is_counted.md): the outcome's
        // sums stay what every other reader adds, and the parts say what each launch was, cost and took.
        return after with
        {
            Launches =
            [
                new LaunchPart(LaunchAttempts.First, usage, started, LaunchLines.OutcomeOf(new ReviewerOutcome.Unparseable(Said(answer, Complaint(evidence))))),
                SecondPart(next.Attempt, second.Outcome, second.Usage, spent.Elapsed - started, after),
            ],
        };
    }

    /// <summary>
    /// The second launch as a part: what IT reported — a failed launch's own usage (a kill's is unknown, not zero), an
    /// answer's as its launch read it — so the two parts add up to the outcome's <see cref="ReviewerOutcome.LastTurnUsage"/>.
    /// </summary>
    private static LaunchPart SecondPart(string attempt, ReviewerOutcome? terminal, Usage launched, TimeSpan took, ReviewerOutcome after) =>
        new(attempt, terminal?.LastTurnUsage ?? launched, took, LaunchLines.OutcomeOf(after));

    /// <summary>
    /// The reviewer's outcome once its repair has run: the repair's answer, the repair's own failure, or
    /// a second answer that would not parse either — and in every one of the three, BOTH launches billed.
    /// </summary>
    /// <remarks>
    /// The first attempt is a process that ran to completion and reported what it consumed; only its
    /// ANSWER was malformed. A repaired reviewer has counted it since 2026-09-01, and a second
    /// unparseable answer since the same day — but a repair whose own launch failed (timed out, exited
    /// non-zero, was rate limited, never started) was returned exactly as the launch produced it, and the
    /// first attempt's usage went nowhere: not on the outcome, not in the ledger, not in the round total.
    /// Found by the epic 3 risk consultation (159f0397). It rides on <see cref="ReviewerOutcome.EarlierLaunches"/>;
    /// whatever the repair's own launch reported before it failed is the failure's OWN usage already.
    /// <para><b>Once, not twice</b> (story 3.3 of todo/PLAN_every_round_is_counted.md). The repair's launch usage was
    /// added to <see cref="ReviewerOutcome.EarlierLaunches"/> as well, and a <see cref="ReviewerOutcome.NonZeroExit"/>
    /// carries that same usage as its own — so a repair billed and then refused (the api shim's exit 70) counted twice in
    /// the turn, the ledger line and the round total. Every other failure's launch reported nothing, which is why it
    /// went unseen; splitting the turn into one line per launch is what made the two sums disagree.</para>
    /// </remarks>
    private ReviewerOutcome AfterTheRepair(
        ReviewerInvocation repair,
        (Usage Usage, string? Answer, string Evidence) first,
        OneLaunch second)
    {
        if (second.Outcome is { } failed)
        {
            return failed with { EarlierLaunches = first.Usage };
        }

        return second.Review is { } fixedReview
            // Both launches are billed, so both are counted — a repaired reviewer that
            // reported only its second attempt would under-report every time.
            ? Answered(fixedReview, Repaired: true, first.Usage.Add(second.Usage), repair, second.Answer) with { NotApplied = second.NotApplied }
            // BOTH launches are kept, and the first one wins when the repair came back
            // empty: a vendor whose envelope broke leaves nothing to read, and the
            // evidence file was landing at zero bytes exactly when it was most needed.
            : new ReviewerOutcome.Unparseable(
                Because(
                    // The complaint comes from whichever launch actually explained itself,
                    // starting with the repair — its answer is the one being described. Picking
                    // by SIZE was wrong and the gate said so: a first attempt that returned a
                    // large malformed answer beats a repair that returned nothing plus a
                    // permission refusal on stderr, and the refusal is the whole point.
                    Said(second.Answer ?? first.Answer, FirstComplaint(second.Evidence, first.Evidence)),
                    "after one repair attempt",
                    Keep(repair, Longer(first.Evidence, second.Evidence))),
                first.Usage.Add(second.Usage));
    }

    /// <summary>
    /// Everything the process said, for when what it MEANT to say is missing.
    /// </summary>
    /// <remarks>
    /// Both streams, labelled. An empty answer with an empty evidence file tells nobody anything;
    /// the vendor's own stream carries its status and its error, and that is the whole diagnosis.
    /// </remarks>
    /// <summary>What the child said with every text in <paramref name="texts"/> replaced — see <see cref="ReviewerInvocation.Redact"/>.</summary>
    private static ProcessResult Redacted(ProcessResult result, IReadOnlyList<string> texts) =>
        texts.Count == 0 ? result : result with { StdOut = Without(result.StdOut, texts), StdErr = Without(result.StdErr, texts) };

    private static string Without(string said, IReadOnlyList<string> texts) =>
        texts.Where(text => text.Length > 0).Aggregate(said, (now, text) => now.Replace(text, RedactedText, StringComparison.Ordinal));

    /// <summary>What stands where a redacted text was — saying that something was there, and what kind.</summary>
    internal const string RedactedText = "[the row's system prompt]";

    private static string Transcript(ProcessResult result) =>
        $"--- stdout ---\n{result.StdOut}\n--- stderr ---\n{result.StdErr}";

    private static string Because(string what, string when, string? keptAt) =>
        keptAt is null ? $"{what} {when}" : $"{what} {when} (the answer was kept at {keptAt})";

    /// <summary>
    /// What actually went wrong, which is not always what the old sentence claimed.
    /// </summary>
    /// <remarks>
    /// "Still not the schema's JSON" describes a syntax problem. Measured on a real round, the
    /// vendor had returned NOTHING — an empty envelope — and the sentence sent the reader looking
    /// for malformed JSON that did not exist.
    /// </remarks>
    private static string Said(string? raw, string complaint) =>
        string.IsNullOrWhiteSpace(raw)
            ? Explained("the vendor returned an empty answer", complaint)
            : "the answer was not the schema's JSON";

    /// <summary>The first transcript that carries an explanation, in the order given.</summary>
    /// <remarks>
    /// The KEEP path still saves the LONGER transcript deliberately — a repair whose envelope broke
    /// leaves a zero-byte file, and that was landing exactly when it was most needed. Which file to
    /// save and which stream explained the failure are two different questions, and answering them
    /// with one rule dropped the explanation.
    /// </remarks>
    internal static string FirstComplaint(params string?[] transcripts)
    {
        foreach (var transcript in transcripts)
        {
            var said = Complaint(transcript);
            if (said.Length > 0)
            {
                return said;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// The CLI's own sentence, when it produced nothing and said why.
    /// </summary>
    /// <remarks>
    /// Measured on a real round, 2026-09-06: three antigravity reviewers reported "the vendor
    /// returned an empty answer" and the panel showed nothing more, while the CLI's stderr said
    /// exactly what happened — a tool wanted the "command" permission, headless mode cannot prompt
    /// for one, so it auto-denied itself and printed nothing. That sentence was only in the kept
    /// evidence file, which nobody knows to open; finding it took a quarter of an hour, and reading
    /// the note should have taken seconds. The transcript is already here — this reads the stderr
    /// half of it.
    /// </remarks>
    internal static string Complaint(string? evidence)
    {
        const string marker = "--- stderr ---";
        var at = evidence?.LastIndexOf(marker, StringComparison.Ordinal) ?? -1;
        if (at < 0)
        {
            return string.Empty;
        }

        var said = evidence![(at + marker.Length)..]
            .Split(Environment.NewLine.ToCharArray(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.Length > 0) ?? string.Empty;

        // One line, capped: a note is read in a table cell, and a vendor's stack trace is not the
        // sentence a person needs.
        return said.Length <= ComplaintCap ? said : said[..ComplaintCap] + "…";
    }

    private static string Explained(string what, string why) =>
        why.Length == 0 ? what : $"{what} — it said: {why}";

    private const int ComplaintCap = 240;

    /// <summary>The launch that actually said something — the repair is often the empty one.</summary>
    private static string? Longer(string? first, string? second) =>
        (second?.Trim().Length ?? 0) >= (first?.Trim().Length ?? 0) ? second : first;

    /// <summary>One launch. A non-null outcome is terminal; a null review means "unparseable".</summary>
    /// <summary>
    /// Let the adapter clean up after a run we killed — and never let that cleanup fail the round.
    /// </summary>
    /// <remarks>
    /// It runs on a path where something has ALREADY gone wrong, so an exception here would replace a
    /// reviewer's real failure with a cleanup's. The token is deliberately NOT the round's: the round's
    /// is usually the reason we are here, and a cancelled token would abort the very request that stops
    /// the work.
    /// </remarks>
    private static async Task AbandonAsync(ReviewerInvocation invocation)
    {
        if (invocation.Adapter is null)
        {
            return;
        }

        try
        {
            using var budget = new CancellationTokenSource(AbandonBudget);
            await invocation.Adapter.AbandonAsync(invocation, budget.Token);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Deliberately broad: this is a courtesy call on a failure path, and the vendor's own
            // failure is the one the caller must still see.
        }
    }

    /// <summary>How long a courtesy cleanup may take before the round stops waiting for it.</summary>
    private static readonly TimeSpan AbandonBudget = TimeSpan.FromSeconds(10);

    /// <summary>What one launch came to, read: a terminal outcome, or the parsed review with what the run said beside it.</summary>
    private sealed record OneLaunch(
        ReviewerOutcome? Outcome, NormalisedReview? Review, Usage Usage, string? Answer, string Evidence, string NotApplied, string Warning)
    {
        /// <summary>The CLI release its launch control asked (<see cref="LaunchTurn.CliVersion"/>), or empty.</summary>
        public string CliVersion { get; init; } = string.Empty;
    }

    private async Task<OneLaunch> RunOnceAsync(ReviewerInvocation invocation, CancellationToken ct)
    {
        var launch = await LaunchAsync(invocation, ct);

        return launch.Terminal is not null
            ? new(launch.Terminal, null, launch.Usage, null, string.Empty, string.Empty, launch.Warning) { CliVersion = launch.CliVersion }
            : new(null, Cited(ParseAnswer(launch.Answer, invocation.Provider), invocation.Citations), launch.Usage, launch.Answer, launch.Evidence, launch.NotApplied, launch.Warning)
            {
                CliVersion = launch.CliVersion,
            };
    }

    /// <summary>
    /// A review whose cited files are the repository's: an absolute path the launch's <see cref="ReviewerInvocation.Citations"/>
    /// maps back (a file inside a sanitised snapshot that its manifest holds, D6) becomes that repository-relative path; any
    /// other path stays as the reviewer wrote it.
    /// </summary>
    internal static NormalisedReview? Cited(NormalisedReview? review, ICitedPaths citations) =>
        review is null || ReferenceEquals(citations, CitedAsWritten.Instance)
            ? review
            : review with { Findings = [.. review.Findings.Select(finding => finding with { File = MappedBack(finding.File, citations) })] };

    private static string MappedBack(string file, ICitedPaths citations) =>
        Path.IsPathRooted(file) && citations.MapBack(file) is { Length: > 0 } relative ? relative : file;

    /// <summary>
    /// One launch, as far as "what the process said" — and no further.
    /// </summary>
    /// <remarks>
    /// <para><b>Public because a second binary needs exactly this half.</b> The Team server runs the
    /// same vendor CLIs through the same adapters and hands the vendor's RAW answer back over HTTP;
    /// parsing, the repair launch and de-duplication stay with the client that asked for the review,
    /// which already does all three for a local reviewer. A copy of this classification in the
    /// server is how the vendor-set drift in this repository happened twice.</para>
    /// <para><b><c>Terminal == null</c> means the process ran and exited zero</b> — it is not a
    /// promise that there is an answer. An adapter whose output file never appeared says so with a
    /// null <see cref="ReviewerLaunch.Answer"/>, and what that means is the caller's judgement.</para>
    /// <para>Cancellation and the kill belong to <see cref="IProcessLauncher"/>, which takes the
    /// token and terminates the whole process tree; a cancelled launch throws out of here and
    /// <c>BoundedScheduler</c> reports the reviewer as abandoned. Nothing about that changes here.
    /// A vendor adapter that THROWS out of its own read is a defect in that adapter — every shipped
    /// one catches its IO and JSON failures and answers with a null answer or
    /// <see cref="Usage.None"/>.</para>
    /// </remarks>
    /// <summary>
    /// <see cref="LaunchAsync"/> for an invocation launched exactly ONCE — a consultation turn, a question row — whose
    /// files go when it does, however it ended. A review's go when its retry ladder is over (BoundedScheduler), because
    /// a retry launches the same invocation again.
    /// </summary>
    public async Task<ReviewerLaunch> LaunchOnceAsync(ReviewerInvocation invocation, CancellationToken ct)
    {
        try
        {
            return await LaunchAsync(invocation, ct);
        }
        finally
        {
            LaunchFiles.Forget(invocation.TempFiles);
        }
    }

    /// <summary>
    /// Every launch passes through here, so this is where the adapter is asked for the launch AS IT WILL START
    /// (<see cref="IReviewerRuntime.ForLaunch"/>): the Team server shim's own deadline, attempt and round are argv, and the
    /// executor and the ladder decide two of the three after <c>Build</c> (story 4.1 of todo/PLAN_every_round_is_counted.md).
    /// </summary>
    public Task<ReviewerLaunch> LaunchAsync(ReviewerInvocation invocation, CancellationToken ct) =>
        LaunchBuiltAsync(invocation.Adapter?.ForLaunch(invocation) ?? invocation, ct);

    /// <summary>
    /// The adapter's launch control first (<see cref="IReviewerRuntime.TakeTurnAsync"/>): a refusal is this launch's
    /// <c>NotStarted</c> with the adapter's sentence, and a turn it holds is given back only after the launch has been
    /// read — its answer, usage and reason — so a per-attempt file the adapter reads (Vibe's export, D7) is still there.
    /// </summary>
    private async Task<ReviewerLaunch> LaunchBuiltAsync(ReviewerInvocation invocation, CancellationToken ct)
    {
        using var turn = invocation.Adapter is { } adapter ? await adapter.TakeTurnAsync(invocation, launcher, ct) : LaunchTurn.Free;

        return turn.Refusal.Length > 0
            ? new ReviewerLaunch(new ReviewerOutcome.NotStarted(turn.Refusal), null, Usage.None, string.Empty)
            : (await LaunchTakenAsync(invocation, ct)) with { Warning = turn.Warning, CliVersion = turn.CliVersion };
    }

    /// <summary>The launch, once its turn is taken: started, then judged — killed, rate limited, failed, or read.</summary>
    private async Task<ReviewerLaunch> LaunchTakenAsync(ReviewerInvocation invocation, CancellationToken ct)
    {
        var (result, never) = await StartAsync(invocation, ct);

        return never ?? await KilledAsync(invocation, result) ?? Classified(invocation, result);
    }

    /// <summary>What a launch that never started is given in place of a result — never read.</summary>
    private static readonly ProcessResult Unstarted = new(-1, string.Empty, string.Empty, TimedOut: false);

    /// <summary>The process run to its end, redacted — or the launch that could not start it.</summary>
    private async Task<(ProcessResult Result, ReviewerLaunch? Never)> StartAsync(ReviewerInvocation invocation, CancellationToken ct)
    {
        try
        {
            // Labelled HERE rather than in each vendor adapter: every reviewer launch passes
            // through this one line, and a label added per adapter is a label the next adapter
            // forgets. Only reviewers are tracked — the git commands around them finish in
            // milliseconds and are never the thing left running for ten hours.
            var request = invocation.Request with
            {
                TrackAs = $"{invocation.Provider}/{invocation.Role}",
            };
            // Redacted before anything below reads it: every tail, transcript and reason is cut from these two streams.
            return (Redacted(await launcher.RunAsync(request, ct), invocation.Redact), null);
        }
        catch (OperationCanceledException)
        {
            // The round was abandoned and the tree was killed. A killed process runs no cleanup of
            // its own, so anything it started ELSEWHERE is still running — for a Team server
            // reviewer, on the company's subscription, for an answer nobody will now collect. This
            // is the only moment anybody still knows the job existed.
            await AbandonAsync(invocation);

            throw;
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            // One reviewer that cannot start is one reviewer's failure, never the round's.
            return (Unstarted, new ReviewerLaunch(
                new ReviewerOutcome.NotStarted($"'{invocation.Request.Executable}' could not be started: {e.Message}"),
                null,
                Usage.None,
                string.Empty));
        }
    }

    /// <summary>A launch whose tree was killed — by the caller or on its own deadline — or null when it ran to its end.</summary>
    private static async Task<ReviewerLaunch?> KilledAsync(ReviewerInvocation invocation, ProcessResult result)
    {
        // `Cancelled` BEFORE `TimedOut` (story 3.2 of todo/PLAN_every_round_is_counted.md): a kill by the caller's
        // token sets both flags, a kill on the launch's own budget sets only the second, and reading the second alone
        // filed every withdrawal as the vendor's timeout. The courtesy DELETE is kept on this branch — it is what stops
        // a withdrawn Team-server job, and an early return without it leaves the job running on the company's
        // subscription for an answer nobody will collect. What the launch consumed is unknown, never a free 0/0.
        if (result.Cancelled)
        {
            await AbandonAsync(invocation);

            return new ReviewerLaunch(new ReviewerOutcome.Cancelled(), null, Usage.Unknown, string.Empty, result);
        }

        if (result.TimedOut)
        {
            // Same reasoning as the cancellation above: the tree was killed on our deadline, so the
            // shim never reached its own polite DELETE.
            await AbandonAsync(invocation);

            return new ReviewerLaunch(new ReviewerOutcome.TimedOut(), null, Usage.None, string.Empty, result);
        }

        return null;
    }

    /// <summary>A launch that ran to its end, judged: rate limited, failed (or disowned), or answered.</summary>
    private static ReviewerLaunch Classified(ReviewerInvocation invocation, ProcessResult result)
    {
        if (RateLimit.Hit(result))
        {
            return new ReviewerLaunch(new ReviewerOutcome.RateLimited(RateLimit.Reason(result)), null, Usage.None, string.Empty, result);
        }

        // Both reads go through the vendor's own adapter: where the answer lands and how the run
        // is billed are vendor knowledge, and keeping them here would have made every new vendor
        // an edit to this class. The usage is read BEFORE the exit code is judged: a process that
        // was billed and then refused its own answer (the api shim on a reasoning-only completion or
        // a cut at the ceiling, 2026-09-26) reports its usage on stdout exactly like an answered one,
        // and answering `Usage.None` for every non-zero exit is what left that money at stdout.
        var usage = UsageOf(invocation, result);

        var reason = Failed(invocation, result);
        if (result.ExitCode != 0 || reason.Length > 0)
        {
            return new ReviewerLaunch(
                new ReviewerOutcome.NonZeroExit(result.ExitCode, TailOf(result.StdErr), reason) { Usage = usage },
                null,
                usage,
                string.Empty,
                result);
        }

        var (answer, evidence) = Read(invocation, result);

        return new ReviewerLaunch(null, answer, usage, evidence, result, NotAppliedOf(invocation, result));
    }

    /// <summary>What the run consumed, as its own adapter reads it; the schema-less scan for a launch with none.</summary>
    private static Usage UsageOf(ReviewerInvocation invocation, ProcessResult result) =>
        invocation.Adapter?.ReadUsage(invocation, result) ?? UsageParser.Parse(result.StdOut);

    private static string NotAppliedOf(ReviewerInvocation invocation, ProcessResult result) =>
        invocation.Adapter?.NotApplied(invocation, result) ?? string.Empty;

    /// <summary>
    /// Why this launch is not an answer, or empty: a non-zero exit, with whatever the adapter can say about it — or an exit
    /// of zero the adapter DISOWNS (<see cref="IReviewerRuntime.Disowned"/>: Vibe ran on another model than it was asked).
    /// </summary>
    /// <remarks>
    /// The adapter is asked where ITS reasons live, the same way it is asked where its answer and its usage live. codex
    /// writes its failure into the <c>--json</c> event stream on stdout and leaves stderr empty, so without this the summary
    /// can only report the emptiness. A non-zero exit with nothing to add is still a failure — its stderr tail says it.
    /// </remarks>
    private static string Failed(ReviewerInvocation invocation, ProcessResult result) =>
        invocation.Adapter is not { } adapter ? string.Empty
        : result.ExitCode != 0 ? adapter.WhyItFailed(invocation, result)
        : adapter.Disowned(invocation, result);

    /// <summary>What the vendor said, and what is left to show when it said nothing.</summary>
    private static (string? Answer, string Evidence) Read(ReviewerInvocation invocation, ProcessResult result)
    {
        var raw = invocation.Adapter is { } adapter
            ? adapter.ReadAnswer(invocation, result)
            : ReviewerOutput.FileOrStdout(invocation, result);
        // The EVIDENCE is not always the answer. When a vendor's envelope comes back empty there
        // is nothing in the field the adapter reads, and the diagnosis — its status, its error,
        // its own event stream — is sitting in stdout, which was being thrown away. A kept file
        // of zero bytes is what that looked like from outside. An EMPTY answer takes the same path
        // as a missing one, which is why the test is `IsNullOrWhiteSpace` and not `is null`.
        return (raw, string.IsNullOrWhiteSpace(raw) ? Transcript(result) : raw);
    }

    /// <summary>
    /// The vendor's answer as findings, or null when it is not findings at all.
    /// </summary>
    /// <remarks>
    /// Pure, and it takes exactly what it reads: the text the adapter produced, and the provider,
    /// which stamps each finding with where it came from. Every vendor-specific decision — which
    /// file, which envelope, which NDJSON event — has already happened in the adapter's own
    /// <c>ReadAnswer</c>, which is why nothing else from the invocation reaches here.
    /// </remarks>
    internal static NormalisedReview? ParseAnswer(string? raw, string provider)
    {
        if (raw is null)
        {
            return null;
        }

        // Gemini answers through its envelope and its habits; codex's -o file is schema-bound
        // already, but the same balanced extraction costs nothing and forgives a stray banner.
        return GeminiPayload.Extract(raw) is ExtractOutcome.Payload payload
               && ReviewParser.Parse(payload.Json, provider) is ParseOutcome.Success success
            ? success.Review
            : null;
    }
}

/// <summary>
/// What ONE launch produced, before anyone reads it as findings.
/// </summary>
/// <param name="Terminal">
/// The outcome when the launch itself decided the answer — <c>NotStarted</c>, <c>TimedOut</c>,
/// <c>RateLimited</c>, <c>NonZeroExit</c> — and null when the process ran and exited zero.
/// </param>
/// <param name="Answer">
/// The vendor's own answer, as ITS adapter extracts it, unparsed. Null when there was nothing where
/// this vendor puts one; that is a fact about the run, not a verdict on it.
/// </param>
/// <param name="Usage">What the run consumed, from the vendor's own reporting. Never null: an
/// adapter that cannot read its own numbers answers <see cref="Usage.None"/>.</param>
/// <param name="Evidence">
/// The answer, or the process transcript when the envelope came back empty — the diagnosis, for the
/// one failure whose raw text is the whole story.
/// </param>
/// <param name="Process">
/// What the process itself said and how it ended — null only when it never started. Carried for the
/// caller whose answer is not the whole story: a consultation reads the vendor's conversation id off
/// this stream, and reads it on a TIMED-OUT run too, because the vendor may have accepted the turn
/// before the kill and that handle is the only way not to pay for it twice.
/// </param>
public sealed record ReviewerLaunch(
    ReviewerOutcome? Terminal,
    string? Answer,
    Usage Usage,
    string Evidence,
    ProcessResult? Process = null,
    string NotApplied = "")
{
    /// <summary>What the adapter's launch control warned about this launch (<see cref="LaunchTurn.Warning"/>) — or empty.</summary>
    public string Warning { get; init; } = string.Empty;

    /// <summary>The CLI release the adapter's launch control asked for this launch (<see cref="LaunchTurn.CliVersion"/>) — or empty.</summary>
    public string CliVersion { get; init; } = string.Empty;
}
