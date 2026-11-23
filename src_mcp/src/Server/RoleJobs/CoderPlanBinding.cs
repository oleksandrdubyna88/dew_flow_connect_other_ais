using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Worktrees;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// What a coder job's <c>job.json</c> records about the plan it was started under (<see cref="RoleJobRecord.Plan"/>): written once, at the start, from the feature's own session.
/// </summary>
/// <param name="StartBranch">The branch <c>repoPath</c> was on when the job started; empty on a detached HEAD.</param>
/// <param name="SourceSession">The session id of that branch; empty when it had no session or no plan that proceeded.</param>
/// <param name="PlanHash">The git blob id of the plan file as the plan round read it; empty with no source session.</param>
/// <param name="PlanCommit">The commit that plan round ran on.</param>
/// <param name="PlanPath">The plan file, repository-relative; empty for a plan that was pasted as text (no task can be shown to belong to one).</param>
internal sealed record CoderPlanLink(string StartBranch, string SourceSession, string PlanHash, string PlanCommit, string PlanPath)
{
    public static CoderPlanLink None { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

    /// <summary>Strings a reader may meet as null (a field an older or newer build did not write, doctrine 4a) become empty.</summary>
    public CoderPlanLink Normalised() => new(StartBranch ?? string.Empty, SourceSession ?? string.Empty, PlanHash ?? string.Empty, PlanCommit ?? string.Empty, PlanPath ?? string.Empty);
}

/// <summary>How the gate answered a branch: it is no coder job's, the job is refused with a sentence, or the plan is bound.</summary>
internal abstract record PlanBindingOutcome
{
    private PlanBindingOutcome()
    {
    }

    /// <summary>No coder job of this data folder made this branch: nothing is inherited and the session is judged exactly as it always was.</summary>
    public sealed record NotACoderJob : PlanBindingOutcome;

    /// <summary>A coder job made it, and the binding does not hold; <paramref name="Sentence"/> says which part.</summary>
    public sealed record Refused(string Sentence) : PlanBindingOutcome;

    /// <summary>The binding holds: the plan the session inherits and the text of that plan revision, read from the commit that was approved.</summary>
    public sealed record Bound(InheritedPlan Plan, string PlanText) : PlanBindingOutcome;
}

/// <summary>
/// The plan binding of a coder job (epoch 3 E2 s5; <c>server.md</c> § 16 item 1, plan gate d4): <see cref="CaptureAsync"/> writes into <c>job.json</c>, at the start, which branch the job was started from, the session of that branch and the
/// plan revision that session had proceeded; <see cref="CheckAsync"/> proves that record again, every time the gate is asked, and only then lets the job's session inherit the proceed.
/// </summary>
/// <remarks>
/// <para><b>The record is the authority, the branch name is only the key.</b> A branch <c>coder/&lt;jobId&gt;</c> inherits nothing unless <c>job.json</c> of that id in THIS data folder says a coder job of this repository made it and carries
/// a link; a branch named like one that no job made is <see cref="PlanBindingOutcome.NotACoderJob"/> and is judged as it always was (no plan round proceeded, so refused).</para>
/// <para><b>What must hold</b>, each with its own sentence: the starting branch's session is still the one the job named and its plan has proceeded; that session's plan revision is the one recorded (a different blob or commit means the
/// plan changed); the plan was a committed file (a pasted text has no folder a task can belong to); the job's base commit descends from the commit the plan round ran on; at that base the plan file is byte for byte the approved blob; and the
/// task file is a file of that base inside the folder of the plan file. The plan text the round is judged against is read from the approved commit, never from the session's mutable text.</para>
/// <para>Every git read goes through <see cref="GitHistory"/>, so the one launcher's timeout and tree-kill apply; one that does not answer in time is a refusal that says so, never a pass.</para>
/// </remarks>
internal static class CoderPlanBinding
{
    private const string GateFirst = "the plan gate comes first: review the plan on the branch the job is started from (review_plan with committedPlan), then start the coder job";

