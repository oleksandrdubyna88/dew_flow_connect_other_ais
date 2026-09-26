using System.Collections.Concurrent;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Tests;

/// <summary>
/// The real launcher, with every request written down — and, when a test says so, one request answered
/// without running.
/// </summary>
/// <remarks>
/// A test can see what each git process was ASKED, which is how "never read" and "never built" are proven
/// rather than inferred from the output; and <paramref name="answer"/> lets one call fail on purpose (a
/// blob read that cannot happen) while everything around it still runs for real.
/// </remarks>
/// <param name="answer">The answer for a request the test wants to decide itself, or null to run it.</param>
internal sealed class WatchedLauncher(IProcessLauncher inner, Func<ProcessRequest, ProcessResult?>? answer = null) : IProcessLauncher
{
    private readonly ConcurrentQueue<ProcessRequest> _requests = new();

    public IReadOnlyList<ProcessRequest> Requests => [.. _requests];

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
    {
        _requests.Enqueue(request);

        return answer?.Invoke(request) is { } decided ? Task.FromResult(decided) : inner.RunAsync(request, ct);
    }
}
