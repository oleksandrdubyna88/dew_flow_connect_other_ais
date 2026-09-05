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
    string? CommandsPreamble = null);

/// <summary>
/// One round's consumption. Tokens come from every vendor that reports them; <paramref name="Usd"/>
/// only from vendors that price their own runs (claude does), because a price table we maintained
/// ourselves would be wrong within a month and a wrong number is worse than an absent one.
/// </summary>
public sealed record RoundCost(long TokensIn, long TokensOut, double? Usd);

public sealed record ResolveAnswer(string Stage, bool AwaitingResolve, int RecordedDecisions, string Instruction);

/// <summary>A refusal or error, as data — the sentence is the interface.</summary>
public sealed record ErrorAnswer(string Error);

/// <summary>
/// What `ask_human` returns: the person's decision, or why there is none yet.
/// </summary>
/// <param name="Answer">In the language the question was asked in.</param>
/// <param name="AnswerOriginal">The person's own words, verbatim — empty when nobody answered.</param>
public sealed record HumanAnswer(string Status, string Answer, string AnswerOriginal, string Instruction);

/// <summary>The wire shape of one decision passed to `resolve`.</summary>
public sealed record DecisionDto(int Finding, string Action, string Reason = "");

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
[JsonSerializable(typeof(HumanAnswer))]
[JsonSerializable(typeof(List<DecisionDto>))]
[JsonSerializable(typeof(Store.LoggedLog))]
internal sealed partial class ServerJsonContext : JsonSerializerContext;
