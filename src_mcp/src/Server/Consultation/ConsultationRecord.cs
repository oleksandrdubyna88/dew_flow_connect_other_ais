using System.Text.Json.Serialization;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Findings;

namespace CoaiMcp.Server;

/// <summary>The states a consultation moves through. Strings on disk, a closed set here.</summary>
public static class ConsultationStatuses
{
    /// <summary>A turn is running — written BEFORE the launch, with the pid that owns it.</summary>
    public const string Asking = "asking";

    /// <summary>Answered; a follow-up may come.</summary>
    public const string Open = "open";

    /// <summary>The process ended without an answer but the vendor's handle is known: resumable, and the turn is not counted.</summary>
    public const string Interrupted = "interrupted";

    /// <summary>Over — the budget was spent, or it sat idle past its budget. <see cref="ConsultationRecord.Reason"/> says which.</summary>
    public const string Closed = "closed";

    /// <summary>Over, because something went wrong. <see cref="ConsultationRecord.Reason"/> says what.</summary>
    public const string Failed = "failed";
}

/// <summary>How a consultation's vendor remembers it, as the record spells it.</summary>
public static class ConsultationMemories
{
    /// <summary>The vendor holds the conversation; we send a handle.</summary>
    public const string VendorRemembers = "vendorRemembers";

    /// <summary>The vendor holds nothing; we carry the transcript into every turn.</summary>
    public const string WeRemember = "weRemember";
}

/// <summary>One answered turn, as the record keeps it.</summary>
/// <param name="FollowedUp">
/// The answer came from a SECOND launch that continued the first in the same conversation, because the
/// first answered nothing past a denied permission (<c>ConsultantTurn</c>, PLAN_the_consultant_works_on_every_vendor.md
/// E1.3). Trailing and defaulted, so every record already on disk reads as the one launch it was.
/// </param>
/// <param name="Confinement">
/// What the turn was SENT — see <see cref="ConsultationRecord.Confinement"/>. Trailing and defaulted like
/// <paramref name="FollowedUp"/>: a turn an older build wrote reads as <c>""</c>, because the source-generated
/// deserializer passes a missing constructor parameter its DECLARED default — measured by
/// <c>ConsultOnClaudeScenarioTests.ARecordWrittenBeforeTheConfinementWasRecorded_ReadsAsEmpty_NotNull</c>, which stayed
/// green with an accessor-level <c>?? ""</c> removed here, and went null when the record's own property lost its.
/// </param>
public sealed record ConsultationTurn(
    string Utc,
    string Problem,
    string Advice,
    double Seconds,
    long TokensIn,
    long TokensOut,
    double? CostUsd,
    bool FollowedUp = false,
    string Confinement = "");

