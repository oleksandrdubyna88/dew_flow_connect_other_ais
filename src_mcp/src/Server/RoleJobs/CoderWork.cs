using CoaiMcp.Core.Coder;
using CoaiMcp.Roles;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The coder role's work, turn 0 (epoch 3 E2 s3 t3; <c>v2/architecture/server.md</c> §§ 16 item 8, 19, 20): it makes the job's worktree at the base commit, launches claude in it with the write launch, reads the coder's
/// report (ONE repair turn in the same session when the report is not valid), makes ONE commit of the worktree's changes on <c>coder/&lt;jobId&gt;</c> through coai's hardened git, and answers with the envelope
/// (<see cref="CoderEnvelope"/>).
/// </summary>
/// <remarks>
/// <para><b>The coder writes files; coai does everything else.</b> The coder has no shell (the measured write launch): it cannot commit, build or test. The commit, the base, the branch and the list of changed files are coai's, taken
/// from git. A report is a claim — <c>met</c> true makes nothing green (green is story s4's, from checks).</para>
/// <para><b>The loop is the failover loop</b> (<see cref="RoleFailover"/>): the models of the route in order, every launch one ledger line, every attempt in <c>job.json</c>, the repair turn in the same session of the same model. A model
/// that fails over leaves what it wrote in the worktree and the next one starts from turn 0 on that tree; the prompt is the same.</para>
/// <para><b>The generation is compared twice</b>: before the commit and before the result. A stale runner (a cancel or a sweep fenced it) writes neither; the runner then exits 75. A cancel stops the CLI child by the
/// runner's token and leaves the worktree where it is — removing it is the retention sweep's work, never this turn's.</para>
/// <para><b>Ends:</b> <c>succeeded</c> with the envelope; <c>failed</c> with <c>coder-task</c> (the task is not fit — checked again here, the repository may have changed since the start), <c>coder-worktree</c>
/// (git refused the worktree: the cap, a taken name, an unknown base), <c>coder-commit</c> (git could not commit), or the failover's own stops (<c>refusal</c> when the coder declared one, <c>models-exhausted</c>…).</para>
/// </remarks>
internal sealed class CoderWork : IRoleWork
{
    /// <summary>How many repair turns the coder's report may use (one — the task's rule).</summary>
    public const int RepairTurns = 1;

    /// <summary>The most one model may take for all of its turns; a test shortens it. A coder's turn is long, so it is the job's budget that bounds it in practice.</summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromMinutes(60);

    /// <summary>The waits of the last model's ladder; a test shortens them.</summary>
    public IReadOnlyList<TimeSpan> Ladder { get; init; } = Runners.Reviewers.RetryLadder.Default;

    /// <summary>
    /// A hook at the two points where the generation is compared (<c>before-commit</c>, <c>before-result</c>) — for the tests, which move the generation there; null in the product.
    /// </summary>
    public Action<string>? Boundary { get; init; }

    public async Task<RoleWorkOutcome> RunAsync(IRoleRun run, CancellationToken ct)
    {
        run.Phase("coder-prepare", 0);
        var (site, refusal) = CoderTask.Locate(run.Request.RepoPath, run.Request.TaskFile);
        if (site is null)
        {
            return new RoleWorkOutcome.Failed("coder-task", refusal);
        }

        var git = new GitHistory(new ProcessLauncher());
        var made = await MakeWorktreeAsync(run, git, ct);
        if (made.Failure is { } failure)
        {
            return failure;
        }

        var prepared = made.Prepared!;
        RoleFailover? failover = null;
        var judge = new CoderJudge(site.DodIds, () => failover!.RepairTurnsUsed);
        failover = new RoleFailover(run, judge.Judge) { MaxRepairTurns = RepairTurns, AttemptTimeout = AttemptTimeout, Ladder = Ladder, Coder = new LaunchConfinement.Coder(run.Request.JobId, prepared.Tree.Root) };
        var outcome = await failover.RunAsync(CoderPrompt.Build(site.PromptInputs, new RolePrompts(run.DataDir)), ct);

        return outcome is RoleWorkOutcome.Succeeded ? await PublishAsync(run, git, prepared, site, judge.Accepted, ct) : outcome;
    }

