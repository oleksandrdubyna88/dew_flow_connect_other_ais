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
    public static async Task<AcpOutcome> DrivenAsync(AcpClient client, AcpAsk ask, Func<CancellationTokenSource, Task> drive)
    {
        var ct = TestContext.Current.CancellationToken;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var turn = client.AskAsync(ask, caller.Token);
        try
        {
            await ReachedAsync(drive(caller), ct);

            return await turn.WaitAsync(Guard, ct);
        }
        finally
        {
            await caller.CancelAsync();
            await Task.WhenAny(turn, Task.Delay(Guard, CancellationToken.None));
        }
    }

    /// <summary>The drive under the guard; a drive that never gets where it means is named as such, not left as a bare timeout.</summary>
    private static async Task ReachedAsync(Task drive, CancellationToken ct)
    {
        try
        {
            await drive.WaitAsync(Guard, ct);
        }
        catch (TimeoutException e)
        {
            throw new TimeoutException($"the product never reached what the test waits for within {Guard.TotalSeconds:F0}s on the manual clock — a deadline or bound that is not on the clock never arms its timer there", e);
        }
    }

    /// <summary>The drive of a deadline: wait until the clock holds <paramref name="timers"/> timers, then move it by <paramref name="by"/>.</summary>
    public static Func<CancellationTokenSource, Task> AdvanceOnceArmed(ManualClock clock, int timers, TimeSpan by) =>
        async _ =>
        {
            await clock.WhenTimersAsync(timers);
            clock.Advance(by);
        };
}
