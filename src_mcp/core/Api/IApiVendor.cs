namespace CoaiMcp.Core.Api;

/// <summary>
/// One hosted API vendor or model family, as a module: everything that differs per vendor, behind
/// one interface — the request it spells, the headers it needs, how its answer is read, what its
/// refusals mean, what it can be told (its capabilities) and what calibration settled for it (its
/// defaults).
/// </summary>
/// <remarks>
/// <para><b>One class per vendor, and no branch on a vendor name anywhere else</b> (the operator's
/// architecture rule, 2026-09-27). <c>--ask-api</c> asks the module; the roster asks the module; the
/// panel reads the module's capabilities. The only place a NAME becomes a TYPE is
/// <see cref="ApiVendors"/>. The three Alibaba families share one transport
/// (<see cref="DashScopeTransport"/>) by composition, never by a branch inside a class.</para>
/// <para><b>The measured rows stay data.</b> A module's <see cref="Dialect"/> is a row of
/// <c>shared/api-dialects.json</c>: what a vendor ANSWERED to the probe (PLAN_feature_review.md §4.10),
/// copied into a file the extension mirrors by name and a test on each side is held to. The module owns
/// the behaviour around the row — its capabilities, its defaults, the spelling of a thinking switch —
/// which a JSON file cannot hold.</para>
/// <para><b>Nothing measured changes.</b> <c>ApiVendorGoldensTests</c> pins the bytes each module sends
/// and the numbers it reads, recorded before the modules existed.</para>
/// </remarks>
public interface IApiVendor
{
    /// <summary>The registry key — <c>openai</c>, <c>xai</c>, <c>qwen</c>, <c>deepseek</c>, <c>glm</c>.</summary>
    string Name { get; }

    /// <summary>
    /// The ONE model this module's capabilities and defaults were measured and read for — <c>grok-4.7</c>,
    /// <c>qwen3.8-max</c>, <c>deepseek-v4-pro</c>, <c>glm-5.3</c> — or empty for the generic module, which
    /// declares nothing. A row naming this module with another model is set aside to the generic module
    /// over the same row (<see cref="ApiVendors.Resolve"/>): a sibling documents other levels and switches
    /// (glm-5.2 against glm-5.3), and a module must not speak for a model it was not measured on.
    /// </summary>
    string MeasuredModel { get; }

    /// <summary>The measured request row this module spells its requests from.</summary>
    ApiDialect Dialect { get; }

    /// <summary>What this vendor can be told: a thinking switch, the effort levels it accepts, their exclusions.</summary>
    ApiCapabilities Capabilities { get; }

    /// <summary>What calibration settled — the settings a row uses until a person changes them.</summary>
    ApiDefaults Defaults { get; }

    /// <summary>
    /// The price list a model of this vendor is looked up under — the panel's <c>PriceRoute</c>
    /// (<c>modelPrices.ts</c>): <c>xai</c>, <c>dashscope</c>, or empty for a vendor with no list of its own.
    /// </summary>
    string PriceRoute { get; }

    /// <summary>The completion request body for one turn, byte for byte what the wire gets.</summary>
    string RequestBody(ApiTurn turn);

    /// <summary>The request headers this vendor needs beyond <c>Authorization</c> — none, or a cache-routing key.</summary>
    IReadOnlyDictionary<string, string> Headers(string conversation);

    /// <summary>What a 200 said: content, usage (reasoning and cached tokens included), how it stopped.</summary>
    ChatAnswer ReadAnswer(string response);

    /// <summary>What an HTTP status and body mean — answered, a refused key, a rate limit, or a failure.</summary>
    ApiOutcome Classify(int status, string body);

    /// <summary>
    /// Why a row's per-model settings cannot be sent to this vendor — an effort it does not accept, a
    /// thinking switch it does not have — or empty when they can.
    /// </summary>
    string Refusal(ApiRowSettings row);
}
