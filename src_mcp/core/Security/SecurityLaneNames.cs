using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Core.Security;

/// <summary>How a pairing's context is built — one spelling for the settings, the source reader and the composer.</summary>
public static class SecurityContextModes
{
    /// <summary>The patch plus bounded source windows read from the pinned head.</summary>
    public const string Slice = "slice";

    /// <summary>The patch alone.</summary>
    public const string Diff = "diff";
}

/// <summary>The stage names a pairing's <c>stages</c> list accepts, and the stage each one means.</summary>
public static class SecurityStages
{
    public const string Code = "code";
    public const string Feature = "feature";

    /// <summary>
    /// The name a stage is configured under; the lane serves only these two stages, and any other
    /// stage maps to no name, so no pairing serves it. Explicit per stage, with no catch-all that a
    /// future stage would inherit unasked. (coai code round 9.)
    /// </summary>
    public static string Of(Stage stage) => stage switch
    {
        Stage.CodeReview => Code,
        Stage.FeatureReview => Feature,
        _ => string.Empty,
    };
}
