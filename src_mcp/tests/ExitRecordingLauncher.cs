using System.Collections.Concurrent;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Tests;

/// <summary>
/// The real launcher, with every request AND what came of it written down.
/// </summary>
/// <remarks>
/// <see cref="WatchedLauncher"/> keeps the requests only. A test that must prove a particular git call
/// EXITED 0 — not merely that the directory it was meant to remove is gone, which a .NET delete fallback
/// would also achieve — needs the pair.
/// </remarks>
internal sealed class ExitRecordingLauncher(IProcessLauncher inner) : IProcessLauncher
{
    private readonly ConcurrentQueue<(ProcessRequest Request, ProcessResult Result)> _calls = new();

    public IReadOnlyList<(ProcessRequest Request, ProcessResult Result)> Calls => [.. _calls];

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
    {
        var result = await inner.RunAsync(request, ct);
        _calls.Enqueue((request, result));

        return result;
    }
}
