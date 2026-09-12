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
/// <param name="Plan">Whether this vendor reviews PLANS. Absent means yes.</param>
/// <param name="Code">
/// Whether this vendor reviews CODE. Absent means yes, which is what keeps an existing
/// configuration reviewing exactly what it reviewed before an update.
/// </param>
internal sealed record VendorDto(
    string? Id,
    string? Runtime,
    string? Model,
    string? BaseUrl,
    string? ExecutablePath = null,
    bool? Plan = null,
    bool? Code = null,
    /// <summary>For a `remote` row: the vendor id the TEAM SERVER knows, which is not this row's id.</summary>
    string? RemoteVendor = null);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<VendorDto>))]
[JsonSerializable(typeof(List<CoaiMcp.Core.Rounds.RoleEntry>), TypeInfoPropertyName = "ListRoleEntry")]
[JsonSerializable(typeof(Dictionary<string, List<string>>), TypeInfoPropertyName = "DictionaryStringListString")]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
