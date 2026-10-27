using System.Collections.Concurrent;
using CoaiMcp.Acp;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Tests;

/// <summary>
/// What the shim PRINTS and a launcher that plays it (E4.4 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>): the report is made by the shim's own
/// <c>AskAcpMode.Report</c>, so the parent's reader (<c>KimiReport</c>) is proved against the writer and not against a hand-typed copy of it.
/// </summary>
internal static class KimiShimScripts
{
    public const string CleanReview = """{"findings": []}""";

    /// <summary>The report the shim prints for a turn that ended as given.</summary>
    /// <param name="session">The session the report names — the one the agent minted, or the one <c>--session</c> asked for (E5.2).</param>
    /// <param name="tokensIn">The session's CUMULATIVE input total, as <c>/usage</c> gives it (M-K5b) — when <paramref name="tokens"/> is captured.</param>
    /// <param name="tokensOut">The session's cumulative output total.</param>
    public static string Report(
        AcpEnd end = AcpEnd.Answered,
        string answer = CleanReview,
        string reason = "",
        string failure = "",
        string failureLine = "",
        bool tokens = true,
        IReadOnlyList<AcpReadRecord>? reads = null,
        IReadOnlyList<AcpToolCall>? calls = null,
        string session = "session-1",
        long tokensIn = 19973,
        long tokensOut = 31)
    {
        var outcome = new AcpOutcome(end, answer, reason, end == AcpEnd.Answered ? "end_turn" : string.Empty, session, AcpUsage.Unknown, [], calls ?? [], reads ?? [], string.Empty, 0)
        {
            Tokens = tokens ? new AcpTokens(tokensIn, tokensOut, true) : AcpTokens.Unknown,
        };
        var diagnosis = failure.Length > 0 ? new AcpDiagnosis(failure, "the cure", AcpFailureKind.Refused, failureLine) : AcpDiagnosis.None;

        return AskAcpMode.Report(outcome, diagnosis);
    }

    /// <summary>An answered turn, exit 0.</summary>
    public static ProcessResult Answered(string answer = CleanReview) => new(0, Report(answer: answer), string.Empty, TimedOut: false);

    /// <summary>A turn the shim ended with <paramref name="exit"/>, its report on stdout and its sentence on stderr.</summary>
    public static ProcessResult Ended(int exit, AcpEnd end, string reason, string failure = "", string stderr = "", IReadOnlyList<AcpReadRecord>? reads = null, IReadOnlyList<AcpToolCall>? calls = null, string failureLine = "") =>
        new(exit, Report(end, string.Empty, reason, failure, failureLine, tokens: false, reads, calls), stderr, TimedOut: false);
}

/// <summary>
/// A Kimi on this "machine", as the executor's launcher sees it: <c>--version</c> answers the version, anything else is the SHIM, answered by the
/// script (after an optional delay). Records every request; counts the shim's launches apart from the version probes.
/// </summary>
internal sealed class ScriptedKimi(Func<ProcessRequest, ProcessResult>? shim = null, string version = "2.1.1", TimeSpan? delay = null) : IProcessLauncher
{
    private int _shims;
    private int _running;
    private int _peak;

    public ConcurrentQueue<ProcessRequest> Requests { get; } = new();

    /// <summary>How many times the SHIM (not <c>--version</c>) was started.</summary>
    public int Shims => _shims;

    /// <summary>The most shim launches ever running at once.</summary>
    public int PeakShims => _peak;

    public ProcessRequest[] ShimRequests => [.. Requests.Where(static request => !request.Arguments.Contains("--version"))];

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
    {
        Requests.Enqueue(request);
        if (request.Arguments.Contains("--version"))
        {
            return new ProcessResult(0, $"kimi {version}\r\n", string.Empty, TimedOut: false);
        }

        Interlocked.Increment(ref _shims);
        var running = Interlocked.Increment(ref _running);
        InterlockedMax(ref _peak, running);
        try
        {
            if (delay is { } wait)
            {
                await Task.Delay(wait, ct);
            }

            return (shim ?? (_ => KimiShimScripts.Answered()))(request);
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }
}

/// <summary>
/// The real <see cref="KimiRuntime"/>'s READ side and flags, with no launch control in front of it — so the classification
/// scenarios (S4.5–S4.9) run on every OS, through the real executor and scheduler, without a fake person's home. The launch control has its own
/// scenarios (<c>KimiLaunchControlTests</c>) and its own end-to-end ones (<c>AKimiReviewerRunsThroughTheShimTests</c>).
/// </summary>
internal sealed class KimiReadSide(KimiRuntime inner) : IReviewerRuntime
{
    private IReviewerRuntime Inner => inner;

    public string Provider => inner.Provider;

    public ReviewerInvocation Build(string role, string prompt, string worktreePath, string schemaFilePath, string outputDir, ReviewerSettings settings) =>
        inner.Build(role, prompt, worktreePath, schemaFilePath, outputDir, settings);

    public string? ReadAnswer(ReviewerInvocation invocation, ProcessResult result) => Inner.ReadAnswer(invocation, result);

    public Usage ReadUsage(ReviewerInvocation invocation, ProcessResult result) => Inner.ReadUsage(invocation, result);

    public string WhyItFailed(ReviewerInvocation invocation, ProcessResult result) => Inner.WhyItFailed(invocation, result);

    public RateLimitVerdict RateLimited(ReviewerInvocation invocation, ProcessResult result) => Inner.RateLimited(invocation, result);

    public bool LaunchesOneAtATime => Inner.LaunchesOneAtATime;
}

/// <summary>An adapter with nothing of its own: the default of every member (a vendor that may run two at a time).</summary>
internal sealed class PlainAdapter(string provider) : IReviewerRuntime
{
    public string Provider => provider;

    public ReviewerInvocation Build(string role, string prompt, string worktreePath, string schemaFilePath, string outputDir, ReviewerSettings settings) =>
        new(provider, role, new ProcessRequest(provider, [], worktreePath), string.Empty, this);

    public string? ReadAnswer(ReviewerInvocation invocation, ProcessResult result) => result.StdOut;
}
