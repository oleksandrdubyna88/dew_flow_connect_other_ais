namespace CoaiMcp.Core.Api;

/// <summary>
/// What the three Alibaba Model Studio families share — the measured <c>dashscope</c> row, spoken through
/// the one OpenAI-compatible transport, and the facts of that route — composed into
/// <see cref="QwenVendor"/>, <see cref="DeepSeekVendor"/> and <see cref="GlmVendor"/>.
/// </summary>
/// <remarks>
/// <para><b>The row</b> (probed and measured 2026-09-26/27 on the compatible-mode endpoint, Token Plan host
/// included): every field accepted, but the schema is NOT enforced in thinking mode (a GLM-5.3 answer
/// without the required <c>fix</c>), so the row asks for <c>json_object</c> and the parser and the repair
/// hold the shape; the ceiling bounds reasoning PLUS the answer, in the field the vendor documents as the
/// total — <c>max_completion_tokens</c>, floored at 65,536 (on qwen3.8-max <c>max_tokens</c> let 2,344
/// reasoning tokens through a 64-token ceiling; on deepseek-v4-pro both names cut at 64); no cache
/// header — the implicit cache is content-addressed from a 1,024-token common prefix.</para>
/// <para><b>The route's facts</b>: reasoning tokens are reported INSIDE <c>completion_tokens</c>; cached
/// input is billed at a fifth of the input rate; one price list (<c>dashscope</c>) covers the general
/// hosts and the Token Plan host alike.</para>
/// </remarks>
public static class DashScopeTransport
{
    /// <summary>The row's name in the dialect table and the panel's price route.</summary>
    public const string RowName = "dashscope";

    /// <summary>The measured Alibaba row.</summary>
    public static ApiDialect Row => ApiDialects.Named(RowName)!;

    /// <summary>The one transport the three families speak through.</summary>
    public static readonly OpenAiCompatibleTransport Shared = new(Row);

    /// <summary>The ceiling every Alibaba family runs at: the row's floor, because there the ceiling bounds the thinking too.</summary>
    public static int MaxTokens => Row.MaxTokensFloor;

    /// <summary>
    /// The row with the thinking switch spelled as a vendor-specific top-level field — what the OpenAI SDK
    /// calls <c>extra_body</c> — for a family whose switch is <c>enable_thinking</c>.
    /// </summary>
    public static ApiDialect WithThinkingOff() =>
        Row with
        {
            ExtraBody = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal)
            {
                ["enable_thinking"] = System.Text.Json.JsonDocument.Parse("false").RootElement.Clone(),
            },
        };

    /// <summary>The row with the effort map emptied, so a level the row would otherwise omit (<c>none</c>) goes to the wire verbatim.</summary>
    public static ApiDialect WithEffortVerbatim() =>
        Row with { ReasoningEffortMap = new Dictionary<string, string>(StringComparer.Ordinal) };
}
