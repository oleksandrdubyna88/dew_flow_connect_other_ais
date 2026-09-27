namespace CoaiMcp.Core.Api;

/// <summary>
/// The generic hosted vendor: any OpenAI-compatible endpoint nobody has calibrated, spoken to from a
/// measured row and nothing else — no capabilities declared, the endpoint's own defaults.
/// </summary>
/// <remarks>
/// <para>The <c>openai</c> row by default (<see cref="Generic"/>): no sampling fields, the schema demanded,
/// <c>max_completion_tokens</c>, <c>none</c> → no effort field. Also what a row of the dialect table that
/// belongs to no calibrated family runs on — <c>dashscope</c> with a model outside the three Alibaba
/// families, for one — so a person can point a key at a new model before anyone has measured it.</para>
/// <para>It declares nothing it has not measured: every configured effort is sent verbatim (the vendor
/// answers 400 to one it does not take, and the shim reports that), thinking has no switch here, and the
/// defaults are the panel's own numbers.</para>
/// </remarks>
public sealed class OpenAiCompatibleVendor(ApiDialect dialect) : IApiVendor
{
    /// <summary>The generic <c>openai</c> row — what an api row that names no dialect speaks.</summary>
    public static readonly OpenAiCompatibleVendor Generic = new(ApiDialects.OpenAi);

    private readonly OpenAiCompatibleTransport _transport = new(dialect);

    public string Name => dialect.Name;

    /// <summary>Measured on no model: it declares nothing, so it can speak for any.</summary>
    public string MeasuredModel => string.Empty;

    public ApiDialect Dialect => dialect;

    public ApiCapabilities Capabilities => ApiCapabilities.Undeclared;

    public ApiDefaults Defaults => ApiDefaults.Uncalibrated;

    /// <summary>No price list of its own: the panel's lookup asks by model alone.</summary>
    public string PriceRoute => string.Empty;

    public string RequestBody(ApiTurn turn) => _transport.Body(turn);

    public IReadOnlyDictionary<string, string> Headers(string conversation) => _transport.Headers(conversation);

    public ChatAnswer ReadAnswer(string response) => _transport.Read(response);

    public ApiOutcome Classify(int status, string body) => _transport.Classify(status, body);

    /// <summary>Any effort goes to the wire; only a thinking switch is refused, because there is none to spell.</summary>
    public string Refusal(ApiRowSettings row) => VendorRefusal.Of(this, row);
}

/// <summary>The two refusal sentences every module composes, so a person reads one wording whatever the vendor.</summary>
public static class VendorRefusal
{
    /// <summary>Why the row's settings cannot be sent to this module, or empty.</summary>
    public static string Of(IApiVendor vendor, ApiRowSettings row) =>
        !vendor.Capabilities.Accepts(row.Effort) ? UnknownEffort(vendor, row.Effort)
        : row.Thinking == ThinkingSetting.Off && !vendor.Capabilities.ThinkingSwitchable ? NoSwitch(vendor)
        : string.Empty;

    private static string UnknownEffort(IApiVendor vendor, string effort) =>
        $"{vendor.Name} does not take reasoning effort '{effort.Trim()}' — it accepts {string.Join(", ", vendor.Capabilities.EffortLevels)}"
        + (vendor.Capabilities.ThinkingOffLevel.Length > 0 ? $" (and '{vendor.Capabilities.ThinkingOffLevel}' switches thinking off)" : string.Empty);

    /// <summary>
    /// A calibrated module says what its vendor documents; the generic module can only say that nothing here
    /// spells a switch for this row — the vendor may well document one for the model, and the road is a module
    /// measured on it.
    /// </summary>
    private static string NoSwitch(IApiVendor vendor) =>
        vendor.Capabilities.EffortLevels.Count == 0
            ? $"no calibrated module spells a thinking switch on the '{vendor.Name}' row — run this model with thinking on, or name a module measured on it in the row's dialect field"
            : $"{vendor.Name} has no thinking switch — the vendor documents no way to turn reasoning off for this family";
}
