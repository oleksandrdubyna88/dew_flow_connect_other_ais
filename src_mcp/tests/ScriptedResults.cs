using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Tests;

/// <summary>
/// A launcher that answers each launch with the next scripted <see cref="ProcessResult"/>, and the last one for every
/// launch after the script runs out — for a test whose subject sits ABOVE <see cref="IProcessLauncher"/>: the
/// executor, the ladder, the ledger.
/// </summary>
/// <remarks>
/// Extracted from <c>EveryPaidLaunchIsWrittenTests</c> by story 3.2 of todo/PLAN_every_round_is_counted.md, which
/// needed the same shape and would have been the fourth private copy of it (reuse-first). It answers at once and
/// never looks at the token: a test that needs a launch to WAIT for its token writes a launcher of its own and says so.
/// </remarks>
internal sealed class ScriptedResults(params ProcessResult[] results) : IProcessLauncher
{
    private int _launched;

    /// <summary>How many launches were asked for — a repair or a follow-up is the second.</summary>
    public int Launched => _launched;

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
    {
        var at = Interlocked.Increment(ref _launched);

        return Task.FromResult(results[Math.Min(at, results.Length) - 1]);
    }
}
