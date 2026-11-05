using CoaiMcp.Runners.Acp;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// One ACP turn driven on a <see cref="ManualClock"/> by the TEST (E4 plan §5.4 point 6: <c>Advance</c> is the test driver's call, never a
/// product thread's): the turn runs on its own, the test waits for the EVENT it means (timers armed, a method received), moves the clock or
/// cancels the caller, and awaits the end.
/// </summary>
/// <remarks>
/// Every wait here is under <see cref="Guard"/> — a bound a correct run never reaches, not a decision: a turn whose deadline is not on the
/// clock never arms the timer the test waits for, and the guard turns that into a failure with its sentence rather than a hang. After the
/// last advance nothing is advanced for the teardown (plan §15 point 5): the agent's end runs on the system clock, so awaiting the turn
/// below is real time, bounded by <c>GraceWait</c> + <c>DrainGrace</c>.
/// </remarks>
internal static class ClockedAcpTurn
{
    /// <summary>A bound a correct run never reaches.</summary>
    public static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);

    /// <summary>The clock's start: a fixed instant, so what a turn did to it is one subtraction.</summary>
    public static readonly DateTimeOffset Start = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Starts the turn, lets <paramref name="drive"/> do what the test means once the product is where it should be (it gets the caller's
    /// source, to cancel it), and returns the outcome. Whatever happens, the caller is cancelled on the way out so a failed drive never
    /// leaves the agent running.
    /// </summary>
    public static async Task<AcpOutcome> DrivenAsync(AcpClient client, AcpAsk ask, Func<CancellationTokenSource, Task> drive, ManualClock? clock = null, int expectedTimers = 0)
    {
        var ct = TestContext.Current.CancellationToken;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var turn = client.AskAsync(ask, caller.Token);
        try
        {
            await ReachedAsync(drive(caller), ct, clock, expectedTimers);

            return await turn.WaitAsync(Guard, ct);
        }
        finally
        {
            await caller.CancelAsync();
            await Task.WhenAny(turn, Task.Delay(Guard, CancellationToken.None));
        }
    }

    /// <summary>
    /// The drive under the guard; a drive that never gets where it means is named as such, not left as a bare timeout. A drive that waited on
    /// <see cref="ManualClock.WhenTimersAsync"/> faults with ITS sentence (counts included) first — the outer net is a few seconds longer than the
    /// clock's own bound — and that is rethrown as it is; otherwise the sentence names the timers the test expected and the number armed.
    /// </summary>
    private static async Task ReachedAsync(Task drive, CancellationToken ct, ManualClock? clock, int expectedTimers)
    {
        try
        {
            await drive.WaitAsync(Guard + OuterSlack, ct);
        }
        catch (TimeoutException e) when (!drive.IsFaulted)
        {
            throw new TimeoutException(NeverReached(clock, expectedTimers), e);
        }
    }

    /// <summary>The guard's sentence: what the product never reached, the timers the test expected and how many the clock holds.</summary>
    internal static string NeverReached(ManualClock? clock, int expectedTimers) =>
        $"the product never reached what the test waits for within {Guard.TotalSeconds:F0}s on the manual clock — a deadline or bound that is not on the clock never arms its timer there "
        + $"(timers the test expected: {(expectedTimers > 0 ? expectedTimers.ToString() : "not stated")}; armed: {(clock is null ? "unknown, no clock was handed to the driver" : clock.ActiveTimers.ToString())})";

    /// <summary>How much longer the outer net waits than the clock's own named bound, so the named sentence is the one that surfaces.</summary>
    private static readonly TimeSpan OuterSlack = TimeSpan.FromSeconds(5);

    /// <summary>The drive of a deadline: wait until the clock holds <paramref name="timers"/> timers, then move it by <paramref name="by"/>.</summary>
    public static Func<CancellationTokenSource, Task> AdvanceOnceArmed(ManualClock clock, int timers, TimeSpan by) =>
        async _ =>
        {
            await clock.WhenTimersAsync(timers, Guard);
            clock.Advance(by);
        };
}
