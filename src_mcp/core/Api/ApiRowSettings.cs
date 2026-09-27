namespace CoaiMcp.Core.Api;

/// <summary>A row's thinking switch: unset (the module's default), on, or off.</summary>
public enum ThinkingSetting
{
    Default,
    On,
    Off,
}

/// <summary>
/// What a person set on ONE <c>api</c> row, over the module's defaults: an effort, a thinking switch, a
/// maximum processing time. Each is "unset" by default, and unset means the default applies.
/// </summary>
/// <remarks>
/// Read from the row (<c>effort</c>, <c>thinking</c>, <c>reviewMinutes</c> in <c>coai.vendors</c>) and
/// validated by the module (<see cref="IApiVendor.Refusal"/>) before anything is launched — an effort the
/// vendor does not accept is refused with a sentence, never sent to be answered 400 after a round trip.
/// </remarks>
/// <param name="Effort">The effort to send, in the vendor's spelling; empty is unset.</param>
/// <param name="Thinking">The thinking switch; <see cref="ThinkingSetting.Default"/> is unset.</param>
/// <param name="ReviewMinutes">The whole-review limit in minutes; zero is unset.</param>
public sealed record ApiRowSettings(string Effort = "", ThinkingSetting Thinking = ThinkingSetting.Default, int ReviewMinutes = 0)
{
    public static readonly ApiRowSettings None = new();
}

/// <summary>
/// What this process was told for EVERY api row, from its environment — the developer's and the
/// measurement harness's knob, between the row and the module's default.
/// </summary>
/// <remarks>
/// <c>COAI_LOCAL_REASONING_EFFORT</c>, <c>COAI_LOCAL_MAX_TOKENS</c> and <c>COAI_FEATURE_API_REVIEW_MINUTES</c>
/// were the only settings an api row had before the modules existed, and every calibration run set them
/// explicitly; they keep meaning what they meant when they are SET. Unset (empty, zero) they yield to the
/// module's calibrated default rather than to the panel's local-engine numbers.
/// </remarks>
/// <param name="Effort">The effort for every api row, or empty when the environment said nothing.</param>
/// <param name="MaxTokens">The ceiling for every api row, or zero when the environment said nothing.</param>
/// <param name="ReviewMinutes">The whole-review limit for every api row, or zero when the environment said nothing.</param>
public sealed record ApiOverrides(string Effort = "", int MaxTokens = 0, int ReviewMinutes = 0)
{
    public static readonly ApiOverrides None = new();
}

/// <summary>
/// The settings one launch actually runs with — the row's where it set one, else the environment's where
/// it set one, else the module's calibrated default.
/// </summary>
/// <param name="Effort">The effort to send; empty sends nothing.</param>
/// <param name="ThinkingOn">Whether the model thinks.</param>
/// <param name="MaxTokens">The configured ceiling (the row's floor may raise what is sent).</param>
/// <param name="FollowUps">Source follow-up turns after the first.</param>
/// <param name="ReviewMinutes">The whole-review limit in minutes.</param>
public sealed record ApiEffective(string Effort, bool ThinkingOn, int MaxTokens, int FollowUps, int ReviewMinutes)
{
    /// <summary>Row over environment over module default, field by field.</summary>
    public static ApiEffective Of(IApiVendor vendor, ApiRowSettings row, ApiOverrides overrides) => new(
        First(row.Effort, overrides.Effort, vendor.Defaults.Effort),
        row.Thinking == ThinkingSetting.Default ? vendor.Defaults.ThinkingOn : row.Thinking == ThinkingSetting.On,
        First(overrides.MaxTokens, vendor.Defaults.MaxTokens),
        vendor.Defaults.FollowUps,
        First(row.ReviewMinutes, overrides.ReviewMinutes, vendor.Defaults.ReviewMinutes));

    private static string First(params string[] candidates) =>
        candidates.FirstOrDefault(c => c.Trim().Length > 0)?.Trim() ?? string.Empty;

    private static int First(params int[] candidates) =>
        candidates.FirstOrDefault(c => c > 0);
}
