namespace CoaiMcp.Core.Api;

/// <summary>
/// xAI — grok-4.7 through <c>api.x.ai/v1</c>, on the measured <c>xai</c> row.
/// </summary>
/// <remarks>
/// <para><b>The row</b> (probed 2026-09-26): 400 to <c>frequency_penalty</c>, 200 to strict
/// <c>json_schema</c>, <c>seed</c>, <c>temperature</c> and every <c>reasoning_effort</c>;
/// <c>max_completion_tokens</c> bounds the ANSWER only (iteration 04 of the calibration: 26,369 reasoning
/// tokens ran under a 64-token ceiling, <c>finish_reason: length</c>, 64 completion tokens); reasoning
/// tokens are reported OUTSIDE <c>completion_tokens</c> and billed as output; the prompt cache is per
/// server and routed by <c>x-grok-conv-id</c> — the row's header, the conversation key its value.</para>
/// <para><b>Capabilities</b>, from the vendor's reasoning guide (read 2026-09-27): <c>reasoning_effort</c>
/// takes <c>low</c>, <c>medium</c>, <c>high</c> (the default) and <c>xhigh</c> on grok-4.7, and "reasoning
/// cannot be disabled" — no thinking switch.</para>
/// <para><b>Defaults</b> (calibrated under the twenty-minute limit, 2026-09-27): <c>medium</c> — one
/// documented level below the vendor's <c>high</c> default. At the default the calibration measured 12.9 /
/// 20.2 / 25.7 minutes a review, two of three over the limit; the field's effect was verified on a real
/// review prompt first (2,448 / 12,108 / 25,917 reasoning tokens and 36 / 153 / 333 s at low / medium /
/// high — the 948-token probe prompt does not discriminate); at <c>medium</c> the mid-size task took 9.0
/// minutes with both planted seeds found and the largest 12.2 over four turns, at 9–19K reasoning tokens a
/// turn. Recorded in <c>RESULTS_feature_reviewer_models.md</c>.</para>
/// </remarks>
public sealed class XaiVendor : IApiVendor
{
    public static readonly XaiVendor Instance = new();

    private readonly OpenAiCompatibleTransport _transport = new(ApiDialects.Named("xai")!);

    private XaiVendor()
    {
    }

    public string Name => "xai";

    public string MeasuredModel => "grok-4.7";

    public ApiDialect Dialect => _transport.Dialect;

    public ApiCapabilities Capabilities { get; } = new(
        ThinkingSwitchable: false,
        EffortLevels: ["low", "medium", "high", "xhigh"],
        EffortExcludesThinkingBudget: false);

    public ApiDefaults Defaults { get; } = new(
        Effort: "medium",
        ThinkingOn: true,
        MaxTokens: ApiDefaults.PanelMaxTokens,
        FollowUps: ApiDefaults.PanelFollowUps,
        ReviewMinutes: ApiDefaults.PanelReviewMinutes);

    public string PriceRoute => "xai";

    public string RequestBody(ApiTurn turn) => _transport.Body(turn);

    public IReadOnlyDictionary<string, string> Headers(string conversation) => _transport.Headers(conversation);

    public ChatAnswer ReadAnswer(string response) => _transport.Read(response);

    public ApiOutcome Classify(int status, string body) => _transport.Classify(status, body);

    public string Refusal(ApiRowSettings row) => VendorRefusal.Of(this, row);
}
