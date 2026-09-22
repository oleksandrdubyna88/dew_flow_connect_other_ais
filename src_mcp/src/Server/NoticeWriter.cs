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

    /// <summary>
    /// How long a process leaving may wait for what it queued.
    /// </summary>
    /// <remarks>
    /// It is a CEILING and not a wait: an empty queue drains at once, and the ordinary session that
    /// wrote a handful of notices pays the time of a handful of appends. Two seconds is what a slow
    /// share needs for those and what a person does not notice on exit — the plan round pushed back
    /// on three, and was right that this is paid by the release smoke, which runs a real
    /// <c>initialize</c> over stdio against the published binary and then exits.
    /// </remarks>
    internal static readonly TimeSpan DrainBudget = TimeSpan.FromSeconds(2);

    private readonly ManualResetEventSlim _idle = new(initialState: true);

    private readonly Task _writing;

    private int _inFlight;

    /// <summary>
    /// Volatile because <see cref="Drain"/> writes it and <see cref="Offer"/> reads it from another
    /// thread with no lock between them. Without it a late offer can see a stale <c>false</c> and be
    /// refused with the wrong reason — which is the whole point of the field. (The code round.)
    /// </summary>
    private volatile bool _closing;

    internal NoticeWriter(Func<ResolvedDataDir, ServerNotice, bool> append)
    {
        _append = append;
        _writing = Task.Run(Draining);
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
        Safely(() => log.Warning(Why(), Depth));

        return false;
    }

    /// <summary>
    /// Why an offer was refused — and never the wrong reason.
    /// </summary>
    /// <remarks>
    /// After <see cref="Drain"/> the channel is closed, so a late offer is refused for a completely
    /// different reason than a full queue. Saying "256 are already waiting and the disk is not
    /// answering" there would be a lie in a log, which is worse than silence. (The plan round.)
    /// </remarks>
    private string Why() => _closing
        ? "a notice arrived after the writer was closing, and was not written"
        : "a notice was dropped: {Depth} are already waiting and the disk is not answering";

    /// <summary>
    /// Closes the queue and waits for the writer, up to <paramref name="within"/>. Never throws.
    /// </summary>
    /// <remarks>
    /// <para><b>It waits on the TASK, not on the count.</b> Three findings of the plan round were the
    /// same thing: <see cref="Idle"/> watches <c>_inFlight</c>, which an append that hangs never
    /// decrements — so a wait on the count both fails to end and fails to tell anyone that the
    /// draining task is still holding the file while the process leaves.</para>
    /// <para><b>What it answers is UNRESOLVED, not lost.</b> codex: a record dequeued and mid-append
    /// may already be on disk when the bound expires, so the count is what this writer cannot
    /// account for — and that is what the warning says.</para>
    /// <para><b>And zero means the QUEUE emptied, not that every notice reached the disk.</b> An
    /// append the disk refused was already reported by <see cref="Said"/> when it happened; a late
    /// offer refused after the close was reported by <see cref="Offer"/>. Zero here is the absence of
    /// anything still outstanding, which is the only thing a drain can honestly claim. (The code
    /// round, gemini.)</para>
    /// <para><b>What remains after a timeout</b> is a task still blocked in a synchronous append.
    /// There is no cancellation for that — the append is one kernel write to a share that has
    /// stopped answering — so the process leaves with it outstanding. Surviving that is story 3.1's
    /// run marker, not a property a writer can have.</para>
    /// <para><b>Honest about the test.</b> Swapping this back to <c>Idle(within)</c> leaves every one
    /// of story 2.3.1's tests GREEN — measured, by doing it — because with today's
    /// <see cref="Draining"/> the count reaching zero and the task completing are the same moment,
    /// and <see cref="Wrote"/> catches everything so the task cannot fault. The task wait is kept
    /// anyway: it is strictly stronger, it is what stays correct when somebody adds work after the
    /// loop or a path that lets the task fault, and the alternative is a method whose contract says
    /// "waits for the writer" while waiting for a counter. What is NOT claimed is that a test would
    /// catch the swap today.</para>
    /// </remarks>
    internal int Drain(TimeSpan within)
    {
        _closing = true;
        _queue.Writer.TryComplete();

        return Waited(within) ? 0 : Volatile.Read(ref _inFlight);
    }

    /// <summary>
    /// Whether the writer finished — and never an exception, which the contract above promises.
    /// </summary>
    /// <remarks>
    /// <c>Task.Wait</c> RETHROWS a faulted task as an <c>AggregateException</c>, and this is called
    /// from a <c>finally</c> during process exit: a throw there skips the warning that says what was
    /// unresolved and turns a clean <c>return 0</c> into a crash. The task cannot fault today —
    /// <see cref="Wrote"/> catches everything — which is exactly why the guard is cheap and why the
    /// docstring's "never throws" has to be true of the code and not of today's call graph. (gemini,
    /// on the code round.)
    /// </remarks>
    private bool Waited(TimeSpan within)
    {
        try
        {
            return _writing.Wait(within);
        }
        catch (Exception)
        {
            // A faulted writer is a writer that is finished, and its own catch already reported why.
            return true;
        }
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
