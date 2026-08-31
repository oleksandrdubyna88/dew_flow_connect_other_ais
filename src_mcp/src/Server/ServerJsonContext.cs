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
    string VaultNote);

/// <summary>What `open` and `status` return.</summary>
public sealed record SessionAnswer(
    string SessionId,
    string Stage,
    int RoundsRunThisStage,
    bool AwaitingResolve,
    bool PlanProceeded,
    int Threshold,
    int MaxRounds,
    IReadOnlyList<RoundRecord> Rounds);

/// <summary>What a review tool returns: the verdict, the honest reviewer count, the findings.</summary>
public sealed record ReviewAnswer(
    string Verdict,
    string? EscalationStep,
    int GatingCount,
    int Threshold,
    string Reviewers,
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<Finding> Discounted,
    IReadOnlyList<string> RejectedEntries,
    string Instruction);

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
internal sealed partial class ServerJsonContext : JsonSerializerContext;
