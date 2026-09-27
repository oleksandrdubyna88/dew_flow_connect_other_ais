namespace CoaiMcp.Core.Api;

/// <summary>
/// What calibration settled for a vendor module — the settings an <c>api</c> row runs with until a
/// person changes them (the operator's rule, 2026-09-27: everything settled in calibration becomes the
/// model's default).
/// </summary>
/// <param name="Effort">The effort sent, in the vendor's own spelling; empty sends nothing (the endpoint decides).</param>
/// <param name="ThinkingOn">Whether the model thinks — on for every calibrated model; off is never a default.</param>
/// <param name="MaxTokens">The token ceiling configured for a turn (the row's floor may raise what is sent).</param>
/// <param name="FollowUps">How many source follow-up turns a feature review may take after the first.</param>
/// <param name="ReviewMinutes">The whole-review limit — every turn of one reviewer's conversation, launch to final answer.</param>
public sealed record ApiDefaults(string Effort, bool ThinkingOn, int MaxTokens, int FollowUps, int ReviewMinutes)
{
    /// <summary>The panel's own numbers before any calibration: a local engine's ceiling, three follow-ups, twenty minutes.</summary>
    public const int PanelMaxTokens = 8192;

    /// <inheritdoc cref="PanelMaxTokens"/>
    public const int PanelFollowUps = 3;

    /// <inheritdoc cref="PanelMaxTokens"/>
    public const int PanelReviewMinutes = 20;

    /// <summary>A vendor nobody calibrated: the endpoint decides the effort, the panel's numbers apply.</summary>
    public static readonly ApiDefaults Uncalibrated = new(string.Empty, true, PanelMaxTokens, PanelFollowUps, PanelReviewMinutes);
}
