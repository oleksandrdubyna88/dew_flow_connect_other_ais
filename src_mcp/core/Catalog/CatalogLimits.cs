namespace CoaiMcp.Core.Catalog;

/// <summary>The catalog's limits, as this binary enforces them (research/PLAN_one_model_catalog.md D1).</summary>
/// <remarks>
/// The extension refuses the same limit at save time (<c>catalogRules.ts</c> <c>MAX_PROMPT_BYTES</c>); the server refuses
/// it again because a hand-edited settings file reaches it too. Each half pins its own constant to 8192 in a test
/// (<c>ARowsSystemPromptReachesItsReviewerTests</c>, <c>catalogRow.test.ts</c>), so changing one without the other is a red
/// test — rather than one half reading the other's source.
/// </remarks>
public static class CatalogLimits
{
    /// <summary>A row's system prompt, in UTF-8 bytes — bytes because bytes are what crosses.</summary>
    public const int MaxPromptBytes = 8192;

    /// <summary>A CLI row's own timeout, in whole minutes — a day, as the extension's <c>MAX_REVIEW_MINUTES</c>.</summary>
    public const int MaxTimeoutMinutes = 1440;
}
