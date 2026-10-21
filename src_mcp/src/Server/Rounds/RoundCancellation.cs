using System.Diagnostics;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// The round's cancellation, and WHICH of its sources ended it FIRST (E3.S2 of todo/PLAN_the_round_counter_tail_E3.md).
/// </summary>
/// <remarks>
/// <para>The first version read <c>IsCancellationRequested</c> of every token when the ledger line was written, which answers "which have
/// fired by now", not "which ended it": a reviewer could end <c>Cancelled</c> for its own reason, the clock could fire before the line was
/// classified and the line say <c>roundClock</c>; and a host that stops cancels the request it was serving too, so a round the host killed
/// could be filed as a person's withdrawal — or the reverse, whichever flag the precedence preferred.</para>
/// <para><b>What each source does</b> (one rule: a cause is RECORDED only by a source that CANCELS the round, at the moment it does):</para>
/// <list type="bullet">
/// <item><description><b>The caller's token</b> (the MCP client's, for the call) cancels the round. The cause it records is <c>withdrawn</c> — a person
/// took the call back — unless the host's token is already cancelled, in which case it is <c>hostStopped</c>: a stopping host takes its in-flight
/// requests' tokens down with it, and that is not a person's decision.</description></item>
/// <item><description><b>The round's own clock</b> cancels the round and records <c>roundClock</c>, whether or not the host was stopping: the
/// deadline is its own, and a stop that merely preceded it did not end the round.</description></item>
/// <item><description><b>The host's stopping token</b> is only OBSERVED: it cancels nothing of the round and records nothing itself — the round
/// keeps running, exactly as before E3. It reaches a ledger line only through the caller's callback above, i.e. only when a caller token is
/// actually wired and fires while the host is stopping. (Live, no review tool passes the request's token, so a client closing stdin fires the host's
/// token alone and any later cancellation is the clock's.)</description></item>
/// </list>
/// <para>The source is recorded when it fires: a callback sets the first cause once (<see cref="Interlocked.CompareExchange{T}(ref T, T, T)"/>)
/// together with the moment it fired. <see cref="CauseAt"/> then names that cause, but only when it fired at or before the moment the
/// outcome landed — an outcome that landed before anything fired is <c>unknown</c>, whatever fires afterwards.</para>
/// <para>The round's token (<see cref="Token"/>) is cancelled BY those callbacks, after the cause is recorded, instead of being a linked
/// source that .NET cancels on its own schedule. That is what makes the record deterministic: no reviewer can observe the round cancelled
/// before the cause is written down.</para>
/// <para><b>Simultaneous sources.</b> Callbacks run on the thread that cancels, one at a time, so two real firings are ordered. The only
/// tie is tokens that had ALREADY fired when the round began (a host stopping as a call arrives): the caller's callback runs at once and sees
/// the host's token cancelled, so that tie resolves <c>hostStopped</c> over <c>withdrawn</c> — the host's stop must not be filed as a person's
/// withdrawal.</para>
/// </remarks>
internal sealed class RoundCancellation : IDisposable
{
    private readonly CancellationTokenSource _clock;
    private readonly CancellationTokenSource _round = new();
    private readonly List<CancellationTokenRegistration> _registrations = [];
    private FirstFire? _first;

    /// <summary>The first source to fire: the word it files under, and the moment it fired.</summary>
    private sealed record FirstFire(string Cause, long At);

    /// <param name="host">The server's own stopping token.</param>
    /// <param name="caller">The MCP client's token for this call.</param>
    /// <param name="budget">How long the round may take; the clock starts now.</param>
    public RoundCancellation(CancellationToken host, CancellationToken caller, TimeSpan budget)
    {
        _clock = new CancellationTokenSource(budget);
        // The host's token is not registered: it ends nothing of the round, so it records nothing. It is read, at the moment the
        // caller's token fires, to tell a stopping host's doing from a person's.
        _registrations.Add(caller.Register(() => Fired(CallerCause(host))));
        _registrations.Add(_clock.Token.Register(() => Fired(EndCauses.RoundClock)));
    }

    /// <summary>The token every reviewer of the round runs under: fires when the caller or the round's own clock does — never when the host's alone does.</summary>
    public CancellationToken Token => _round.Token;

    /// <summary>
    /// The cause of the round's ending as of NOW — what the "the deadline ended it" sentence is read from, so that sentence and the ledger's
    /// <c>endCause</c> are one answer: the first source to fire, or <c>unknown</c> when none has.
    /// </summary>
    public string CauseNow() => CauseAt(Stopwatch.GetTimestamp());

    /// <summary>
    /// The ledger's <c>endCause</c> word for an outcome that landed at <paramref name="landedAt"/> (a <see cref="Stopwatch.GetTimestamp"/>
    /// value): the first source to fire, if it fired at or before then; <c>unknown</c> otherwise.
    /// </summary>
    public string CauseAt(long landedAt) =>
        Volatile.Read(ref _first) is { } first && first.At <= landedAt ? first.Cause : EndCauses.Unknown;

    public void Dispose()
    {
        _registrations.ForEach(r => r.Dispose());
        _round.Dispose();
        _clock.Dispose();
    }

    /// <summary>The caller's token firing is a withdrawal, unless the host was stopping — then it is the host's doing.</summary>
    private static string CallerCause(CancellationToken host) =>
        host.IsCancellationRequested ? EndCauses.HostStopped : EndCauses.Withdrawn;

    private void Fired(string cause)
    {
        Interlocked.CompareExchange(ref _first, new FirstFire(cause, Stopwatch.GetTimestamp()), null);
        Cancel();
    }

    private void Cancel()
    {
        try
        {
            _round.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The round is over and this is a late callback: nothing is left to cancel.
        }
    }
}
