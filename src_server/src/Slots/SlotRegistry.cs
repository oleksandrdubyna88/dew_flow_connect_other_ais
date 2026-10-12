using CoaiMcp.Runners.Reviewers;

namespace CoaiServer;

/// <summary>
/// A held account. Releasing it is closing the file handle, which the kernel also does if this
/// process dies — so an account cannot be stranded by a crash.
/// </summary>
public sealed class SlotLease(AccountSlot slot, FileStream handle, Action<AccountSlot> onRelease) : IDisposable
{
    public AccountSlot Slot { get; } = slot;

    private bool _released;

    /// <summary>Write the account's state, THEN let go of it.</summary>
    /// <remarks>
    /// <para>The order is the point, and it was the other way round until this change's code round.
    /// Closing the handle first opens a window in which another process takes the account, reads the
    /// state, and writes it back — while this lease is still about to write its own. The later write
    /// wins, and what it overwrites is whatever the other process just recorded: a fresh cooldown, or
    /// a needs-sign-in. The account then looks ready and is picked again immediately, which is the
    /// exact outcome the lock exists to prevent.</para>
    /// <para>The release is in a <c>finally</c> so a failure to persist cannot strand the account.
    /// Losing a <c>LastUsedUtc</c> costs one round of unfair scheduling; never releasing the handle
    /// costs the account until the process exits.</para>
    /// </remarks>
    public void Dispose()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        try
        {
            onRelease(Slot);
        }
        finally
        {
            handle.Dispose();
        }
    }
}
/// <summary>What taking an account answered: the lease, a holder that would not let go in time, or a barrier.</summary>
/// <remarks>
/// A closed set rather than a nullable lease, since story 6.1a of todo/PLAN_every_round_is_counted.md: "busy" and
/// "unavailable until recovery proves it safe" have different cures — one is a wait, the other a look at the box —
/// and <c>login</c> has to say which it met.
/// </remarks>
public abstract record Acquisition
{
    private Acquisition()
    {
    }

    /// <summary>The account is held by the caller, and the previous holder's reader marker, if any, was reconciled.</summary>
    public sealed record Taken(SlotLease Lease) : Acquisition;

    /// <summary>Another process holds it, and the wait ran out.</summary>
    public sealed record Busy : Acquisition;

    /// <summary>The lock opened and the barrier refused: a reader marker that could not be cleared, for this <see cref="BlockedReasons"/> code.</summary>
    public sealed record Blocked(string Code) : Acquisition;
}

