using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.ServiceDefaults;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// What a recorded run WAS, as the ledger spells it.
/// </summary>
/// <remarks>
/// <para>The names live HERE, in the library both halves compile against, rather than only in the
/// server's own <c>JobKind</c> — which the ledger cannot see, because the dependency runs
/// server → mcp and not back. Two vocabularies for one field is how a writer and a reader come to
/// disagree about what a spending row means, and the reader's fallback ("anything I do not know is a
/// review") would hide the disagreement rather than report it. A test in <c>src_server</c> asserts
/// the server's enum spells exactly these. (codex, code round.)</para>
/// <para><b>An unknown value is still WRITTEN.</b> A ledger line is history: refusing one because a
/// newer producer used a word this build has not heard would lose a run that already happened and
/// already cost money, which is the one thing a spending record must never do.</para>
/// </remarks>
public static class UsageKinds
{
    public const string Review = "review";
    public const string Chat = "chat";

    /// <summary>
    /// The AGENT asking another vendor's model about the working tree, mid-task, on a consultant the
    /// operator configured. A third question beside the other two — "what did my agent spend getting
    /// unstuck" — and the one the phase-2 decision about an automatic consultation is made on, which
    /// is why it is not folded into <see cref="Chat"/>.
    /// </summary>
    public const string Consult = "consult";

    /// <summary>
    /// One row of the QUESTION consultant (PLAN_question_consultant.md, S2): the agent asking several
    /// models a question before it asks the person. A fourth kind rather than a <see cref="Consult"/>,
    /// because the operator's Logs tab asks "what did the question consultant cost" apart from "what
    /// did being stuck cost" — and the Team server never runs one (out of scope), so it is local-only.
    /// </summary>
    public const string Question = "question";

    /// <summary>
    /// The deploy's own check — one real review per vendor the release script sends after a swap (story 3.5 of
    /// todo/PLAN_every_round_is_counted.md). A Team-server kind: written by the server, never by this machine.
    /// </summary>
    public const string Canary = "canary";

    /// <summary>
    /// A MEASUREMENT a person ran from a terminal — <c>coai-mcp --probe-api --model</c>, whose request matrix makes
    /// ten real completions against a metered key (story 3.3 of todo/PLAN_every_round_is_counted.md). It wrote nothing
    /// before, which is how a probe's spend sat in the vendor's bill and in no record here; it is its own kind rather
    /// than a review, because a probe is not a round and must never be counted as one.
    /// </summary>
    public const string Probe = "probe";

    /// <summary>Every kind this build knows how to name. Order is the order a report reads best in.</summary>
    public static IReadOnlyList<string> Known { get; } = [Review, Chat, Consult, Question, Canary, Probe];

    /// <summary>
    /// Kinds the Team server never produces: a consultation, a question row and a probe run on this machine
    /// only. The server's wire vocabulary is therefore <see cref="Known"/> minus these, and the test
    /// that holds the two vocabularies level says so rather than asserting a false equality.
    /// </summary>
    public static IReadOnlyList<string> LocalOnly { get; } = [Consult, Question, Probe];

    /// <summary>Whether a non-empty kind is one this build knows. Empty is "not said", which is fine.</summary>
    public static bool IsKnown(string kind) =>
        kind.Length == 0 || Known.Contains(kind, StringComparer.Ordinal);
}

