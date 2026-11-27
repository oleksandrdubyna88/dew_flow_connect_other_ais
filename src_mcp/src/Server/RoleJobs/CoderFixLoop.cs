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

    /// <summary>
    /// A fix turn of the model that was running stopped in a way that goes on to the next model — a limit, a refusal, a crash (<see cref="Why"/>). Nothing was committed for that turn; the work puts the worktree back to <see cref="CoderFixLoop.LastCommit"/> and the
    /// next model starts from turn 0. The fix turns used so far stay used.
    /// </summary>
    public sealed record FailOver(RoleStop Why) : CoderLoopEnd;

    /// <summary>The loop ended without an envelope: the job was stopped (a cancel, a stale lease) or a commit failed. This is what the job ends with.</summary>
    public sealed record Halted(RoleWorkOutcome Outcome) : CoderLoopEnd;
}

/// <summary>
/// The way to green (epoch 3 E2 s4 t2, t3; <c>v2/architecture/server.md</c> § 10): the checks of a coder job run on the commit of turn 0 and, while a step is red, the failure digest goes back to the SAME claude session — <c>--resume</c> with the handle of turn 0, bound
/// to the job and the worktree — as a fix turn; the turn's work is committed by coai (ONE commit per fix turn) and checked again. At most <see cref="Cap"/> fix turns (<c>COAI_FIX_ROUNDS</c>, <see cref="FixRounds"/>); a job still red after them fails with <c>not-green</c>
/// and the last digest in its result.
/// </summary>
/// <remarks>
/// <para><b>The same session, never a new one — within one model.</b> A fix turn is built by the same launcher as turn 0 (<see cref="RoleModelLauncher"/> with the job's <see cref="LaunchConfinement.Coder"/> and OS layer), so it runs in the same worktree, with the same flags, inside the
/// same layer; the handle is the one turn 0 reported, whatever a later turn answered with. A vendor that named no session cannot be resumed, so the job ends <c>not-green</c> and says why — a fresh session would have lost what the coder knows.</para>
/// <para><b>One loop for the whole job, across models.</b> The object lives as long as the job: when a model stops (a limit, a refusal, a crash — in turn 0 or in a fix turn) and the next model of the route starts, <see cref="RunAsync"/> is called again with the new model's session,
/// and the fix turns already used STAY used — the cap is the job's, not the model's — as do the rounds of checks. The rounds are numbered in the order they ran, which is the number of the fix turn until a switch; the new model's turn 0 is checked as the next round.</para>
/// <para><b>What a fix turn's answer is for.</b> The checks are the verdict. The answer is read as turn 0's was (a declared refusal fails over to the next model; a valid report replaces the earlier one), but a report that is not valid costs nothing: no repair turn is spent on it and the report
/// of the turn before stays.</para>
/// <para><b>The budget.</b> The job's <c>timeoutMinutes</c> bounds the whole of it: a round of checks or a fix turn is not begun with no time left, a fix turn gets at most what is left (and at most <see cref="AttemptTimeout"/>), and the runner's own token ends a turn or a
/// check that passes it — the unit is stopped by name. The branch and its commits stay.</para>
/// <para><b>A stop that is not red.</b> No toolchain file, a file that is wrong, a boundary that did not hold and a worktree the checks changed end the job at once with their own words (<see cref="CoderChecksRound.Stop"/>): none of them is a fault the coder can fix with a turn,
/// and none of them is a model that stopped, so none fails over.</para>
/// </remarks>
internal sealed class CoderFixLoop(IRoleRun run, GitHistory git, CoderOsLayer layer, CoderTree tree, string baseSha, CoderTaskSite site, long started)
{
    private const int DigestChars = 1500;

    private readonly List<CoderChecksRound> _rounds = [];
    private int _fixes;
    private string _last = string.Empty;
    private ConsultationBilled? _counter;

    /// <summary>The release the CLI of the last fix turn named (empty before any): a CLI may update itself between two launches of one job, and the envelope names the latest beside the last measured one. A record, never a condition.</summary>
    public string LastCliVersion { get; private set; } = string.Empty;

