using System.Threading.Channels;
using CoaiMcp.Core.Notices;
using CoaiMcp.ServiceDefaults;

namespace CoaiMcp.Server;

/// <summary>
/// One thread writes the notices, and the thing being written down never waits for it.
/// </summary>
/// <remarks>
/// <para><b>Why a queue and not a timeout.</b> Story 2.2's first implementation ran the append on the
/// thread pool and waited two seconds for it. Twelve findings across all three providers said the same
/// thing about that, and they were right: a stalled share blocks the pool worker FOREVER — the wait
/// only stops waiting — so every refusal cost two threads, one of them permanently. At a hundred
/// concurrent refusals against a wedged NAS the pool is gone and the server stops answering, which is
/// a far worse failure than the one the budget was protecting against.</para>
///
/// <para>So there is exactly ONE writer thread. <see cref="Offer"/> puts a notice on a bounded channel
/// and returns immediately — no lock, no I/O, no wait — and a refusal is serialised and returned while
/// the writer is still deciding what to do. If the share is wedged, the one writer blocks on it and
/// nothing else does; the queue fills to <see cref="Depth"/> and then <see cref="Offer"/> starts
/// answering <c>false</c>, which is the loss becoming visible instead of becoming a stall.</para>
///
/// <para><b>It takes a record, not a refusal.</b> Story 2.3 writes reviewer and startup notices through
/// this same writer: the resolve-append-report policy is here, and what each kind of notice IS stays
/// with its own caller. The first version specialised this boundary to refusals, and codex was right
/// that 2.3 would then have to bypass it or copy it.</para>
///
/// <para><b>The directory is resolved HERE, on the writer's thread</b>, for the same reason the append
/// is: <c>PanelSettings.DataDirectoryFor</c> is cheap but it is not the refusal's business, and a
/// resolver that ever grows a filesystem check must not thereby grow one onto the refusal path.</para>
/// </remarks>
internal sealed class NoticeWriter
{
    /// <summary>How many notices may be waiting before one is dropped rather than queued.</summary>
    /// <remarks>
    /// Deep enough that a burst of refusals in one round never reaches it — the census counts 48
    /// refusal sites — and shallow enough that a wedged share costs a bounded amount of memory and
    /// says so within seconds rather than filling the process.
    /// </remarks>
    internal const int Depth = 256;

    /// <summary>The one every refusal uses. A second one would be a second writer thread.</summary>
    // A lambda and not a method group: `Append` has an optional `rollAt`, and a method group with one
    // does not convert — which is the compiler saying the default is a choice worth writing down.
    internal static NoticeWriter Shared { get; } =
        new((dir, notice) => ServerNotices.Append(dir, notice));

    private readonly Func<ResolvedDataDir, ServerNotice, bool> _append;

    private readonly Channel<Pending> _queue = Channel.CreateBounded<Pending>(
        new BoundedChannelOptions(Depth)
        {
            // `Wait` is what makes a synchronous `TryWrite` answer FALSE when the queue is full
            // rather than block — `DropWrite` discards the notice and answers true, which is the
            // silent loss this class exists to make visible. Nothing ever awaits the writer side.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    private readonly ManualResetEventSlim _idle = new(initialState: true);

    private int _inFlight;

    internal NoticeWriter(Func<ResolvedDataDir, ServerNotice, bool> append)
    {
        _append = append;
        _ = Task.Run(Draining);
    }

    /// <summary>
    /// Hands one notice to the writer. Never blocks, never throws; <c>false</c> means it was dropped.
    /// </summary>
    internal bool Offer(Func<ResolvedDataDir> where, ServerNotice notice, Serilog.ILogger log)
    {
        Interlocked.Increment(ref _inFlight);
        _idle.Reset();

        if (_queue.Writer.TryWrite(new Pending(where, notice, log)))
        {
            return true;
        }

        Done();
        Safely(() => log.Warning(
            "a notice was dropped: {Depth} are already waiting and the disk is not answering", Depth));

        return false;
    }

    /// <summary>Whether the queue has drained, for a test that needs to look at the file.</summary>
    internal bool Idle(TimeSpan within) => Volatile.Read(ref _inFlight) == 0 || _idle.Wait(within);

    private async Task Draining()
    {
        await foreach (var pending in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            Wrote(pending);
        }
    }

    /// <summary>One notice, and every way it can fail to land, reported rather than swallowed.</summary>
    private void Wrote(Pending pending)
    {
        try
        {
            Said(_append(pending.Where(), pending.Notice), pending.Log);
        }
        catch (Exception failure)
        {
            // A directory that cannot resolve, a path the OS refuses, a share that stopped answering,
            // a record the line cannot write. The thing this notice was about already happened.
            Safely(() => pending.Log.Warning(failure, "a notice could not be written down"));
        }
        finally
        {
            Done();
        }
    }

    private static void Said(bool wrote, Serilog.ILogger log)
    {
        if (wrote)
        {
            return;
        }

        Safely(() => log.Warning(
            "a notice was not written to {File} — the disk refused it, or the file is at its ceiling",
            ServerNotices.Name));
    }

    /// <summary>
    /// A log that throws must not become the silence it was there to prevent.
    /// </summary>
    /// <remarks>
    /// The code round, local: a disposed logger or a full async sink throws from inside the very call
    /// that reports a lost notice, and the outer catch then swallows both. There is nowhere left to
    /// say it — this process's stdout may be carrying a protocol — so what this buys is that the
    /// writer thread survives to write the next one.
    /// </remarks>
    private static void Safely(Action saying)
    {
        try
        {
            saying();
        }
        catch (Exception)
        {
            // Nowhere left to say it: this process's stdout may be carrying a protocol.
        }
    }

    private void Done()
    {
        if (Interlocked.Decrement(ref _inFlight) == 0)
        {
            _idle.Set();
        }
    }

    private readonly record struct Pending(
        Func<ResolvedDataDir> Where, ServerNotice Notice, Serilog.ILogger Log);
}
