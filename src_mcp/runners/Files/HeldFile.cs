using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using CoaiMcp.Runners.Platform;

namespace CoaiMcp.Runners.Files;

/// <summary>
/// A lock that is a FILE HELD OPEN with <see cref="FileShare.None"/> — the one primitive every cross-process
/// lock here rests on.
/// </summary>
/// <remarks>
/// <para><b>The atomic operation is the exclusive open.</b> It either returns a handle nobody else can open
/// or fails, and the kernel releases the handle when the holder dies — a kill, a crash, a power cut — so
/// there is no stale lock to break, no pid to trust and no rename. A holder that is merely paused keeps
/// holding it, which is the property a heartbeat-fenced lock cannot have (the risk consultation of
/// PLAN_the_consultant_works_on_every_vendor.md, faa596bf, 2026-10-03).</para>
/// <para><b>Extracted, not written a fourth time.</b> <c>EngineLease</c> (one local engine across every
/// process), <c>SessionTurn</c> (one session file's readers and writers), <c>RepositoryLock</c> (one
/// consultation per checkout) each held their own copy of this open and its two catches; the consultant
/// check's lock (epic 4) would have been the fourth.</para>
/// <para><b>The residual, stated where a caller reads it.</b> On Windows the share mode is enforced by the
/// kernel. On Unix .NET implements <see cref="FileShare.None"/> as an ADVISORY <c>flock</c>: it is honoured
/// between .NET processes — every holder and reader here is one — but not by a program that does not ask,
/// and not across the WSL/Windows 9P boundary, where a Windows process and a WSL process opening one file
/// through <c>\\wsl$</c> do not see each other's lock. Every lock here guards state on ONE side, so the
/// boundary is never crossed by a writer.</para>
/// <para><b>An inheritable hold is held by every process that inherits the handle</b> until the last of them is gone — the property
/// the Kimi tree hold wants (decision 10 of E4.2, <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>), and the reason it is never the
/// default: .NET's non-shell <c>Process.Start</c> passes <c>bInheritHandles = TRUE</c> and no handle list, so EVERY child started
/// while the hold is open inherits it, redirected or not — which is also why no TEST ever holds one in the test host (S2.43 holds it
/// in a child). <see cref="FileShare.Inheritable"/> is not a share mode but the <c>bInheritHandle</c> of the handle's security
/// attributes, so the open stays exclusive.</para>
/// <para><b>On Linux the inheritable take is native</b> (<see cref="LinuxHeldFile"/>): <c>open</c> without <c>O_CLOEXEC</c> and <c>flock(LOCK_EX|LOCK_NB)</c>, because .NET's combined share mode was measured to take a
/// SHARED lock there (L-N1, CI run 38923199105; <c>research/RESULTS_kimi_confinement.md</c> §30) — a second inheritable take succeeded. Every other take, inheritable or not, on any host, is .NET's.</para>
/// </remarks>
public static class HeldFile
{
    /// <summary>The file, held exclusively — or null when somebody already holds it, or it cannot be opened.</summary>
    /// <remarks>
    /// Created when it does not exist; its directory must. The two nulls are one answer here on purpose — the engine
    /// lease, the session turn and the repository lock all treat "cannot open" as "wait / not now" — and
    /// <see cref="Take"/> keeps them apart for a caller that must not.
    /// </remarks>
    public static FileStream? TryHold(string path) => Take(path) is Hold.Taken taken ? taken.Stream : null;

