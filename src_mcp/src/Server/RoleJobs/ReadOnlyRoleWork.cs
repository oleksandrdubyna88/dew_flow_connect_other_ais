using CoaiMcp.Core.Roles;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// What the scout and the judge do in common (epoch 3 E3 s2; spec A): they are role cases of <c>start_role</c> on the road the BA uses — a READ-ONLY launch through the consultant adapters (<see cref="RoleModelLauncher"/> with no
/// coder, so <c>LaunchConfinement.AsShipped</c> and never the coder's write case), the tree compared around every attempt (<see cref="RoleFailover"/>: <c>tree-changed</c> never fails over), the answer read by a strict reader, at most
/// <see cref="RepairTurns"/> repair turns in the SAME session, and a declared refusal goes to the next model. A role brings its prompt and its judge of an answer; coai builds the envelope (<see cref="ReadOnlyEnvelope"/>).
/// </summary>
/// <remarks>
/// <para><b>No file is written.</b> Neither role asks a model to write, neither writes for it: the job folder's own <c>attempts/</c> evidence and <c>result.json</c> are the only things that change, and they are the runner's.</para>
/// <para><b>The commit</b> in the envelope is the HEAD of the checkout when the job began, read once before any model is asked. A folder that is not a git work tree has none, and the envelope says so with an empty string.</para>
/// </remarks>
internal abstract class ReadOnlyRoleWork : IRoleWork
{
    /// <summary>How many repair turns one attempt may use (spec A: at most 2).</summary>
    public const int RepairTurns = 2;

    /// <summary>The most one model may take for the whole of its turns; a test shortens it.</summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromMinutes(20);

    /// <summary>The waits of the last model's ladder; a test shortens them.</summary>
    public IReadOnlyList<TimeSpan> Ladder { get; init; } = Runners.Reviewers.RetryLadder.Default;

    /// <summary>The role's name as the phase of the heartbeat spells it.</summary>
    protected abstract string Name { get; }

    protected abstract string FirstPrompt(IRoleRun run);

    protected abstract EvidenceJudge JudgeOf(IEvidenceSource files, string commit);

    public async Task<RoleWorkOutcome> RunAsync(IRoleRun run, CancellationToken ct)
    {
        run.Phase($"{Name}-prepare", 0);
        var commit = await HeadAsync(run.Request.RepoPath, ct);
        var judge = JudgeOf(new RoleEvidenceFiles(run.Request.RepoPath), commit);
        var failover = new RoleFailover(run, judge.Judge) { MaxRepairTurns = RepairTurns, AttemptTimeout = AttemptTimeout, Ladder = Ladder };
        judge.Turn = () => failover.RepairTurnsUsed;

        return await failover.RunAsync(FirstPrompt(run), ct);
    }

    /// <summary>The commit HEAD is on, or empty when git does not say (not a git work tree, or no commit yet).</summary>
    private static async Task<string> HeadAsync(string repoPath, CancellationToken ct)
    {
        var answer = await new GitHistory(new ProcessLauncher()).RootAndHeadAsync(repoPath, ct);

        return answer is { Ran: true, Ok: true } && answer.Lines is [_, var head, ..] && GitHistory.IsCommitish(head) ? head : string.Empty;
    }
}