    /// <summary>How many fix turns the job may make — for the whole job, whatever the number of models that take part.</summary>
    public int Cap { get; init; }

    /// <summary>The most one turn may take; the job's budget bounds it as well.</summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromMinutes(60);

    /// <summary>A hook at the commit points (<c>before-commit</c>), for the tests; null in the product.</summary>
    public Action<string>? Boundary { get; init; }

    /// <summary>The vendors a coder can be launched on (epoch 3 E3 s7 t2): a fix turn runs on its session's vendor, with that vendor's prompt words, and its stream is checked by that vendor's seam before the turn is committed.</summary>
    internal CoderVendorSet Vendors { get; init; } = CoderVendors.Shipped;

    /// <summary>
    /// The last commit coai made in the worktree — the base commit until it makes one. Where the worktree goes back to when a model stops (<see cref="CoderRestore"/>); moves with every commit of a turn 0 or a fix turn.
    /// </summary>
    public string LastCommit => _last.Length > 0 ? _last : baseSha;

    /// <summary>
    /// What a model's turn 0 wrote is committed (the one commit of that turn, <paramref name="message"/>), checked, and fixed while it is red.
    /// </summary>
    /// <param name="report">The valid report of turn 0, or null.</param>
    /// <param name="message">The message of the commit of this turn 0.</param>
    /// <param name="session">The session of THIS model's turn 0: what its fix turns resume.</param>
    /// <param name="ct">The job's token.</param>
    public async Task<CoderLoopEnd> RunAsync(CoderReport? report, string message, CoderFixSession session, CancellationToken ct)
    {
        var first = await CommitAsync(message, session, ct);
        if (first.Halted is { } halted)
        {
            return halted;
        }

        var head = first.Head;
        var checks = new CoderChecks(run, git, layer, tree, baseSha);
        while (true)
        {
            if (OverBudget())
            {
                return Ended(head, report, "timeout", TimeoutReason());
            }

            var round = await checks.RunAsync(_rounds.Count, head, ct);
            _rounds.Add(round);
            if (!round.Red)
            {
                return Ended(head, report, round.Stop, round.Note);
            }

            if (_fixes >= Cap || !ConsultantHandle.IsWellFormed(session.Handle))
            {
                return Ended(head, report, round.Stop, NotGreen(round, session));
            }

            if (OverBudget())
            {
                return Ended(head, report, "timeout", TimeoutReason());
            }

            _fixes++;
            var turn = await FixTurnAsync(_fixes, round, report, session, ct);
            if (turn.Failure is { } failure)
            {
                return failure.FailsOver is { } why ? new CoderLoopEnd.FailOver(why) : Ended(head, report, failure.Stop, failure.Reason);
            }

            report = turn.Report;
            var committed = await CommitAsync(CoderCommits.FixMessage(run.Request.JobId, site.Id, _fixes), session, ct);
            if (committed.Halted is { } stopped)
            {
                return stopped;
            }

            head = committed.Head;
        }
    }

    private CoderLoopEnd Ended(string head, CoderReport? report, string stop, string reason) =>
        new CoderLoopEnd.Done(head, _fixes, report, [.. _rounds], stop, reason);

    // ---------- one fix turn ----------

    /// <summary>Why a fix turn gave no answer: the word and sentence the job would end with, and — for a limit, a refusal or a crash — the stop that fails over to the next model instead.</summary>
    private sealed record FixStop(string Stop, string Reason, RoleStop? FailsOver);

    private sealed record FixTurn(CoderReport? Report, FixStop? Failure);

