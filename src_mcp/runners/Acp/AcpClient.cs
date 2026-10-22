using System.ComponentModel;
using System.Text.Json;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Acp;

/// <summary>
/// One turn against an ACP agent, from its start to its end: <c>initialize</c> → <c>session/new</c> → the model found among what
/// the session announced → <c>session/set_config_option</c> (the model) → <c>session/prompt</c> → (answered) <c>/usage</c>, with every
/// reverse request answered meanwhile, and the agent's tree gone before this returns — whichever way it returns.
/// </summary>
/// <remarks>
/// <para><b>The order is the finding.</b> The model is set BEFORE the prompt because a session comes up on the
/// vendor's own default, and on this plan that default was a model the plan does not include: the prompt failed
/// with a 401 (<c>research/RESULTS_kimi_confinement.md</c> §2). The vendor says which option carries the model
/// (<see cref="IAcpProfile.ModelOptionId"/>); the client only insists on the order.</para>
/// <para><b>The model is looked for in the session first</b> (D10, E4.3): a session announces the values of its model option, and an asked
/// model that is not among them is refused with the list — <see cref="AcpEnd.ModelNotOffered"/> — before it is set, because the agent's own
/// answer to a model it lacks is a bare <c>-32603</c> (RESULTS §22).</para>
/// <para><b>The agent's request comes from the profile</b> (<see cref="IAcpProfile.AgentRequest"/>), and so does the right to start it
/// (<see cref="IAcpProfile.AgentRefusal"/>): a profile that builds its own environment (D19) is never started by inheritance, and a request
/// it refuses never starts a process.</para>
/// <para><b>Its own deadline, under the caller's.</b> Reaching it ends the turn with a reason the parent can read
/// (<see cref="AcpEnd.Deadline"/>); being killed by the parent would leave the round guessing — the same rule
/// the local and api shims follow.</para>
/// <para><b>How it ends is one <c>finally</c>.</b> Normal end, deadline, a protocol failure, the agent's premature
/// EOF, the caller's cancellation — each reaches <see cref="AcpTransport.EndAsync"/>: <c>session/cancel</c> when a
/// session exists, stdin closed, a short wait, the tree kill by pid, the wait. A test with a fake agent that spawns
/// a heartbeat grandchild checks both pids are gone on each of those paths.</para>
/// </remarks>
public sealed class AcpClient(IAcpProfile profile, IAcpReadBoundary boundary, TimeSpan usageBound, ITreeSupervisor supervisor, TimeProvider? clock = null)
{
    /// <summary>The one clock the turn's budgets read (E4 plan §5.1): a test hands a manual one; the product is on the system clock.</summary>
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>The prompt that asks the agent for the session's token totals — its own command, answered without a model call.</summary>
    private const string UsagePrompt = "/usage";

    /// <summary>The prompt's <c>stopReason</c> that IS an answer. Everything else is the agent stopping short.</summary>
    public const string EndTurn = "end_turn";

    /// <summary>
    /// How long the follow-up <c>/usage</c> may take (M-K2 measured 2-3 s): its own short bound, under what is left of the turn's
    /// deadline. A usage that cannot be had in this time is not captured; it never delays or changes the answer.
    /// </summary>
    public static readonly TimeSpan DefaultUsageBound = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The longest deadline a turn may be given — a day. A review is minutes; a value the clock cannot hold at all
    /// (<c>CancelAfter</c> refuses past ~24.8 days) used to throw AFTER the agent was started and leave it running
    /// (finding 4 of the security review). Refused here, before any start, with the number in the sentence.
    /// </summary>
    public static readonly TimeSpan MaxDeadline = TimeSpan.FromDays(1);

    public AcpClient(IAcpProfile profile, IAcpReadBoundary boundary)
        : this(profile, boundary, DefaultUsageBound)
    {
    }

    /// <summary>The client with today's end of the agent's tree (<see cref="UnsupervisedTree"/>).</summary>
    public AcpClient(IAcpProfile profile, IAcpReadBoundary boundary, TimeSpan usageBound)
        : this(profile, boundary, usageBound, UnsupervisedTree.Instance)
    {
    }

    public async Task<AcpOutcome> AskAsync(AcpAsk ask, CancellationToken ct = default)
    {
        var request = profile.AgentRequest(ask);
        if (Refusal(ask, request) is { Length: > 0 } refused)
        {
            return AcpOutcome.NotStarted(refused);
        }

        AcpTransport transport;
        try
        {
            transport = AcpTransport.Start(request, supervisor);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            return AcpOutcome.NotStarted($"could not start `{ask.Executable}`: {e.Message}");
        }

        using (transport)
        {
            Told(supervisor, transport.Pid);

            return await TurnAsync(transport, ask, ct);
        }
    }

    /// <summary>The supervisor is told of the agent by its REAL identity; a start time that cannot be read is told as an unidentified pid, never a made-up time.</summary>
    private static void Told(ITreeSupervisor supervisor, int pid)
    {
        if (ProcessTracking.StartedAt(pid) is { } started)
        {
            supervisor.Started(new ProcessIdentity(pid, started));
        }
        else
        {
            supervisor.StartedUnidentified(pid);
        }
    }

