using System.Text.Json.Serialization;
using CoaiMcp.Core.QuestionConsult;

namespace CoaiMcp.Server;

/// <summary>The states a question moves through. Strings on disk, a closed set here (PLAN_question_consultant.md, §4).</summary>
public static class QuestionConsultStatuses
{
    /// <summary>Rows are running — written BEFORE any launch, with the pid and a heartbeat.</summary>
    public const string Consulting = "consulting";

    /// <summary>Every launched row answered.</summary>
    public const string Answered = "answered";

    /// <summary>Some rows answered; the rest timed out, failed or were refused (A1).</summary>
    public const string Partial = "partial";

    /// <summary>No row answered — or the question was refused before a launch (quota, nobody to ask).</summary>
    public const string Failed = "failed";

    /// <summary>The server running it died with rows still consulting — the sweep's verdict (D14 d).</summary>
    public const string Interrupted = "interrupted";
}

/// <summary>What happened to the question in the end, as the record spells it.</summary>
public static class QuestionOutcomes
{
    public const string AnsweredByConsultants = "answered_by_consultants";

    /// <summary>S3: the person was asked after the consultants.</summary>
    public const string PersonAsked = "person_asked";

    /// <summary>S3: the person was asked at once, the consultants ran beside (D8).</summary>
    public const string ProductionRisk = "production_risk";

    public const string QuotaSpent = "quota_spent";

    public const string NoneAvailable = "none_available";
}

/// <summary>A row's state while it runs — the terminal words are <see cref="RowOutcomes"/>.</summary>
public static class QuestionRowStatuses
{
    public const string Consulting = "consulting";
}

/// <summary>One model row of a question, as the record keeps it.</summary>
/// <param name="Flag">D13's caveat on an admitted pair — <c>unconfined</c>, <c>default-deny</c>, or empty — carried beside every answer.</param>
public sealed record QuestionRowRecord(
    string RowId,
    string Vendor,
    string Model,
    string Runtime,
    string PromptId,
    string PromptTitle,
    string Capability,
    string Flag)
{
    /// <summary>EVERY member below normalises null in its accessor — see <see cref="QuestionConsultRecord"/>.</summary>
    public string Status { get => field ?? string.Empty; init; } = QuestionRowStatuses.Consulting;

    public string Reason { get => field ?? string.Empty; init; } = string.Empty;

    public double Seconds { get; init; }

    public long TokensIn { get; init; }

    public long TokensOut { get; init; }

    public double? CostUsd { get; init; }

    /// <summary>The advice, whole; empty until the row answers, and emptied again when a disk breach withholds it.</summary>
    public string Advice { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>What the row's turns served and refused, one line — the api row's source turns.</summary>
    public string Note { get => field ?? string.Empty; init; } = string.Empty;

    public string EndedUtc { get => field ?? string.Empty; init; } = string.Empty;

    [JsonIgnore]
    public bool Answered => Status == RowOutcomes.Answered;

    [JsonIgnore]
    public bool IsOver => Status != QuestionRowStatuses.Consulting;
}

/// <summary>
/// One question, as it sits in <c>&lt;dataDir&gt;/question-consults/&lt;id&gt;.json</c> (D5: its own store,
/// never a kind of <c>ConsultationRecord</c>).
/// </summary>
/// <remarks>
/// <para>Keyed by the CALLER like a consultation, referencing a review session only when one exists;
/// the vendor, model and prompt of every row are frozen on it, so a settings edit mid-question neither
/// strands a row nor changes what the log says ran.</para>
/// <para>EVERY string and list member normalises null in its accessor, for the reason
/// <c>ConsultationRecord</c> gives: the source-generated deserializer skips property INITIALISERS for
/// members the JSON does not carry, so a record written before a field existed came back with it null
/// and the first <c>.Length</c> downstream threw. The positional members are what every record has
/// always had; the rest arrived later or may be absent.</para>
/// </remarks>
public sealed record QuestionConsultRecord(
    string Id,
    string Caller,
    string CallerKind,
    string SessionId,
    string RepoPath,
    string Branch,
    string HeadSha,
    string Question,
    string StartedUtc)
{
    /// <summary>S3: the plan the question was asked under, as the phase store keys it. Empty until then.</summary>
    public string PlanKey { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>What the caller tried and the options — capped by the tool at 8 KB.</summary>
    public string Context { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>S3 (D8): the caller declared a production risk. Never read in S2.</summary>
    public bool ProductionRisk { get; init; }

    public string RiskReason { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>
    /// What was cut to fit the tool's limits, one sentence — empty when nothing was. Only the consultants BESIDE a
    /// person's card (D8) are ever handed a cut question: the person was asked the whole of it, and a refusal there
    /// would have meant no consultant ran at all.
    /// </summary>
    public string Truncated { get => field ?? string.Empty; init; } = string.Empty;

    public string Status { get => field ?? string.Empty; init; } = QuestionConsultStatuses.Consulting;

    public string Outcome { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>S3: the escalation the question became, when the person was asked.</summary>
    public string EscalationId { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>The server running the fan-out — one of the two facts the sweep reads (D14 d).</summary>
    public int RunnerPid { get; init; }

    /// <summary>Rewritten every 30 s while consulting — the other fact (D14 d).</summary>
    public string HeartbeatUtc { get => field ?? string.Empty; init; } = string.Empty;

    public string UpdatedUtc { get => field ?? string.Empty; init; } = string.Empty;

    public string EndedUtc { get => field ?? string.Empty; init; } = string.Empty;

    public IReadOnlyList<QuestionRowRecord> Rows { get => field ?? []; init; } = [];

    /// <summary>The filesystem invariant's sentence, when it fired over a disk row's roots. The one field a person must read.</summary>
    public string Alert { get => field ?? string.Empty; init; } = string.Empty;

    [JsonIgnore]
    public bool IsOver => Status is not QuestionConsultStatuses.Consulting;

    /// <summary>This record with one row replaced by id — rows are immutable, the list is rebuilt.</summary>
    public QuestionConsultRecord WithRow(QuestionRowRecord row) =>
        this with { Rows = [.. Rows.Select(r => r.RowId == row.RowId ? row : r)] };
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(QuestionConsultRecord))]
internal sealed partial class QuestionConsultJsonContext : JsonSerializerContext;