    /// <summary>One fix turn: the digest as the prompt, the session of turn 0 resumed, the answer read. The ledger gets its line; the evidence — the digest sent and the answer — is kept under <c>fix/turn-&lt;k&gt;/</c>.</summary>
    private async Task<FixTurn> FixTurnAsync(int turn, CoderChecksRound round, CoderReport? report, CoderFixSession session, CancellationToken ct)
    {
        run.Phase("fix", turn);
        var folder = Path.Combine(run.Folder, "fix", $"turn-{turn.ToString(CultureInfo.InvariantCulture)}");
        var prompt = CoderPrompt.Fix(turn, Cap, round.Digest, round.Note, VendorOf(session.Model));
        var left = Left();
        var begun = run.Clock.GetUtcNow().UtcDateTime;
        var launched = await new RoleModelLauncher(run.Settings, run.Children) { Coder = session.Confinement, Layer = layer, Vendors = Vendors }
            .LaunchAsync(session.Model, prompt, run.Request.RepoPath, folder, left < AttemptTimeout ? left : AttemptTimeout, ct, session.Handle, RoleKeyRoad.For(run).Resume(session.Model, session.KeyId));
        var ended = run.Clock.GetUtcNow().UtcDateTime;
        Keep(folder, "prompt.txt", prompt, turn);
        var stop = ct.IsCancellationRequested
            ? await CoderTurnCheck.StopOfACutTurnAsync(Vendors, session.Model, launched, session.Handle, layer.Job) ?? new RoleStop("stopped", "the job was stopped while this model ran", "stopped", false, false)
            : CoderTurnCheck.StopFirst(Vendors, session.Model, launched, session.Handle, layer.Job) ?? RoleFailover.StopOfLaunch(session.Model, launched);
        if (launched.CliVersion.Length > 0)
        {
            LastCliVersion = launched.CliVersion;
        }

        // The usage line of this turn holds this turn only: a cumulative vendor's report is cut by what the session had reported before (V16).
        var reported = launched.Launch?.Usage ?? Usage.None;
        var share = launched.Runtime.UsageIsCumulative ? ConsultationUsage.Less(reported, _counter ?? session.Counter) : reported;
        if (launched.Runtime.UsageIsCumulative && ConsultationUsage.CarriesACounter(reported))
        {
            _counter = ConsultationBilled.Of(reported);
        }

        RoleFailover.RecordLedger(run, session.Model, launched, stop, ended - begun, share);
        if (stop is not { Word: RoleStopWords.CoderBoundary })
        {
            ct.ThrowIfCancellationRequested();
        }

        if (stop is not null)
        {
            // A boundary outranks the budget's word: it is what the job ends with, as it is on a turn that was not cut (M11 leg a).
            if (OverBudget() && stop.Word != RoleStopWords.CoderBoundary)
            {
                return new FixTurn(report, new FixStop("timeout", TimeoutReason(), null));
            }

            var said = stop with { Reason = $"fix turn {turn} of {Cap}: {stop.Reason}" };

            return new FixTurn(report, new FixStop(said.Word, said.Reason, said.FailsOver ? said : null));
        }

        var answer = launched.Runtime.ReadAdvice(launched.Launch!.Answer!);
        Keep(folder, "answer.txt", answer, turn);
        var judge = new CoderJudge(site.DodIds, () => CoderWork.RepairTurns);

        if (judge.Judge(answer) is RoleAnswerVerdict.Refused refusal)
        {
            var reason = $"fix turn {turn} of {Cap}: {Redaction.SafeText(refusal.Reason, Redaction.TitleLimit)}";

            return new FixTurn(report, new FixStop(RoleStopWords.Refusal, reason, new RoleStop(RoleStopWords.Refusal, reason, "refused", false, false)));
        }

        return new FixTurn(judge.Accepted ?? report, null);
    }

    /// <summary>The seam of the model's vendor; a vendor that cannot be launched never has a fix turn, so claude's words are the fallback only for a model this loop was never given.</summary>
    private ICoderVendor VendorOf(ModelRef model) => Vendors.Resolve(model.Vendor) is CoderVendorResolution.Launchable found ? found.Vendor : CoderVendors.Claude;

    private void Keep(string folder, string name, string text, int turn) =>
        EvidenceFile.Keep(folder, name, Redaction.SafeSource(text), e => run.Log.Warning(e, "role job {JobId}: {Name} of fix turn {Turn} could not be kept", run.Request.JobId, name, turn));

