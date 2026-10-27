namespace CoaiMcp.Tests;

/// <summary>
/// The one signal-and-wait a product hook uses on a test that drives a <see cref="ManualClock"/> (E4 plan §5.4 points 6 and 11).
/// </summary>
/// <remarks>
/// A hook runs on the PRODUCT's thread, under the product's own locks and between its awaits; an <c>Advance</c> from there would run
/// the clock's callbacks inline on that thread (a cancellation registration cancelling the very copy that is calling the hook, or a
/// callback waiting for a lock the hook holds). So the hook only says that it got there (<see cref="Signal"/>) and then waits for a
/// release the TEST controls (<see cref="WaitForReleaseAsync"/>). The test side is
/// <c>await handshake.Reached; clock.Advance(…); handshake.Release();</c>, with <see cref="Release"/> also in the test's
/// <c>finally</c> so a failing test never leaves a product thread parked. Both ends are idempotent. One handshake serves one
/// meeting: a hook that is reached many times (a launcher) is given one handshake per meeting.
/// </remarks>
internal sealed class ClockHandshake
{
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the hook has reached the point the test means.</summary>
    public Task Reached => _reached.Task;

    /// <summary>The hook's side: says it is there. Idempotent.</summary>
    public void Signal() => _reached.TrySetResult();

    /// <summary>The hook's side: waits until the test releases it, or until <paramref name="ct"/> (the test's guard) ends the wait.</summary>
    public Task WaitForReleaseAsync(CancellationToken ct) => _released.Task.WaitAsync(ct);

    /// <summary>The test's side: lets the parked hook go. Idempotent, so it can sit in a <c>finally</c> beside the normal call.</summary>
    public void Release() => _released.TrySetResult();
}
