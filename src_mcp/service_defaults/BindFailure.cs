using System.Net.Sockets;

namespace CoaiMcp.ServiceDefaults;

/// <summary>
/// The one decision every HTTP host here makes about a port somebody else holds: it is an EXPECTED
/// startup failure — one line and <see cref="ExitCode"/> — and never a crash.
/// </summary>
/// <remarks>
/// <para>Both Kestrel hosts let the bind error escape <c>RunAsync</c> as an unhandled
/// <see cref="IOException"/>: the runtime's "Unhandled exception." block, an exit code no restart
/// policy can branch on, and on Windows a `.NET Runtime 1026` event plus an error report per
/// occurrence — 415 of them on one developer machine between 2026-09-17 and 2026-10-01, every one
/// from this repository's own collision tests. One place decides it so the two hosts cannot drift.</para>
/// <para>It reads only base-library types — <see cref="SocketException"/> anywhere in the chain —
/// because this library is in the Native-AOT stdio host's graph too, which must not pull in ASP.NET
/// for a predicate. Kestrel wraps the socket error twice (an <see cref="IOException"/> around its own
/// <c>AddressInUseException</c> around the <see cref="SocketException"/>), so the chain is walked
/// rather than one level read.</para>
/// </remarks>
public static class BindFailure
{
    /// <summary>
    /// EX_TEMPFAIL: try again later. Not 78 (EX_CONFIG), which a deploy unit treats as permanent — a
    /// port is held only until its holder lets go, which is exactly what a restart policy is for.
    /// </summary>
    public const int ExitCode = 75;

    /// <summary>Whether <paramref name="failure"/> is, at any depth, a socket address already in use.</summary>
    public static bool IsAddressInUse(Exception failure) =>
        failure is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse }
        || (failure.InnerException is { } inner && IsAddressInUse(inner));

    /// <summary>The one line a host prints: what it could not take, and what that means.</summary>
    /// <remarks>
    /// <para>Carries Kestrel's own message, because that is the sentence that names the address — and the
    /// one a test harness's port-race retry already recognises.</para>
    /// <para>ONE line whatever the message holds: any run of line breaks becomes a single space, because a
    /// journal reader and a log grep treat the line as the record (CodeRabbit, PR #636). A host prints it to
    /// stderr and adds no stack of its own — an expected failure is reported, not traced; the host
    /// framework's one "Hosting failed to start" record already carries the exception into the log file.
    /// ASCII only: a Windows console re-encodes stderr into its code page, and a dash outside it arrives
    /// as something else (measured: an em dash came back as a hyphen).</para>
    /// </remarks>
    public static string Explained(string appName, Exception failure) =>
        $"[{appName}] cannot listen: {OneLine(failure.Message)} Another process holds that address; "
        + "nothing was served, and a restart can succeed once it is released.";

    private static string OneLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
