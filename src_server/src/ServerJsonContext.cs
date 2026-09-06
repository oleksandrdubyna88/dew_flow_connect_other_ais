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
public sealed partial class ServerJsonContext : JsonSerializerContext;
