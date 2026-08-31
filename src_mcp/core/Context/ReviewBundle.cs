namespace CoaiMcp.Core.Context;

/// <summary>
/// Everything a code reviewer is shown: the intent (the plan), the coordinates (branch, base,
/// SHA), and the shaped diff. A reviewer that cannot see the intent reviews the code against its
/// own guess at the intent.
/// </summary>
public sealed record ReviewBundle(
    string PlanText,
    string Branch,
    string BaseRef,
    string Sha,
    ShapedDiff Diff);