    /// <summary>The file held — or <see cref="Hold.Busy"/> when somebody holds it, <see cref="Hold.Unusable"/> when it cannot be opened at all.</summary>
    /// <remarks>
    /// No access, a directory where the file should be, a missing directory: none of those is a holder, and a caller
    /// that read them as one answered "already running" for a check that could never run (epic 4's code round). Any
    /// other <see cref="IOException"/> is the share-mode refusal a held file answers with — on Windows a sharing
    /// violation, on Unix the advisory <c>flock</c> .NET takes.
    /// </remarks>
    public static Hold Take(string path, bool inheritable = false) =>
        inheritable && !InheritableSupported(HostKinds.Of(OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), OperatingSystem.IsMacOS(), wsl: false))
            ? UnsupportedInheritable()
            : inheritable && OperatingSystem.IsLinux() ? LinuxHeldFile.TakeInheritable(path) : TakenByDotNet(path, inheritable);

    /// <summary>Whether an EXCLUSIVE inheritable hold exists on <paramref name="host"/>: Windows (the share mode is the kernel's), Linux and WSL (the native <c>flock</c>) — <see cref="HostKinds.HasKimiLockFamily"/>, the one predicate. Nowhere else: .NET's own combined share mode is a shared lock on Unix.</summary>
    internal static bool InheritableSupported(HostKind host) => HostKinds.HasKimiLockFamily(host);

    /// <summary>The answer on a host with no exclusive inheritable hold: unusable, never .NET's shared lock (two holders of the sign-in would be let in — two refreshers of a rotating token).</summary>
    internal static Hold UnsupportedInheritable() =>
        new Hold.Unusable(new PlatformNotSupportedException("an exclusive inheritable hold exists on Windows and Linux only; .NET's FileShare.None | Inheritable is a shared lock on any other host (L-N1)"));

    private static Hold TakenByDotNet(string path, bool inheritable)
    {
        try
        {
            return new Hold.Taken(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, inheritable ? FileShare.None | FileShare.Inheritable : FileShare.None));
        }
        catch (Exception e) when (e is UnauthorizedAccessException or DirectoryNotFoundException or PathTooLongException)
        {
            return new Hold.Unusable(e);
        }
        catch (IOException)
        {
            return new Hold.Busy();
        }
    }

    /// <summary>The most times a taker whose file was unlinked under it retakes the new one before it refuses.</summary>
    public const int MostRetakes = 3;

    /// <summary>
    /// Linux: <see cref="TakeWithin"/>, and then verified — the file taken is still THE file at <paramref name="path"/> (<c>fstat</c> of the descriptor against <c>statx</c> of the path,
    /// <see cref="FileStatus.SameFileAs"/>). A mismatch means a retirement sweep unlinked the file between the open and the lock, so the taker holds an inode nobody else will ever open — two holders of one
    /// sign-in; it releases and takes again, at most <see cref="MostRetakes"/> times, then refuses. Elsewhere exactly <see cref="TakeWithin"/>: a file held open on Windows cannot be unlinked.
    /// </summary>
    public static Hold TakeVerified(string path, TimeSpan within, bool inheritable = false) => TakeVerified(path, within, inheritable, null);

    /// <summary>The same, with a hook told the path right after each open and lock, so a test can unlink the file in the window the guard exists for.</summary>
    internal static Hold TakeVerified(string path, TimeSpan within, bool inheritable, Action<string>? afterOpen) =>
        OperatingSystem.IsLinux() ? Verified(path, within, inheritable, afterOpen) : TakeWithin(path, within, inheritable);

    [SupportedOSPlatform("linux")]
    private static Hold Verified(string path, TimeSpan within, bool inheritable, Action<string>? afterOpen)
    {
        for (var attempt = 0; attempt < MostRetakes; attempt++)
        {
            var (hold, replaced) = Attempted(path, within, inheritable, afterOpen);
            if (!replaced)
            {
                return hold;
            }
        }

        return new Hold.Unusable(new IOException($"the lock file {path} was replaced under its holder three times in a row — refused"));
    }

    /// <summary>One take and its verification: the hold, and whether the file was replaced under it (the hold is then already given back).</summary>
    [SupportedOSPlatform("linux")]
    private static (Hold Hold, bool Replaced) Attempted(string path, TimeSpan within, bool inheritable, Action<string>? afterOpen)
    {
        var hold = TakeWithin(path, within, inheritable);
        if (hold is not Hold.Taken taken)
        {
            return (hold, false);
        }

        afterOpen?.Invoke(path);
        if (StillTheFile(taken.Stream.SafeFileHandle, path))
        {
            return (hold, false);
        }

        taken.Stream.Dispose();

        return (hold, true);
    }

    [SupportedOSPlatform("linux")]
    internal static bool StillTheFile(SafeFileHandle handle, string path) =>
        LinuxStatx.OfHandle(handle, LinuxStatx.StatxBasic) is StatxRead.Read held
        && LinuxStatx.OfPath(path, LinuxStatx.AtSymlinkNoFollow, LinuxStatx.StatxBasic) is StatxRead.Read named
        && held.Status.SameFileAs(named.Status);

    /// <summary>
    /// Whether somebody holds the file RIGHT NOW — asked by trying the exclusive open, non-blocking, and letting
    /// go at once. A file that does not exist is held by nobody; a file this reader cannot open is held, as before.
    /// </summary>
    /// <remarks>
    /// The probe itself holds the file for the length of one open, so a holder acquiring at that instant can
    /// be refused once: an acquirer that must not mistake a reader for a holder retries for a moment
    /// (<see cref="TakeWithin"/>). A reader that must tell "held" from "cannot tell" asks <see cref="Probe"/>.
    /// </remarks>
    public static bool IsHeld(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        using var probe = TryHold(path);

        return probe is null;
    }

    /// <summary>What a non-blocking try of the lock says: free, held, or — this reader cannot open it — unknown.</summary>
    public static LockProbe Probe(string path) =>
        File.Exists(path) || Directory.Exists(path) ? Probed(Take(path)) : LockProbe.Free;

    /// <summary>
    /// <see cref="Take"/>, retried with a short jitter while the file is BUSY until <paramref name="within"/> has
    /// passed — so a reader's momentary probe is waited out, while a real holder (who holds for minutes) still
    /// refuses. An unusable file is answered at once: waiting does not make it openable.
    /// </summary>
    public static Hold TakeWithin(string path, TimeSpan within, bool inheritable = false)
    {
        var until = DateTime.UtcNow + within;
        while (true)
        {
            var hold = Take(path, inheritable);
            if (hold is not Hold.Busy || DateTime.UtcNow >= until)
            {
                return hold;
            }

            Thread.Sleep(Random.Shared.Next(2, 8));
        }
    }

    private static LockProbe Probed(Hold hold)
    {
        switch (hold)
        {
            case Hold.Taken taken:
                taken.Stream.Dispose();
                return LockProbe.Free;
            case Hold.Busy:
                return LockProbe.Held;
            default:
                return LockProbe.Unknown;
        }
    }
}

/// <summary>The outcome of one exclusive open — a closed union, so "busy" and "cannot open" are never one answer.</summary>
public abstract record Hold
{
    /// <summary>Held: the caller owns the stream and releases the lock by disposing it.</summary>
    public sealed record Taken(FileStream Stream) : Hold;

    /// <summary>Somebody else holds it.</summary>
    public sealed record Busy : Hold;

    /// <summary>It cannot be opened at all — no access, not a file, no directory — and why.</summary>
    public sealed record Unusable(Exception Cause) : Hold;

    private Hold() { }
}

/// <summary>What a reader learned by trying a lock.</summary>
public enum LockProbe
{
    /// <summary>Nobody holds it — or it does not exist.</summary>
    Free,

    /// <summary>Somebody holds it.</summary>
    Held,

    /// <summary>This reader cannot open it at all (no access, not a file) — so it cannot tell.</summary>
    Unknown,
}
