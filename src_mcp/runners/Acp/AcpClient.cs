using System.ComponentModel;
using System.Text.Json;

namespace CoaiMcp.Runners.Acp;

/// <summary>
/// One turn against an ACP agent, from its start to its end: <c>initialize</c> → <c>session/new</c> →
/// <c>session/set_config_option</c> (the model) → <c>session/prompt</c>, with every reverse request answered
/// meanwhile, and the agent's tree gone before this returns — whichever way it returns.
/// </summary>
/// <remarks>
/// <para><b>The order is the finding.</b> The model is set BEFORE the prompt because a session comes up on the
/// vendor's own default, and on this plan that default was a model the plan does not include: the prompt failed
/// with a 401 (<c>research/RESULTS_kimi_confinement.md</c> §2). The vendor says which option carries the model
/// (<see cref="IAcpProfile.ModelOptionId"/>); the client only insists on the order.</para>
/// <para><b>Its own deadline, under the caller's.</b> Reaching it ends the turn with a reason the parent can read
/// (<see cref="AcpEnd.Deadline"/>); being killed by the parent would leave the round guessing — the same rule
/// the local and api shims follow.</para>
/// <para><b>How it ends is one <c>finally</c>.</b> Normal end, deadline, a protocol failure, the agent's premature
/// EOF, the caller's cancellation — each reaches <see cref="AcpTransport.EndAsync"/>: <c>session/cancel</c> when a
/// session exists, stdin closed, a short wait, the tree kill by pid, the wait. A test with a fake agent that spawns
/// a heartbeat grandchild checks both pids are gone on each of those paths.</para>
/// </remarks>
public sealed class AcpClient(IAcpProfile profile, IAcpReadBoundary boundary)
{
    /// <summary>The prompt's <c>stopReason</c> that IS an answer. Everything else is the agent stopping short.</summary>
    public const string EndTurn = "end_turn";

    /// <summary>
    /// The longest deadline a turn may be given — a day. A review is minutes; a value the clock cannot hold at all
    /// (<c>CancelAfter</c> refuses past ~24.8 days) used to throw AFTER the agent was started and leave it running
    /// (finding 4 of the security review). Refused here, before any start, with the number in the sentence.
    /// </summary>
    public static readonly TimeSpan MaxDeadline = TimeSpan.FromDays(1);

    public async Task<AcpOutcome> AskAsync(AcpAsk ask, CancellationToken ct = default)
    {
        if (ask.Deadline <= TimeSpan.Zero || ask.Deadline > MaxDeadline)
        {
            return NotStarted(ask, $"a deadline of {ask.Deadline.TotalSeconds:F0}s is outside 1..{MaxDeadline.TotalSeconds:F0} seconds, so nothing was started");
        }

        AcpTransport transport;
        try
        {
            transport = AcpTransport.Start(ask.Executable, profile.Arguments, ask.Cwd);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            return NotStarted(ask, $"could not start `{ask.Executable}`: {e.Message}");
        }

        using (transport)
        {
            return await TurnAsync(transport, ask, ct);
        }
    }

    /// <summary>
    /// The turn. Everything after the start is inside the one <c>try</c> whose <c>finally</c> ends the agent — the
    /// clock, the pump, the four requests — so nothing that throws can leave the child running.
    /// </summary>
    private async Task<AcpOutcome> TurnAsync(AcpTransport transport, AcpAsk ask, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var session = new AcpSession(transport, boundary, deadline.Token);
        var sessionId = string.Empty;
        var (end, reason, stop) = (AcpEnd.Answered, string.Empty, string.Empty);
        try
        {
            deadline.CancelAfter(ask.Deadline);
            session.Pump();
            await session.CallAsync("initialize", AcpMessages.Initialize());
            sessionId = AcpMessages.Text(await session.CallAsync("session/new", AcpMessages.NewSession(ask.Cwd)), "sessionId");
            if (sessionId.Length == 0)
            {
                // Without an id there is no session to configure or prompt — and a request sent with an empty one
                // would be answered by whatever the agent does with it (finding 6 of the security review).
                throw new AcpProtocolException("session/new answered with no sessionId — nothing to configure or prompt");
            }

            await session.CallAsync("session/set_config_option", AcpMessages.SetConfigOption(sessionId, profile.ModelOptionId, ask.Model));
            stop = AcpMessages.Text(await session.CallAsync("session/prompt", AcpMessages.Prompt(sessionId, ask.Prompt)), "stopReason");
            (end, reason) = Stopped(stop);
        }
        catch (AcpAgentException e)
        {
            (end, reason) = (AcpEnd.AgentFailed, $"the agent answered with an error ({e.Code}): {e.Message}");
        }
        catch (AcpProtocolException e)
        {
            (end, reason) = (AcpEnd.Protocol, e.Message);
        }
        catch (OperationCanceledException)
        {
            (end, reason) = ct.IsCancellationRequested
                ? (AcpEnd.Cancelled, "the turn was cancelled by its caller")
                : (AcpEnd.Deadline, $"the agent did not answer within the {ask.Deadline.TotalSeconds:F0}s this turn was given");
        }
        finally
        {
            await transport.EndAsync(() => GracefulAsync(session, sessionId));
            await SettledAsync(session.Reading);
        }

        return new AcpOutcome(
            end, session.Answer, reason, stop, sessionId, session.Usage, session.Refusals, session.ToolCalls, session.Reads, transport.StdErr, transport.Pid,
            session.DroppedTraceEntries, session.TruncatedTraceFields);
    }

    /// <summary>
    /// The graceful step before the kill: <c>session/cancel</c> when a session exists, then at most the grace for the
    /// agent to answer what is still pending (an answered turn has nothing pending and waits for nothing). The agent
    /// is alive throughout — its stdin stays open — so the kill that follows still finds its children.
    /// </summary>
    private static async Task GracefulAsync(AcpSession session, string sessionId)
    {
        if (sessionId.Length == 0)
        {
            return;
        }

        await session.NotifyAsync("session/cancel", AcpMessages.Cancel(sessionId));
        await session.WaitUntilQuietAsync(AcpTransport.GraceWait);
    }

    /// <summary>
    /// Waits for the pump to end, for as long as the drain grace allows: the agent's tree is dead, so its output
    /// ends now — unless something that survived holds the pipe, which is the case the grace bounds.
    /// </summary>
    private static async Task SettledAsync(Task reading)
    {
        try
        {
            await reading.WaitAsync(AcpTransport.DrainGrace);
        }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException or IOException or ObjectDisposedException)
        {
            // The transport is disposed next, which ends the read the grace walked away from.
        }
    }

    /// <summary>What a <c>stopReason</c> means: <c>end_turn</c> is the answer; anything else is the agent stopping short, in its own word.</summary>
    private static (AcpEnd End, string Reason) Stopped(string stopReason)
    {
        if (stopReason == EndTurn)
        {
            return (AcpEnd.Answered, string.Empty);
        }

        var said = stopReason.Length > 0 ? stopReason : "no stopReason";

        return (AcpEnd.AgentFailed, $"the agent stopped with `{said}` rather than an answer");
    }

    private static AcpOutcome NotStarted(AcpAsk ask, string why) => new(
        AcpEnd.NotStarted,
        string.Empty,
        why,
        string.Empty,
        string.Empty,
        AcpUsage.Unknown,
        [],
        [],
        [],
        string.Empty,
        0);
}