    /// <summary>The link a coder job records at its start. Never throws and never refuses: a start under no proceeded plan is recorded as such, and the gate says so when it is asked.</summary>
    public static async Task<CoderPlanLink> CaptureAsync(SessionStore sessions, RoleJobRequest request, CancellationToken ct = default)
    {
        var branch = await HeadBranchAsync(new GitHistory(new ProcessLauncher()), request.RepoPath, ct);
        var session = branch.Length == 0 ? null : sessions.Load(request.RepoPath, branch);

        return session is { State.PlanProceeded: true } && ApprovedPlan(session) is { } plan
            ? new CoderPlanLink(branch, session.State.SessionId, HashOf(plan), plan.Commit, plan.Path)
            : CoderPlanLink.None with { StartBranch = branch };
    }

    /// <summary>What the gate says of <paramref name="branch"/>: no coder job's, refused with the reason, or bound.</summary>
    public static async Task<PlanBindingOutcome> CheckAsync(string dataDir, SessionStore sessions, string repoPath, string branch, Serilog.ILogger log, CancellationToken ct = default)
    {
        if (CoderJobOf(dataDir, repoPath, branch, log) is not { } job)
        {
            return new PlanBindingOutcome.NotACoderJob();
        }

        var (plan, unproven) = Proven(job, sessions, repoPath);
        if (plan is null)
        {
            return Refused(unproven);
        }

        var git = new GitHistory(new ProcessLauncher());
        var unmet = await UnmetAtBaseAsync(git, repoPath, job, plan, ct);
        if (unmet.Length > 0)
        {
            return Refused(unmet);
        }

        var read = await CommittedPlanReader.ReadAtCommitAsync(git, repoPath, plan.Commit, plan.Path, ct);

        return read is PlanRead.Accepted { Source.Blob: var blob } accepted && blob == plan.Blob
            ? new PlanBindingOutcome.Bound(new InheritedPlan(job.Plan!.SourceSession, job.Plan.StartBranch, HashOf(plan), plan.Commit, plan.Path, job.JobId), accepted.Text)
            : Refused($"the approved plan file {plan.Path} cannot be read at commit {GitIds.Short(plan.Commit)}: {(read as PlanRead.Refused)?.Sentence ?? "its blob is not the one that was approved"}");
    }

    private static PlanBindingOutcome Refused(string why) => new PlanBindingOutcome.Refused($"{why}; {GateFirst}");

    /// <summary>The coder job of this data folder and repository that made <paramref name="branch"/>, or null — the branch is only the key to the record.</summary>
    private static RoleJobRecord? CoderJobOf(string dataDir, string repoPath, string branch, Serilog.ILogger log) =>
        branch.StartsWith(CoderWorktree.BranchPrefix, StringComparison.Ordinal) && RoleJobIds.IsValid(branch[CoderWorktree.BranchPrefix.Length..])
            && new RoleJobStore(dataDir, TimeProvider.System, log).Read(branch[CoderWorktree.BranchPrefix.Length..]) is { Role: RoleDescriptors.Coder } job
            && SessionKey.For(job.Repo, string.Empty) == SessionKey.For(repoPath, string.Empty)
            ? job
            : null;

    /// <summary>The plan the job's record names, shown to be the one its source session proceeded — or the sentence that says it is not. No git.</summary>
    private static (PlanSource? Plan, string Refusal) Proven(RoleJobRecord job, SessionStore sessions, string repoPath)
    {
        if (job.Plan is not { } link)
        {
            return (null, $"coder job {job.JobId} records no plan it was started under (a job of an older build), so its branch has no plan to inherit");
        }

        if (link.SourceSession.Length == 0)
        {
            return (null, $"coder job {job.JobId} was started {(link.StartBranch.Length == 0 ? "on a detached HEAD" : $"from branch '{link.StartBranch}'")}, which had no plan that proceeded, so its branch has no plan to inherit");
        }

        return sessions.Load(repoPath, link.StartBranch) is { } source
            ? Revision(job, link, source)
            : (null, $"coder job {job.JobId} was started from branch '{link.StartBranch}' (session {link.SourceSession}), which has no session now");
    }