/// <summary>One reviewer run, as it will be counted forever.</summary>
/// <param name="Outcome">`ok` or the failure's own name — a round that cost money without
/// answering is exactly what a spending record must not hide.</param>
public sealed record UsageEntry(
    string Utc,
    string Provider,
    string Model,
    string Role,
    string Stage,
    double Seconds,
    long TokensIn,
    long TokensOut,
    double? CostUsd,
    string Outcome,

    /// <summary>Who spent it, on a Team server.</summary>
    /// <remarks>
    /// Empty for a local run, where there is exactly one person and asking would be theatre.
    /// Trailing and defaulted, so every existing construction still compiles and every line
    /// already on disk stays valid — an old line simply has no email, which is the truth about it.
    /// </remarks>
    string Email = "",

    /// <summary>What this run WAS: a review, or a conversation.</summary>
    /// <remarks>
    /// The owner's ruling, 2026-09-08: chat turns go into the spending record like review turns and
    /// are DISTINGUISHED from them, because "what did the gate cost me" and "what did asking cost me"
    /// are two questions and one total answers neither.
    /// <para>Trailing and defaulted, like the email above it and for the same reason: every line
    /// already on disk stays valid. An old line has no kind, and everything written before this
    /// existed was a review — there was nothing else to be — so an absent kind is READ as one rather
    /// than shown as unknown. That is the truth about those lines, not a convenience.</para>
    /// </remarks>
    string Kind = "",

    /// <summary>How many of <paramref name="TokensIn"/> the vendor served from its prompt cache — zero when it did not say.</summary>
    /// <remarks>
    /// One line per TURN of a feature reviewer's conversation (S3.2), and this is the number those
    /// lines exist to record: the follow-up turns resend the base prompt byte for byte so a vendor can
    /// cache it (D25), and whether one does is read from here. Trailing and defaulted, so an old line
    /// simply has none; the extension's reader coerces an absent number to zero.
    /// </remarks>
    long TokensCached = 0,

    /// <summary>Why a line that should have a cost has none — <c>no price set</c> — or empty.</summary>
    /// <remarks>
    /// A metered <c>api</c> run whose row carried no rate (S3.7): its money is UNKNOWN, and a null cost on
    /// its own reads exactly like a CLI that never reports money. Written as words because a person reads
    /// this file; never a zero, which would say the run was free. Trailing and defaulted like the rest.
    /// </remarks>
    string CostNote = "",

    /// <summary>How many of <paramref name="TokensOut"/> the vendor reported as reasoning — zero when it did not say.</summary>
    /// <remarks>
    /// For the record, not the bill (the bill is <paramref name="TokensOut"/>, which holds them): the number
    /// that says whether a reasoning model's minutes went into thinking. Measured 2026-09-26, grok-4.7 spent
    /// 27,728 reasoning tokens on a 1,557-token answer. Trailing and defaulted like the rest.
    /// </remarks>
    long TokensReasoning = 0,

    /// <summary>
    /// <c>usage not captured</c> when the launch ended before the vendor reported what it consumed — or empty.
    /// </summary>
    /// <remarks>
    /// A turn killed on its deadline or by a cancellation, or an <c>api</c> call whose connection dropped
    /// (<see cref="Usage.NotCaptured"/>): <paramref name="TokensIn"/> and <paramref name="TokensOut"/> then
    /// hold only what was counted before it — usually nothing — and this says that the rest is UNKNOWN,
    /// not zero. Written as words beside <paramref name="CostNote"/>'s, for the same reason: a person
    /// reads this file, and a bare zero says the run was free. Trailing and defaulted like the rest, so an
    /// old line simply has none.
    /// </remarks>
    string UsageNote = "",

    /// <summary>
    /// The id every launch of ONE reviewer turn shares — its first launch, a repair, a follow-up, a retry, an attempt
    /// the Team server rotated onto another account — or empty.
    /// </summary>
    /// <remarks>
    /// Story 3.3 of todo/PLAN_every_round_is_counted.md: one ledger line per paid launch, and this is what ties them
    /// back together, so retries, repairs and follow-ups are COUNTED from lines rather than inferred from a folded
    /// sum. Twelve hex characters of a fresh GUID on this machine (a turn lives inside one process, so it only has to
    /// be unique among the lines it sits beside); the job id on a Team server, where a job IS one turn. Empty on a
    /// line written before the field, and on a kind with no turns — a probe. Trailing and defaulted like the rest.
    /// </remarks>
    string Turn = "",

    /// <summary>What this launch WAS inside its turn — one of <see cref="LaunchAttempts"/>, or empty.</summary>
    /// <remarks>
    /// Empty on an old line, where a repair or a follow-up is folded into the one line its turn wrote, and on a kind
    /// that has no attempts (a consultation, a question row, a probe). Trailing and defaulted like the rest.
    /// </remarks>
    string Attempt = "",

    /// <summary>The runtime that ran the launch — <see cref="LaunchOptions.Cli"/> — or empty.</summary>
    /// <remarks>
    /// <para><b>The eight fields below are story 3.4 of todo/PLAN_every_round_is_counted.md</b>: each line says what its
    /// launch RAN WITH. A cross-vendor measurement must say which CLI ran each model and at which effort and tier, and
    /// the ledger could not — the plan's hand-built answer had to reconstruct that a codex row inherited
    /// <c>~/.codex/config.toml</c>'s <c>high</c> effort and, for a while, its <c>priority</c> tier.</para>
    /// <para>Empty on a line written before them, and on a path that labels nothing yet — a consultation, a question row,
    /// a probe, a chat turn: an empty field says "not recorded", never "none". Trailing and defaulted like the rest.</para>
    /// </remarks>
    string Cli = "",

    /// <summary>The first line of the CLI's own <c>--version</c>, where one was already asked — see <see cref="LaunchOptions.CliVersion"/>.</summary>
    string CliVersion = "",

    /// <summary>The effort the launch ran with: what was sent, else what the CLI's own config names — or empty.</summary>
    string Effort = "",

    /// <summary>Where <paramref name="Effort"/> came from — one of <see cref="OptionSources"/>, or empty for unknown.</summary>
    /// <remarks>
    /// <c>row</c> (sent from the row's settings), <c>server</c> (sent, the Team server's choice: a cap or its own default),
    /// <c>cli-config</c> (nothing was sent, and the CLI's own configuration names the value shown — read to label the line,
    /// never to change the launch), or empty (unknown: the vendor's own default). Whether the row asked for a value that
    /// did not reach the launch is <paramref name="EffortNotSent"/>'s, apart — the consultation of 2026-10-11.
    /// </remarks>
    string EffortFrom = "",

    /// <summary>The service tier the launch ran with, in codex's words (<c>default</c> is the standard tier, <c>fast</c>) — or empty.</summary>
    string Tier = "",

    /// <summary>Where <paramref name="Tier"/> came from — the same vocabulary as <paramref name="EffortFrom"/>.</summary>
    string TierFrom = "",

    /// <summary>The Team server's job id — empty on a local line. The same id the line's <paramref name="Turn"/> carries there.</summary>
    string Job = "",

    /// <summary>
    /// The Team server account slot the launch ran on, by its NAME from <c>vendors.json</c> — never the account's email,
    /// which nothing on this line may carry but <paramref name="Email"/>, the caller's. Empty on a local line.
    /// </summary>
    string Slot = "",

    /// <summary>
    /// <c>ignored</c> or <c>dropped</c> when the row (or the request) asked for an effort that did not reach the CLI — or
    /// empty. Apart from <paramref name="Effort"/>, which says what RAN, so an inert row setting never changes it.
    /// </summary>
    string EffortNotSent = "",

    /// <summary>The same as <paramref name="EffortNotSent"/>, for the tier.</summary>
    string TierNotSent = "",

    /// <summary>
    /// WHY a launch the caller's token killed was killed — one of <see cref="EndCauses"/> — or empty.
    /// </summary>
    /// <remarks>
    /// Story 3.2 of todo/PLAN_every_round_is_counted.md. Written by the Team server from the STORE's terminal record
    /// (the store fires the token at the transition and is the only thing that knows why), never from the runner's own
    /// recomputation; the line's <paramref name="Outcome"/> stays the failure's name (<c>Cancelled</c>), so a reader
    /// that groups by outcome is unchanged and one that asks "how many withdrawals against how many backstops" has a
    /// column to ask. Empty on a line older than the field, on every ending but a cancellation, and on a local line,
    /// where nothing above the executor records which token fired. Trailing and defaulted like the rest.
    /// </remarks>
    string EndCause = "")
{
    /// <summary>This line with the fields of what its launch ran with — the ONE place a <see cref="LaunchOptions"/> becomes columns.</summary>
    public UsageEntry RanWith(LaunchOptions ranWith) => this with
    {
        Cli = ranWith.Cli,
        CliVersion = ranWith.CliVersion,
        Effort = ranWith.Effort,
        EffortFrom = ranWith.EffortFrom,
        Tier = ranWith.Tier,
        TierFrom = ranWith.TierFrom,
        EffortNotSent = ranWith.EffortNotSent,
        TierNotSent = ranWith.TierNotSent,
    };
}

