namespace CoaiMcp.Core.Work;

/// <summary>
/// The rule that sends a task to the <c>important</c> model list: <c>security: true</c>, kind <c>security</c>, or complexity 10. ONE rule — the validator lists the tasks it
/// applies to, and the router (epoch 3 E1 s3) asks the same function — so the two cannot disagree on which task is important.
/// </summary>
public static class ImportantRule
{
    /// <summary>The highest complexity, which always takes the important models.</summary>
    public const int TopComplexity = 10;

    /// <summary>Why a task is important, or empty when it is not.</summary>
    public static string Reason(string kind, int complexity, bool securityFlag) =>
        securityFlag ? "security: true"
        : kind == "security" ? "kind: security"
        : complexity >= TopComplexity ? $"complexity {TopComplexity}"
        : string.Empty;
}
