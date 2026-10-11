using System.Diagnostics;
using CoaiMcp.Runners.Processes;

namespace CoaiServer;

/// <summary>Reads a vendor's own gauge on a slot, inside that slot's lease, after a job.</summary>
public interface IGaugeCapture
{
    /// <summary>
    /// Captures the gauge on <paramref name="slot"/> — which the caller HOLDS the lease of — and says what came of it.
    /// Never throws: a capture failure never changes the finished job's result.
    /// </summary>
    Task<GaugeCaptured> CaptureAsync(VendorConfig vendor, AccountSlot slot, CancellationToken ct);
}

/// <summary>A capture that captures nothing — the default, and what every test of something else gets.</summary>
public sealed class NoGaugeCapture : IGaugeCapture
{
    public static readonly NoGaugeCapture Instance = new();

    public Task<GaugeCaptured> CaptureAsync(VendorConfig vendor, AccountSlot slot, CancellationToken ct) =>
        Task.FromResult<GaugeCaptured>(new GaugeCaptured.Skipped(GaugeCodes.SlotNotReadable));
}

/// <summary>
/// The codex gauge read (story 6.1a of todo/PLAN_every_round_is_counted.md): <c>codex app-server</c> ·
/// <c>account/rateLimits/read</c>, run on the slot's own token set — under the lease the caller already holds, with the
/// containment the epic-6 risk consultation fixed, in its order.
/// </summary>
/// <remarks>
/// <para><b>The order is the design.</b> (1) The caller holds the lock, and <c>SlotRegistry.AcquireAsync</c> reconciled
/// the previous holder's marker before it handed the lease out. (2) Nothing is spawned unless the pre-spawn guards pass:
/// a codex slot, not signed out, not blocked, not suspended; a platform that can list a reader's descendants; an access
/// token whose expiry is further away than the reader's lifetime plus a margin. (3) The fail-closed marker is written
/// BEFORE the spawn — no marker written, no spawn. (4) The child's identity is recorded with its start time the moment
/// it exists, by the tracker the launcher calls. (5) On completion every descendant is confirmed exited — the launcher's
/// tree discipline for a request that ends its child itself — and only then is the marker cleared; a survivor blocks the
/// slot instead, and the lease is released blocked.</para>
/// <para><b>What it reads, and never sends.</b> One request, <c>account/rateLimits/read</c>. Never
/// <c>account/rateLimitResetCredit/consume</c>, never a login, never an answer to a server's request: an authentication
/// error or a refresh request stops the capture, suspends it on the slot — its OWN state, never needs-sign-in, which
/// would make the recovery launch unschedulable — and keeps the previous reading marked stale. A later launch on the
/// slot that succeeds lifts the suspension (<c>SlotRegistry.MarkSucceeded</c>).</para>
/// <para><b>What it logs.</b> Fixed codes and allow-listed numbers — the slot's name from <c>vendors.json</c>, a
/// percentage, a window length, a code. Never a line of stdout or stderr, never an account id, never a machine name
/// (research/RESULTS_vendor_quota_gauges.md §2.6: the raw stream carries all three).</para>
/// </remarks>
/// <param name="launchers">
/// Makes the launcher the reader runs through, given the tracker that records the reader's identity on the slot —
/// <c>tracker => new ProcessLauncher(tracker)</c> in production; a fake in the tests, which still receives the tracker.
/// </param>
/// <param name="note">Where one line per capture goes — codes and numbers only.</param>
public sealed class GaugeCapture(
    SlotRegistry slots,
    Func<IProcessTracker, IProcessLauncher> launchers,
    string clientVersion,
    Action<string>? note = null) : IGaugeCapture
{
    /// <summary>How long a reader may live, start-up to kill. The measured answer arrived 0.7 s after the start.</summary>
    public static readonly TimeSpan ReaderLifetime = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How far beyond the reader's lifetime the access token must still be valid before a reader is spawned.
    /// </summary>
    /// <remarks>
    /// codex 0.160.0 refreshes proactively when the token expires within FIVE minutes of a request
    /// (<c>CHATGPT_ACCESS_TOKEN_REFRESH_WINDOW_MINUTES</c>); ten is that window doubled, for a clock a few minutes off.
    /// </remarks>
    public static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(10);

    /// <summary>The label the reader is tracked under.</summary>
    public const string ReaderLabel = "gauge-reader";

    private const string CodexRuntime = "codex";

    private readonly Action<string> _note = note ?? (_ => { });

    public async Task<GaugeCaptured> CaptureAsync(VendorConfig vendor, AccountSlot slot, CancellationToken ct)
    {
        try
        {
            var outcome = await GuardedAsync(vendor, slot, ct);
            _note(Sentence(slot, outcome));

            return outcome;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Anything the infrastructure throws: the job this ran after is already final. The exception's type is a
            // code; its message is not. The previous reading, if any, is the last the vendor said.
            _note($"gauge {slot.Vendor}/{slot.Name}: failed ({e.GetType().Name})");
            AfterAFailedSpawn(slot);

            return new GaugeCaptured.Failed(GaugeCodes.NoAnswer);
        }
    }

    /// <summary>
    /// After the launcher threw: a marker still saying "spawning" names nothing because nothing was recorded — and the
    /// launcher ends a child it could not record before it throws — so it is cleared; a marker that names the child is
    /// KEPT for the next holder to reconcile. A marker that was never written leaves nothing to do.
    /// </summary>
    private void AfterAFailedSpawn(AccountSlot slot)
    {
        try
        {
            slots.MarkGaugeStale(slot);
            if (slots.ReaderMarkerOf(slot) is { Phase: ReaderPhases.Spawning })
            {
                slots.ClearReader(slot);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The state file would not take the write; the next holder reconciles whatever is on disk.
            _note($"gauge {slot.Vendor}/{slot.Name}: state not written ({e.GetType().Name})");
        }
    }

    private async Task<GaugeCaptured> GuardedAsync(VendorConfig vendor, AccountSlot slot, CancellationToken ct)
    {
        if (!vendor.Runtime.Equals(CodexRuntime, StringComparison.OrdinalIgnoreCase))
        {
            return new GaugeCaptured.Skipped(GaugeCodes.SlotNotReadable);
        }

        var current = slots.Read(slot.Vendor, slot.Name);
        if (current.NeedsSignIn || current.Blocked.Length > 0 || current.GaugeSuspended.Length > 0)
        {
            return new GaugeCaptured.Skipped(GaugeCodes.SlotNotReadable);
        }

        if (!ProcessTree.Supported)
        {
            return new GaugeCaptured.Skipped(GaugeCodes.PlatformUnsupported);
        }

        return Guard(CodexAuthExpiry.Of(slot.Directory), DateTimeOffset.UtcNow) is { } skipped
            ? skipped
            : await ReadAsync(vendor, current, ct);
    }

    /// <summary>The expiry guard: an unknown expiry skips; a known one skips unless it clears the lifetime plus the margin.</summary>
    internal static GaugeCaptured? Guard(Expiry expiry, DateTimeOffset nowUtc) => expiry switch
    {
        Expiry.Unknown unknown => new GaugeCaptured.Skipped(unknown.Code),
        Expiry.Known known when known.AtUtc - nowUtc <= ReaderLifetime + ExpiryMargin => new GaugeCaptured.Skipped(GaugeCodes.TokenNearExpiry),
        _ => null,
    };

    private async Task<GaugeCaptured> ReadAsync(VendorConfig vendor, AccountSlot slot, CancellationToken ct)
    {
        // (3) The marker first. A marker that could not be written is a reader nobody could reconcile: no spawn.
        if (!slots.MarkReaderExpected(slot, Environment.ProcessId, Process.GetCurrentProcess().StartTime.ToUniversalTime()))
        {
            return new GaugeCaptured.Skipped(GaugeCodes.MarkerNotWritten);
        }

        // (4) The tracker writes the child's identity into the marker as soon as the child exists.
        var result = await launchers(new SlotReaderTracker(slots, slot)).RunAsync(Request(vendor, slot), ct);
        var observed = DateTimeOffset.UtcNow;

        // (5) Confirmed gone, or blocked. The marker clears only on confirmation.
        if (result.Survivors.Count > 0)
        {
            slots.MarkBlocked(slot, BlockedReasons.ReaderTerminationFailed);

            return new GaugeCaptured.SlotBlocked(BlockedReasons.ReaderTerminationFailed);
        }

        slots.ClearReader(slot);

        return Store(slot, result.TimedOut ? new GaugeAnswer.Failed(GaugeCodes.NoAnswer) : GaugeProtocol.Parse(result.StdOut, observed));
    }

    private GaugeCaptured Store(AccountSlot slot, GaugeAnswer answer)
    {
        switch (answer)
        {
            case GaugeAnswer.Read read:
                slots.SaveGauge(slot, read.Reading);

                return new GaugeCaptured.Read(read.Reading);
            case GaugeAnswer.Suspend suspend:
                slots.SuspendGauge(slot, suspend.Code);

                return new GaugeCaptured.Suspended(suspend.Code);
            case GaugeAnswer.Failed failed:
                slots.MarkGaugeStale(slot);

                return new GaugeCaptured.Failed(failed.Code);
            default:
                slots.MarkGaugeStale(slot);

                return new GaugeCaptured.Failed(GaugeCodes.Unreadable);
        }
    }

    /// <summary>The reader's launch: the slot's own environment, confined, ended by the answer or its lifetime.</summary>
    private ProcessRequest Request(VendorConfig vendor, AccountSlot slot) =>
        new(vendor.Runtime, ["app-server"], slot.Directory)
        {
            Environment = SlotEnvironment.For(vendor.Runtime, slot.Directory),
            InheritsEnvironment = false,
            StdIn = GaugeProtocol.Requests(clientVersion),
            Timeout = ReaderLifetime,
            EndsWhen = GaugeProtocol.Answered,
            TrackAs = ReaderLabel,
        };

    /// <summary>One line for the log: the slot by its configured name, a code, and for a reading its numbers.</summary>
    private static string Sentence(AccountSlot slot, GaugeCaptured outcome) => outcome switch
    {
        GaugeCaptured.Read read => $"gauge {slot.Vendor}/{slot.Name}: read {Numbers(read.Reading)}",
        GaugeCaptured.Skipped skipped => $"gauge {slot.Vendor}/{slot.Name}: skipped ({skipped.Code})",
        GaugeCaptured.Suspended suspended => $"gauge {slot.Vendor}/{slot.Name}: capture suspended ({suspended.Code})",
        GaugeCaptured.SlotBlocked blocked => $"gauge {slot.Vendor}/{slot.Name}: slot blocked ({blocked.Code})",
        GaugeCaptured.Failed failed => $"gauge {slot.Vendor}/{slot.Name}: no reading ({failed.Code})",
        _ => $"gauge {slot.Vendor}/{slot.Name}: no reading",
    };

    private static string Numbers(GaugeReadingDto reading) =>
        string.Join(
            ", ",
            reading.Windows.Select(window =>
                $"{window.LimitId} {window.Kind} {window.UsedPercent:0.#}% of {window.WindowDurationMins?.ToString() ?? "?"} min, resets {(window.ResetsAtUtc.Length > 0 ? window.ResetsAtUtc : "unsaid")}"));
}

/// <summary>
/// The tracker a gauge reader is launched with: every identity the launcher reports — the child, then its descendants
/// before the kill — goes into the slot's marker, and nothing is ever forgotten by it. The capture clears the marker
/// once the tree is confirmed gone; a crash before that leaves every member named for the next holder.
/// </summary>
public sealed class SlotReaderTracker(SlotRegistry slots, AccountSlot slot) : IProcessTracker
{
    public void Record(int pid, DateTime startedUtc, string label) => slots.RecordReader(slot, new ProcessIdentity(pid, startedUtc));

    public void Forget(int pid)
    {
        // Never. The marker is cleared by the capture, after the confirmation — not by a launcher's finally.
    }
}
