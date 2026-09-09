using System.Text.Json.Serialization;

namespace CoaiServer;

public sealed record HealthDto(bool Ok, string Version);

/// <param name="MicrosoftScope">
/// What a client must ask Entra for. Published anonymously because the caller has no token yet, and
/// safe because a client id is public by construction — it is in every authorization URL.
/// </param>
/// <param name="Providers">The sign-ins this server actually has, so a client offers no other.</param>
public sealed record ClientConfigDto(string MicrosoftScope, IReadOnlyList<string> Providers);

public sealed record SessionDto(string Token, DateTimeOffset ExpiresUtc, string Email);

public sealed record WhoAmIDto(string Email, string Name, bool IsAdmin);

public sealed record ErrorDto(string Error);

/// <summary>What the catalog says about one vendor's CLI. Presence and version only.</summary>
/// <remarks>
/// Deliberately NOT an authentication verdict. The probe runs <c>--version</c> with the server's own
/// environment, which is not any slot's <c>HOME</c>, so it can only answer "is the CLI installed and
/// does it run". Whether an ACCOUNT is usable is a per-slot fact and is reported by the counts in
/// <see cref="SlotSummaryDto"/>. Conflating the two was the plan round's most-repeated finding: a
/// vendor showing healthy while every account was signed out is the one answer this endpoint must
/// never give.
/// </remarks>
public sealed record VendorHealthDto(bool CliFound, string Version, string Note);

/// <summary>How many accounts of this vendor are in each state, right now.</summary>
/// <param name="Ready">Signed in, not cooling down — one of these can take a review.</param>
/// <param name="CoolingDown">Rate-limited until a time the vendor named; they come back by themselves.</param>
/// <param name="NeedsSignIn">Signed out. These never come back on their own — a human runs `login`.</param>
public sealed record SlotSummaryDto(int Total, int Ready, int CoolingDown, int NeedsSignIn);

public sealed record CatalogVendorDto(
    string Id,
    string Runtime,
    IReadOnlyList<string> Models,
    VendorHealthDto Health,
    SlotSummaryDto Slots);

/// <param name="Error">
/// Empty when the vendors below are what <c>vendors.json</c> says. Otherwise the reason the file was
/// refused, and the vendors are the last good ones — see <see cref="VendorCatalog"/> for why the
/// disagreement is surfaced here rather than left in a log.
/// </param>
public sealed record CatalogDto(
    string ServerVersion,
    bool IsAdmin,
    IReadOnlyList<CatalogVendorDto> Vendors,
    string Error);

/// <summary>What a client asks this server to run.</summary>
/// <param name="TimeoutSeconds">
/// How long the VENDOR may take, clamped to 30..1800. It is not how long the job may wait for a free
/// account — that is the server's queue clock, and conflating the two was the plan round's blocking
/// finding: a single deadline stamped at submit burns down while the job queues.
/// </param>
/// <param name="Kind">
/// <c>review</c>, <c>chat</c>, or absent — and absent is NOT the same as <c>review</c>. It is a
/// client older than the field, and it is judged exactly as it was before the field existed. See
/// <see cref="JobKinds"/> for the table.
/// </param>
/// <param name="IdempotencyKey">
/// The caller's own name for this attempt. Repeating it returns the job the first one made instead of
/// making a second — see <see cref="Idempotency"/>. Optional: a client that sends none is accepting
/// that a retry may cost two slots, which is what every client did before this existed.
/// </param>
public sealed record ReviewRequestDto(
    string Vendor,
    string Model,
    string Prompt,
    string? Role = null,
    int TimeoutSeconds = 600,
    string? Kind = null,
    string? IdempotencyKey = null);

/// <param name="Position">Where it sits in its vendor's queue, 1-based; 0 once it is running.</param>
public sealed record ReviewAcceptedDto(string Id, int Position);

/// <param name="Answer">
/// The vendor's RAW text. Parsing, repair and de-duplication stay in the client, so the same parser
/// does not exist in two places and drift.
/// </param>
/// <param name="Failure">The wire name of the failure, or empty. <param name="Reason">its own words.</param></param>
public sealed record ReviewStatusDto(
    string Id,
    string Status,
    int Position,
    string Answer,
    double Seconds,
    long TokensIn,
    long TokensOut,
    string Failure,
    string Reason);

