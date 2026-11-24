using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Tests;

/// <summary>A launcher that starts nothing and answers <c>--version</c> with what it was told — for the planner and finder tests that never need a child.</summary>
internal sealed class FakeLauncher(string stdout = "", int exitCode = 0) : IProcessLauncher
{
    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
        Task.FromResult(new ProcessResult(exitCode, stdout, string.Empty, TimedOut: false));
}
