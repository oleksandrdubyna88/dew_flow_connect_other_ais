using System.Diagnostics;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// The round's cancellation, and WHICH of its three sources fired FIRST (E3.S2 of todo/PLAN_the_round_counter_tail_E3.md).
/// </summary>
/// <remarks>
/// <para>The sources are the host's stopping token, the MCP client's token for the call, and the round's own deadline timer. The first
/// version read <c>IsCancellationRequested</c> of all three when the ledger line was written, which answers "which have fired by now",
/// not "which ended it": a reviewer could end <c>Cancelled</c> for its own reason, the clock could fire before the line was classified
/// and the line say <c>roundClock</c>; and a host that stops cancels the request it was serving too, so a round the host killed could be
/// filed as a person's withdrawal — or the reverse, whichever flag the precedence preferred.</para>
/// <para>So the source is RECORDED when it fires: a callback on each token sets the first source once (<see cref="Interlocked.CompareExchange(ref int, int, int)"/>)
/// together with the moment it fired. <see cref="CauseAt"/> then names that source, but only when it fired at or before the moment the
/// outcome landed — an outcome that landed before anything fired is <c>unknown</c>, whatever fires afterwards.</para>
/// <para>The round's token (<see cref="Token"/>) is cancelled BY those callbacks, after the source is recorded, instead of being a linked
/// source that .NET cancels on its own schedule. That is what makes the record deterministic: no reviewer can observe the round cancelled
/// before the cause is written down. The host's stop is recorded but does not cancel the round: the request token the SDK hands the call
/// is what carries it today, exactly as before.</para>
/// <para><b>Simultaneous sources.</b> Callbacks run on the thread that cancels, one at a time, so two real firings are ordered. The only
/// tie is tokens that had ALREADY fired when the round began (a host stopping as a call arrives): registration is in the order host, caller,
/// clock and an already-cancelled token runs its callback at once, so that tie resolves <c>hostStopped</c> over <c>withdrawn</c> over
/// <c>roundClock</c> — the host's stop must not be filed as a person's withdrawal.</para>
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
        _registrations.Add(host.Register(() => Fired(EndCauses.HostStopped, cancelRound: false)));
        _registrations.Add(caller.Register(() => Fired(EndCauses.Withdrawn, cancelRound: true)));
        _registrations.Add(_clock.Token.Register(() => Fired(EndCauses.RoundClock, cancelRound: true)));
    }

    /// <summary>The token every reviewer of the round runs under: fires when the caller or the round's own clock does.</summary>
    public CancellationToken Token => _round.Token;

    /// <summary>The round's deadline timer alone — what the "the deadline ended it" sentence is read from.</summary>
    public CancellationToken Clock => _clock.Token;

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

    private void Fired(string cause, bool cancelRound)
    {
        Interlocked.CompareExchange(ref _first, new FirstFire(cause, Stopwatch.GetTimestamp()), null);
        if (cancelRound)
        {
            Cancel();
        }
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
