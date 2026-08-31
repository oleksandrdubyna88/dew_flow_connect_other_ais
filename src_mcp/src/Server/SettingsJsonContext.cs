using System.Text.Json.Serialization;

namespace CoaiMcp.Server;

/// <summary>One entry of `COAI_VENDORS`, as the extension writes it.</summary>
internal sealed record VendorDto(string? Id, string? Runtime, string? Model, string? BaseUrl);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<VendorDto>))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
