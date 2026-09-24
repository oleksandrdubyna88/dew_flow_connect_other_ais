using System.Text.Json.Serialization;
using CoaiMcp.Core.Findings;

namespace CoaiMcp.Server;

/// <summary>What `providers` reports for one vendor — configuration meets reality.</summary>
public sealed record ProviderStatus(
    string Provider,
    bool Enabled,
    bool CliFound,
    string Version,
    string Auth,
    string Note);

/// <summary>The providers answer, with when the vault was read (rotation lands on restart).</summary>
public sealed record ProvidersAnswer(
    IReadOnlyList<ProviderStatus> Providers,
    string VaultReadUtc,
    string VaultNote,
    /// <summary>
    /// Settings whose value this build does not understand, each as a sentence for a person.
    /// </summary>
    /// <remarks>
    /// It rides on the health probe because that is where somebody looks when the gate does not
    /// behave as configured — and the case it exists for is exactly that: a panel newer than this
    /// server writes a policy this server has never heard of, falls back correctly, and looks like a
    /// setting that was never applied.
    /// </remarks>
    IReadOnlyList<string> Unrecognised);

/// <summary>
/// A consultation this repository has open, as `status` reports it.
/// </summary>
/// <remarks>
/// The point of it is RESUMPTION. A conversation that was compacted, or an agent picking the work up
/// again, has lost the <c>consultationId</c> the first reply carried — and without it a follow-up
/// opens a second consultation, pays for the working tree again and asks a model that has already
/// answered. The id is the field that matters; the rest is what a caller needs to decide whether to
/// continue this one or leave it.
/// </remarks>
public sealed record OpenConsultation(
    string Id,
    string Vendor,
    string Model,
    string Status,
    string Branch,
    int TurnsUsed,
    int MaxTurns,
    string StartedUtc,
    string Alert);

/// <summary>What `open` and `status` return.</summary>
public sealed record SessionAnswer(
    string SessionId,
    string Stage,
    int RoundsRunThisStage,
    bool AwaitingResolve,
    bool PlanProceeded,
    int Threshold,
    int MaxRounds,
    IReadOnlyList<RoundRecord> Rounds)
{
    /// <summary>
    /// What the PERSON answered a <c>call_human</c> with, if they have: <c>proceed</c>, <c>fix</c>,
    /// or empty. Their own words come with it whether or not they pressed a button.
    /// </summary>
    /// <remarks>
    /// Here because a resumed conversation must be able to LEARN of the decision rather than be
    /// told about it. The notice used to be written by a round that then returned, so a person's
    /// answer landed in a file nothing read — they could decide, watch the card disappear, and have
    /// changed nothing.
    /// </remarks>
    public string HumanDecision { get; init; } = string.Empty;

    public string HumanAnswer { get; init; } = string.Empty;

    /// <summary>
    /// Consultations still open in this repository — none, almost always.
    /// </summary>
    /// <remarks>
    /// Defaulted, so every existing construction of this record is unchanged and a caller that does
    /// not know the field reads a session answer exactly as it always did. It is keyed by the
    /// REPOSITORY rather than by the session: a consultation belongs to whoever is stuck in a
    /// checkout, and half the moments that start one happen before a review session exists at all.
    /// </remarks>
    public IReadOnlyList<OpenConsultation> Consultations { get; init; } = [];

    /// <summary>
    /// The findings a round is waiting on a <c>resolve</c> for, in the order <c>resolve</c> indexes
    /// them — empty whenever nothing awaits a decision.
    /// </summary>
    /// <remarks>
    /// A lost reply used to be a dead end: the round had saved them as pending, the next round was
    /// refused until they were resolved, and <c>resolve</c> addresses them by POSITION — while this
    /// answer carried counts only, so a resumed caller decided blind. The read-back idea of the
    /// unmerged <c>coai-ar1</c> branch, without its locators (S3b of
    /// research/PLAN_a_failed_round_can_be_retried.md).
    /// </remarks>
    public IReadOnlyList<Finding> Pending { get; init; } = [];
}

/// <summary>What a review tool returns: the verdict, the honest reviewer count, the findings.</summary>
/// <param name="Cost">What the round consumed, as the vendors themselves reported it.</param>
public sealed record ReviewAnswer(
    string Verdict,
    string? EscalationStep,
    int GatingCount,
    int Threshold,
    string Reviewers,
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<Finding> Discounted,
    IReadOnlyList<string> RejectedEntries,
    string Instruction,
    RoundCost? Cost = null,
    /// <summary>
    /// What the operator's switches tell the caller to do, and the sentence that says they must be
    /// followed. Empty when no switch is on, which is every release before this one.
    /// </summary>
    IReadOnlyList<string>? Commands = null,
    string? CommandsPreamble = null,
    /// <summary>
    /// Each reviewer's prose about the whole artefact, when its prompt asked for one — the SUMMARY.
    /// </summary>
    /// <remarks>
    /// Null on every code round, and on any document round whose reviewers wrote none. Never merged
    /// and never deduplicated: findings are merged because two vendors agreeing is stronger evidence
    /// of one defect, and three vendors' accounts of one document are three accounts — merging them
    /// destroys the only property that makes reading them worth the tokens.
    /// </remarks>
    IReadOnlyList<ReviewerNote>? Notes = null);

