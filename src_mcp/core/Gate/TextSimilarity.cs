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
    internal const double SameRemarkThreshold = 0.5;

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

    private static HashSet<string> Tokens(string text) =>
        [.. text.ToLowerInvariant()
            .Split(default(char[]?), StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string([.. w.Where(char.IsLetterOrDigit)]))
            .Where(w => w.Length > 0)];
}
