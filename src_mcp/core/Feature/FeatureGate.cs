namespace CoaiMcp.Core.Feature;

/// <summary>The feature gate's decisions that are numbers — as against <see cref="FeatureBudget"/>, whose numbers are measurements.</summary>
public static class FeatureGate
{
    /// <summary>
    /// The feature gate is for plans of THREE or more epics.
    /// </summary>
    /// <remarks>
    /// The operator's ruling of 2026-09-25 (D17), after the month's count: 222 plans shipped in
    /// 30 days, most of them one story. A smaller plan is covered by <c>review_code</c>; the feature
    /// stage given fewer epics records a <c>skipped</c> round with that reason and does not block.
    /// Read here; applied by the stage's input check (S2.2). Configurable as
    /// <c>COAI_FEATURE_MIN_EPICS</c>.
    /// </remarks>
    public const int DefaultMinEpics = 3;
}
