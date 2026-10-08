using System.Collections.Concurrent;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Tests;

/// <summary>
/// A launcher that answers the VENDOR launches a test scripts — by what the request carries — and runs
/// everything else (git) for real, writing every request down with the moment it arrived.
/// </summary>
/// <remarks>
/// <para>For the question consultant's fan-out, where the thing under test is what each row is GIVEN
/// and how six of them settle, not what a real CLI says: the fake CLI is env-steered and process-wide,
/// which cannot make ONE row of six slow or inspect a row's prompt apart from its siblings'. A script
/// over the request can — it reads the prompt (stdin or the <c>--prompt-file</c>), writes the <c>-o</c>
/// / <c>--out</c> file a codex or api shape is read from, answers a claude envelope on stdout, and may
/// wait first, honouring the launcher's own timeout the way the real one does (a wait past
/// <see cref="ProcessRequest.Timeout"/> answers <c>TimedOut</c>).</para>
/// <para>It is <see cref="WatchedLauncher"/>'s shape with an async decision; that one's sync answer
/// could not wait, and a fan-out test is about waiting.</para>
/// </remarks>
internal sealed class ScriptedLauncher(IProcessLauncher inner, Func<ScriptedLaunch, Task<ScriptedAnswer?>> script) : IProcessLauncher
{
    private readonly ConcurrentQueue<(ProcessRequest Request, DateTime StartedUtc, string Prompt)> _launches = new();

    /// <summary>Every request this launcher was handed, in arrival order, with the prompt it carried.</summary>
    public IReadOnlyList<(ProcessRequest Request, DateTime StartedUtc, string Prompt)> Launches => [.. _launches];

    /// <summary>
    /// The vendor launches alone — anything that is not git, and not a codex row's tier probe: a bare <c>--version</c>
    /// asked before the launch (research/PLAN_codex_tier_floor.md) is a question about the installed CLI, not a launch of it.
    /// </summary>
    public IReadOnlyList<(ProcessRequest Request, DateTime StartedUtc, string Prompt)> Vendors =>
        [.. _launches.Where(l => !string.Equals(l.Request.Executable, "git", StringComparison.OrdinalIgnoreCase) && l.Request.Arguments is not ["--version"])];

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
    {
        var prompt = PromptOf(request);
        _launches.Enqueue((request, DateTime.UtcNow, prompt));
        var launch = new ScriptedLaunch(request, prompt);
        if (await script(launch) is not { } answer)
        {
            return await inner.RunAsync(request, ct);
        }

        return await Answered(request, answer, ct);
    }

    /// <summary>The scripted answer, after its wait — cut by the request's own timeout as the real launcher would cut a child.</summary>
    private static async Task<ProcessResult> Answered(ProcessRequest request, ScriptedAnswer answer, CancellationToken ct)
    {
        if (answer.Wait > TimeSpan.Zero)
        {
            // Cut short only when the BUDGET is the shorter of the two — decided before the wait, never by
            // comparing a timer's elapsed time against the wait, which a timer can undershoot by a millisecond.
            var cut = request.Timeout < answer.Wait;
            await Task.Delay(cut ? request.Timeout : answer.Wait, ct);
            if (cut)
            {
                return new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true);
            }
        }

        if (answer.OutFileText is { } text && OutFileOf(request) is { } file)
        {
            File.WriteAllText(file, text);
        }

        return new ProcessResult(answer.ExitCode, answer.StdOut, answer.StdErr, TimedOut: false);
    }

    /// <summary>The prompt a launch was handed: stdin, or the file after <c>--prompt-file</c>.</summary>
    private static string PromptOf(ProcessRequest request)
    {
        if (request.StdIn is { Length: > 0 } stdin)
        {
            return stdin;
        }

        var at = IndexOf(request.Arguments, "--prompt-file");

        return at >= 0 && at + 1 < request.Arguments.Count && File.Exists(request.Arguments[at + 1])
            ? File.ReadAllText(request.Arguments[at + 1])
            : string.Empty;
    }

    /// <summary>Where the launch is read from when it is a file: codex's <c>-o</c>, the shims' <c>--out</c>.</summary>
    private static string? OutFileOf(ProcessRequest request)
    {
        var at = Math.Max(IndexOf(request.Arguments, "-o"), IndexOf(request.Arguments, "--out"));

        return at >= 0 && at + 1 < request.Arguments.Count && Path.IsPathRooted(request.Arguments[at + 1]) ? request.Arguments[at + 1] : null;
    }

    private static int IndexOf(IReadOnlyList<string> arguments, string flag)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i] == flag)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>One launch as the script sees it: the request, and the prompt it carried however it travelled.</summary>
internal sealed record ScriptedLaunch(ProcessRequest Request, string Prompt)
{
    public bool Is(string executableOrFlag) =>
        Request.Executable.Contains(executableOrFlag, StringComparison.OrdinalIgnoreCase)
        || Request.Arguments.Contains(executableOrFlag, StringComparer.Ordinal);
}

/// <summary>What the script answers a launch with: a wait first, then the streams, and a file where the adapter reads one.</summary>
internal sealed record ScriptedAnswer(string StdOut = "", string? OutFileText = null, int ExitCode = 0, string StdErr = "", TimeSpan Wait = default)
{
    /// <summary>A claude envelope on stdout.</summary>
    public static ScriptedAnswer Claude(string result) =>
        new(StdOut: System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["result"] = result, ["session_id"] = Guid.NewGuid().ToString() }));

    /// <summary>A codex answer in its <c>-o</c> file, with a thread id on the stream.</summary>
    public static ScriptedAnswer Codex(string advice) =>
        new(StdOut: "{\"type\":\"thread.started\",\"thread_id\":\"0198f2c1-" + Guid.NewGuid().ToString("N")[..12] + "\"}\n", OutFileText: advice);

    /// <summary>An api shim's answer file (the question-answer envelope) and its usage line on stdout.</summary>
    public static ScriptedAnswer Api(string envelopeJson, long tokensIn = 1200, long tokensOut = 300) =>
        new(StdOut: $$"""{"tokensIn":{{tokensIn}},"tokensOut":{{tokensOut}},"tokensCached":0,"tokensReasoning":0}""", OutFileText: envelopeJson);

    /// <summary>A launch that never answers in time: the real launcher's <c>TimedOut</c>.</summary>
    public static ScriptedAnswer Hangs() => new(Wait: TimeSpan.FromDays(1));
}