    /// <summary>Why nothing may be started: a deadline the clock cannot hold, else the profile's own refusal of this request (D19) — or empty.</summary>
    private string Refusal(AcpAsk ask, ProcessRequest request) =>
        ask.Deadline <= TimeSpan.Zero || ask.Deadline > MaxDeadline
            ? $"a deadline of {ask.Deadline.TotalSeconds:F0}s is outside 1..{MaxDeadline.TotalSeconds:F0} seconds, so nothing was started"
            : profile.AgentRefusal(request, ask);

    /// <summary>
    /// The turn. Everything after the start is inside a <c>try</c> whose <c>finally</c> ends the agent — the clock, the pump, the requests —
    /// so nothing that throws can leave the child running. The deadline's two sources are armed INSIDE the first <c>try</c>: a clock that cannot
    /// make a timer (or a caller's source already disposed) would otherwise throw before any <c>finally</c> existed, with the agent started.
    /// Once <see cref="ConverseUntilAsync"/> has the turn its own <c>finally</c> ends the agent, and this one stands down.
    /// </summary>
    private async Task<AcpOutcome> TurnAsync(AcpTransport transport, AcpAsk ask, CancellationToken ct)
    {
        var handedOver = false;
        try
        {
            // Two sources, the timer FIRST and the link second (plan §5.2): the `using`s dispose the link before the timer, so a timer that
            // fires between the two disposals meets a disposed link rather than a live link holding a disposed timer. The timer is armed here, a few
            // microseconds before the session exists; the tests claim only that the TIMER decides the turn's end. `ct.IsCancellationRequested` still
            // tells the caller's cancellation (`Failed`) from this deadline's.
            using var turnTimer = new CancellationTokenSource(ask.Deadline, _clock);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, turnTimer.Token);
            handedOver = true;

            return await ConverseUntilAsync(transport, ask, ct, deadline.Token);
        }
        finally
        {
            if (!handedOver)
            {
                await transport.EndAsync(() => Task.CompletedTask);
            }
        }
    }

    /// <summary>The conversation under an armed deadline: the session, the pump, the requests, and the one <c>finally</c> that ends the agent and settles the pump.</summary>
    private async Task<AcpOutcome> ConverseUntilAsync(AcpTransport transport, AcpAsk ask, CancellationToken ct, CancellationToken deadline)
    {
        var session = new AcpSession(transport, boundary, deadline);
        Ended ended;
        try
        {
            session.Pump();
            ended = await ConverseAsync(session, ask);
        }
        catch (Exception e) when (e is AcpAgentException or AcpProtocolException or OperationCanceledException)
        {
            ended = Ended.Of(Failed(e, ct.IsCancellationRequested, ask.Deadline));
        }
        finally
        {
            await transport.EndAsync(() => GracefulAsync(session));
            await SettledAsync(session.Reading);
        }

        return new AcpOutcome(
            ended.End, Cut(session.Answer, ended.AnswerChars), ended.Reason, ended.Stop, session.SessionId, session.Usage, session.Refusals, session.ToolCalls, session.Reads,
            transport.StdErr, transport.Pid, session.DroppedTraceEntries, session.TruncatedTraceFields)
        {
            Tokens = ended.Tokens,
            Survivors = transport.Survivors,
        };
    }

    /// <summary><c>initialize</c>, <c>session/new</c>, the model found among what the session announced (D10) — and only then the prompt.</summary>
    private async Task<Ended> ConverseAsync(AcpSession session, AcpAsk ask)
    {
        await session.CallAsync("initialize", AcpMessages.Initialize());
        var created = await session.CallAsync("session/new", AcpMessages.NewSession(ask.Cwd));
        session.SessionId = AcpMessages.Text(created, "sessionId");
        if (session.SessionId.Length == 0)
        {
            // Without an id there is no session to configure or prompt — and a request sent with an empty one
            // would be answered by whatever the agent does with it (finding 6 of the security review).
            throw new AcpProtocolException("session/new answered with no sessionId — nothing to configure or prompt");
        }

        return NotOffered(created, ask) is { Length: > 0 } why ? Ended.Of((AcpEnd.ModelNotOffered, why)) : await PromptedAsync(session, ask);
    }

    /// <summary>
    /// Why the asked model cannot be set — the session does not offer it, or offers no such option at all — or empty. Found out in the
    /// session's own announcement, before <c>set_config_option</c> and before a prompt is spent: the agent's own answer to a model it lacks
    /// is a bare <c>-32603</c> (RESULTS §22).
    /// </summary>
    private string NotOffered(JsonElement created, AcpAsk ask)
    {
        var offered = AcpMessages.AnnouncedValues(created, profile.ModelOptionId);

        return offered.Count == 0 ? $"the agent's session offers no '{profile.ModelOptionId}' option, so '{ask.Model}' cannot be set"
            : offered.Contains(ask.Model, StringComparer.Ordinal) ? string.Empty
            : $"the agent's session offers {string.Join(", ", offered)} for '{profile.ModelOptionId}'; '{ask.Model}' is not one of them";
    }

    /// <summary>The model set, the prompt sent — and, when it was answered, the session's tokens asked for.</summary>
    private async Task<Ended> PromptedAsync(AcpSession session, AcpAsk ask)
    {
        await session.CallAsync("session/set_config_option", AcpMessages.SetConfigOption(session.SessionId, profile.ModelOptionId, ask.Model));
        var stop = AcpMessages.Text(await session.CallAsync("session/prompt", AcpMessages.Prompt(session.SessionId, ask.Prompt)), "stopReason");
        var (end, reason) = Stopped(stop);
        var ended = new Ended(end, reason, stop);

        return end == AcpEnd.Answered ? await UsageAsync(session, ended) : ended;
    }

    /// <summary>
    /// <c>/usage</c> on the SAME session, after an answer only (D15, M-K2: the agent answers it itself in about two seconds, no model call).
    /// The answer is what arrived BEFORE it; the reply is what arrived after, read by <see cref="AcpUsageLine"/>. Whatever goes wrong here —
    /// an error, a broken stream, the bound, the turn's own deadline — leaves the turn's end, answer and exit exactly as they were and the
    /// tokens unknown: the second prompt is never a reason for the first to fail.
    /// </summary>
    private async Task<Ended> UsageAsync(AcpSession session, Ended answered)
    {
        var mark = session.Answer.Length;
        var kept = answered with { AnswerChars = mark };
        try
        {
            await session.CallAsync("session/prompt", AcpMessages.Prompt(session.SessionId, UsagePrompt)).WaitAsync(usageBound, _clock);

            return kept with { Tokens = AcpUsageLine.Parse(After(session.Answer, mark)) };
        }
        catch (Exception e) when (e is AcpAgentException or AcpProtocolException or OperationCanceledException or TimeoutException)
        {
            return kept;
        }
    }

    private static string After(string answer, int mark) => mark < answer.Length ? answer[mark..] : string.Empty;

    private static string Cut(string answer, int chars) => chars < answer.Length ? answer[..chars] : answer;

    /// <summary>
    /// The graceful step before the kill: <c>session/cancel</c> when a session exists, then at most the grace for the
    /// agent to answer what is still pending (an answered turn has nothing pending and waits for nothing). The agent
    /// is alive throughout — its stdin stays open — so the kill that follows still finds its children.
    /// </summary>
    private static async Task GracefulAsync(AcpSession session)
    {
        if (session.SessionId.Length == 0)
        {
            return;
        }

        await session.NotifyAsync("session/cancel", AcpMessages.Cancel(session.SessionId));
        await session.WaitUntilQuietAsync(AcpTransport.GraceWait);
    }

    /// <summary>
    /// Waits for the pump to end, for as long as the drain grace allows: the agent's tree is dead, so its output
    /// ends now — unless something that survived holds the pipe, which is the case the grace bounds.
    /// </summary>
    /// <remarks>
    /// <b>On the SYSTEM clock, on purpose</b> (E4 plan §5.1, teardown): this bounds how long a REAL pipe may still deliver bytes after the tree was
    /// killed. It decides nothing a test races — the deadline has already fired or the answer is in — and on a manual clock that nobody advances it
    /// would turn a two-second bound on a real pipe into a hang. The same holds for the graces under <see cref="AcpTransport.EndAsync"/> and
    /// <see cref="AcpSession"/>.
    /// </remarks>
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

    /// <summary>How a turn that threw ended: the agent's error, a broken stream, or a cancellation — the caller's or the deadline's.</summary>
    /// <param name="cancelledByCaller">Whether the caller's own token fired; otherwise a cancellation is the deadline.</param>
    private static (AcpEnd End, string Reason) Failed(Exception e, bool cancelledByCaller, TimeSpan deadline) => e switch
    {
        AcpAgentException agent => (AcpEnd.AgentFailed, $"the agent answered with an error ({agent.Code}): {agent.Message}"),
        AcpProtocolException protocol => (AcpEnd.Protocol, protocol.Message),
        _ => Cancelled(cancelledByCaller, deadline),
    };

    private static (AcpEnd End, string Reason) Cancelled(bool cancelledByCaller, TimeSpan deadline) =>
        cancelledByCaller
            ? (AcpEnd.Cancelled, "the turn was cancelled by its caller")
            : (AcpEnd.Deadline, $"the agent did not answer within the {deadline.TotalSeconds:F0}s this turn was given");

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

    /// <summary>How a turn ended, as far as the conversation got: what the outcome is made from, apart from the trace.</summary>
    /// <param name="AnswerChars">How much of the session's answer is the turn's: all of it unless <c>/usage</c> was asked, whose reply follows it.</param>
    private sealed record Ended(AcpEnd End, string Reason, string Stop, int AnswerChars = int.MaxValue)
    {
        public AcpTokens Tokens { get; init; } = AcpTokens.Unknown;

        public static Ended Of((AcpEnd End, string Reason) ending) => new(ending.End, ending.Reason, string.Empty);
    }
}
