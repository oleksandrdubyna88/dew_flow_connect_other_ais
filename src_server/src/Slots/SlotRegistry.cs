namespace CoaiServer;

/// <summary>
/// A held account. Releasing it is closing the file handle, which the kernel also does if this
/// process dies — so an account cannot be stranded by a crash.
/// </summary>
public sealed class SlotLease(AccountSlot slot, FileStream handle, Action<AccountSlot> onRelease) : IDisposable
{
    public AccountSlot Slot { get; } = slot;

    private bool _released;

    public void Dispose()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        handle.Dispose();
        onRelease(Slot);
    }
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
public sealed class SlotRegistry(string dataDir, JsonFileStore files)
{
    /// <summary>How long <c>login</c> and a job wait for a busy account before giving up.</summary>
    /// <remarks>
    /// Bounded rather than infinite: an operator who ran <c>login</c> and sees nothing cannot tell a
    /// held lock from a hung command, and "it printed nothing for an hour" is indistinguishable from
    /// a broken server. Raised twice on the plan round.
    /// </remarks>
    public static readonly TimeSpan DefaultWait = TimeSpan.FromMinutes(2);

    private readonly string _root = Path.Combine(dataDir, "accounts");

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
            state?.ConsecutiveCooldowns ?? 0);
    }

    /// <summary>Take the account, waiting up to <paramref name="wait"/> for whoever holds it.</summary>
    /// <returns>The lease, or null when the wait ran out.</returns>
    public async Task<SlotLease?> AcquireAsync(
        AccountSlot slot, TimeSpan? wait = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(slot.Directory);
        Restrict(slot.Directory);
        var deadline = DateTimeOffset.UtcNow + (wait ?? DefaultWait);

        while (true)
        {
            if (TryOpen(LockPath(slot.Directory)) is { } handle)
            {
                return new SlotLease(slot, handle, Touch);
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                return null;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }
    }

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
    /// limited three times last week would start its next back-off at four hours.
    /// </remarks>
    public void MarkSucceeded(AccountSlot slot) =>
        Save(slot, s => s with
        {
            CooldownUntilUtc = null,
            ConsecutiveCooldowns = 0,
            NeedsSignIn = false,
            Note = string.Empty,
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
    private static void Restrict(string directory)
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
            // A bind-mounted volume owned by another uid cannot be chmod'ed by us. Not fatal — the
            // deployment check is where that is caught, and failing to start over it would be worse.
        }
    }

    private static string ReadTextOrEmpty(string path)
    {
        try
        {
            return path.Length > 0 && File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private void Touch(AccountSlot slot) => Save(slot, s => s with { LastUsedUtc = DateTimeOffset.UtcNow });

    private void Save(AccountSlot slot, Func<SlotStateDto, SlotStateDto> change)
    {
        var path = StatePath(slot.Directory);
        var current = files.Read(path, ServerJsonContext.Default.SlotStateDto)
            ?? new SlotStateDto(slot.CooldownUntilUtc, slot.NeedsSignIn, slot.Note, slot.LastUsedUtc, slot.ConsecutiveCooldowns);
        files.Write(path, change(current), ServerJsonContext.Default.SlotStateDto);
    }
}
