using System.Collections.Immutable;
using CoaiMcp.Core.Findings;

namespace CoaiMcp.Core.Gate;

/// <summary>
/// The same defect found by two vendors is ONE finding with two names on it — stronger evidence,
/// not twice the work. Without this, three providers mechanically triple the gate count and the
/// threshold becomes unreachable.
/// </summary>
/// <remarks>
/// Two findings merge when the file matches (case-insensitive, separators normalised), the
/// category matches, the lines are within ±5, and the titles are the same remark
/// (<see cref="TextSimilarity"/>). The merged finding keeps the FIRST title and the WORST
/// severity — two vendors disagreeing on how bad it is resolves toward caution, never away
/// from it.
/// </remarks>
public static class FindingDedup
{
    private const int LineSlack = 5;

    public static ImmutableArray<Finding> Merge(IEnumerable<Finding> findings)
    {
        var merged = new List<Finding>();
        foreach (var finding in findings)
        {
            var index = merged.FindIndex(m => SameDefect(m, finding));
            if (index < 0)
            {
                merged.Add(finding);
                continue;
            }

            var existing = merged[index];
            merged[index] = existing with
            {
                Severity = (Severity)Math.Min((int)existing.Severity, (int)finding.Severity),
                Providers = [.. existing.Providers.Union(finding.Providers, StringComparer.OrdinalIgnoreCase)],
            };
        }

        return [.. merged];
    }

    internal static bool SameDefect(Finding a, Finding b) =>
        a.Category == b.Category &&
        NormalisePath(a.File).Equals(NormalisePath(b.File), StringComparison.OrdinalIgnoreCase) &&
        Math.Abs(a.Line - b.Line) <= LineSlack &&
        TextSimilarity.SameRemark(a.Title, b.Title);

    private static string NormalisePath(string path) => path.Replace('\\', '/');
}
