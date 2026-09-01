using System.Text.Json.Serialization;

namespace CoaiMcp.Server;

/// <summary>One entry of `COAI_VENDORS`, as the extension writes it.</summary>
/// <param name="ExecutablePath">
/// Where this vendor's CLI actually is, when PATH cannot be trusted to answer.
/// </param>
/// <remarks>
/// It was missing, and <c>COAI_EXE_&lt;VENDOR&gt;</c> was read only in the <c>COAI_PROVIDERS</c>
/// fallback branch — so the moment anybody opened the panel, which always writes
/// <c>COAI_VENDORS</c>, the only way to say WHERE a CLI lives stopped working. In WSL that is fatal:
/// <c>codex</c> resolves there to the Windows npm shim on the interop PATH, which runs Linux node
/// against a Windows install and dies on a missing native dependency, while the native Linux one
/// sits in <c>~/.npm-global/bin</c> with nothing able to point at it.
/// </remarks>
internal sealed record VendorDto(
    string? Id,
    string? Runtime,
    string? Model,
    string? BaseUrl,
    string? ExecutablePath = null);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<VendorDto>))]
[JsonSerializable(typeof(Dictionary<string, List<string>>), TypeInfoPropertyName = "DictionaryStringListString")]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