/// <summary>
/// One consultation, as it sits in <c>&lt;dataDir&gt;/consultations/&lt;id&gt;.json</c>.
/// </summary>
/// <remarks>
/// <para>Its own entity, keyed by the CALLER and referencing a review session only when one exists:
/// the moments an agent is stuck are the moments the round machine refuses a round, and half the
/// triggers happen before <c>open</c>.</para>
/// <para>The cap, the vendor and the model are FROZEN here at creation, so a panel edit
/// mid-consultation neither strands an open one nor extends it — the <c>memoryOf</c> lesson from the
/// extension's chat. Every collection normalises null in its accessor because the source-generated
/// deserializer skips initialisers for absent members (the <c>PersistedSession</c> precedent).</para>
/// </remarks>
public sealed record ConsultationRecord(
    string Id,
    string Caller,
    string CallerKind,
    string SessionId,
    string RepoPath,
    string Branch,
    string HeadSha,
    string Vendor,
    string Model,
    string Runtime,
    string Memory,
    int MaxTurns,
    string StartedUtc)
{
    /// <summary>The vendor's own conversation id — guarded before it is ever reused.</summary>
    public string Handle { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>
    /// EVERY member below normalises null in its accessor, and the reason is the same for all of
    /// them: the source-generated deserializer skips property INITIALISERS for members the JSON does
    /// not carry, so <c>= string.Empty</c> protects a record built in code and nothing read back
    /// from a file. A record written before a field existed therefore came back with that field
    /// null, and the first <c>.Length</c> anywhere downstream threw.
    /// </summary>
    /// <remarks>
    /// It was only the collections, on the <c>PersistedSession</c> precedent, and the strings had
    /// the identical hole for as long. Found by the <c>--close-consult</c> scenario on issue #309:
    /// a real process crashed with a <c>NullReferenceException</c> reading a record whose
    /// <c>reason</c> the file simply did not have — which is every record older than that change.
    /// </remarks>
    public IReadOnlyList<ConsultationTurn> Turns { get => field ?? []; init; } = [];

    public string Status { get => field ?? string.Empty; init; } = ConsultationStatuses.Asking;

    public string UpdatedUtc { get => field ?? string.Empty; init; } = string.Empty;

    public string EndedUtc { get => field ?? string.Empty; init; } = string.Empty;

    public string Reason { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>
    /// How it ENDED, as against why it stopped — <c>solved</c>, <c>not_solved</c>, <c>abandoned</c>,
    /// or the server's own <c>lapsed</c>. Empty on every record written before this field existed.
    /// </summary>
    /// <remarks>
    /// <para>Beside <see cref="Reason"/> rather than inside it, because they answer two different
    /// questions and one string was answering only the first: a consultation that did its job and one
    /// that was useless both ended <c>closed</c> with a sentence about the budget. (issue #309.)</para>
    /// <para><b>Empty is not a verdict</b>, and neither is <c>lapsed</c>. Nothing anywhere may read
    /// either as one — a reader that inferred <c>solved</c> from a closed status would turn a spent
    /// budget into a success.</para>
    /// </remarks>
    public string Outcome { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>
    /// Who supplied the outcome: <c>caller</c>, <c>person</c>, or the server's own <c>server</c>.
    /// Empty wherever <see cref="Outcome"/> is.
    /// </summary>
    /// <remarks>
    /// A FIELD rather than a sentence inside <see cref="Reason"/>, because <c>Reason</c> is kept as
    /// the server left it — a consultation the sweep closed keeps its line about the budget — so a
    /// verdict recorded afterwards left no trace of which door it came through. "The AI said it
    /// worked" and "a person marked it closed" are different claims and an export that had to parse
    /// prose to tell them apart would be reading English for a fact. (codex Architecture, the code
    /// round; the plan promised this distinction and the first build did not keep it.)
    /// </remarks>
    public string OutcomeBy { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>
    /// What the consultation was FOR — <c>stuck</c>, <c>cadence</c> or <c>risk</c>
    /// (<c>research/PLAN_consult_on_a_cadence.md</c>). Absent on every record older than the cadence, which
    /// reads as <c>stuck</c> because that is what every one of them was.
    /// </summary>
    public string Kind { get => field ?? Core.Consultation.ConsultKinds.Stuck; init; } = Core.Consultation.ConsultKinds.Stuck;

    /// <summary>The plan a cadence or risk consultation was about, repo-relative; empty for a stuck one.</summary>
    public string Plan { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>The group (<c>4-6</c>) or risk item (<c>7/7.2</c>) it covers, canonical; empty for a stuck one.</summary>
    public string Epics { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>
    /// The repository it was about — the git common dir, so a consultation taken in one worktree counts for
    /// the plan in another. Empty on records older than the cadence, which therefore never cover a group.
    /// </summary>
    public string RepoId { get => field ?? string.Empty; init; } = string.Empty;

    public int RunnerPid { get; init; }

    /// <summary>The filesystem invariant's sentence, when it fired. The one field a person must read.</summary>
    public string Alert { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>
    /// The carry budget the adapter declared when this consultation opened. Zero for a vendor that
    /// keeps the conversation itself, and for a record written before this field existed.
    /// </summary>
    public int CarryBudget { get; init; }

    /// <summary>
    /// The consultant's row's system prompt as it stood when this consultation opened (research/PLAN_one_model_catalog.md, C2)
    /// — frozen with the model and the runtime, because a turn's prompt is composed from the record alone: an edit made
    /// today never reaches a conversation opened yesterday. Empty for none, and for a record written before it existed.
    /// </summary>
    public string RowInstruction { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>
    /// What the LAST failed turn was, as a word of <c>shared/consult-failure-kinds.json</c> — empty while
    /// nothing has failed, and cleared by a turn that answers.
    /// </summary>
    /// <remarks>
    /// <para>Three fields beside <see cref="Reason"/> rather than inside it, because a sentence is for a
    /// person and these are for a reader: the panel draws the kind's label and the cure, an export counts
    /// kinds, and none of them should parse English to do it (PLAN_the_consultant_works_on_every_vendor.md,
    /// E2.4). An old record has none of them and reads empty — the null-normalising accessor, like every
    /// field here.</para>
    /// </remarks>
    public string FailureKind { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>What a person can do about <see cref="FailureKind"/>; empty with it.</summary>
    public string FailureCure { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>Where the failed turn's transcript was kept, or empty when nothing was.</summary>
    public string Evidence { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>
    /// The confinement the LATEST launched turn was SENT — <c>restricted</c> or <c>no-restricted</c> for claude
    /// (<c>ConsultantPreparation.Ready.Confinement</c>), empty for a vendor whose confinement does not depend on its
    /// CLI, and empty again for a turn refused before it launched.
    /// </summary>
    /// <remarks>
    /// Set on the <c>asking</c> record, so every ending derived from it — answered, failed, interrupted — carries it.
    /// What a reader such as <c>--consultants</c> shows is then what RAN, not what a fresh probe says now: a claude
    /// upgraded since the turn would otherwise be credited with a confinement that turn never had (epic 3's review).
    /// </remarks>
    public string Confinement { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>
    /// What this conversation has been BILLED so far, over every turn that billed — answered, failed and
    /// interrupted alike.
    /// </summary>
    /// <remarks>
    /// <para>A vendor that reports the conversation's RUNNING total (antigravity) is billed each turn's
    /// share: what it said, less what was already billed. That used to be "less the answered turns' own
    /// figures", and a turn that broke after the vendor billed it — interrupted, keeping the conversation
    /// but adding no turn — was billed again inside the next answer's share (epic 1's review). The running
    /// total lives here and every billing outcome advances it.</para>
    /// <para><b>A record written before this field</b> reads as the sum of its answered turns, which is
    /// exactly what the old rule had billed — zero for a consultation with none, and never less than what
    /// was billed, so an open consultation is not charged its whole history again on its next turn.</para>
    /// </remarks>
    public ConsultationBilled Billed
    {
        get => field ?? BilledBy(Turns);
        init;
    }

    /// <summary>What the old rule had billed: the answered turns' own figures.</summary>
    private static ConsultationBilled BilledBy(IReadOnlyList<ConsultationTurn> turns) =>
        new(turns.Sum(t => t.TokensIn), turns.Sum(t => t.TokensOut), turns.Sum(t => t.CostUsd ?? 0));

    [JsonIgnore]
    public TurnBudget Budget => new(MaxTurns, Turns.Count);

    /// <summary>
    /// Whether WE carry the conversation for this consultation — read from the record, never from
    /// the runtime as it is configured now.
    /// </summary>
    /// <remarks>
    /// The record says the memory mode is frozen, so a follow-up must honour it: a server upgrade that
    /// changed an adapter's mode would otherwise make an open consultation silently stop carrying its
    /// transcript, or start carrying one to a vendor that already has it. (codex, second code round.)
    /// </remarks>
    [JsonIgnore]
    public bool WeCarryTheConversation => Memory == ConsultationMemories.WeRemember;

    [JsonIgnore]
    public bool IsOver => Status is ConsultationStatuses.Closed or ConsultationStatuses.Failed;
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ConsultationRecord))]
internal sealed partial class ConsultationJsonContext : JsonSerializerContext;
