using System.Collections.Concurrent;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Tests;

/// <summary>
/// Kimi as a consultation scenario plays it, in process (E5.4 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>): <c>kimi --version</c> answers a
/// measured version, every launch of <see cref="Shim"/> (the shim the seams name) is answered with the report the shim's OWN writer prints
/// (<see cref="KimiShimScripts.Report"/>), and every other process (git) runs for real. The launch control in front of it is the real one, over a
/// fake person (<see cref="KimiFixture"/>).
/// </summary>
/// <remarks>
/// Each shim launch is kept with the moment it started and ended, and the number of shims alive at once is counted — what the scenarios of one
/// sign-in assert (decision k of E5.3). Shaped like <c>PlayedVibe</c>; no turn is paid for.
/// </remarks>
internal sealed class PlayedKimi(IProcessLauncher inner) : IProcessLauncher
{
    /// <summary>The shim's executable as the seams name it — never started, only recognised.</summary>
    public const string Shim = "played-coai-mcp";

    /// <summary>The session the played agent mints on a first turn.</summary>
    public const string Minted = "0d6a5f2e-1111-2222-3333-444455556666";

    private int _started;
    private int _running;
    private int _peak;

    /// <summary>One shim launch: what it was asked, and when it ran.</summary>
    public sealed record Launch(ProcessRequest Request, DateTime StartUtc, DateTime EndUtc)
    {
        /// <summary>The argument that follows <paramref name="flag"/> (<c>--home</c>, <c>--cwd</c>).</summary>
        public string ArgAfter(string flag)
        {
            var args = Request.Arguments.ToList();

            return args[args.IndexOf(flag) + 1];
        }
    }

    public ConcurrentQueue<Launch> Shims { get; } = new();

    public ConcurrentQueue<ProcessRequest> Versions { get; } = new();

    /// <summary>The most shims ever running at once.</summary>
    public int Peak => _peak;

    /// <summary>What the n-th shim launch (0-based) prints — by default an answered turn of the default totals.</summary>
    public Func<int, ProcessRequest, ProcessResult> Turn { get; init; } = static (_, request) => Answered(SessionOf(request), 19973, 31);

    /// <summary>Runs while a shim is "alive", after its start is stamped — a test's seam for what holds at that moment.</summary>
    public Func<ProcessRequest, CancellationToken, Task> During { get; init; } = static (_, _) => Task.CompletedTask;

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
    {
        if (request.Arguments is ["--version"])
        {
            Versions.Enqueue(request);

            return new ProcessResult(0, "kimi 2.1.1\r\n", string.Empty, TimedOut: false);
        }

        return string.Equals(request.Executable, Shim, StringComparison.Ordinal) ? await ShimAsync(request, ct) : await inner.RunAsync(request, ct);
    }

    private async Task<ProcessResult> ShimAsync(ProcessRequest request, CancellationToken ct)
    {
        var index = Interlocked.Increment(ref _started) - 1;
        var start = DateTime.UtcNow;
        InterlockedMax(Interlocked.Increment(ref _running));
        try
        {
            await During(request, ct);

            return Turn(index, request);
        }
        finally
        {
            Interlocked.Decrement(ref _running);
            Shims.Enqueue(new Launch(request, start, DateTime.UtcNow));
        }
    }

    private void InterlockedMax(int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref _peak)) < value && Interlocked.CompareExchange(ref _peak, value, seen) != seen)
        {
        }
    }

    /// <summary>The session a launch continues (<c>--session</c>), else the one the played agent mints.</summary>
    public static string SessionOf(ProcessRequest request)
    {
        var at = request.Arguments.ToList().IndexOf(AcpWords.SessionFlag);

        return at >= 0 ? request.Arguments[at + 1] : Minted;
    }

    /// <summary>An answered turn whose <c>/usage</c> gave the session's CUMULATIVE totals.</summary>
    public static ProcessResult Answered(string session, long tokensIn, long tokensOut, string answer = Advice) =>
        new(0, KimiShimScripts.Report(AcpEnd.Answered, answer, session: session, tokensIn: tokensIn, tokensOut: tokensOut), string.Empty, TimedOut: false);

    /// <summary>An answered turn whose <c>/usage</c> said nothing parseable: <c>notCaptured</c>.</summary>
    public static ProcessResult Uncaptured(string session, string answer = Advice) =>
        new(0, KimiShimScripts.Report(AcpEnd.Answered, answer, tokens: false, session: session), string.Empty, TimedOut: false);

    /// <summary>A continued turn whose session the agent no longer holds: end <c>session_lost</c>, exit 70 (E5.2).</summary>
    public static ProcessResult Lost(string session) =>
        new(70, KimiShimScripts.Report(AcpEnd.SessionLost, string.Empty, $"the agent could not load session {session}: -32602 Unknown sessionId: <id>", tokens: false, session: session),
            $"the agent could not load session {session}\r\n", TimedOut: false);

    /// <summary>The advice every answered turn gives unless told otherwise.</summary>
    public const string Advice = "Your parser counts the separator as a field. Check it with a two-field input.";
}