/// <summary>
/// The append-only record of what every reviewer has consumed.
/// </summary>
/// <remarks>
/// <para>Session files hold the CURRENT story of one repo+branch and are rewritten as rounds
/// advance; a spending history must not live there, because the question it answers — "what has
/// this cost me this month" — spans every session and must survive all of them being deleted.</para>
/// <para>JSON Lines, appended, never rewritten: an append cannot corrupt what is already there,
/// a torn last line costs one entry rather than the file, and reading it is a scan a panel can do
/// in a millisecond for a year of rounds.</para>
/// <para><b>Two servers share one data directory routinely</b>, so the append opens the file with
/// <c>FileShare.ReadWrite</c> and seeks to the end. An in-process lock alone was what the first
/// version had, and the round that reviewed this file caught it from both vendors:
/// <c>File.AppendAllText</c> takes a write lock the other process cannot pass, so the loser's line
/// would be swallowed by the catch — a silent gap in a spending record, which is the one place a
/// gap is worse than an error. Each line is written in ONE call and is far below the atomic-write
/// size, so interleaving cannot split a line.</para>
/// <para><b>That bargain lives in <see cref="JsonlLedger"/> now, and this is its first caller</b>
/// (story 1.4 of the server-notices plan, 2026-09-21). It was the only production append in the C#
/// half, and the notices writer needed every line of it; a second copy would have drifted, and the
/// thing that drifts is the <c>FileShare</c> flag nobody re-derives. Extracting it is also what
/// made "there is one append here" checkable: a test enumerates the call sites and refuses a
/// third. The extraction changed nothing about what this file receives — the same bytes, the same
/// flags — and added the torn-tail quarantine every ledger now gets.</para>
/// </remarks>
public sealed class UsageLedger(string dataDir)
{
    public string Path => System.IO.Path.Combine(dataDir, "usage.jsonl");

