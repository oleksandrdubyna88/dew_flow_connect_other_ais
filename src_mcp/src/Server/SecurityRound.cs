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
        if (!NeedsNotice(results, work)) return;
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

    /// <summary>
    /// A durable notice is owed when the lane was active, no lane pairing gave a complete answer, and the lane
    /// either ran or had a pairing it could not run — never for a lane with nothing due.
    /// </summary>
    private static bool NeedsNotice(IReadOnlyList<(ReviewerInvocation Invocation, ReviewerOutcome Outcome)> results, RoundWork work)
    {
        if (!work.SecurityActive) return false;
        var lane = LaneResults(results, work);
        if (lane.Any(IsComplete)) return false;
        return lane.Length > 0 || HasBrokenPairing(work);
    }

    internal static string Clause(IReadOnlyList<(ReviewerInvocation Invocation, ReviewerOutcome Outcome)> results, RoundWork work)
    {
        if (!work.SecurityActive) return string.Empty;
        var lane = LaneResults(results, work);
        if (lane.Length == 0) return NoLaneAnswer(work);
        return Answered(lane) + string.Join("; ", lane.Where(r => !IsComplete(r)).Select(Unfinished)) + " ";
    }

    private static (ReviewerInvocation Invocation, ReviewerOutcome Outcome)[] LaneResults(
        IReadOnlyList<(ReviewerInvocation Invocation, ReviewerOutcome Outcome)> results, RoundWork work) =>
        [.. results.Where(r => IsLane(r.Invocation, work.Reviewers))];

    /// <summary>A complete lane answer: answered, and not by a local engine, whose input coverage stays unverified.</summary>
    private static bool IsComplete((ReviewerInvocation Invocation, ReviewerOutcome Outcome) result) =>
        result.Outcome is ReviewerOutcome.Ok && !result.Invocation.IsOnEngine;

    private static bool HasBrokenPairing(RoundWork work) => work.Excluded.Any(r => SecurityCatalog.IsPromptId(r.Role));

    private static string NoLaneAnswer(RoundWork work) => HasBrokenPairing(work)
        ? "Security lane incomplete: configured pairings could not run. "
        : "Security lane skipped: no pairing was due (conditions, stage selection or round budget). ";

    private static string Answered((ReviewerInvocation Invocation, ReviewerOutcome Outcome)[] lane)
    {
        var complete = lane.Count(IsComplete);
        return complete == 0 ? "Security lane incomplete: no complete answer. " : $"Security lane: {complete}/{lane.Length} complete answers. ";
    }

    private static string Unfinished((ReviewerInvocation Invocation, ReviewerOutcome Outcome) result) =>
        $"{result.Invocation.Provider}/{result.Invocation.Role}: " + (result.Invocation.IsOnEngine && result.Outcome is ReviewerOutcome.Ok
            ? "input coverage unverified; findings retained as evidence" : ReviewerSummaryFactory.Describe(result.Outcome));
}
