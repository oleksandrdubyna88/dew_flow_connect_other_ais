using System.Globalization;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Coder;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Roles;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Worktrees;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>What the loop of the checks and fix turns ended in.</summary>
internal abstract record CoderLoopEnd
{
    private CoderLoopEnd()
    {
    }

    /// <summary>
    /// The loop reached an end with something to show: <see cref="Stop"/> empty is green; any other word is the stop the job fails with, <see cref="Reason"/> its sentence. The envelope is made from this — the head, the report and every round.
    /// </summary>
    public sealed record Done(string Head, int FixTurns, CoderReport? Report, IReadOnlyList<CoderChecksRound> Rounds, string Stop, string Reason) : CoderLoopEnd;

    /// <summary>The loop ended without an envelope: the job was stopped (a cancel, a stale lease) or a commit failed. This is what the job ends with.</summary>
    public sealed record Halted(RoleWorkOutcome Outcome) : CoderLoopEnd;
}

/// <summary>
/// The way to green (epoch 3 E2 s4 t2; <c>v2/architecture/server.md</c> § 10): the checks of a coder job run on the commit of turn 0 and, while a step is red, the failure digest goes back to the SAME claude session — <c>--resume</c> with the handle of turn 0, bound
/// to the job and the worktree — as a fix turn; the turn's work is committed by coai (ONE commit per fix turn) and checked again. At most <see cref="Cap"/> fix turns (<c>COAI_FIX_ROUNDS</c>, <see cref="FixRounds"/>); a job still red after them fails with <c>not-green</c>
/// and the last digest in its result.
/// </summary>
/// <remarks>
/// <para><b>The same session, never a new one.</b> A fix turn is built by the same launcher as turn 0 (<see cref="RoleModelLauncher"/> with the job's <see cref="LaunchConfinement.Coder"/> and OS layer), so it runs in the same worktree, with the same flags, inside the
/// same layer; the handle is the one turn 0 reported, whatever a later turn answered with. A vendor that named no session cannot be resumed, so the job ends <c>not-green</c> and says why — a fresh session would have lost what the coder knows.</para>
/// <para><b>What a fix turn's answer is for.</b> The checks are the verdict. The answer is read as turn 0's was (a declared refusal ends the job; a valid report replaces the earlier one), but a report that is not valid costs nothing: no repair turn is spent on it and the report
/// of the turn before stays.</para>
/// <para><b>The budget.</b> The job's <c>timeoutMinutes</c> bounds the whole of it: a round of checks or a fix turn is not begun with no time left, a fix turn gets at most what is left (and at most <see cref="AttemptTimeout"/>), and the runner's own token ends a turn or a
/// check that passes it — the unit is stopped by name. The branch and its commits stay.</para>
/// <para><b>A stop that is not red.</b> No toolchain file, a file that is wrong, a boundary that did not hold and a worktree the checks changed end the job at once with their own words (<see cref="CoderChecksRound.Stop"/>): none of them is a fault the coder can fix with a turn.</para>
/// </remarks>
internal sealed class CoderFixLoop(IRoleRun run, GitHistory git, CoderOsLayer layer, CoderTree tree, string baseSha, CoderTaskSite site, CoderFixSession session, long started)
{
    private const int DigestChars = 1500;

    /// <summary>How many fix turns the job may make.</summary>
    public int Cap { get; init; }

    /// <summary>The most one turn may take; the job's budget bounds it as well.</summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromMinutes(60);

    /// <summary>A hook at the commit points (<c>before-commit</c>), for the tests; null in the product.</summary>
    public Action<string>? Boundary { get; init; }

    /// <param name="head">The commit of turn 0.</param>
    /// <param name="report">The valid report of turn 0, or null.</param>
    /// <param name="ct">The job's token.</param>
    public async Task<CoderLoopEnd> RunAsync(string head, CoderReport? report, CancellationToken ct)
    {
        var checks = new CoderChecks(run, git, layer, tree, baseSha);
        var rounds = new List<CoderChecksRound>();
        var fixes = 0;
        while (true)
        {
            if (OverBudget())
            {
                return Ended(head, fixes, report, rounds, "timeout", TimeoutReason());
            }

            var round = await checks.RunAsync(fixes, head, ct);
            rounds.Add(round);
            if (!round.Red)
            {
                return Ended(head, fixes, report, rounds, round.Stop, round.Note);
            }

            if (fixes >= Cap || !ConsultantHandle.IsWellFormed(session.Handle))
            {
                return Ended(head, fixes, report, rounds, round.Stop, NotGreen(round, fixes));
            }

            if (OverBudget())
            {
                return Ended(head, fixes, report, rounds, "timeout", TimeoutReason());
            }

            fixes++;
            var turn = await FixTurnAsync(fixes, round, report, ct);
            if (turn.Failure is { } failure)
            {
                return Ended(head, fixes, report, rounds, failure.Stop, failure.Reason);
            }

            report = turn.Report;
            var committed = await CommitAsync(fixes, ct);
            if (committed.Halted is { } halted)
            {
                return halted;
            }

            head = committed.Head;
        }
    }

    private static CoderLoopEnd Ended(string head, int fixes, CoderReport? report, List<CoderChecksRound> rounds, string stop, string reason) =>
        new CoderLoopEnd.Done(head, fixes, report, [.. rounds], stop, reason);

