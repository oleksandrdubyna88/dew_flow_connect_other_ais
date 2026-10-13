using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// The clause a round's reviewer line carries for the reviewers a launch control WARNED about — a CLI version nobody
/// measured, which is run rather than refused (the owner's decision of 2026-10-13: "Vibe must WARN, not refuse — set it up
/// once and it must work if it can"). Runtime-neutral: it reads <see cref="ReviewerOutcome.Warning"/>, whoever set it.
/// </summary>
internal static class RoundWarnings
{
    /// <summary>
    /// <c>; warned — vibe/PlanCritique, vibe/Architecture: …</c> — each distinct warning once, after the reviewers it is
    /// about — or empty for a round nobody warned about, so its line reads as it did.
    /// </summary>
    public static string Clause(IReadOnlyList<(ReviewerInvocation Invocation, ReviewerOutcome Outcome)> results)
    {
        var warned = results
            .Where(result => result.Outcome.Warning.Length > 0)
            .GroupBy(result => result.Outcome.Warning, StringComparer.Ordinal)
            .Select(Said)
            .ToList();

        return warned.Count == 0 ? string.Empty : $"; warned — {string.Join("; ", warned)}";
    }

    private static string Said(IGrouping<string, (ReviewerInvocation Invocation, ReviewerOutcome Outcome)> group) =>
        $"{string.Join(", ", group.Select(result => $"{result.Invocation.Provider}/{result.Invocation.Role}"))}: {group.Key}";
}