/// <summary>One reviewer's prose, with its name on it.</summary>
public sealed record ReviewerNote(string Provider, string Role, string Notes);

/// <summary>
/// One round's consumption. Tokens come from every vendor that reports them; <paramref name="Usd"/>
/// only from vendors that price their own runs (claude does), because a price table we maintained
/// ourselves would be wrong within a month and a wrong number is worse than an absent one.
/// </summary>
public sealed record RoundCost(long TokensIn, long TokensOut, double? Usd);

public sealed record ResolveAnswer(string Stage, bool AwaitingResolve, int RecordedDecisions, string Instruction);

/// <summary>A refusal or error, as data — the sentence is the interface.</summary>
public sealed record ErrorAnswer(string Error);

/// <summary>What a consultation looks like once it has been closed, and what it was closed as.</summary>
/// <remarks>
/// <c>Recorded</c> is false for a repeat that changed nothing, so a caller retrying after a lost
/// reply can tell "it went through this time" from "it had already gone through" — both successes,
/// and the difference matters to whoever is reading the log afterwards. (issue #309.)
/// </remarks>
public sealed record CloseAnswer(string Id, string Outcome, bool Recorded, string Said);

/// <summary>One round a batch findings read is asked about, as it arrives in the keys file.</summary>
/// <remarks>
/// <c>SessionId</c> rather than <c>Session</c>: the answer side (<c>LoggedRoundOfMany</c>), the query
/// side (<c>RoundKeyAsked</c>) and the extension's own <c>RoundKey</c> all call it that, and one seam
/// spelling its identifier two ways is a translation every future reader has to keep in their head.
/// Nothing in the field speaks this format yet — the mode is new on this branch — so the rename costs
/// no compatibility. (Code round, gemini.)
/// </remarks>
public sealed record RoundKeyDto(string SessionId = "", string Stage = "", int Number = -1);

/// <summary>
/// What `ask_human` returns: the person's decision, or why there is none yet.
/// </summary>
/// <param name="Answer">In the language the question was asked in.</param>
/// <param name="AnswerOriginal">The person's own words, verbatim — empty when nobody answered.</param>
public sealed record HumanAnswer(string Status, string Answer, string AnswerOriginal, string Instruction);

/// <summary>The wire shape of one decision passed to `resolve`.</summary>
public sealed record DecisionDto(int Finding, string Action, string Reason = "");

/// <summary>
/// What `consult` returns: the fenced advice and where the consultation stands. Ascetic on purpose —
/// tokens live in the ledger and the log, and a refusal is an <see cref="ErrorAnswer"/>.
/// </summary>
/// <param name="CostUsd">Only when the vendor priced its own run; null for codex and agy.</param>
public sealed record ConsultAnswer(string ConsultationId, int TurnIndex, int MaxTurns, string Advice, double? CostUsd);

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PersistedSession))]

[JsonSerializable(typeof(ProvidersAnswer))]
[JsonSerializable(typeof(SessionAnswer))]
[JsonSerializable(typeof(ReviewAnswer))]
[JsonSerializable(typeof(ResolveAnswer))]
[JsonSerializable(typeof(ErrorAnswer))]
[JsonSerializable(typeof(CloseAnswer))]
[JsonSerializable(typeof(HumanAnswer))]
[JsonSerializable(typeof(ConsultAnswer))]
[JsonSerializable(typeof(List<DecisionDto>))]
[JsonSerializable(typeof(List<string>), TypeInfoPropertyName = "ListString")]
[JsonSerializable(typeof(Store.LoggedLog))]
[JsonSerializable(typeof(Store.LoggedManyFindings))]
[JsonSerializable(typeof(List<RoundKeyDto>))]
[JsonSerializable(typeof(Store.LoggedRoundFindings))]
[JsonSerializable(typeof(Store.BugCorpus))]
[JsonSerializable(typeof(Normalising.NormalizeRequest))]
[JsonSerializable(typeof(Normalising.NormalizeResult))]
[JsonSerializable(typeof(Collecting.CollectSummary))]
[JsonSerializable(typeof(Collecting.PairsAnswer))]
[JsonSerializable(typeof(Core.Collecting.RealMethod))]
[JsonSerializable(typeof(Core.Collecting.FileAtRevision))]
[JsonSerializable(typeof(Core.Collecting.ReviewTree))]
[JsonSerializable(typeof(Core.Collecting.ReviewTrees))]
[JsonSerializable(typeof(Core.Collecting.ReviewTreeRemoval))]
[JsonSerializable(typeof(Collecting.KeepRequest))]
[JsonSerializable(typeof(Collecting.DecideRequest))]
[JsonSerializable(typeof(Collecting.KeepAnswer))]
// The wire pair lives in the shared core, so BOTH halves serialize the same declaration.
[JsonSerializable(typeof(Core.Collecting.UploadRequest))]
[JsonSerializable(typeof(Core.Collecting.UploadAnswer))]
[JsonSerializable(typeof(Collecting.UploadSummary))]
internal sealed partial class ServerJsonContext : JsonSerializerContext;
