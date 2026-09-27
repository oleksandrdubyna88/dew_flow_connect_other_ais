namespace CoaiMcp.Core.Api;

/// <summary>
/// Qwen — qwen3.8-max on the Alibaba route (<see cref="DashScopeTransport"/>).
/// </summary>
/// <remarks>
/// <para><b>Capabilities</b>, from the vendor's chat-completions reference and the calibration of
/// 2026-09-27: <c>reasoning_effort</c> <c>low</c> (a 4,096-token thinking budget), <c>medium</c> (16,384)
/// and <c>xhigh</c> (262,144 — the default, with <c>high</c> and <c>max</c> mapped onto it, so neither is a
/// level of its own here); <c>none</c> switches thinking off; an effort and a <c>thinking_budget</c> are
/// mutually exclusive in one request. Verified on the product's probe before it was used: one prompt gave
/// 655 / 418 / 1,987 / 2,048-cut output tokens at low / medium / high / no field.</para>
/// <para><b>Defaults</b> (calibrated under the twenty-minute limit): <c>medium</c>, thinking on. At the
/// default tier and at <c>high</c> the mid-size task never answered inside the limit; at <c>medium</c> it
/// answered in 5.4 minutes and the largest task in 7.7 and 11.5. Recorded in
/// <c>RESULTS_feature_reviewer_models.md</c>.</para>
/// </remarks>
public sealed class QwenVendor : IApiVendor
{
    public static readonly QwenVendor Instance = new();

    private const string Off = "none";

    private QwenVendor()
    {
    }

    public string Name => "qwen";

    public string MeasuredModel => "qwen3.8-max";

    public ApiDialect Dialect => DashScopeTransport.Row;

    public ApiCapabilities Capabilities { get; } = new(
        ThinkingSwitchable: true,
        EffortLevels: ["low", "medium", "xhigh"],
        EffortExcludesThinkingBudget: true,
        ThinkingOffLevel: Off);

    public ApiDefaults Defaults { get; } = new(
        Effort: "medium",
        ThinkingOn: true,
        MaxTokens: DashScopeTransport.MaxTokens,
        FollowUps: ApiDefaults.PanelFollowUps,
        ReviewMinutes: ApiDefaults.PanelReviewMinutes);

    public string PriceRoute => DashScopeTransport.RowName;

    /// <summary>Thinking on: the row as measured. Thinking off: the vendor's <c>none</c> level, sent verbatim (the row would otherwise omit it).</summary>
    public string RequestBody(ApiTurn turn) =>
        turn.ThinkingOn && !string.Equals(turn.Effort.Trim(), Off, StringComparison.OrdinalIgnoreCase)
            ? DashScopeTransport.Shared.Body(turn)
            : DashScopeTransport.Shared.Body(turn with { Effort = Off }, DashScopeTransport.WithEffortVerbatim());

    public IReadOnlyDictionary<string, string> Headers(string conversation) => DashScopeTransport.Shared.Headers(conversation);

    public ChatAnswer ReadAnswer(string response) => DashScopeTransport.Shared.Read(response);

    public ApiOutcome Classify(int status, string body) => DashScopeTransport.Shared.Classify(status, body);

    /// <summary>The levels, or <c>none</c> — which is the thinking switch spelled as an effort, and is accepted as one.</summary>
    public string Refusal(ApiRowSettings row) =>
        string.Equals(row.Effort.Trim(), Off, StringComparison.OrdinalIgnoreCase) ? string.Empty : VendorRefusal.Of(this, row);
}