    /// <summary>
    /// Records one reviewer — one line per paid LAUNCH of every turn of its conversation, the last turn's from the
    /// outcome itself. Never throws: a spending record that can fail a review is worse than one with a gap in it.
    /// </summary>
    /// <remarks>
    /// <para>The earlier turns come off the outcome's base (<see cref="ReviewerOutcome.EarlierTurns"/>),
    /// whatever the outcome is: a reviewer whose turn 2 timed out, was cancelled or came back unreadable
    /// still cost what turn 1 cost, and a ledger that read only the terminal usage would file that turn
    /// as free — the very under-report this method was first fixed for, one turn over.</para>
    /// <para><b>One line per launch, since story 3.3 of todo/PLAN_every_round_is_counted.md.</b> A turn that took a
    /// repair, a follow-up or a step of the rate-limit ladder was ONE line, its launches summed into it, so a week's
    /// retries, repairs and follow-ups could not be counted. Each launch is its own line now, naming its attempt and
    /// sharing its turn's id; the lines add up to exactly what the folded line said, and a split that would not is
    /// written as that one line instead (<see cref="LaunchLines.Of"/>). A failure's words are its WHOLE reason
    /// (<see cref="ReviewerSummaryFactory.DescribeWhole"/>), never the summary's 160-character cut.</para>
    /// </remarks>
    public void Record(ReviewerInvocation invocation, ReviewerOutcome outcome, string model, string stage, TimeSpan elapsed)
    {
        var line = new LineOf(invocation, model, stage);
        foreach (var turn in outcome.EarlierTurns)
        {
            AppendTurn(line, turn.Launches, turn.Usage, turn.Elapsed, LaunchLines.Answered);
        }

        // The last turn's own usage, EVERY launch of it — an unparseable answer's, and the malformed
        // first attempt of a repair that then failed. Reading usage from `Ok` alone made every failed
        // reviewer look free, which is the opposite of what a spending record is for; reading it from a
        // switch here made a failed repair's first attempt free until 2026-09-26. One member answers
        // it now, on the outcome, for this line and for the round total alike — a billed non-zero exit included
        // (2026-09-26: a shim billed for a reasoning-only completion, refusing it at exit 70; `NonZeroExit`
        // carries its usage and answers `OwnUsage` with it).
        // The last turn's own seconds: the whole reviewer's less what the earlier turns took.
        var last = elapsed - TimeSpan.FromTicks(outcome.EarlierTurns.Sum(turn => turn.Elapsed.Ticks));

        AppendTurn(
            line, outcome.Launches, outcome.LastTurnUsage, last < TimeSpan.Zero ? TimeSpan.Zero : last, LaunchLines.OutcomeOf(outcome));
    }

