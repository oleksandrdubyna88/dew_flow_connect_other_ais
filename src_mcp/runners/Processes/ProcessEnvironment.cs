using System.Collections.Frozen;

namespace CoaiMcp.Runners.Processes;

/// <summary>
/// The names a confined launch copies from this process's environment into the child's — and
/// nothing else. Read by <see cref="ProcessLauncher"/> when
/// <see cref="ProcessRequest.InheritsEnvironment"/> is false.
/// </summary>
/// <remarks>
/// <para><b>What is in it, and why each name earned its place.</b> <c>PATH</c>, because the CLI is
/// found through it and starts its own children through it. The locale and terminal names, so the
/// child encodes and colours its output the way the parent's environment says to. The temporary
/// directory in the three spellings the platforms use. The proxy and certificate names in every
/// spelling the tools read, because a box behind a corporate proxy reaches its vendor through them,
/// and a reviewer that cannot reach its vendor is a review that never happens. <c>XDG_RUNTIME_DIR</c>
/// for the sockets and caches a Linux tool keeps there.</para>
/// <para><b><c>HOME</c> is not optional on Unix</b>, and the first draft of this list did not have it
/// (gemini, plan round of 2026-09-10, Blocking — the best finding of the round). It carried the
/// Windows profile names and none of the Unix ones, so a confined launch on Linux would have started
/// every CLI with no <c>HOME</c>; every vendor CLI here is a Node program, and a Node runtime with no
/// <c>HOME</c> fails in initialisation, before it reads a prompt. On the Team server the omission was
/// masked — the server sets <c>HOME</c> per slot on the request, and the request's variables are
/// applied last — which is exactly why it deserved to be caught in a plan rather than a deployment:
/// the masking is a property of one caller, and the launcher's contract is for all of them.
/// <c>USER</c>, <c>LOGNAME</c> and <c>SHELL</c> travel with it: a program that asks who it is running
/// as should get an answer rather than a crash.</para>
/// <para>Windows needs a set of its own to create a process at all: <c>SystemRoot</c>,
/// <c>SystemDrive</c> and <c>windir</c> for the loader; <c>ComSpec</c> and <c>PATHEXT</c> for the
/// npm <c>.cmd</c> shims every vendor CLI is on this platform; the profile and program directories,
/// which is where those shims and their caches live; and the processor names, which the runtimes
/// size their thread pools by.</para>
/// </remarks>
public static class ProcessEnvironment
{
    /// <summary>
    /// How two variable names are compared — and it follows the platform rather than a preference,
    /// because the operating system does: Windows resolves <c>Path</c> and <c>PATH</c> to one
    /// variable, Linux keeps them apart. A list compared the other way would either pass a Windows
    /// <c>Path</c> nothing or let a Linux <c>path</c> through as <c>PATH</c>.
    /// </summary>
    public static StringComparer NameComparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static readonly string[] Everywhere =
    [
        "PATH", "LANG", "LC_ALL", "LC_CTYPE", "TERM", "TZ", "TMPDIR", "TMP", "TEMP", "NO_COLOR",
        "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY", "http_proxy", "https_proxy", "no_proxy",
        "SSL_CERT_FILE", "SSL_CERT_DIR", "NODE_EXTRA_CA_CERTS", "XDG_RUNTIME_DIR",
    ];

    private static readonly string[] UnixOnly = ["HOME", "USER", "LOGNAME", "SHELL"];

    private static readonly string[] WindowsOnly =
    [
        "SystemRoot", "SystemDrive", "ComSpec", "PATHEXT", "windir",
        "USERPROFILE", "APPDATA", "LOCALAPPDATA", "ProgramData", "ProgramFiles", "ProgramFiles(x86)",
        "NUMBER_OF_PROCESSORS", "PROCESSOR_ARCHITECTURE",
    ];

    /// <summary>The allowlist for THIS platform, compared by <see cref="NameComparer"/>.</summary>
    public static IReadOnlySet<string> Passthrough { get; } = ForThisPlatform();

    private static FrozenSet<string> ForThisPlatform()
    {
        var platform = OperatingSystem.IsWindows() ? WindowsOnly : UnixOnly;

        return ((string[])[.. Everywhere, .. platform]).ToFrozenSet(NameComparer);
    }
}