/// <summary>
/// The accounts on disk: who is free, who is rate-limited, who is signed out — and the exclusion
/// that stops two processes using one account at once.
/// </summary>
/// <remarks>
/// <para><b>The lock is a file handle, because the other holder is another PROCESS.</b> A review runs
/// inside this server; <c>login</c> arrives as <c>docker compose exec coai-server login codex a</c>,
/// a separate process that an in-memory flag cannot see at all. <see cref="FileShare.None"/> is
/// enforced by the kernel across processes on both platforms, and it is released when the handle
/// closes — including when the holder is killed. That is the same exclusion
/// <c>EngineLease</c> already uses for the GPU in this repository.</para>
/// <para><b>Why this matters more than it looks.</b> Each of these CLIs refreshes its own OAuth token
/// in place. Two processes rotating one refresh token race, and the loser is left holding a token
/// the vendor has already invalidated — <c>invalid_grant</c>, and the account is signed out until a
/// human goes back to the VM. The lock is the only thing preventing that, which is why concurrency
/// is an invariant here and not a setting.</para>
/// <para><b>A leftover <c>.lock</c> file blocks nothing.</b> The file is an empty inode; only the
/// HANDLE carries the lock, and a dead process has no handles. The plan round argued for an age
/// threshold that would break a lock held longer than N minutes — that was declined, because it
/// would break a lock whose holder is alive and working, which is precisely the race this exists to
/// prevent. What a caller gets instead is a bounded wait and a message naming the holder.</para>
/// </remarks>
/// <param name="reconcile">
/// How a previous holder's reader marker is reconciled — <see cref="ReaderReconcile.RunAsync(ReaderMarkerDto)"/> with the
/// real process control by default; a test hands in one whose kills keep failing, which no test machine can make for real.
/// </param>
public sealed class SlotRegistry(
    string dataDir,
    JsonFileStore files,
    Action<string, Exception>? onFailure = null,
    Func<ReaderMarkerDto, Task<Reconciled>>? reconcile = null)
{
    private readonly Func<ReaderMarkerDto, Task<Reconciled>> _reconcile = reconcile ?? ReaderReconcile.RunAsync;
    /// <summary>How long <c>login</c> and a job wait for a busy account before giving up.</summary>
    /// <remarks>
    /// Bounded rather than infinite: an operator who ran <c>login</c> and sees nothing cannot tell a
    /// held lock from a hung command, and "it printed nothing for an hour" is indistinguishable from
    /// a broken server. Raised twice on the plan round.
    /// </remarks>
    public static readonly TimeSpan DefaultWait = TimeSpan.FromMinutes(2);

    private readonly string _root = Path.Combine(dataDir, "accounts");

    /// <summary>Where a swallowed filesystem failure goes, so an operator can see it.</summary>
    /// <remarks>
    /// Every catch below returns a safe fallback AND reports. The repository rule is explicit that an
    /// error is never silently swallowed, and these are the cases where it matters most: a directory
    /// that could not be locked down still holds OAuth credentials, and a token file that could not be
    /// read is indistinguishable from one that is absent unless somebody says so. (codex, code round.)
    /// </remarks>
    private readonly Action<string, Exception> _report = onFailure ?? ((_, _) => { });

    /// <summary>The directory this vendor's accounts live in.</summary>
    public string DirectoryFor(string vendor, string slot) => Path.Combine(_root, vendor, slot);

    /// <summary>Every configured account of one vendor, with its persisted state.</summary>
    /// <remarks>
    /// Driven by <c>vendors.json</c> rather than by what is on disk: a slot the operator named but
    /// never signed in must still appear, as <see cref="AccountSlot.NeedsSignIn"/>, or the catalog
    /// would silently show fewer accounts than were configured and nobody would know to sign one in.
    /// </remarks>
    public IReadOnlyList<AccountSlot> SlotsOf(VendorConfig vendor) =>
        [.. vendor.Slots.Select(name => Read(vendor.Id, name))];

    /// <summary>One account's state as last persisted.</summary>
    public AccountSlot Read(string vendor, string slot)
    {
        var directory = DirectoryFor(vendor, slot);
        var state = files.Read(StatePath(directory), ServerJsonContext.Default.SlotStateDto);

        return new AccountSlot(
            vendor,
            slot,
            directory,
            state?.LastUsedUtc ?? DateTimeOffset.MinValue,
            state?.CooldownUntilUtc,
            // No state file at all means nobody has ever signed this account in — which IS
            // needs-sign-in, and saying so is how the catalog tells the operator what to do next.
            state?.NeedsSignIn ?? !SignedIn(directory),
            state?.Note ?? string.Empty,
            state?.ConsecutiveCooldowns ?? 0,
            // Absent on an older file, and absent means none (doctrine §4a: an omitted field is null).
            state?.Blocked ?? string.Empty,
            state?.GaugeSuspended ?? string.Empty,
            state?.Gauge);
    }

    /// <summary>Take the account, waiting up to <paramref name="wait"/> for whoever holds it.</summary>
    /// <returns>The lease, busy when the wait ran out, or blocked when the barrier refused.</returns>
    /// <param name="onWaiting">
    /// Called once, if the account is busy on the first attempt. `login` uses it to say so: an
    /// operator staring at a terminal that has printed nothing for two minutes cannot tell a held
    /// lock from a hung command. (gemini, code round.)
    /// </param>
    /// <remarks>
    /// <b>The barrier is here, under the lock, for every caller</b> — a job, <c>login</c>, the startup sweep (story 6.1a
    /// of todo/PLAN_every_round_is_counted.md). The moment the lock opens, the previous holder's reader marker, if there
    /// is one, is reconciled by <see cref="ReaderReconcile"/>: what it names is killed and confirmed gone before the
    /// lease is usable, and a marker that cannot be cleared keeps the handle closed and the account blocked. A startup
    /// sweep alone could not do this: the lease used to be handed back the moment the lock opened.
    /// </remarks>
    public async Task<Acquisition> AcquireAsync(
        AccountSlot slot,
        TimeSpan? wait = null,
        Action<TimeSpan>? onWaiting = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(slot.Directory);
        Restrict(slot.Directory);
        var budget = wait ?? DefaultWait;
        var deadline = DateTimeOffset.UtcNow + budget;
        var announced = false;

        while (true)
        {
            if (TryOpen(LockPath(slot.Directory)) is { } handle)
            {
                return await BehindTheBarrierAsync(slot, handle, ct);
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                return new Acquisition.Busy();
            }

            if (!announced)
            {
                announced = true;
                onWaiting?.Invoke(budget);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }
    }

    /// <summary>The lock is open: reconcile what the previous holder left, then hand the lease out or keep the handle closed.</summary>
    private async Task<Acquisition> BehindTheBarrierAsync(AccountSlot slot, FileStream handle, CancellationToken ct)
    {
        try
        {
            // Between opening the handle and handing back a lease, the caller can be
            // cancelled — and an unowned open handle holds the account until this process
            // exits, with nobody able to release it because nobody has the lease.
            // (gemini, code round.)
            ct.ThrowIfCancellationRequested();

            if (await ReconcileReaderAsync(slot) is { } code)
            {
                handle.Dispose();

                return new Acquisition.Blocked(code);
            }

            return new Acquisition.Taken(new SlotLease(slot, handle, Touch));
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>The previous holder's reader marker, reconciled: null when the lease may be used, else the blocking code.</summary>
    /// <remarks>
    /// Read from disk here, not from the <see cref="AccountSlot"/> the caller holds: that value may be seconds old, and
    /// the marker is the one fact that must be current. A blocked slot without a marker is a leftover of nothing and is
    /// cleared; a marker is reconciled on EVERY acquisition, so a block over a member that later died by itself lifts.
    /// </remarks>
    private async Task<string?> ReconcileReaderAsync(AccountSlot slot)
    {
        var state = files.Read(StatePath(slot.Directory), ServerJsonContext.Default.SlotStateDto);
        if (state?.Reader is not { } marker)
        {
            if (state?.Blocked is { Length: > 0 })
            {
                Save(slot, s => s with { Blocked = string.Empty });
            }

            return null;
        }

        switch (await _reconcile(marker))
        {
            case Reconciled.Blocked blocked:
                Save(slot, s => s with { Blocked = blocked.Code });
                _report($"{slot.Vendor}/{slot.Name} stays unavailable: {blocked.Code}", new InvalidOperationException(blocked.Code));

                return blocked.Code;
            default:
                Save(slot, s => s with { Reader = null, Blocked = string.Empty });

                return null;
        }
    }

    /// <summary>
    /// Writes the fail-closed "a reader may exist" marker — BEFORE the reader is spawned (story 6.1a). False when it
    /// could not be written, which the caller reads as "no spawn".
    /// </summary>
    public bool MarkReaderExpected(AccountSlot slot, int ownerPid, DateTime ownerStartedUtc)
    {
        try
        {
            Save(slot, s => s with { Reader = new ReaderMarkerDto(ReaderPhases.Spawning, ownerPid, ownerStartedUtc, DateTimeOffset.UtcNow) });

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _report($"the reader marker of {slot.Vendor}/{slot.Name} could not be written, so no reader was started", e);

            return false;
        }
    }

    /// <summary>Adds one process the reader's launcher reported — the child, then each descendant before the kill — to the marker.</summary>
    public void RecordReader(AccountSlot slot, CoaiMcp.Runners.Processes.ProcessIdentity identity) =>
        Save(slot, s => s with { Reader = Running(s.Reader, identity) });

    /// <summary>The marker with one more process in its tree, phase Running — a fresh Spawning marker when none was written.</summary>
    private static ReaderMarkerDto Running(ReaderMarkerDto? reader, CoaiMcp.Runners.Processes.ProcessIdentity identity)
    {
        var marker = reader ?? new ReaderMarkerDto(ReaderPhases.Spawning, Environment.ProcessId, DateTime.UtcNow, DateTimeOffset.UtcNow);

        return marker with { Phase = ReaderPhases.Running, Tree = [.. (reader?.Tree ?? []), identity] };
    }

    /// <summary>The reader's tree is confirmed gone: the marker is cleared, and with it any block.</summary>
    public void ClearReader(AccountSlot slot) =>
        Save(slot, s => s with { Reader = null, Blocked = string.Empty });

    /// <summary>The reader marker on the slot's state as it is on disk now, or null when none is.</summary>
    public ReaderMarkerDto? ReaderMarkerOf(AccountSlot slot) =>
        files.Read(StatePath(slot.Directory), ServerJsonContext.Default.SlotStateDto)?.Reader;

    /// <summary>The account is unavailable until recovery proves it safe — the marker is KEPT for the next reconcile.</summary>
    public void MarkBlocked(AccountSlot slot, string code) =>
        Save(slot, s => s with { Blocked = code });

    /// <summary>A reading arrived: the one overwritten snapshot.</summary>
    public void SaveGauge(AccountSlot slot, GaugeReadingDto reading) =>
        Save(slot, s => s with { Gauge = reading with { Stale = false } });

    /// <summary>A capture produced no reading: the previous one, if any, is the last the vendor said and is marked so.</summary>
    public void MarkGaugeStale(AccountSlot slot) =>
        Save(slot, s => s with { Gauge = s.Gauge is { } reading ? reading with { Stale = true } : null });

    /// <summary>
    /// The read met an authentication error or a refresh request: capture is suspended on this account — its OWN
    /// state, never needs-sign-in — and the previous reading is marked stale. Lifted by <see cref="MarkSucceeded"/>.
    /// </summary>
    public void SuspendGauge(AccountSlot slot, string code) =>
        Save(slot, s => s with
        {
            GaugeSuspended = code,
            GaugeSuspendedUtc = DateTimeOffset.UtcNow,
            Gauge = s.Gauge is { } reading ? reading with { Stale = true } : null,
        });

    /// <summary>Record that the vendor rate-limited this account, and until when.</summary>
    public void MarkCoolingDown(AccountSlot slot, string reason, DateTimeOffset nowUtc)
    {
        var consecutive = slot.ConsecutiveCooldowns + 1;
        Save(slot, s => s with
        {
            CooldownUntilUtc = CooldownParser.Until(reason, nowUtc, slot.ConsecutiveCooldowns),
            ConsecutiveCooldowns = consecutive,
            Note = reason,
        });
    }

    /// <summary>
    /// Record that the CLI said this account is not signed in.
    /// </summary>
    /// <remarks>
    /// The transition the plan round found missing: <c>needs-signin</c> was only ever described as
    /// being CLEARED. Without this, an account whose credentials are revoked externally stays
    /// "ready" for ever, is selected for every job, and fails every one of them — the catalog saying
    /// it is fine the whole time. The caller recognises the failure with
    /// <c>VendorDiagnosis.For</c> rather than matching text here.
    /// </remarks>
    public void MarkNeedsSignIn(AccountSlot slot, string reason) =>
        Save(slot, s => s with { NeedsSignIn = true, Note = reason });

    /// <summary>A job finished on this account without a refusal — it is healthy.</summary>
    /// <remarks>
    /// Clears the cooldown counter as well as the cooldown. Without that, an account that was
    /// limited three times last week would start its next back-off at four hours. And it lifts a suspended gauge
    /// capture (story 6.1a): a launch that succeeded on this token set is the proof the read's authentication error
    /// is behind it.
    /// </remarks>
    public void MarkSucceeded(AccountSlot slot) =>
        Save(slot, s => s with
        {
            CooldownUntilUtc = null,
            ConsecutiveCooldowns = 0,
            NeedsSignIn = false,
            Note = string.Empty,
            GaugeSuspended = null,
            GaugeSuspendedUtc = null,
        });

    /// <summary>A human signed this account in. The only thing that clears needs-sign-in.</summary>
    public void MarkSignedIn(AccountSlot slot) =>
        Save(slot, s => s with { NeedsSignIn = false, Note = string.Empty });

    /// <summary>The long-lived token this slot was given instead of a sign-in, or empty.</summary>
    public string TokenFor(AccountSlot slot, string runtime)
    {
        var name = SlotEnvironment.TokenFileName(runtime);

        return ReadTextOrEmpty(name.Length == 0 ? string.Empty : Path.Combine(slot.Directory, name));
    }

    private static string LockPath(string directory) => Path.Combine(directory, ".lock");

    private static string StatePath(string directory) => Path.Combine(directory, "state.json");

    /// <summary>Has anything ever been written into this account's HOME?</summary>
    private static bool SignedIn(string directory) =>
        Directory.Exists(directory)
        && Directory.EnumerateFileSystemEntries(directory).Any(e => !IsOurs(Path.GetFileName(e)));

    private static bool IsOurs(string name) =>
        name is ".lock" or "state.json" || name.EndsWith(".tmp", StringComparison.Ordinal);

    private static FileStream? TryOpen(string path)
    {
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            // Somebody else holds it. Not an error — it is the answer.
            return null;
        }
    }

    /// <summary>
    /// Owner-only on the account directory, because it will hold OAuth credentials.
    /// </summary>
    /// <remarks>
    /// A slot HOME accumulates <c>auth.json</c>, <c>claude.token</c> and the vendors' own caches. On a
    /// shared host, default permissions make those readable by every other user on the box. No-op on
    /// Windows, where the API does not apply and development happens.
    /// </remarks>
    private void Restrict(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // A bind-mounted volume owned by another uid cannot be chmod'ed by us. Not fatal —
            // refusing to serve over it would take the whole server down for a permission this
            // process may simply not own. But it is REPORTED, every time, naming the directory: the
            // credentials in it are readable by other users on the host until somebody fixes it, and
            // an unreported chmod failure is a silent exposure.
            _report($"{directory} could not be restricted to its owner — OAuth credentials in it may "
                + "be readable by other users on this host", e);
        }
    }

    /// <summary>The file's text, or empty when there is none.</summary>
    /// <remarks>
    /// An EXISTING file that cannot be read is reported, because it is a completely different fact
    /// from an absent one: `login` would otherwise start an interactive sign-in on an account that
    /// already has a perfectly good token it simply could not open. (codex, code round.)
    /// </remarks>
    private string ReadTextOrEmpty(string path)
    {
        if (path.Length == 0 || !File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _report($"{path} exists but could not be read, so this account is being treated as having "
                + "no token", e);

            return string.Empty;
        }
    }

    /// <summary>Record that the account was just used. Never throws — it runs inside a Dispose.</summary>
    /// <remarks>
    /// A failure here is reported and swallowed on purpose. The vendor operation has already happened
    /// by the time a lease is released; letting a disk error escape would fail the whole request over
    /// a timestamp that decides nothing, and would do it from inside a `using`. (codex, code round.)
    /// </remarks>
    private void Touch(AccountSlot slot)
    {
        try
        {
            Save(slot, s => s with { LastUsedUtc = DateTimeOffset.UtcNow });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _report($"the last-used time of {slot.Vendor}/{slot.Name} could not be written", e);
        }
    }

    private void Save(AccountSlot slot, Func<SlotStateDto, SlotStateDto> change)
    {
        var path = StatePath(slot.Directory);
        var current = files.Read(path, ServerJsonContext.Default.SlotStateDto)
            ?? new SlotStateDto(slot.CooldownUntilUtc, slot.NeedsSignIn, slot.Note, slot.LastUsedUtc, slot.ConsecutiveCooldowns);
        files.Write(path, change(current), ServerJsonContext.Default.SlotStateDto);
    }
}
