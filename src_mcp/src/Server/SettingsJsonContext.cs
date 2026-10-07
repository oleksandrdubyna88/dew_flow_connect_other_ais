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
    string? RemoteVendor = null,
    /// <summary>For an `api` row: the dialect its request is spelled in. Absent = the generic `openai`.</summary>
    string? Dialect = null,
    /// <summary>Whether this vendor reviews FEATURES. Absent is NO — see <c>ProviderSettings.Feature</c>.</summary>
    bool? Feature = null,
    /// <summary>
    /// For an `api` row: the NAME of the vault key it uses, when that is not the row's id — a second
    /// model on one key (S3.6). Absent is the row's own id, as it has always been. A name, never a value.
    /// </summary>
    string? Key = null,
    /// <summary>For an `api` row: what it charges per million tokens (S3.7). Absent is no price.</summary>
    PriceDto? Price = null,
    /// <summary>For an `api` row: the reasoning effort a person set, in the vendor's spelling. Absent is the module's default.</summary>
    string? Effort = null,
    /// <summary>For an `api` row: the thinking switch a person set. Absent is the module's default (on).</summary>
    bool? Thinking = null,
    /// <summary>For an `api` row: the whole-review limit a person set, in minutes. Absent is the module's default.</summary>
    int? ReviewMinutes = null,
    /// <summary>
    /// The person's own instruction for this row (PLAN_one_model_catalog.md E2.2) — delivered inside the prompt body after
    /// the product's reviewer instruction, never on a command line. Absent is none.
    /// </summary>
    string? SystemPrompt = null,
    /// <summary>A CLI row's own reviewer timeout, in whole minutes (PLAN_one_model_catalog.md E2.2). Absent is the round's.</summary>
    int? TimeoutMinutes = null);

/// <summary>
/// One vendor row's price on the wire — dollars per million tokens (PLAN_feature_review.md S3.7).
/// </summary>
/// <remarks>
/// Every field NULLABLE, for the doctrine's reason (<see cref="ConsultantDto"/>): an omitted field arrives
/// null whatever a declaration says. <c>PanelSettings.PriceOf</c> is the one place a null, a negative or a
/// non-finite number becomes "no rate". The tier is optional: <c>TierFrom</c> is the prompt-token count
/// from which the tier's rates apply (xAI: 200 000), absent for a vendor with one rate.
/// </remarks>
internal sealed record PriceDto(
    double? In = null,
    double? Cached = null,
    double? Out = null,
    long? TierFrom = null,
    double? TierIn = null,
    double? TierCached = null,
    double? TierOut = null);

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
    string? ExecutablePath = null,
    /// <summary>
    /// The consultant's WHOLE catalog row, as the extension writes a reviewer's (todo/PLAN_one_model_catalog.md, C2) —
    /// written only to a binary that lists <c>consultantRow</c>. Absent is the five fields above and nothing more.
    /// </summary>
    System.Text.Json.JsonElement? Row = null);

/// <summary>One caller kind's row of <c>COAI_COMMAND_MODELS</c> (issue #117).</summary>
/// <remarks>Nullable for the reason <see cref="ConsultantDto"/> gives: an omitted field arrives null.</remarks>
internal sealed record CommandModelDto(string? Strongest = null, string? Implementation = null);

/// <summary>One row of <c>COAI_COMMANDS</c> — a command a person added (issue #467).</summary>
/// <remarks>Nullable for the reason <see cref="ConsultantDto"/> gives: an omitted field arrives null.</remarks>
internal sealed record CustomCommandDto(string? Id = null, string? Title = null, bool? Enabled = null, string? Stage = null);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Dictionary<string, CommandModelDto?>), TypeInfoPropertyName = "DictionaryStringCommandModelDto")]
[JsonSerializable(typeof(List<CustomCommandDto?>), TypeInfoPropertyName = "ListCustomCommandDto")]
[JsonSerializable(typeof(List<VendorDto>))]
[JsonSerializable(typeof(Dictionary<string, ConsultantDto>), TypeInfoPropertyName = "DictionaryStringConsultantDto")]
[JsonSerializable(typeof(List<CoaiMcp.Core.Rounds.RoleEntry>), TypeInfoPropertyName = "ListRoleEntry")]
[JsonSerializable(typeof(Dictionary<string, List<string>>), TypeInfoPropertyName = "DictionaryStringListString")]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