    // ---------- one fix turn ----------

    private sealed record FixTurn(CoderReport? Report, (string Stop, string Reason)? Failure);

    /// <summary>One fix turn: the digest as the prompt, the session of turn 0 resumed, the answer read. The ledger gets its line; the evidence — the digest sent and the answer — is kept under <c>fix/turn-&lt;k&gt;/</c>.</summary>
    private async Task<FixTurn> FixTurnAsync(int turn, CoderChecksRound round, CoderReport? report, CancellationToken ct)
    {
        run.Phase("fix", turn);
        var folder = Path.Combine(run.Folder, "fix", $"turn-{turn.ToString(CultureInfo.InvariantCulture)}");
        var prompt = CoderPrompt.Fix(turn, Cap, round.Digest, round.Note);
        var left = Left();
        var begun = run.Clock.GetUtcNow().UtcDateTime;
        var launched = await new RoleModelLauncher(run.Settings, run.Children) { Coder = session.Confinement, Layer = layer }
            .LaunchAsync(session.Model, prompt, run.Request.RepoPath, folder, left < AttemptTimeout ? left : AttemptTimeout, ct, session.Handle);
        var ended = run.Clock.GetUtcNow().UtcDateTime;
        Keep(folder, "prompt.txt", prompt, turn);
        var stop = ct.IsCancellationRequested
            ? new RoleStop("stopped", "the job was stopped while this model ran", "stopped", false, false)
            : RoleFailover.StopOfLaunch(session.Model, launched);
        RoleFailover.RecordLedger(run, session.Model, launched, stop, ended - begun, launched.Launch?.Usage ?? Usage.None);
        ct.ThrowIfCancellationRequested();
        if (stop is not null)
        {
            return new FixTurn(report, OverBudget() ? ("timeout", TimeoutReason()) : (stop.Word, $"fix turn {turn} of {Cap}: {stop.Reason}"));
        }

        var answer = launched.Runtime.ReadAdvice(launched.Launch!.Answer!);
        Keep(folder, "answer.txt", answer, turn);
        var judge = new CoderJudge(site.DodIds, () => CoderWork.RepairTurns);

        return judge.Judge(answer) is RoleAnswerVerdict.Refused refusal
            ? new FixTurn(report, (RoleStopWords.Refusal, $"fix turn {turn} of {Cap}: {Redaction.SafeText(refusal.Reason, Redaction.TitleLimit)}"))
            : new FixTurn(judge.Accepted ?? report, null);
    }

    private void Keep(string folder, string name, string text, int turn) =>
        EvidenceFile.Keep(folder, name, Redaction.SafeSource(text), e => run.Log.Warning(e, "role job {JobId}: {Name} of fix turn {Turn} could not be kept", run.Request.JobId, name, turn));

    // ---------- the commit of a fix turn ----------

    private sealed record Committed(string Head, CoderLoopEnd? Halted);

    /// <summary>The ONE commit of fix turn <paramref name="turn"/>; the generation is compared first, as before turn 0's. No change means no commit and the head stays where it was.</summary>
    private async Task<Committed> CommitAsync(int turn, CancellationToken ct)
    {
        run.Phase("coder-commit", turn);
        Boundary?.Invoke("before-commit");
        if (ct.IsCancellationRequested || !run.StillWriter())
        {
            return new Committed(string.Empty, new CoderLoopEnd.Halted(new RoleWorkOutcome.Stopped()));
        }

        var made = await CoderCommits.CommitAsync(git, tree, CoderCommits.FixMessage(run.Request.JobId, site.Id, turn), ct);

        return made.Failure is { } failure
            ? new Committed(string.Empty, new CoderLoopEnd.Halted(ct.IsCancellationRequested ? new RoleWorkOutcome.Stopped() : failure))
            : new Committed(made.Head, null);
    }

    // ---------- the budget and the words ----------

    private TimeSpan Budget() => TimeSpan.FromMinutes(Math.Max(1, run.Request.TimeoutMinutes));

    private TimeSpan Left() => Budget() - run.Clock.GetElapsedTime(started);

    private bool OverBudget() => Left() <= TimeSpan.Zero;

    private string TimeoutReason() => $"the job ran past its {run.Request.TimeoutMinutes} minute budget";

    private string NotGreen(CoderChecksRound round, int fixes)
    {
        var why = ConsultantHandle.IsWellFormed(session.Handle) ? string.Empty : "; the vendor named no session for turn 0, so the coder cannot be resumed and no fix turn was made";
        var made = fixes == 0 && why.Length == 0 ? $"{FixRounds.Key} is {Cap}, so no fix turn was made" : $"still red after {fixes} fix turn{(fixes == 1 ? string.Empty : "s")} (the cap is {Cap})";
        var digest = FailureDigest.Json(round.Digest);

        return $"the checks are {made}{why}: {round.Note}; the last failure digest: {(digest.Length > DigestChars ? digest[..DigestChars] + "…" : digest)} (the whole of it is in the result, lastDigest)";
    }
}

/// <summary>The session of turn 0, which every fix turn resumes: the model that answered, the handle it reported, and the confinement that binds the handle to this job and worktree.</summary>
internal sealed record CoderFixSession(ModelRef Model, string Handle, LaunchConfinement.Coder Confinement);
