using System.Text.Json.Serialization;
using CoaiMcp.Core.Consultation;

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
public sealed record ConsultationTurn(
    string Utc,
    string Problem,
    string Advice,
    double Seconds,
    long TokensIn,
    long TokensOut,
    double? CostUsd);

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
    public string Handle { get; init; } = string.Empty;

    public IReadOnlyList<ConsultationTurn> Turns { get => field ?? []; init; } = [];

    public string Status { get; init; } = ConsultationStatuses.Asking;

    public string UpdatedUtc { get; init; } = string.Empty;

    public string EndedUtc { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

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
    public string Outcome { get; init; } = string.Empty;

    public int RunnerPid { get; init; }

    /// <summary>The filesystem invariant's sentence, when it fired. The one field a person must read.</summary>
    public string Alert { get; init; } = string.Empty;

    /// <summary>
    /// The carry budget the adapter declared when this consultation opened. Zero for a vendor that
    /// keeps the conversation itself, and for a record written before this field existed.
    /// </summary>
    public int CarryBudget { get; init; }

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
