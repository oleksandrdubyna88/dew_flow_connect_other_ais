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
public sealed partial class ServerJsonContext : JsonSerializerContext;
