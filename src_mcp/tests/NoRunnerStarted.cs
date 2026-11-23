using CoaiMcp.Runners.Detach;

namespace CoaiMcp.Tests;

/// <summary>
/// The Windows road of a <see cref="DetachedLauncher"/> that must start nothing. The POSIX roads go through the injected process launcher, so a test hands it one that starts nothing; the Windows
/// road calls <c>CreateProcess</c> directly, and on a Windows runner a test that meant "no runner" would start a real <c>coai-mcp --role-run</c>. This is the seam that closes that.
/// </summary>
internal static class NoRunnerStarted
{
    /// <summary>A Windows road that answers started, and starts nothing — what a process launcher answering exit 0 is on the POSIX roads.</summary>
    internal static readonly Func<DetachSpec, DetachResult> Succeeds = _ => new DetachResult(true, "windows", string.Empty, string.Empty);

    /// <summary>A Windows road that answers it could not start, saying why — what a process launcher answering a non-zero exit is on the POSIX roads.</summary>
    internal static readonly Func<DetachSpec, DetachResult> Fails = _ => new DetachResult(false, string.Empty, string.Empty, "windows: no runner could be started in this test");

    /// <summary>The Windows road that matches a process launcher answering <paramref name="exit"/>.</summary>
    internal static Func<DetachSpec, DetachResult> For(int exit) => exit == 0 ? Succeeds : Fails;
}
