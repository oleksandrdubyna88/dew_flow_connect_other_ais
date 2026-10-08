using CoaiMcp.Core.Consultation;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// One consultation turn, with the lookups an antigravity consultant asks for served inside it
/// (todo/PLAN_agy_searches_through_coai.md, S3): the turn runs as it always did (<see cref="ConsultantTurn"/>), and while
/// its answer carries a <c>coai-lookup</c> block coai serves it and continues the SAME conversation — up to the runtime's
/// follow-up cap, the tree asked before every continuation.
/// </summary>
/// <remarks>
/// <para>Its own unit because <c>ConsultationService</c> is already past the 800-line ceiling
/// (todo/PLAN_consultation_service_and_program_under_800_lines.md): the service calls this where it called
/// <see cref="ConsultantTurn.RunAsync"/> and reads the result the same way.</para>
/// <para>The launches of every continuation land through <paramref name="landed"/> as they return, so the failure path
/// reads the handle and the usage of whatever exists — the rule <see cref="ConsultantTurn"/> already keeps.</para>
/// </remarks>
internal static class ConsultationLookups
{
    /// <param name="Result">Every launch the turn made, billed once — a cumulative vendor by its latest report.</param>
    /// <param name="Advice">The advice when the lookups decided it (the prose of a capped last turn); null to read the final answer as always.</param>
    /// <param name="Note">Why lookups stopped short, said beside the advice; empty when they did not.</param>
    public sealed record Looked(ConsultantTurnResult Result, string? Advice, string Note);

    public static async Task<Looked> RunAsync(
        ReviewerExecutor executor,
        IConsultantRuntime runtime,
        ConsultantLaunch launch,
        ReviewerInvocation first,
        Func<CancellationToken, Task<IReadOnlyList<TreeChange>>> changesSoFar,
        Action<ReviewerLaunch> landed,
        CancellationToken ct)
    {
        var result = await ConsultantTurn.RunAsync(executor, runtime, first, changesSoFar, landed, ct);
        if (runtime is not IAnsweringFollowUps { FollowUps: > 0 } followUps)
        {
            return new Looked(result, null, string.Empty);
        }

        return await LookedAsync(executor, runtime, followUps, launch, first, result, changesSoFar, landed, ct);
    }

    private static async Task<Looked> LookedAsync(
        ReviewerExecutor executor,
        IConsultantRuntime runtime,
        IAnsweringFollowUps followUps,
        ConsultantLaunch launch,
        ReviewerInvocation invocation,
        ConsultantTurnResult result,
        Func<CancellationToken, Task<IReadOnlyList<TreeChange>>> changesSoFar,
        Action<ReviewerLaunch> landed,
        CancellationToken ct)
    {
        var memory = AnsweringMemory.Start(launch.Prompt);
        while (Answered(result))
        {
            var after = await followUps.AfterAsync(launch, memory with { Handle = result.SurvivingHandle, Last = invocation }, result.Final.Answer!, ct);
            if (after is not AnsweringTurn.Next { Invocation: { } next } carry)
            {
                return Done(result, after);
            }

            // The consultation's promise: a consultant after which the checkout changed is never handed another turn.
            var changes = await changesSoFar(ct);
            if (changes.Count > 0)
            {
                return new Looked(result with { ChangesBeforeFollowUp = changes }, null, string.Empty);
            }

            (invocation, memory) = (next, carry.Memory);
            result = Combined(runtime, result, await ConsultantTurn.RunAsync(executor, runtime, next, changesSoFar, landed, ct));
        }

        // A continuation that failed or said nothing ends the turn on it — the ordinary failure path classifies it.
        return new Looked(result, null, string.Empty);
    }

    private static bool Answered(ConsultantTurnResult result) =>
        result.Final.Terminal is null && !string.IsNullOrWhiteSpace(result.Final.Answer) && !result.Breached;

    /// <summary>The turn's ending as the lookups decided it — the prose of a capped block with the note, or the final answer read as always.</summary>
    private static Looked Done(ConsultantTurnResult result, AnsweringTurn after) => after switch
    {
        AnsweringTurn.Done done when done.Note.Length > 0 => new Looked(result, done.Advice.Trim().Length > 0 ? done.Advice : done.Note, done.Note),
        _ => new Looked(result, null, string.Empty),
    };

    /// <summary>Two parts of one turn as one result: every launch, the latest handle, billed once.</summary>
    private static ConsultantTurnResult Combined(IConsultantRuntime runtime, ConsultantTurnResult before, ConsultantTurnResult more) =>
        new(
            [.. before.Launches, .. more.Launches],
            more.ChangesBeforeFollowUp,
            more.SurvivingHandle.Length > 0 ? more.SurvivingHandle : before.SurvivingHandle,
            ConsultationUsage.OfTwoLaunches(runtime.UsageIsCumulative, before.TurnUsage, more.TurnUsage));
}
