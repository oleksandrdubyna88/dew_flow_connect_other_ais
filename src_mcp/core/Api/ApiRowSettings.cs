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
/// <param name="Stream">Whether the answer is asked for as a stream (research/PLAN_api_streaming.md); off is today's call.</param>
public sealed record ApiRowSettings(string Effort = "", ThinkingSetting Thinking = ThinkingSetting.Default, int ReviewMinutes = 0, bool Stream = false)
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
/// <remarks>
/// <para><b>The environment's effort keeps the meaning it had before the modules</b> (the calibration branch's
/// code round). It is ONE value for every api row: a word the row's dialect makes nothing of (<c>none</c> on
/// every dialect row, <c>engine</c>) sends no effort — the vendor's own depth, thinking on — rather than
/// becoming qwen's thinking-off switch, which is a row's to throw. Any other word is SENT, as every calibration
/// run sent it (qwen's calibration ran <c>high</c>, a word its module does not declare); when it is not one of
/// the module's declared levels <see cref="Unlisted"/> says so, so a refusal that follows has its cause named on
/// the card. The row's own effort is validated by the module instead (<see cref="IApiVendor.Refusal"/>), because
/// a person chose it for this row from the module's own list.</para>
/// <para><b><see cref="ThinkingOn"/> says what the wire does</b>: an effort that is the module's thinking-off
/// level (a row's own <c>none</c> on qwen) reports thinking off.</para>
/// </remarks>
public sealed record ApiEffective(string Effort, bool ThinkingOn, int MaxTokens, int FollowUps, int ReviewMinutes, bool Stream = false)
{
    /// <summary>The variable the environment's effort comes from — named in the sentence that sets it aside.</summary>
    public const string EffortVariable = "COAI_LOCAL_REASONING_EFFORT";

    /// <summary>Row over environment over module default, field by field.</summary>
    public static ApiEffective Of(IApiVendor vendor, ApiRowSettings row, ApiOverrides overrides)
    {
        var effort = row.Effort.Trim().Length > 0 ? row.Effort.Trim() : FromEnvironment(vendor, overrides.Effort.Trim());

        return new(
            effort,
            ThinkingOf(vendor, row.Thinking) && !IsThinkingOff(vendor, effort),
            First(overrides.MaxTokens, vendor.Defaults.MaxTokens),
            vendor.Defaults.FollowUps,
            First(row.ReviewMinutes, overrides.ReviewMinutes, vendor.Defaults.ReviewMinutes),
            // The row's alone: no environment knob and no module default — a stream is a person's choice per row.
            row.Stream);
    }

    /// <summary>What the environment's effort is, for this row, when it is sent without being one of the module's levels — or empty.</summary>
    public static string Unlisted(IApiVendor vendor, ApiRowSettings row, ApiOverrides overrides) =>
        row.Effort.Trim().Length == 0 && Classify(vendor, overrides.Effort.Trim()) == EnvironmentEffort.Unlisted
            ? $"{EffortVariable}='{overrides.Effort.Trim()}' is sent to {vendor.Name}, which declares only "
              + $"{string.Join(", ", vendor.Capabilities.EffortLevels)} — the environment outranks the calibrated '{vendor.Defaults.Effort}'"
            : string.Empty;

    private enum EnvironmentEffort
    {
        Unset,
        SendsNothing,
        Declared,
        Unlisted,
    }

    private static string FromEnvironment(IApiVendor vendor, string effort) => Classify(vendor, effort) switch
    {
        EnvironmentEffort.Unset => vendor.Defaults.Effort,
        EnvironmentEffort.SendsNothing => ApiDialect.EngineDecides,
        _ => effort,
    };

    private static EnvironmentEffort Classify(IApiVendor vendor, string effort) => effort switch
    {
        { Length: 0 } => EnvironmentEffort.Unset,
        _ when vendor.Dialect.EffortToSend(effort).Length == 0 => EnvironmentEffort.SendsNothing,
        _ when vendor.Capabilities.Accepts(effort) => EnvironmentEffort.Declared,
        _ => EnvironmentEffort.Unlisted,
    };

    private static bool ThinkingOf(IApiVendor vendor, ThinkingSetting thinking) =>
        thinking == ThinkingSetting.Default ? vendor.Defaults.ThinkingOn : thinking == ThinkingSetting.On;

    private static bool IsThinkingOff(IApiVendor vendor, string effort) =>
        vendor.Capabilities.ThinkingOffLevel.Length > 0
        && string.Equals(effort, vendor.Capabilities.ThinkingOffLevel, StringComparison.OrdinalIgnoreCase);

    private static int First(params int[] candidates) =>
        candidates.FirstOrDefault(c => c > 0);
}
