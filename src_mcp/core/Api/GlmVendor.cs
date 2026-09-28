namespace CoaiMcp.Core.Api;

/// <summary>
/// GLM — glm-5.3 on the Alibaba route (<see cref="DashScopeTransport"/>).
/// </summary>
/// <remarks>
/// <para><b>Capabilities</b>, from the vendor's reference (read 2026-09-27) for <c>glm-5.3</c> and this model
/// alone: it "supports only the thinking mode, which cannot be disabled" — no switch; <c>reasoning_effort</c>
/// accepts <c>low</c>, <c>high</c> and <c>max</c>; only <c>response_format: json_object</c> is supported,
/// which is what the row sends. Whether an effort and a <c>thinking_budget</c> may share a request is not
/// documented for this model, so no exclusion is declared (unverified, not contradicted). The same page
/// gives <c>glm-5.2</c> a thinking switch and a <c>medium</c> level — which is why the registry binds this
/// module to <c>glm-5.3</c> by exact model, never to the family. GLM through Z.ai's own API would be another
/// module on another price list (<c>zai</c>); it is not measured and so not here.</para>
/// <para><b>Defaults</b> (calibrated under the twenty-minute limit): <c>high</c> — the first level tried,
/// kept because it fit, not a verified vendor default — thinking on (there is no other mode); 6.8 minutes
/// on the mid-size task with both planted seeds found, 8.3 on the largest, at 1–17K reasoning tokens a
/// turn. The first trial's fear — every call thinking to a 16,384 ceiling — did not recur under the row's
/// 65,536 total. One of the two calibration reviews needed a schema repair: the first answer was JSON but
/// not the schema (bare strings in <c>findings</c>, an invented key) under <c>json_object</c>, and the
/// product's repair launch corrected it at the cost of one more billed call; whether that is the model's
/// habit or the prompt's doing is not established. Recorded in <c>RESULTS_feature_reviewer_models.md</c>.</para>
/// </remarks>
public sealed class GlmVendor : IApiVendor
{
    public static readonly GlmVendor Instance = new();

    private GlmVendor()
    {
    }

    public string Name => "glm";

    public string MeasuredModel => "glm-5.3";

    public ApiDialect Dialect => DashScopeTransport.Row;

    public ApiCapabilities Capabilities { get; } = new(
        ThinkingSwitchable: false,
        EffortLevels: ["low", "high", "max"],
        EffortExcludesThinkingBudget: false);

    public ApiDefaults Defaults { get; } = new(
        Effort: "high",
        ThinkingOn: true,
        MaxTokens: DashScopeTransport.MaxTokens,
        FollowUps: ApiDefaults.PanelFollowUps,
        ReviewMinutes: ApiDefaults.PanelReviewMinutes);

    public string PriceRoute => DashScopeTransport.RowName;

    /// <summary>Always the row as measured: there is no thinking switch to spell.</summary>
    public string RequestBody(ApiTurn turn) => DashScopeTransport.Shared.Body(turn);

    public IReadOnlyDictionary<string, string> Headers(string conversation) => DashScopeTransport.Shared.Headers(conversation);

    public ChatAnswer ReadAnswer(string response) => OpenAiCompatibleTransport.Read(response);

    public ApiOutcome Classify(int status, string body) => OpenAiCompatibleTransport.Classify(status, body);

    public string Refusal(ApiRowSettings row) => VendorRefusal.Of(this, row);
}
