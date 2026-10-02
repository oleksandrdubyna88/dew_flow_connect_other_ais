using CoaiMcp.Core.Cadence;
using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>
/// Whether the work has been released to the stage environment — INFERRED, never declared
/// (<c>todo/PLAN_question_consultant.md</c> D7, A7: a declared flag is a bypass the AI grants itself).
/// </summary>
/// <remarks>
/// <para>Two readings, by what the session declares. With an epic (<c>k/N</c>), the plan is released when every
/// epic from its own first to <c>N</c> is through the code gate in the cadence record — the plan's first epic is
/// not stored anywhere, so it is read as the lowest number the record holds (a plan that continues another numbers
/// from 5, and its record holds only its own closes). Without one, a session that reached <c>Done</c> after its code
/// round is the work shipped. Anything else — no session, a plan not yet proceeded, a cadence record that could not
/// be read — is unknown, and unknown is not released: erring toward the consultant is the safe direction.</para>
/// <para>The limit, stated: the lowest-numbered close stands in for the plan's first epic, so a plan whose first epic
/// was never closed while every later one was reads as released. Reading the plan text would need git at the sha;
/// the cadence desk does that for its own refusals, and this inference deliberately asks no process.</para>
/// </remarks>
public static class StageRelease
{
    /// <param name="cadence">The plan's cadence record by (repo id, plan) — null when it cannot be read.</param>
    public static bool Of(PersistedSession? session, Func<string, string, CadenceState?> cadence)
    {
        if (session is null || !session.State.PlanProceeded)
        {
            return false;
        }

        return EpicRef.Parse(session.Epic, session.Plan) switch
        {
            EpicRef.Some some => AllClosed(cadence(session.CadenceRepoId, session.Plan), some.Last),
            _ => session.State.Stage == Stage.Done,
        };
    }

    private static bool AllClosed(CadenceState? state, int last)
    {
        if (state is null || state.Closed.Count == 0 || !state.IsClosed(last))
        {
            return false;
        }

        var first = state.Closed.Min(closed => closed.Number);

        return Enumerable.Range(first, last - first + 1).All(state.IsClosed);
    }
}