/// <summary>A ledger line as it sits on disk. Mirrors <c>UsageEntry</c> in Runners.</summary>
/// <remarks>
/// Read here rather than shared, because the ledger's own type lives with the WRITER and its shape is
/// that binary's business. Every field is nullable or defaulted so a line written by an older version
/// — one with no email — parses as valid rather than being reported as damage.
/// </remarks>
public sealed record UsageEntryDto(
    string? Utc = null,
    string? Provider = null,
    string? Model = null,
    string? Role = null,
    string? Stage = null,
    double Seconds = 0,
    long TokensIn = 0,
    long TokensOut = 0,
    double? CostUsd = null,
    string? Outcome = null,
    string? Email = null,
    string? Kind = null);

/// <param name="Scope">"me" or "company", so a client cannot mistake one answer for the other.</param>
/// <param name="People">Empty unless the scope is company.</param>
/// <param name="UnreadableLines">
/// Null unless the scope is company. A torn line is an operator's problem, and showing it on a
/// person's own page tells them their record is damaged about something they cannot fix.
/// </param>
public sealed record UsageDto(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string Scope,
    IReadOnlyList<VendorTotal> Vendors,
    IReadOnlyList<PersonTotal> People,
    int? UnreadableLines,
    /// <summary>The gate against asking, over the same lines and the same window.</summary>
    /// <remarks>
    /// Trailing and defaulted so a client older than the field deserialises the answer unchanged: the
    /// panel that shipped before this simply does not read the property, which is what it did when
    /// the property did not exist. Defaulted to <c>[]</c> and NOT nullable — doctrine §4 and §7, and
    /// the reason bites here: a consumer could not otherwise tell "this server sent no kinds" from
    /// "this window had none", and those are different facts. (codex and gemini, code round.)
    /// </remarks>
    IReadOnlyList<KindTotal> Kinds = null!)
{
    /// <summary>Never null, whatever a deserialiser did with the property.</summary>
    /// <remarks>
    /// A record's positional default cannot be <c>[]</c> and survive JSON deserialisation, which
    /// writes the property directly and will write <c>null</c> for an absent one. Normalising at the
    /// boundary is what the rule asks for, so nothing downstream has to ask.
    /// </remarks>
    public IReadOnlyList<KindTotal> Kinds { get; init; } = Kinds ?? [];
}

/// <summary>One account's persisted state, in its own directory.</summary>
/// <param name="CooldownUntilUtc">Null when it is not rate-limited.</param>
/// <param name="NeedsSignIn">Set when a CLI said so; cleared only by a successful `login`.</param>
public sealed record SlotStateDto(
    DateTimeOffset? CooldownUntilUtc,
    bool NeedsSignIn,
    string Note,
    DateTimeOffset LastUsedUtc,
    int ConsecutiveCooldowns);

/// <summary>
/// Every shape this server serialises, source-generated because reflection is switched off.
/// </summary>
/// <remarks>
/// <see cref="SessionRecord"/> is here for the same reason the wire shapes are: it is written to
/// and read from disk with the same serializer, and under Native AOT a type that is missing from
/// this list throws at the first attempt rather than at build time. Raised on this story's plan
/// round, before a session had been written.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(HealthDto))]
[JsonSerializable(typeof(ClientConfigDto))]
[JsonSerializable(typeof(SessionDto))]
[JsonSerializable(typeof(WhoAmIDto))]
[JsonSerializable(typeof(ErrorDto))]
[JsonSerializable(typeof(SessionRecord))]
[JsonSerializable(typeof(VendorConfig[]))]
[JsonSerializable(typeof(VendorHealthDto))]
[JsonSerializable(typeof(SlotSummaryDto))]
[JsonSerializable(typeof(CatalogVendorDto))]
[JsonSerializable(typeof(CatalogDto))]
[JsonSerializable(typeof(SlotStateDto))]
[JsonSerializable(typeof(ReviewRequestDto))]
[JsonSerializable(typeof(ReviewAcceptedDto))]
[JsonSerializable(typeof(ReviewStatusDto))]
[JsonSerializable(typeof(UsageEntryDto))]
[JsonSerializable(typeof(UsageDto))]
public sealed partial class ServerJsonContext : JsonSerializerContext;
