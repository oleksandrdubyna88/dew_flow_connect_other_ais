using System.Runtime.Versioning;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Platform;

/// <summary>
/// The three signs of a desktop keyring session on a Linux host (DN10 of <c>todo/PLAN_kimi_and_vibe_on_native_linux.md</c>), each with its own value: a D-Bus session address in the environment, a session bus
/// socket on disk, and a keyring daemon in the process listing. <b>They are RECORDED, never read by any decision</b> — the admission does not ask them, because coai closes the keyring road itself
/// (<c>PYTHON_KEYRING_BACKEND</c>, E2) instead of guessing from three signs; <c>coai-mcp --host-facts</c> prints them so a person can set them beside the facts script's own raw reading (two readings, on purpose).
/// </summary>
/// <param name="BusAddressSet"><c>DBUS_SESSION_BUS_ADDRESS</c> is set and not empty.</param>
/// <param name="BusSocket">A socket is at <c>$XDG_RUNTIME_DIR/bus</c> — or at <c>/run/user/&lt;uid&gt;/bus</c> when that variable is unset or empty.</param>
/// <param name="Daemons">The names of the keyring daemons running (<c>gnome-keyring-daemon</c>, <c>kwalletd*</c>, <c>keepassxc</c>), sorted; empty when none runs or the listing could not be read.</param>
/// <param name="Listing"><see cref="Read"/>, <see cref="CannotTell"/> (the process listing was unreadable — an empty <paramref name="Daemons"/> then says nothing) or <see cref="NotLinux"/>.</param>
public sealed record KeyringSessionFacts(bool BusAddressSet, bool BusSocket, IReadOnlyList<string> Daemons, string Listing)
{
    /// <summary>The process listing was read; <see cref="Daemons"/> is what it held.</summary>
    public const string Read = "read";

    /// <summary>The process listing could not be read: no daemon was ruled out.</summary>
    public const string CannotTell = "cannot tell";

    /// <summary>Not a Linux host: there is no session bus to look for.</summary>
    public const string NotLinux = "not linux";

    /// <summary>The three facts, read through the seams: the environment, a socket test, the process listing, and the account's uid (for the bus path when <c>XDG_RUNTIME_DIR</c> is not set).</summary>
    public static KeyringSessionFacts Of(Func<string, string?> environment, Func<string, bool> isSocket, IProcessList listing, int uid)
    {
        var (daemons, listed) = DaemonsIn(listing.Snapshot());

        return new KeyringSessionFacts(IsSet(environment, BusAddressVariable), isSocket(BusPath(environment, uid)), daemons, listed);
    }

    private const string BusAddressVariable = "DBUS_SESSION_BUS_ADDRESS";

    private const string RuntimeDirVariable = "XDG_RUNTIME_DIR";

    private static bool IsSet(Func<string, string?> environment, string name) => environment(name) is { Length: > 0 };

    /// <summary><c>$XDG_RUNTIME_DIR/bus</c>, else <c>/run/user/&lt;uid&gt;/bus</c> — written with <c>/</c> on every host, it is a Linux path.</summary>
    private static string BusPath(Func<string, string?> environment, int uid) =>
        environment(RuntimeDirVariable) is { Length: > 0 } runtimeDir ? $"{runtimeDir.TrimEnd('/')}/bus" : $"/run/user/{uid}/bus";

    private static (IReadOnlyList<string> Daemons, string Listing) DaemonsIn(ProcessListing listing) => listing switch
    {
        ProcessListing.Listed listed => ([.. listed.Processes.SelectMany(DaemonNamed).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)], Read),
        _ => ([], CannotTell),
    };

    /// <summary>The daemon a process is, by its name (the executable's, on Linux) or its <c>comm</c> (cut at fifteen characters by the kernel) — one name, or none. A lookalike (<c>keepassxc-proxy</c>) is not a daemon.</summary>
    private static IEnumerable<string> DaemonNamed(ProcessEntry process) =>
        new[] { process.Name, process.Comm }.Where(IsDaemon).Take(1);

    private static bool IsDaemon(string name) =>
        name is "gnome-keyring-daemon" or "gnome-keyring-d" or "keepassxc" || name.StartsWith("kwalletd", StringComparison.Ordinal);

    /// <summary>This process's host: the real environment, <c>statx</c> for the socket, the host's process listing and this account's uid — or <see cref="NotLinux"/> off Linux.</summary>
    public static KeyringSessionFacts ThisHost() => OperatingSystem.IsLinux()
        ? Of(Environment.GetEnvironmentVariable, IsSocket, HostProcessList.Current, (int)ProcReads.Real.OwnUid())
        : new KeyringSessionFacts(false, false, [], NotLinux);

    [SupportedOSPlatform("linux")]
    private static bool IsSocket(string path) => LinuxStatx.OfPath(path, 0, LinuxStatx.StatxType) is StatxRead.Read { Status.IsSocket: true };
}
