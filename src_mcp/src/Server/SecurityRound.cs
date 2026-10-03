using System.Collections.Immutable;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Core.Security;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>Projects lane results without allowing them to impersonate an ordinary reviewer.</summary>
internal static class SecurityRound
{
    internal static bool IsLane(ReviewerInvocation invocation, IReadOnlyList<ReviewerWork> work) =>
        work.Any(w => w.IsSecurity && w.Invocation.Provider == invocation.Provider && w.Invocation.Role == invocation.Role);

    internal static ImmutableArray<Finding> Merge(
        IReadOnlyList<(ReviewerInvocation Invocation, ReviewerOutcome Outcome)> results, IReadOnlyList<ReviewerWork> work) =>
        SecurityEvidence.Merge(results.Where(r => r.Outcome is ReviewerOutcome.Ok).SelectMany(r =>
            ((ReviewerOutcome.Ok)r.Outcome).Review.Findings.Select(f => IsLane(r.Invocation, work)
                ? SecurityEvidence.Attribute(f, r.Invocation.Provider, r.Invocation.Role)
                : f with { Role = r.Invocation.Role })));

    internal static ReviewerSummary DecisionSummary(
        IReadOnlyList<(ReviewerInvocation Invocation, ReviewerOutcome Outcome)> results, RoundWork work, ReviewerSummary all)
    {
        var ordinary = results.Where(r => !IsLane(r.Invocation, work.Reviewers)).ToArray();
        // A later lane-only round is allowed after ordinary roles spent their budgets. An ordinary
        // reviewer that WAS asked and failed can never be rescued by a lane answer.
        // Only the COUNTS are the ordinary reviewers'. Who could not run, who was not asked and the
        // deadline are facts about the round, and a call_human reason without them sends a person
        // to look for a failure the round already named.
        return ordinary.Length == 0 && !work.OrdinaryDue ? all
            : ReviewerSummaryFactory.From(ordinary) with
            {
                Excluded = all.Excluded,
                NotAsked = all.NotAsked,
                EndedByDeadline = all.EndedByDeadline,
            };
    }

    /// <summary>Why a round only the lane's budget admitted has nobody in it — or the work unchanged.</summary>
    internal static RoundWork NamingAnEmptyLaneRound(RoundWork work) => work.Reviewers.Count > 0 ? work : work with
    {
        NotAsked = [.. work.NotAsked, new SkippedRole(SecurityCatalog.Gate,
            "this round lies past every ordinary role's budget and was admitted for the security lane, "
            + "which had no work to run in it; nothing was reviewed")],
    };

    internal static void Notice(IReadOnlyList<(ReviewerInvocation Invocation, ReviewerOutcome Outcome)> results,
        RoundWork work, Noticing noticing)
    {
        var lane = results.Where(r => IsLane(r.Invocation, work.Reviewers)).ToArray();
        if (!work.SecurityActive || lane.Any(r => r.Outcome is ReviewerOutcome.Ok && !r.Invocation.IsOnEngine)) return;
        if (lane.Length == 0 && !work.Excluded.Any(r => SecurityCatalog.IsPromptId(r.Role))) return;
        noticing.Offer(new Core.Notices.ServerNotice
        {
            Utc = Core.Notices.ServerNotice.Iso(TimeProvider.System.GetUtcNow()),
            Class = "failure",
            Source = "coai-mcp",
            Code = Core.Notices.ServerNoticeCodes.SecurityLaneIncomplete,
            Subject = SecurityCatalog.Gate,
            Title = "Security lane incomplete: no complete answer",
            Detail = Core.Notices.ServerNotice.Shortened(Clause(results, work)),
        });
    }

    internal static string Clause(IReadOnlyList<(ReviewerInvocation Invocation, ReviewerOutcome Outcome)> results, RoundWork work)
    {
        if (!work.SecurityActive) return string.Empty;
        var lane = results.Where(r => IsLane(r.Invocation, work.Reviewers)).ToArray();
        if (lane.Length == 0) return work.Excluded.Any(r => SecurityCatalog.IsPromptId(r.Role))
            ? "Security lane incomplete: configured pairings could not run. "
            : "Security lane skipped: no pairing was due (conditions, stage selection or round budget). ";
        var complete = lane.Count(r => r.Outcome is ReviewerOutcome.Ok && !r.Invocation.IsOnEngine);
        var prefix = complete == 0 ? "Security lane incomplete: no complete answer. " : $"Security lane: {complete}/{lane.Length} complete answers. ";
        return prefix + string.Join("; ", lane.Where(r => r.Outcome is not ReviewerOutcome.Ok || r.Invocation.IsOnEngine)
            .Select(r => $"{r.Invocation.Provider}/{r.Invocation.Role}: " + (r.Invocation.IsOnEngine && r.Outcome is ReviewerOutcome.Ok
                ? "input coverage unverified; findings retained as evidence" : ReviewerSummaryFactory.Describe(r.Outcome)))) + " ";
    }
}