    /// <summary>The base commit, the worktree made at it and the tree opened for coai's own git — or the failure that ends the job.</summary>
    private async Task<(Prepared? Prepared, RoleWorkOutcome.Failed? Failure)> MakeWorktreeAsync(IRoleRun run, GitHistory git, CancellationToken ct)
    {
        var looked = await git.RootAndHeadAsync(run.Request.RepoPath, ct);
        if (!looked.Ran || !looked.Ok || looked.Lines.Count < 2)
        {
            return (null, new RoleWorkOutcome.Failed("coder-worktree", $"the repository {run.Request.RepoPath} has no HEAD that can be read, so the coder has no base commit"));
        }

        var main = looked.Lines[0];
        var baseSha = run.Request.BaseSha.Length > 0 ? run.Request.BaseSha : looked.Lines[1];
        var root = CoderWorktree.DefaultIn(run.Settings, said => run.Log.Warning("role job {JobId}: {Said}", run.Request.JobId, said));
        var started = await new CoderWorktree(new ProcessLauncher(), root).StartAsync(run.Request.RepoPath, run.Request.JobId, baseSha, ct);
        if (started is not CoderWorktreeOutcome.Started worktree)
        {
            return (null, new RoleWorkOutcome.Failed("coder-worktree", started is CoderWorktreeOutcome.Refused refused ? refused.Sentence : "the worktree could not be made"));
        }

        return CoderTree.Open(worktree.Path, main) switch
        {
            CoderTreeOpening.Opened opened => (new Prepared(opened.Tree, worktree.BaseSha, worktree.Branch), null),
            CoderTreeOpening.Refused refused => (null, new RoleWorkOutcome.Failed("coder-worktree", refused.Sentence)),
            _ => (null, new RoleWorkOutcome.Failed("coder-worktree", "the worktree could not be opened")),
        };
    }

    /// <summary>The coder has answered: compare the generation, make the one commit, compare again, and give the envelope.</summary>
    private async Task<RoleWorkOutcome> PublishAsync(IRoleRun run, GitHistory git, Prepared prepared, CoderTaskSite site, CoderReport? report, CancellationToken ct)
    {
        run.Phase("coder-commit", 0);
        Boundary?.Invoke("before-commit");
        if (ct.IsCancellationRequested || !run.StillWriter())
        {
            return new RoleWorkOutcome.Stopped();
        }

        var committed = await CommitAsync(git, prepared, run.Request.JobId, site.Id, ct);
        if (committed.Failure is { } failed)
        {
            return ct.IsCancellationRequested ? new RoleWorkOutcome.Stopped() : failed;
        }

        var head = committed.Head;
        var changes = head == prepared.BaseSha ? [] : await ChangesAsync(git, prepared, head, ct);
        if (changes is null)
        {
            return ct.IsCancellationRequested ? new RoleWorkOutcome.Stopped() : new RoleWorkOutcome.Failed("coder-commit", "git could not list the files the commit changed");
        }

        Boundary?.Invoke("before-result");
        if (ct.IsCancellationRequested || !run.StillWriter())
        {
            return new RoleWorkOutcome.Stopped();
        }

        return new RoleWorkOutcome.Succeeded(new CoderEnvelope(run.Request.JobId, prepared.BaseSha, head, prepared.Branch, report is not null, 0, changes, report).ToJson());
    }

