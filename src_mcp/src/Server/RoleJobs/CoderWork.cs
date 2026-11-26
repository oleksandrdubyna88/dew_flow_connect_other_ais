using CoaiMcp.Core.Coder;
using CoaiMcp.Roles;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Runners.Worktrees;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The coder role's work, turn 0 (epoch 3 E2 s3 t3; <c>v2/architecture/server.md</c> §§ 16 item 8, 19, 20): it makes the job's worktree at the base commit, launches claude in it with the write launch, reads the coder's
/// report (ONE repair turn in the same session when the report is not valid), makes ONE commit of the worktree's changes on <c>coder/&lt;jobId&gt;</c> through coai's hardened git, and answers with the envelope
/// (<see cref="CoderEnvelope"/>).
/// </summary>
/// <remarks>
/// <para><b>The coder runs inside the OS layer — the one road</b> (<see cref="CoderOsLayer"/>, <see cref="CoderTurns"/>): every launch, the repair turn included, is a named unit with the main checkout and the coder root read-only and the worktree the one writable folder; a machine
/// that cannot prove that refuses the job before the coder starts. A change of the main checkout during the job is recorded in the envelope (<c>mainCheckout</c>) as evidence and never fails the job (decision 8).</para>
/// <para><b>The coder writes files; coai does everything else.</b> The coder has no shell (the measured write launch): it cannot commit, build or test. The commit, the base, the branch and the list of changed files are coai's, taken
/// from git. A report is a claim — <c>met</c> true makes nothing green (green is story s4's, from checks).</para>
/// <para><b>The loop is the failover loop</b> (<see cref="RoleFailover"/>): the models of the route in order, every launch one ledger line, every attempt in <c>job.json</c>, the repair turn in the same session of the same model. The commit, the checks and the fix turns
/// of a model are part of ITS attempt (<see cref="RoleFailover.Follow"/>), so a limit, a refusal or a crash in turn 0 OR in a fix turn goes on to the next model. Before that model starts, the worktree is put back to the last coai commit — what the first one had written and coai
/// had not committed is dropped, proven by <c>git status</c> through the hardened git (<see cref="CoderRestore"/>) — and it starts from turn 0 with the same task prompt, in a NEW session (a new resume handle, bound to the job and the worktree). The fix-turn count and the cap
/// are the JOB's, read once: a switch gives no new turns. <c>output-invalid</c>, <c>cancelled</c>, <c>not-green</c>, <c>checks-changed-tree</c>, <c>timeout</c> and <c>coder-boundary</c> are not a model that stopped, and never fail over.</para>
/// <para><b>The generation is compared twice</b>: before the commit and before the result. A stale runner (a cancel or a sweep fenced it) writes neither; the runner then exits 75. A cancel stops the CLI child by the
/// runner's token and leaves the worktree where it is — removing it is the retention sweep's work, never this turn's.</para>
/// <para><b>Ends:</b> <c>succeeded</c> with the envelope; <c>failed</c> with <c>coder-task</c> (the task is not fit — checked again here, the repository may have changed since the start), <c>coder-worktree</c>
/// (git refused the worktree: the cap, a taken name, an unknown base), <c>coder-commit</c> (git could not commit), <c>coder-boundary</c> (the OS layer refused to start the coder, or stopped a write of it — the sentence names the path),
/// or the failover's own stops (<c>refusal</c> when the coder declared one, <c>models-exhausted</c>…).</para>
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

    /// <summary>
    /// For a test only: makes the <see cref="CoderTurns"/> of the job from the job's one launcher — over a stand-in for the person's home (never the real one), a machine with no user manager, or units with a property taken away so the preflight
    /// refuses. Null in the product (<see cref="CoderOsLayer.ShippedTurns"/>): the boundary of this machine.
    /// </summary>
    internal Func<IProcessLauncher, CoderTurns>? Turns { get; init; }

    public async Task<RoleWorkOutcome> RunAsync(IRoleRun run, CancellationToken ct)
    {
        var started = run.Clock.GetTimestamp();
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

        var layer = await LayerForAsync(run, made.Prepared!, ct);
        var prepared = made.Prepared! with { Tree = layer.Job.Tree };

        // The fix cap is read ONCE, here: it is the job's, and a model that takes over does not bring turns of its own (epoch 3 E2 s4 t3).
        var (cap, note) = FixRounds.Read(run.Settings);
        if (note.Length > 0)
        {
            run.Log.Warning("role job {JobId}: {Note}", run.Request.JobId, note);
        }

        // ONE loop for the whole job: the fix turns used and the rounds run are the job's, whichever models take part.
        var loop = new CoderFixLoop(run, git, layer, prepared.Tree, prepared.BaseSha, site, started) { Cap = cap, AttemptTimeout = AttemptTimeout, Boundary = Boundary };
        // The instruction step (epoch 3 E3 s7 t3; SPEC C9): a command the base commit declares, run ONCE here, before any model is launched. A declared step that cannot run ends the job: no half instruction, no launch.
        var instructed = await new CoderInstructions(run, git, layer, prepared.Tree, prepared.BaseSha, site).RunAsync(ct);
        if (instructed is CoderInstructionsOutcome.Cancelled)
        {
            return new RoleWorkOutcome.Stopped();
        }

        if (instructed is CoderInstructionsOutcome.Failed unrun)
        {
            return new RoleWorkOutcome.Failed(unrun.Stop, unrun.Reason, new CoderEnvelope(run.Request.JobId, prepared.BaseSha, prepared.BaseSha, prepared.Branch, false, 0, [], null) { Instructions = unrun.Record }.ToJson());
        }

        var instructions = instructed is CoderInstructionsOutcome.Ran ran ? ran.Record : CoderInstructionsRecord.NotDeclared;
        var answers = 0;
        RoleFailover? failover = null;
        var judge = new CoderJudge(site.DodIds, () => failover!.RepairTurnsUsed);
        var confinement = new LaunchConfinement.Coder(run.Request.JobId, prepared.Tree.Root);
        failover = new RoleFailover(run, judge.Judge)
        {
            MaxRepairTurns = RepairTurns,
            AttemptTimeout = AttemptTimeout,
            Ladder = Ladder,
            Coder = confinement,
            OsLayer = layer,
            Follow = (answered, token) => FollowAsync(run, git, prepared, site, loop, judge.Accepted, new CoderFixSession(answered.Model, answered.Handle, confinement, answered.KeyId, answered.Secret), ++answers, layer, cap, note, instructions, token),
            Restore = token => CoderRestore.RestoreAsync(git, prepared.Tree, loop.LastCommit, token),
        };

        return await failover.RunAsync(CoderPrompt.Build(site.PromptInputs, new RolePrompts(run.DataDir)), ct);
    }

    /// <summary>
    /// The OS layer of this job: the boundary job (the worktree writable, the main checkout and the coder root read-only, the job's own folders — the coder's home among them — under <see cref="CoderOsLayer.JobFolder"/>), and ONE launcher for
    /// every process of it, so the systemd-run client of a turn is recorded in <c>job.json</c> like any CLI child. The layer lives as long as the work: the repair turn and the fix turns of the checks run in it too, on the same home.
    /// </summary>
    private async Task<CoderOsLayer> LayerForAsync(IRoleRun run, Prepared prepared, CancellationToken ct)
    {
        var folder = CoderOsLayer.JobFolder(run.Settings, said => run.Log.Warning("role job {JobId}: {Said}", run.Request.JobId, said), run.Request.JobId);

        var launcher = new ProcessLauncher(run.Children);

        // The key pool's folder is hidden from a launch that carries a key (InaccessiblePaths), and the preflight proves a read of it fails from inside.
        var job = BoundaryJob.ForCoder(run.Request.JobId, prepared.Tree, folder) with { KeysFolder = Path.Combine(run.DataDir, "keys"), KeysFile = KeysFileOf(run) };

        // The submodules of the base commit (epoch 3 E2 s6, the recorded change): provided read-only into every unit of the job, from the main checkout's own object store and with no network; the tree protects '.gitmodules' and their paths from the coder's diff.
        var mounts = await new CoderSubmodules(launcher).ProvideAsync(prepared.Tree.MainCheckout, prepared.BaseSha, prepared.Tree.Root, job.MountsFolder, ct);
        foreach (var declared in mounts.Declared.Where(mount => !mount.Provided))
        {
            run.Log.Warning("role job {JobId}: the submodule {Path} is {Note}", run.Request.JobId, declared.Path, declared.Note);
        }

        job = job with { Tree = prepared.Tree with { Protected = mounts.Protected }, Mounts = mounts.Binds(prepared.Tree.Root) };

        return new CoderOsLayer(job, (Turns ?? CoderOsLayer.ShippedTurns)(launcher), launcher) { Mounts = mounts };
    }

    /// <summary>The keys file the person named, as a full path (empty when none): hidden from a keyed coder's unit.</summary>
    private static string KeysFileOf(IRoleRun run) =>
        Server.Keys.KeySourceSync.KeysFilePath(run.Settings) is { Length: > 0 } named ? Path.GetFullPath(named) : string.Empty;

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

    /// <summary>
    /// What the coder does after a model's turn 0 gave a valid answer, INSIDE that model's attempt (<see cref="RoleFailover.Follow"/>): the one commit of turn 0 (the generation compared first), then the way to green (<see cref="CoderFixLoop"/> — the checks on that commit
    /// and, while they are red, fix turns in the same session, one commit each), the generation compared again, and the envelope: green when the checks are, failed with the envelope as the evidence when they are not. A fix turn that stops with a limit, a refusal or a
    /// crash is the attempt's stop: the job goes on with the next model (<see cref="RoleFollowed.FailsOver"/>), after the worktree is put back to the last coai commit.
    /// </summary>
    /// <param name="answers">How many models have answered turn 0 so far, this one included: the second and later turn 0 commits say which attempt made them.</param>
    private async Task<RoleFollowed> FollowAsync(IRoleRun run, GitHistory git, Prepared prepared, CoderTaskSite site, CoderFixLoop loop, CoderReport? report, CoderFixSession session, int answers, CoderOsLayer layer, int cap, string note, CoderInstructionsRecord instructions, CancellationToken ct)
    {
        var message = answers == 1 ? CoderCommits.Message(run.Request.JobId, site.Id) : CoderCommits.AttemptMessage(run.Request.JobId, site.Id, answers);
        var ended = await loop.RunAsync(report, message, session, ct);

        return ended switch
        {
            CoderLoopEnd.Done done => new RoleFollowed.Ended(await EnvelopeAsync(run, git, prepared, layer, done, new CoderChecksReport(done.Rounds, cap, note), instructions, ct)),
            CoderLoopEnd.FailOver failOver => new RoleFollowed.FailsOver(failOver.Why),
            _ => new RoleFollowed.Ended(((CoderLoopEnd.Halted)ended).Outcome),
        };
    }

    /// <summary>The loop ended with something to show: list the files from git, compare the generation again, and make the envelope — a success when it is green, a failure carrying the same envelope when it is not.</summary>
    private async Task<RoleWorkOutcome> EnvelopeAsync(IRoleRun run, GitHistory git, Prepared prepared, CoderOsLayer layer, CoderLoopEnd.Done end, CoderChecksReport checks, CoderInstructionsRecord instructions, CancellationToken ct)
    {
        var changes = end.Head == prepared.BaseSha ? [] : await CoderCommits.ChangesAsync(git, prepared.Tree, prepared.BaseSha, end.Head, ct);
        if (changes is null)
        {
            return ct.IsCancellationRequested ? new RoleWorkOutcome.Stopped() : new RoleWorkOutcome.Failed("coder-commit", "git could not list the files the commit changed");
        }

        Boundary?.Invoke("before-result");
        if (ct.IsCancellationRequested || !run.StillWriter())
        {
            return new RoleWorkOutcome.Stopped();
        }

        var json = new CoderEnvelope(run.Request.JobId, prepared.BaseSha, end.Head, prepared.Branch, end.Report is not null, end.FixTurns, changes, end.Report, layer.Evidence, checks) { Instructions = instructions }.ToJson();

        return end.Stop.Length == 0 ? new RoleWorkOutcome.Succeeded(json) : new RoleWorkOutcome.Failed(end.Stop, end.Reason, json);
    }

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
