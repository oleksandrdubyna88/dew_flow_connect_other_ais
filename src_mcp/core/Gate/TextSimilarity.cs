namespace CoaiMcp.Core.Gate;

/// <summary>
/// "Similar enough to be the same remark" — deterministic, explainable, and deliberately dumb.
/// </summary>
/// <remarks>
/// Token-set Jaccard over lowercase alphanumeric words. Chosen over anything cleverer because a
/// gate must be arguable with: when two findings merge or refuse to, the reason is a number a
/// person can recompute on paper. The 0.5 threshold is pinned by the dedup tests.
/// </remarks>
internal static class TextSimilarity
{
    /// <summary>When nothing else pins the two findings together — a repo-level remark with no file.</summary>
    internal const double SameRemarkThreshold = 0.5;

    /// <summary>
    /// When file, line (±5) and category ALL match, far less wording overlap is needed: those
    /// three coordinates already say the reviewers are looking at the same code for the same
    /// reason.
    /// </summary>
    /// <remarks>
    /// The real run of 2026-08-31 measured the cost of the single strict threshold: three
    /// reviewers found ONE path-traversal defect at <c>Store.cs:10</c> and worded it
    /// "Unvalidated paste IDs can escape the configured storage root" and "Unvalidated paste IDs
    /// allow writes and reads outside the configured root" — 0.43 similar, so they counted twice.
    /// The same happened to the quadratic-scan finding. A gate whose count grows with the number
    /// of reviewers is the exact failure de-duplication exists to prevent.
    /// </remarks>
    internal const double AnchoredRemarkThreshold = 0.25;

    internal static double Jaccard(string a, string b)
    {
        var ta = Tokens(a);
        var tb = Tokens(b);
        if (ta.Count == 0 && tb.Count == 0)
        {
            return 1;
        }

        var intersection = ta.Intersect(tb).Count();
        var union = ta.Union(tb).Count();
        return union == 0 ? 0 : (double)intersection / union;
    }

    internal static bool SameRemark(string a, string b) => Jaccard(a, b) >= SameRemarkThreshold;

    /// <summary>The looser test, for two findings already anchored to the same file, line and category.</summary>
    internal static bool SameAnchoredRemark(string a, string b) => Jaccard(a, b) >= AnchoredRemarkThreshold;

    private static HashSet<string> Tokens(string text) =>
        [.. text.ToLowerInvariant()
            .Split(default(char[]?), StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string([.. w.Where(char.IsLetterOrDigit)]))
            .Where(w => w.Length > 0)];
}