    /// <summary>What every line of one reviewer shares: who ran, on which model, at which stage.</summary>
    private sealed record LineOf(ReviewerInvocation Invocation, string Model, string Stage);

    /// <summary>One turn, as its lines: one per launch when they add up to the turn, else the turn as one line.</summary>
    private void AppendTurn(LineOf line, IReadOnlyList<LaunchPart> launches, Usage usage, TimeSpan elapsed, string outcome)
    {
        var turn = LaunchLines.NewTurn();
        foreach (var launch in LaunchLines.Of(launches, usage, elapsed, outcome))
        {
            var entry = Entry(line.Invocation, line.Model, line.Stage, launch.Elapsed, launch.Usage, launch.Outcome);
            Append(entry with { Turn = turn, Attempt = launch.Attempt });
        }
    }

    private static UsageEntry Entry(
        ReviewerInvocation invocation, string model, string stage, TimeSpan elapsed, Usage usage, string outcome) =>
        new UsageEntry(
            DateTime.UtcNow.ToString("O"),
            invocation.Provider,
            model,
            invocation.Role,
            stage,
            Math.Round(elapsed.TotalSeconds, 1),
            usage.TokensIn,
            usage.TokensOut,
            usage.CostUsd,
            outcome,
            TokensCached: usage.TokensCached,
            CostNote: CostNoteOf(usage),
            TokensReasoning: usage.TokensReasoning,
            // Unknown, in words, when the launch ended before its vendor reported anything — never a bare zero.
            UsageNote: UsageNoteOf(usage)).RanWith(invocation.RanWith);

    /// <summary>Records one job a Team server ran on somebody's behalf.</summary>
    /// <remarks>
    /// The same file and the same shape as a local run — ONE ledger, not two — with the email filled
    /// in. By the time the server records a job it no longer holds a <c>ReviewerInvocation</c>: the
    /// job outlived it and the outcome has already been mapped to the wire vocabulary. So this takes
    /// what the server actually has. Widening the ledger was the alternative to a second JSONL writer
    /// in <c>src_server</c>, and a second writer is how two spending records come to disagree.
    /// </remarks>
    public void RecordJob(
        string email,
        string provider,
        string model,
        string role,
        string outcome,
        TimeSpan elapsed,
        long tokensIn,
        long tokensOut,
        double? costUsd = null,
        string kind = "",
        // The Team server's stage by default, because that was this method's only caller for a
        // week; a consultation on this machine names its own, so a local run is not filed under a
        // server that never saw it.
        string stage = "TeamServer") =>
        Append(new UsageEntry(
            DateTime.UtcNow.ToString("O"),
            provider,
            model,
            role,
            stage,
            Math.Round(elapsed.TotalSeconds, 1),
            tokensIn,
            tokensOut,
            costUsd,
            outcome,
            email,
            kind));