    // ---------- the commit of a turn ----------

    private sealed record Committed(string Head, CoderLoopEnd? Halted);

    /// <summary>The ONE commit of a turn (turn 0 or a fix turn); the generation is compared first. No change means no commit and the head stays where it was.</summary>
    private async Task<Committed> CommitAsync(string message, CoderFixSession session, CancellationToken ct)
    {
        run.Phase("coder-commit", _fixes);
        Boundary?.Invoke("before-commit");
        if (ct.IsCancellationRequested || !run.StillWriter())
        {
            return new Committed(string.Empty, new CoderLoopEnd.Halted(new RoleWorkOutcome.Stopped()));
        }

        var keys = RoleKeyRoad.For(run);

        // A turn that ran on a pool key is committed only after its files were searched for that key. The search NEVER fails open: the secret is the one the session kept in memory, else the pool's — and when neither can be had, there is no commit.
        var keyed = Server.Keys.KeyIds.IsValid(session.KeyId);
        var secret = session.Secret ?? (keyed ? keys.SecretOf(session.KeyId) : null);
        if (keyed && secret is null)
        {
            return new Committed(string.Empty, new CoderLoopEnd.Halted(new RoleWorkOutcome.Failed("coder-commit", "the claude key of this session cannot be read, so the files the coder wrote could not be searched for it; nothing was committed")));
        }

        var made = await CoderCommits.CommitAsync(git, tree, message, ct, secret, layer.SignInTokens());
        if (made.Failure is { } failure)
        {
            if (failure.Stop == RoleStopWords.KeyInOutput)
            {
                // The text of the turn's key is in the files the coder wrote: nothing was committed, and the key is dead (exposed), with ONE notice.
                keys.Exposed(session.KeyId);
            }

            return new Committed(string.Empty, new CoderLoopEnd.Halted(ct.IsCancellationRequested ? new RoleWorkOutcome.Stopped() : failure));
        }

        _last = made.Head;

        return new Committed(made.Head, null);
    }

    // ---------- the budget and the words ----------

    private TimeSpan Budget() => TimeSpan.FromMinutes(Math.Max(1, run.Request.TimeoutMinutes));

    private TimeSpan Left() => Budget() - run.Clock.GetElapsedTime(started);

    private bool OverBudget() => Left() <= TimeSpan.Zero;

    private string TimeoutReason() => $"the job ran past its {run.Request.TimeoutMinutes} minute budget";

    private string NotGreen(CoderChecksRound round, CoderFixSession session)
    {
        var why = ConsultantHandle.IsWellFormed(session.Handle) ? string.Empty : "; the vendor named no session for turn 0, so the coder cannot be resumed and no fix turn was made";
        var made = _fixes == 0 && why.Length == 0 ? $"{FixRounds.Key} is {Cap}, so no fix turn was made" : $"still red after {_fixes} fix turn{(_fixes == 1 ? string.Empty : "s")} (the cap is {Cap})";
        var digest = FailureDigest.Json(round.Digest);

        return $"the checks are {made}{why}: {round.Note}; the last failure digest: {(digest.Length > DigestChars ? digest[..DigestChars] + "…" : digest)} (the whole of it is in the result, lastDigest)";
    }
}

/// <summary>The session of a model's turn 0, which every fix turn of that model resumes: the model that answered, the handle it reported, and the confinement that binds the handle to this job and worktree — and the key (<see cref="RoleKey.KeyId"/>) the session runs on: every fix turn resumes with the SAME key.</summary>
internal sealed record CoderFixSession(ModelRef Model, string Handle, LaunchConfinement.Coder Confinement, string KeyId = "", Server.Keys.ClaudeKeySecret? Secret = null)
{
    /// <summary>The counter of a cumulative vendor after turn 0: the first fix turn's report is measured against it.</summary>
    public ConsultationBilled Counter { get; init; } = new();
}