    /// <summary>Stages everything the coder wrote and makes ONE commit of it; no change means no commit and the head stays the base.</summary>
    private static async Task<(string Head, RoleWorkOutcome.Failed? Failure)> CommitAsync(GitHistory git, Prepared prepared, string jobId, string taskId, CancellationToken ct)
    {
        var staged = await git.InCoderTreeAsync(prepared.Tree, ["add", "-A"], ct);
        if (!staged.Ran || !staged.Ok)
        {
            return (string.Empty, Failed($"git add in the worktree", staged));
        }

        var pending = await git.InCoderTreeAsync(prepared.Tree, ["diff", "--cached", "--name-only", "-z"], ct);
        if (!pending.Ran || !pending.Ok)
        {
            return (string.Empty, Failed("git diff --cached in the worktree", pending));
        }

        if (pending.Out.Length > 0)
        {
            var made = await git.InCoderTreeAsync(prepared.Tree, ["commit", "--no-verify", "--no-gpg-sign", "-m", $"coder {jobId}: {taskId}"], ct);
            if (!made.Ran || !made.Ok)
            {
                return (string.Empty, Failed("git commit in the worktree (is a git identity, user.name and user.email, configured?)", made));
            }
        }

        var head = await git.InCoderTreeAsync(prepared.Tree, ["rev-parse", "HEAD"], ct);

        return head.Ran && head.Ok && head.Lines.Count > 0 && GitHistory.IsCommitish(head.Lines[0])
            ? (head.Lines[0], null)
            : (string.Empty, Failed("git rev-parse HEAD in the worktree", head));
    }

    /// <summary><c>git diff --name-status</c> between the base and the head — the ONLY source of the file list. Null when git could not say.</summary>
    private static async Task<IReadOnlyList<CoderFileChange>?> ChangesAsync(GitHistory git, Prepared prepared, string head, CancellationToken ct)
    {
        var listed = await git.InCoderTreeAsync(prepared.Tree, ["diff", "--name-status", "-z", "--no-renames", prepared.BaseSha, head], ct);

        return listed.Ran && listed.Ok ? CoderEnvelope.ParseNameStatus(listed.Out) : null;
    }

    private static RoleWorkOutcome.Failed Failed(string doing, GitAnswer answer) =>
        new("coder-commit", answer.Refusal.Length > 0 ? answer.Refusal : $"{doing} failed{(answer.Ran ? string.Empty : " or did not finish in time")}; the worktree was left as it is");

    /// <summary>The worktree as the work holds it.</summary>
    private sealed record Prepared(CoderTree Tree, string BaseSha, string Branch);
}

/// <summary>
/// The coder's reading of one answer: refused, a report that passes <see cref="CoderReportReader"/> against the task's dod ids, or a report with problems — which goes back to the SAME session ONCE, and after that is accepted as
/// "no valid report" (<see cref="Accepted"/> stays null): the work is committed all the same, and the envelope says <c>schemaValid: false</c>.
/// </summary>
/// <param name="repairsUsed">How many repair turns the attempt has used so far — read from the loop, which counts them.</param>
internal sealed class CoderJudge(IReadOnlyList<string> dodIds, Func<int> repairsUsed)
{
    /// <summary>The report of the last answer when it was valid; null when it was not (or none came).</summary>
    public CoderReport? Accepted { get; private set; }

    public RoleAnswerVerdict Judge(string answer)
    {
        Accepted = null;
        if (BaJudge.Parsed(answer) is not { } root)
        {
            return Wrong(["the answer is not ONE JSON object: answer with the JSON report alone, with no text before or after it"]);
        }

        switch (CoderReportReader.Read(root, dodIds))
        {
            case CoderRead.Refused refused:
                return new RoleAnswerVerdict.Refused(refused.Reason);
            case CoderRead.Report report:
                Accepted = report.Value;

                return new RoleAnswerVerdict.Valid(root.GetRawText());
            case CoderRead.Wrong wrong:
                return Wrong(wrong.Problems);
            default:
                return Wrong(["the answer could not be read"]);
        }
    }

    private RoleAnswerVerdict Wrong(IReadOnlyList<string> problems) =>
        repairsUsed() >= CoderWork.RepairTurns
            ? new RoleAnswerVerdict.Valid("{}")
            : new RoleAnswerVerdict.Repair(CoderPrompt.Repair(problems), $"the report has {problems.Count} error{(problems.Count == 1 ? string.Empty : "s")}; the first: {problems[0]}");
}