    private static (PlanSource? Plan, string Refusal) Revision(RoleJobRecord job, CoderPlanLink link, PersistedSession source)
    {
        if (source.State.SessionId != link.SourceSession)
        {
            return (null, $"coder job {job.JobId} was started from session {link.SourceSession} of branch '{link.StartBranch}', but that branch's session is now {source.State.SessionId}: the plan it proceeded is not the one the job was started under");
        }

        var approved = source.State.PlanProceeded ? ApprovedPlan(source) : null;
        if (approved is null)
        {
            return (null, $"session {link.SourceSession} of branch '{link.StartBranch}' has no plan that proceeded now");
        }

        return HashOf(approved) != link.PlanHash || approved.Commit != link.PlanCommit
            ? (null, $"the plan of session {link.SourceSession} changed since the job started (its plan hash is now {GitIds.Short(HashOf(approved))} on commit {GitIds.Short(approved.Commit)}; the job recorded {GitIds.Short(link.PlanHash)} on {GitIds.Short(link.PlanCommit)})")
            : approved.Origin == PlanOrigin.Commit
            ? (approved, string.Empty)
            : (null, $"the plan of session {link.SourceSession} was pasted as text, so no task can be shown to belong to it (a plan with no file has no folder)");
    }

    /// <summary>The ground rules of git: base descends from the plan's commit; at the base the plan file is the approved blob; the task is a file of the base below the plan file's folder.</summary>
    private static async Task<string> UnmetAtBaseAsync(GitHistory git, string repoPath, RoleJobRecord job, PlanSource plan, CancellationToken ct)
    {
        if (!GitHistory.IsCommitish(job.BaseSha))
        {
            return $"coder job {job.JobId} records no base commit, so it cannot be shown to descend from the approved plan";
        }

        var ancestry = await git.IsAncestorAsync(repoPath, plan.Commit, job.BaseSha, ct);
        if (!ancestry.Ran || !ancestry.Ok)
        {
            return ancestry.Ran
                ? $"the job's base commit {job.BaseSha} does not descend from {plan.Commit}, the commit the approved plan was reviewed on"
                : "git did not answer in time while the job's base commit was compared with the approved plan; try again in a moment";
        }

        return await PlanAtBaseAsync(git, repoPath, job, plan, ct);
    }

    private static async Task<string> PlanAtBaseAsync(GitHistory git, string repoPath, RoleJobRecord job, PlanSource plan, CancellationToken ct)
    {
        var atBase = await CommittedPlanReader.ReadAtCommitAsync(git, repoPath, job.BaseSha, plan.Path, ct);
        if (atBase is not PlanRead.Accepted { Source.Blob: var blob } || blob != plan.Blob)
        {
            return $"the plan file {plan.Path} at the job's base commit {GitIds.Short(job.BaseSha)} differs from the revision that was approved (blob {GitIds.Short(plan.Blob)}){(atBase is PlanRead.Refused refused ? $": {refused.Sentence}" : string.Empty)}";
        }

        var folder = plan.Path.Contains('/') ? plan.Path[..plan.Path.LastIndexOf('/')] : string.Empty;
        if (folder.Length > 0 && !job.TaskFile.StartsWith(folder + "/", StringComparison.Ordinal))
        {
            return $"task {job.TaskFile} does not belong to the approved plan: it is not below {folder}, the folder of the plan file {plan.Path}";
        }

        var task = await git.HasPathAsync(repoPath, job.BaseSha, job.TaskFile, ct);

        return task.Ran && task.Ok ? string.Empty : $"task {job.TaskFile} is not a file of the job's base commit {GitIds.Short(job.BaseSha)}, so it does not belong to the approved plan revision";
    }

    /// <summary>The plan source of the last completed plan round of the session — the revision its proceed was about. Null when it has none.</summary>
    private static PlanSource? ApprovedPlan(PersistedSession session) =>
        session.Rounds.LastOrDefault(round => round.Stage == nameof(Stage.PlanReview) && round.Status == RoundRecord.Done && round.PlanSource is not null)?.PlanSource;

    /// <summary>The id of the plan revision: the git blob of a committed file; the text id of a pasted one.</summary>
    private static string HashOf(PlanSource plan) => plan.Origin == PlanOrigin.Commit ? plan.Blob : plan.TextId;

    private static async Task<string> HeadBranchAsync(GitHistory git, string repoPath, CancellationToken ct)
    {
        var branch = await git.HeadBranchAsync(repoPath, ct);

        return branch is { Ran: true, Ok: true } && branch.Lines.Count > 0 ? branch.Lines[0] : string.Empty;
    }
}
