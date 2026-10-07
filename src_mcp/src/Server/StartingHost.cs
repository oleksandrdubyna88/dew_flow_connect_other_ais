namespace CoaiMcp.Server;

/// <summary>Where a tool call gets the service to serve it with.</summary>
public interface IPanelServiceSource
{
    /// <summary>The service for THIS call — waiting, without holding a thread, while the first one is still being built.</summary>
    ValueTask<PanelService> CurrentAsync(CancellationToken ct = default);
}

/// <summary>
/// The server's start failed. Its own type, so that no filter written for the SERVING road — an
/// <see cref="IOException"/> read as "the client went away", a cancellation read as "a signal" — can take a failed
/// start for an ordinary ending (own review, 2026-10-06: a start failing on a NAS with an <see cref="IOException"/>
/// would have exited 0 as a closed connection, with no crash recorded).
/// </summary>
public sealed class ServerStartFailed(Exception cause) : Exception($"the server's start failed: {cause.Message}", cause);

/// <summary>
/// The stdio server's first service, built in the background while the transport already answers <c>initialize</c>
/// (<c>todo/PLAN_an_idle_server_is_idle_and_starts_at_once.md</c>, D2).
/// </summary>
/// <remarks>
/// <para><b>Why.</b> A client gives a server about thirty seconds to answer <c>initialize</c>. This server used to read
/// the vault (<c>creds config</c>, its own thirty-second timeout) and build its service — every startup sweep, all the
/// session files — before it read a byte of stdin. Under load that took 19–62 s; the client killed it and started
/// another, seven sessions at once (2026-10-06). <c>initialize</c> and <c>tools/list</c> need nothing the start makes:
/// the tools are closures over this source, and the first tool CALL is what waits.</para>
/// <para><b>A start that fails</b> ends serving the way it always did — <c>Program</c> closes the transport, records
/// the crash and exits non-zero (<see cref="ThrowIfFailed"/>); every call waiting on it gets the same exception. A start
/// cancelled by its stop — a signal, or the client closing stdin — is an ending, not a failure.</para>
/// </remarks>
public sealed class StartingHost : IPanelServiceSource
{
    /// <summary>
    /// How long an ending process waits for a start or a survey still running before it goes on without it — well
    /// inside <see cref="ServeStop.Grace"/>, so a signal's drain of the notices still happens.
    /// </summary>
    public static readonly TimeSpan EndBudget = TimeSpan.FromSeconds(1);

    private StartingHost(Task<PanelServiceHost> started) => Started = started;

    /// <summary>The start itself: the host once it is built, or the start's own exception.</summary>
    public Task<PanelServiceHost> Started { get; }

    /// <summary>
    /// Whether the start FAILED: it threw, or it was cancelled although its stop was not asked for — a timeout inside it
    /// surfaces as a cancellation, and is a failure all the same. Decided INSIDE the start, when it ends (see
    /// <see cref="Start"/>): Program's ending always cancels the start's stop before it asks, so a failure read off the
    /// stop afterwards would turn a timed-out start into an ending (own review of the branch, 2026-10-07).
    /// </summary>
    public bool Failed => Started.IsFaulted;

    /// <summary>Begins the start on the thread pool and returns at once.</summary>
    /// <param name="stop">What ends the start on purpose: a start cancelled by it is an ending, never a failure.</param>
    public static StartingHost Start(Func<Task<PanelServiceHost>> start, CancellationToken stop) =>
        new(Task.Run(() => Settled(start, stop), CancellationToken.None));

    /// <summary>The start, with a cancellation its stop did not ask for turned into the failure it is.</summary>
    private static async Task<PanelServiceHost> Settled(Func<Task<PanelServiceHost>> start, CancellationToken stop)
    {
        try
        {
            return await start().ConfigureAwait(false);
        }
        catch (OperationCanceledException cancelled) when (!stop.IsCancellationRequested)
        {
            throw new TimeoutException("the server's start was cancelled although nothing asked it to stop", cancelled);
        }
    }

    public async ValueTask<PanelService> CurrentAsync(CancellationToken ct = default) =>
        (await Started.WaitAsync(ct).ConfigureAwait(false)).Current;

    /// <summary>
    /// Throws <see cref="ServerStartFailed"/>, carrying the start's own exception, when the start FAILED — so
    /// <c>Program</c> records it as the crash it is. A start cancelled by its stop throws nothing.
    /// </summary>
    public void ThrowIfFailed()
    {
        if (Started is { IsFaulted: true, Exception.InnerException: { } cause })
        {
            throw new ServerStartFailed(cause);
        }
    }

    /// <summary>
    /// Waits for <paramref name="task"/> to end, but no longer than <paramref name="budget"/>; whether it ended. A fault
    /// is OBSERVED here, never thrown: the caller decides what a failure means.
    /// </summary>
    public static async Task<bool> Ended(Task task, TimeSpan budget)
    {
        var ended = await Task.WhenAny(task, Task.Delay(budget, CancellationToken.None)).ConfigureAwait(false) == task;
        _ = task.Exception; // observed, so an ending that gave up on it raises no unobserved-task event later

        return ended;
    }
}
