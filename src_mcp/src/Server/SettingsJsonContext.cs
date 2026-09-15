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
/// <param name="Document">
/// Whether this vendor reviews DOCUMENTS — and absent stays ABSENT, unlike its two neighbours.
/// <para>The other two fold to <c>true</c> here in the parser, because "yes" is what an older
/// configuration meant by saying nothing. This one cannot: for a vendor on this machine absent means
/// the plan tick, and for a Team server it means no — a document must not cross the network because
/// somebody once ticked a box about plans. Folding here would throw away the difference before
/// <c>ProviderSettings.Serves</c> could read it.</para>
/// </param>
internal sealed record VendorDto(
    string? Id,
    string? Runtime,
    string? Model,
    string? BaseUrl,
    string? ExecutablePath = null,
    bool? Plan = null,
    bool? Code = null,
    bool? Document = null,
    /// <summary>For a `remote` row: the vendor id the TEAM SERVER knows, which is not this row's id.</summary>
    string? RemoteVendor = null);

/// <summary>
/// One entry of `COAI_CONSULTANTS`: what consults for one caller kind — a DEFINITION, or a legacy
/// reference to a reviewer row by id.
/// </summary>
/// <param name="Vendor">The id: what names the vault entry, the usage ledger and every refusal.</param>
/// <param name="Model">Empty means the runtime's own default — for a legacy reference, the reviewer row's model.</param>
/// <param name="Runtime">
/// Which CLI answers. Present makes the entry a definition; ABSENT makes it a legacy reference, which
/// <see cref="ConsultantResolver"/> resolves through the reviewer rows exactly as the server always did.
/// </param>
/// <param name="BaseUrl">For a vendor riding the codex CLI. Empty = the CLI's own endpoint.</param>
/// <param name="ExecutablePath">Where the CLI is. Empty = look it up on PATH.</param>
/// <remarks>
/// <para>Three fields more since 2026-09-15 (<c>PLAN_the_consultant_has_its_own_vendors</c>, story B3),
/// and every one of them NULLABLE — by the family's doctrine, not by taste. A deserializer does not run
/// initializers, so a field the client omitted arrives as null whatever the declaration says; this
/// family paid for that twice on one day, in two repositories, each found by an <c>.http</c> suite and
/// invisible to every green unit test because every fixture sent the field. The wire is exactly where
/// absence is legitimate here: a settings file written before these fields existed carries none of
/// them, and that file must keep working with no rewrite. <c>ConsultantRouting.Merge</c> is the one
/// place each null becomes an empty string, so nothing past it ever meets one.</para>
/// </remarks>
internal sealed record ConsultantDto(
    string? Vendor,
    string? Model = null,
    string? Runtime = null,
    string? BaseUrl = null,
    string? ExecutablePath = null);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<VendorDto>))]
[JsonSerializable(typeof(Dictionary<string, ConsultantDto>), TypeInfoPropertyName = "DictionaryStringConsultantDto")]
[JsonSerializable(typeof(List<CoaiMcp.Core.Rounds.RoleEntry>), TypeInfoPropertyName = "ListRoleEntry")]
[JsonSerializable(typeof(Dictionary<string, List<string>>), TypeInfoPropertyName = "DictionaryStringListString")]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
