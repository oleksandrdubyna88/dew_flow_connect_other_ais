namespace CoaiMcp.Core.Api;

/// <summary>
/// DeepSeek — deepseek-v4-pro on the Alibaba route (<see cref="DashScopeTransport"/>).
/// </summary>
/// <remarks>
/// <para><b>Capabilities</b>, from the vendor's reference (read 2026-09-27): thinking on by default and
/// switchable off with <c>enable_thinking: false</c> (the hybrid model); <c>reasoning_effort</c> accepts
/// <c>low</c>, <c>medium</c> and <c>high</c>, and the vendor states the first two "produce the same
/// behavior as high" on this model — so the levels are declared as the vendor spells them, and effort is
/// no lever here. The thinking switch is documented, not measured: no calibration run sent it.</para>
/// <para><b>Defaults</b> (calibrated under the twenty-minute limit): <c>high</c> sent explicitly — the
/// documented default — thinking on; 5.6 / 4.3 minutes on the mid-size task, 8.8 on the largest, at
/// 1.4–8.1K reasoning tokens a turn. Recorded in <c>RESULTS_feature_reviewer_models.md</c>.</para>
/// </remarks>
public sealed class DeepSeekVendor : IApiVendor
{
    public static readonly DeepSeekVendor Instance = new();

    private DeepSeekVendor()
    {
    }

    public string Name => "deepseek";

    public string MeasuredModel => "deepseek-v4-pro";

    public ApiDialect Dialect => DashScopeTransport.Row;

    public ApiCapabilities Capabilities { get; } = new(
        ThinkingSwitchable: true,
        EffortLevels: ["low", "medium", "high"],
        EffortExcludesThinkingBudget: false);

    public ApiDefaults Defaults { get; } = new(
        Effort: "high",
        ThinkingOn: true,
        MaxTokens: DashScopeTransport.MaxTokens,
        FollowUps: ApiDefaults.PanelFollowUps,
        ReviewMinutes: ApiDefaults.PanelReviewMinutes);

    public string PriceRoute => DashScopeTransport.RowName;

    /// <summary>Thinking on: the row as measured. Thinking off: the vendor's <c>enable_thinking: false</c>, a top-level field beside the standard ones.</summary>
    public string RequestBody(ApiTurn turn) =>
        turn.ThinkingOn
            ? DashScopeTransport.Shared.Body(turn)
            : OpenAiCompatibleTransport.Body(turn, DashScopeTransport.WithThinkingOff());

    public IReadOnlyDictionary<string, string> Headers(string conversation) => DashScopeTransport.Shared.Headers(conversation);

    public ChatAnswer ReadAnswer(string response) => OpenAiCompatibleTransport.Read(response);

    public ApiOutcome Classify(int status, string body) => OpenAiCompatibleTransport.Classify(status, body);

    public string Refusal(ApiRowSettings row) => VendorRefusal.Of(this, row);
}