    /// <summary>
    /// Records one job from its whole <see cref="Usage"/> — the cached and reasoning tokens, a missing price and a usage
    /// the vendor never reported mapped exactly as a reviewer's line maps them (<see cref="Entry"/>).
    /// </summary>
    /// <remarks>
    /// The whole-branch review of PLAN_the_consultant_works_on_every_vendor.md (2026-10-03), finding C: a consultation
    /// and a consultant check wrote their rows through the bare-counts overload above, so a turn killed before its vendor
    /// reported anything was filed as a free 0/0, and the cache and reasoning counts never reached the ledger.
    /// <para><paramref name="turn"/> and <paramref name="attempt"/> are the Team server's, one line per launch of a job
    /// (story 3.3); every other caller of this overload records a kind that has no attempts and leaves them empty.</para>
    /// <para><paramref name="ranWith"/>, <paramref name="job"/> and <paramref name="slot"/> are the Team server's too
    /// (story 3.4): what the job's launch ran with, the job, and the account slot by its name. A consultation and a question
    /// row label nothing yet and leave them empty, which the line then says.</para>
    /// <para><paramref name="endCause"/> is the Team server's as well (story 3.2): why the store killed a cancelled job —
    /// one of <see cref="EndCauses"/> — read off the store's own terminal record; empty for every other ending.</para>
    /// <para>An overload rather than defaulted parameters for the Team server's fields, because C# cannot default a
    /// <see cref="LaunchOptions"/> to <see cref="LaunchOptions.None"/> and this repository's rule is that an absent value
    /// is an EMPTY value, never a null (the epic-3 code round).</para>
    /// </remarks>
    public void RecordJob(
        string email, string provider, string model, string role, string outcome, TimeSpan elapsed, Usage usage, string kind, string stage) =>
        RecordJob(email, provider, model, role, outcome, elapsed, usage, kind, stage,
            turn: string.Empty, attempt: string.Empty, ranWith: LaunchOptions.None, job: string.Empty, slot: string.Empty);

    /// <summary>One Team-server launch of a job: the overload above, with what the launch ran with and where.</summary>
    public void RecordJob(
        string email, string provider, string model, string role, string outcome, TimeSpan elapsed, Usage usage, string kind, string stage,
        string turn, string attempt, LaunchOptions ranWith, string job, string slot, string endCause = "") =>
        Append(new UsageEntry(
            DateTime.UtcNow.ToString("O"),
            provider,
            model,
            role,
            stage,
            Math.Round(elapsed.TotalSeconds, 1),
            usage.TokensIn,
            usage.TokensOut,
            usage.CostUsd,
            outcome,
            email,
            kind,
            TokensCached: usage.TokensCached,
            CostNote: CostNoteOf(usage),
            TokensReasoning: usage.TokensReasoning,
            UsageNote: UsageNoteOf(usage),
            Turn: turn,
            Attempt: attempt,
            Job: job,
            Slot: slot,
            EndCause: endCause).RanWith(ranWith));

    /// <summary><c>no price set</c> for a metered run whose row carried no rate — never a zero, which would say it was free.</summary>
    private static string CostNoteOf(Usage usage) => usage.CostUsd is null && usage.NoPriceSet ? CostText.NoPriceSet : string.Empty;

    /// <summary><c>usage not captured</c> when the launch ended before its vendor reported anything — never a bare zero.</summary>
    private static string UsageNoteOf(Usage usage) => usage.NotCaptured ? CostText.UsageNotCaptured : string.Empty;

    /// <summary>Never throws: a spending record that can fail a review is worse than one with a gap.</summary>
    /// <remarks>
    /// The answer is discarded on purpose: a missed line is a gap in a chart, and nothing here has a
    /// place to count one yet. The catch, the directory, the flags and the lock are
    /// <see cref="JsonlLedger.AppendLine(string, string)"/>'s.
    /// </remarks>
    private void Append(UsageEntry entry) =>
        _ = JsonlLedger.AppendLine(Path, JsonSerializer.Serialize(entry, LedgerJsonContext.Default.UsageEntry) + "\n");
}
