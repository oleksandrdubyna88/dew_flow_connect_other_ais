using System.Text.Json;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Roles;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>What a role makes of one model's answer: the result it publishes, a refusal the model declared, or output that is not the role's.</summary>
internal abstract record RoleAnswerVerdict
{
    private RoleAnswerVerdict()
    {
    }

    /// <summary>The answer is the role's output; <see cref="ResultJson"/> is what <c>result.json</c> will hold.</summary>
    public sealed record Valid(string ResultJson) : RoleAnswerVerdict;

    /// <summary>The model declared it would not do the work: the output arm <c>status: "refused"</c> with its <c>reason</c>. Fails over to the next model.</summary>
    public sealed record Refused(string Reason) : RoleAnswerVerdict;

    /// <summary>The model answered and the answer is not the role's output. Ends the job with <c>output-invalid</c> — no failover.</summary>
    public sealed record Invalid(string Reason) : RoleAnswerVerdict;

    /// <summary>
    /// The answer is not acceptable YET and the role can say why: <see cref="Prompt"/> is sent to the SAME session of the SAME model as the next turn (at most <see cref="RoleFailover.MaxRepairTurns"/> times). When the turns
    /// are used up, or the vendor named no session, the attempt ends as <see cref="Invalid"/> would, with <see cref="Reason"/>.
    /// </summary>
    public sealed record Repair(string Prompt, string Reason) : RoleAnswerVerdict;
}

/// <summary>
/// The one reading every role shares of an answer's <c>status</c> arm (spec A, "Stops": no vendor has a structured refusal field, so the role's OUTPUT carries one): the answer must be ONE JSON object; <c>status: "refused"</c>
/// is a refusal with its <c>reason</c>; anything else is the role's own to judge. No prose is matched — a refusal in words is not a refusal here, it is output the role's check will not accept.
/// </summary>
internal static class RoleAnswers
{
    public const string RefusedStatus = "refused";

    /// <param name="text">The answer as the vendor's adapter extracted it.</param>
    /// <param name="accept">The role's own check of a non-refused object: empty for acceptable, else the sentence that says what is wrong.</param>
    public static RoleAnswerVerdict Read(string text, Func<JsonElement, string> accept)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(text.Trim());
            root = document.RootElement.Clone();
        }
        catch (JsonException e)
        {
            return new RoleAnswerVerdict.Invalid($"the answer is not JSON: {Redaction.SafeText(e.Message, 160)}");
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return new RoleAnswerVerdict.Invalid("the answer is JSON but not one object");
        }

        if (root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String && status.GetString() == RefusedStatus)
        {
            var reason = root.TryGetProperty("reason", out var why) && why.ValueKind == JsonValueKind.String ? why.GetString() ?? string.Empty : string.Empty;

            return new RoleAnswerVerdict.Refused(reason.Length > 0 ? reason : "the model declared a refusal and gave no reason");
        }

        return accept(root) is { Length: > 0 } wrong ? new RoleAnswerVerdict.Invalid(wrong) : new RoleAnswerVerdict.Valid(root.GetRawText());
    }
}

/// <summary>The session an attempt's answer came from: the model, the handle its first turn reported (empty when the vendor named none) and the key the session runs on (<see cref="RoleKey.KeyId"/>; empty for a launch with no key) — every later turn of the session uses the SAME key — and the CLI release the session ran with (a record, never a condition).</summary>
internal sealed record RoleSession(ModelRef Model, string Handle, string KeyId = "", Server.Keys.ClaudeKeySecret? Secret = null, string CliVersion = "");

/// <summary>What a role made of an attempt AFTER the model's first valid answer (<see cref="RoleFailover.Follow"/>): the job's end, or a stop of the same model that goes on to the next one.</summary>
internal abstract record RoleFollowed
{
    private RoleFollowed()
    {
    }

    /// <summary>The job ends with this outcome — the envelope of a green job, a failure with its evidence, or a stop (a cancel, a stale lease). The attempt is recorded as a success unless the outcome is a stop.</summary>
    public sealed record Ended(RoleWorkOutcome Outcome) : RoleFollowed;

    /// <summary>The model stopped AFTER its first answer — in a fix turn — with a stop that fails over (<see cref="RoleStop.FailsOver"/>): the attempt is recorded with it and the next model starts from turn 0.</summary>
    public sealed record FailsOver(RoleStop Why) : RoleFollowed;
}

/// <summary>
/// The attempt loop of a role job (spec A, "Failover", epoch 3 E1 s3): the models of the job's route in order, each launched through its vendor's own CLI in a NEW session — a read-only role restarts from turn 0. Limit, refusal and
/// crash go to the next model AT ONCE; only the LAST model waits (the <see cref="RetryLadder"/>, inside the job's budget) and only for a throttle; a spent quota, a missing CLI or a lost sign-in also skip the vendor's other models;
/// <c>cancelled</c>, <c>tree-changed</c> and <c>output-invalid</c> end the job. Every switch is one Information line and one <c>role-model-switched</c> notice that names the vendor; every attempt is in <c>job.json</c>
/// (and the projection's <c>role_attempts</c>) with its evidence under <c>attempts/&lt;n&gt;/</c>, and every launch is one line of the usage ledger.
/// </summary>
/// <remarks>
/// <para><b>What a role brings:</b> its prompt and its reading of an answer (<see cref="RoleAnswers.Read"/> and its own check). Everything else — the CLI, the classification, the budget, the notices — is here, once, so the BA role
/// (story s4) and the roles after it do not each write a failover.</para>
/// <para><b>The tree is checked around every attempt</b> (<see cref="FilesystemInvariant"/>): a role is read-only by the CLI's flags, and a flag is the vendor's promise. A tree that changed ends the job as <c>tree-changed</c> — another
/// model would run on a tree nobody has looked at. A folder that is not a git work tree cannot be snapshotted; the attempt then runs without the check and the log says so once.</para>
/// <para><b>The time.</b> One attempt may take <see cref="AttemptTimeout"/> (ten minutes, the reviewer default) or what is left of the job's budget, whichever is less, so a model that hangs costs its share and not the job.</para>
/// </remarks>
internal sealed class RoleFailover(IRoleRun run, Func<string, RoleAnswerVerdict> judge)
{
    /// <summary>
    /// How many repair turns one attempt may use when the role's judge asks for them (<see cref="RoleAnswerVerdict.Repair"/>), in the same session. 0, the default, is a role that never repairs: its judge cannot answer Repair.
    /// </summary>
    public int MaxRepairTurns { get; init; }

    /// <summary>
    /// Set for the coder (epoch 3 E2 s3 t3): every launch of the loop is this job's WRITE launch, in its worktree, and a repair turn resumes the session through a handle bound to the job and the worktree
    /// (<see cref="RoleModelLauncher.Coder"/>). Null for the read-only roles.
    /// </summary>
    public LaunchConfinement.Coder? Coder { get; init; }

    /// <summary>
    /// The OS layer the coder's launches run inside (<see cref="CoderOsLayer"/>), the one road. Required whenever <see cref="Coder"/> is set — a launch of a coder without it is refused (<see cref="RoleModelLauncher.Layer"/>). With a coder the tree check
    /// below is NOT made: the main checkout is not read-only to the PERSON, so a change of it is evidence the layer records (decision 8, <c>server.md</c> § 16), never a reason to end the job; only the read-only roles keep <c>tree-changed</c>.
    /// </summary>
    public CoderOsLayer? OsLayer { get; init; }

    /// <summary>
    /// Set for the coder (epoch 3 E2 s4 t3): what the role does with an attempt whose first turn gave a valid answer — the commit of that turn, the checks and the fix turns, in the SAME session (<see cref="CoderFixLoop"/>). It runs INSIDE the attempt, so a stop of a
    /// later turn (a limit, a refusal, a crash) is the attempt's stop and fails over exactly as a stop of turn 0 does. Null for the read-only roles, whose attempt ends at the first valid answer.
    /// </summary>
    public Func<RoleSession, CancellationToken, Task<RoleFollowed>>? Follow { get; init; }

    /// <summary>
    /// Set for the coder (epoch 3 E2 s4 t3): called before every attempt after the first, once an attempt stopped and another is to start — puts the worktree back to the last coai commit (<see cref="CoderRestore"/>). Returns empty when the worktree is back and
    /// proven clean; any other text is the sentence the job fails with (<c>coder-worktree</c>): no model is started on a tree nobody has looked at. Null for the read-only roles, which write nothing.
    /// </summary>
    public Func<CancellationToken, Task<string>>? Restore { get; init; }

    /// <summary>
    /// Set for the coder (epoch 3 E3 s8 t1): called with the id of the vendor the job is LEAVING, when the next attempt is on another vendor and before anything of that attempt is made — deletes what the left vendor kept in the job's home (its sign-in copy, SPEC E7), so the unit of the next vendor
    /// cannot see it. Returns empty when nothing is left; any other text ends the job as <c>coder-boundary</c>: no vendor is started beside a copy of another's sign-in. Null for the read-only roles.
    /// </summary>
    public Func<string, string>? VendorLeft { get; init; }

    /// <summary>
    /// The session the answer came from, once an attempt ended with one: the model and the handle of its FIRST turn. A coder's fix turns (<see cref="CoderFixLoop"/>) resume exactly this session — never a new one, and never the handle a later turn
    /// reported. Null until an attempt answered.
    /// </summary>
    public RoleSession? Session { get; private set; }

    /// <summary>The vendors a coder can be launched on, and the check of each one's own stream (epoch 3 E3 s7 t2). The product's set unless a test injects another; read only when <see cref="Coder"/> is set.</summary>
    internal CoderVendorSet Vendors { get; init; } = CoderVendors.Shipped;

    /// <summary>The repair turns the LAST attempt used — what the role reports of how it got its answer.</summary>
    public int RepairTurnsUsed { get; private set; }

    /// <summary>The most one attempt may take; a test shortens it.</summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>The waits of the last model's ladder.</summary>
    public IReadOnlyList<TimeSpan> Ladder { get; init; } = RetryLadder.Default;

    /// <summary>The jitter's roll, 0 to 1 — a test fixes it.</summary>
    public Func<double> Roll { get; init; } = Random.Shared.NextDouble;

    /// <summary>The most key switches one job makes: after a limit on a key, the SAME model runs again on the next key of the pool, in a new session — this many times at most (KEY_POOL_SPEC.md section 5).</summary>
    public const int MaxKeySwitches = 3;

    private readonly HashSet<string> _skipped = new(StringComparer.Ordinal);
    private readonly List<string> _history = [];
    private RoleKeyRoad? _keys;
    private int _attempts;
    private int _keySwitches;
    private string _lastKeyId = string.Empty;
    private string _poolSentence = string.Empty;
    private bool _poolExhausted;
    private bool _warnedNoSnapshot;
    private string _lastVendor = string.Empty;

    /// <summary>The road to the claude key pool of this job's data folder; made once, on the first claude launch.</summary>
    private RoleKeyRoad Keys => _keys ??= RoleKeyRoad.For(run);

    public Task<RoleWorkOutcome> RunAsync(string prompt, CancellationToken ct) => RunAsync(_ => prompt, ct);

    /// <summary>
    /// The attempt loop with a prompt for EACH model (epoch 3 E3 s7 t2): a coder's turn-0 prompt is the vendor's own (SPEC C7), so a failover to another vendor sends that vendor's, and a new session after a failover carries the instruction block again (C9).
    /// </summary>
    public async Task<RoleWorkOutcome> RunAsync(Func<ModelRef, string> promptFor, CancellationToken ct)
    {
        var route = RouteOf(run.Job.Route);
        if (route.Count == 0 || route.Count != run.Job.Route.Count || route.Any(model => !RoleModelLauncher.Knows(model.Vendor)))
        {
            return new RoleWorkOutcome.Failed("route-invalid", $"the job's route is empty, has an entry that is not vendor/model, or names a vendor that is none of claude, codex, agy: [{string.Join(", ", run.Job.Route)}]");
        }

        // A route is read from job.json, which a person or an older build may have written: no name that a CLI is not known to accept reaches a launch, because claude bills the default model for it.
        if (route.Select(model => RoleModelNames.Refusal(model.Model, model.Vendor, RoleRouter.ModelsSetting)).FirstOrDefault(refusal => refusal.Length > 0) is { Length: > 0 } unknown)
        {
            return new RoleWorkOutcome.Failed("route-invalid", $"the job's route has a model name no CLI is known to accept, so nothing was launched — {unknown}");
        }

        var started = run.Clock.GetTimestamp();
        for (var index = 0; index < route.Count; index++)
        {
            if (_skipped.Contains(route[index].Vendor))
            {
                _history.Add($"{RoleRoute.Format(route[index])}: skipped, the {route[index].Vendor} vendor stopped the same way for its first model");

                continue;
            }

            var step = await ModelAsync(route, index, promptFor, started, ct);
            if (step is Step.Finished finished)
            {
                return finished.Outcome;
            }
        }

        // A route of claude models only, and a pool with no key to give: the job fails as a limit does, with the earliest free time — no wait (KEY_POOL_SPEC.md section 5).
        if (_poolExhausted && route.All(model => model.Vendor == "claude"))
        {
            return new RoleWorkOutcome.Failed(RoleStopWords.Limit, _poolSentence);
        }

        return new RoleWorkOutcome.Failed("models-exhausted", $"every model of the route stopped: {string.Join("; ", _history)}");
    }

    /// <summary>One model, with the last model's ladder: finished (success, a stop that ends the job, the exhausted list) or move on to the next model.</summary>
    private async Task<Step> ModelAsync(IReadOnlyList<ModelRef> route, int index, Func<ModelRef, string> promptFor, long started, CancellationToken ct)
    {
        var model = route[index];
        for (var wait = 0; ; wait++)
        {
            var end = await AttemptAsync(model, promptFor(model), started, ct);
            if (end is Ended.Stopped)
            {
                return new Step.Finished(new RoleWorkOutcome.Stopped());
            }

            if (end is Ended.Answer answer)
            {
                return new Step.Finished(new RoleWorkOutcome.Succeeded(answer.ResultJson));
            }

            if (end is Ended.Final final)
            {
                return new Step.Finished(final.Outcome);
            }

            if (end is Ended.NoKey noKey)
            {
                return NoKey(route, index, model, noKey);
            }

            var stop = ((Ended.Stop)end).Why;
            _history.Add($"{RoleRoute.Format(model)}: {stop.Word} — {stop.Reason}");
            if (!stop.FailsOver)
            {
                return new Step.Finished(new RoleWorkOutcome.Failed(stop.Word, stop.Reason));
            }

            if (IsKeySwitch(model, stop))
            {
                // A limit on a pool key is the KEY's, not the vendor's: the same model runs again on the next key, in a new session (a read-only role from turn 0, the coder from the last coai commit).
                _keySwitches++;
                run.Log.Information(
                    "role job {JobId:l} ({Role:l}) switched key {KeySwitch} of {MostKeySwitches} after a limit on {Key:l}: {Model:l} runs again on the next key",
                    run.Request.JobId, run.Request.Role, _keySwitches, MaxKeySwitches, _lastKeyId, RoleRoute.Format(model));

                continue;
            }

            if (stop.SkipsVendor)
            {
                _skipped.Add(model.Vendor);
            }

            if (route.Skip(index + 1).FirstOrDefault(later => !_skipped.Contains(later.Vendor)) is { } next)
            {
                Switched(model, next, stop);

                return new Step.MoveOn();
            }

            if (!stop.Transient || LadderWait(wait, started) is not { } pause)
            {
                return new Step.MoveOn();
            }

            await WaitAsync(pause, ct);
        }
    }

    /// <summary>
    /// A model the pool has no key for: its vendor is skipped for the rest of the job (the next vendor of the route runs, with the usual switch line and notice), the sentence is remembered — and is the job's end when every model of the route is claude and
    /// keys exist but all cool down or are dead (<see cref="_poolExhausted"/>). Nothing waits: a reset is hours away and a job's budget is 30 minutes.
    /// </summary>
    private Step NoKey(IReadOnlyList<ModelRef> route, int index, ModelRef model, Ended.NoKey noKey)
    {
        _skipped.Add(model.Vendor);
        _poolSentence = noKey.Sentence;
        _poolExhausted |= noKey.Exhausted;
        _history.Add($"{RoleRoute.Format(model)}: skipped, {noKey.Sentence}");
        if (route.Skip(index + 1).FirstOrDefault(later => !_skipped.Contains(later.Vendor)) is { } next)
        {
            Switched(model, next, new RoleStop(RoleStopWords.Limit, noKey.Sentence, "no-key", SkipsVendor: true, Transient: false));
        }

        return new Step.MoveOn();
    }

    /// <summary>Whether a stop of <paramref name="model"/> is a limit on a POOL key with a switch left (<see cref="MaxKeySwitches"/>): then the same model runs again on the next key instead of the vendor being given up.</summary>
    private bool IsKeySwitch(ModelRef model, RoleStop stop) =>
        model.Vendor == "claude" && stop is { Word: RoleStopWords.Limit, Transient: false } && Server.Keys.KeyIds.IsValid(_lastKeyId) && _keySwitches < MaxKeySwitches;

    private TimeSpan? LadderWait(int step, long started)
    {
        var elapsed = run.Clock.GetElapsedTime(started);
        var budget = Budget();

        return RetryLadder.NextWait(step, Ladder, Roll(), elapsed, budget);
    }

    private async Task WaitAsync(TimeSpan pause, CancellationToken ct)
    {
        run.Phase("waiting", _attempts);
        try
        {
            await Task.Delay(pause, run.Clock, ct);
        }
        catch (OperationCanceledException)
        {
            // The job was stopped while it waited: the next attempt sees the token and ends the job as stopped.
        }
    }

    private TimeSpan Budget() => TimeSpan.FromMinutes(Math.Max(1, run.Request.TimeoutMinutes));

    // ---------- one attempt ----------

    private TimeSpan Left(long started) => Budget() - run.Clock.GetElapsedTime(started);

    /// <summary>
    /// One model, from turn 0: the launch, and — when the role's judge asks for a repair (<see cref="RoleAnswerVerdict.Repair"/>) — up to <see cref="MaxRepairTurns"/> more turns in the SAME session of the SAME model. The attempt is
    /// ONE entry of <c>job.json</c> however many turns it took; every launch is one line of the ledger; every turn's answer is kept under <c>attempts/&lt;n&gt;/turn-&lt;k&gt;.txt</c>.
    /// </summary>
    private async Task<Ended> AttemptAsync(ModelRef model, string prompt, long started, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || Left(started) <= TimeSpan.Zero)
        {
            return new Ended.Stopped();
        }

        // A FRESH session of a claude model takes its key from the pool now, before anything is made for the attempt: a pool with no usable key skips the model at once (no wait, no attempt, no restore).
        var key = Keys.Fresh(model);
        if (key is RoleKey.Skip skip)
        {
            return new Ended.NoKey(skip.Sentence, skip.Exhausted);
        }

        _lastKeyId = key.KeyId;

        // Another vendor is next: what the vendor left kept in the job's home goes first. A copy that stays ends the job (a boundary stop: the next vendor would meet the same copy).
        if (_attempts > 0 && _lastVendor.Length > 0 && _lastVendor != model.Vendor && VendorLeft?.Invoke(_lastVendor) is { Length: > 0 } notLeft)
        {
            return new Ended.Final(new RoleWorkOutcome.Failed(RoleStopWords.CoderBoundary, notLeft));
        }

        if (_attempts > 0 && Restore is not null && await Restore(ct) is { Length: > 0 } notRestored)
        {
            return ct.IsCancellationRequested ? new Ended.Stopped() : new Ended.Final(new RoleWorkOutcome.Failed("coder-worktree", notRestored));
        }

        var n = ++_attempts;
        _lastVendor = model.Vendor;
        run.Phase("attempt", n);
        if (!run.Trying(RoleRoute.Format(model)))
        {
            return new Ended.Stopped();
        }

        RepairTurnsUsed = 0;
        var attempt = new AttemptRun(n, Path.Combine(run.Folder, "attempts", n.ToString(System.Globalization.CultureInfo.InvariantCulture)), run.Clock.GetUtcNow().UtcDateTime, await SnapshotAsync(ct)) { KeyId = key.KeyId, Secret = (key as RoleKey.Pool)?.Secret };
        var sent = prompt;
        for (var turn = 0; ; turn++)
        {
            var left = Left(started);
            if (turn > 0 && (ct.IsCancellationRequested || left <= TimeSpan.Zero))
            {
                return Interrupted(model, attempt, null);
            }

            var launched = await LaunchTurnAsync(model, sent, attempt, turn == 0 ? key : Keys.Resume(model, attempt.KeyId), left, ct);
            if (turn == 0)
            {
                attempt.FirstHandle = launched.Handle;
            }

            if (ct.IsCancellationRequested)
            {
                return Interrupted(model, attempt, launched);
            }

            var read = await ReadAsync(model, launched, attempt.Before, turn == 0 ? string.Empty : attempt.Handle, ct);
            KeepTurn(attempt, turn, launched);
            var blocked = read.Repair is { } wanted ? CannotRepair(wanted, turn, launched.Handle) : null;
            if (read.Repair is null || blocked is not null)
            {
                if (read.Stop is null && blocked is null && Follow is not null)
                {
                    // Turn 0 itself was fine: its ledger line says so, and the rest of the attempt — the commit, the checks, the fix turns — is the role's, inside this attempt.
                    LedgerTurn(model, attempt, launched, null);

                    return await FollowedAsync(model, attempt, ct);
                }

                return Finished(model, attempt, launched, read.Stop ?? blocked, read.Result);
            }

            LedgerTurn(model, attempt, launched, new RoleStop("repair", "the answer was sent back with its errors", "repair", false, false));
            attempt.Handle = launched.Handle;
            sent = read.Repair.Prompt;
            RepairTurnsUsed = turn + 1;
            run.Repaired(turn + 1);
        }
    }

    /// <summary>One launch of the attempt: the first turn is a fresh session, a later one resumes the handle of the turn before.</summary>
    private async Task<RoleLaunched> LaunchTurnAsync(ModelRef model, string prompt, AttemptRun attempt, RoleKey key, TimeSpan left, CancellationToken ct)
    {
        attempt.TurnBegun = run.Clock.GetUtcNow().UtcDateTime;
        var launched = await new RoleModelLauncher(run.Settings, run.Children) { Coder = Coder, Layer = OsLayer, Vendors = Vendors }
            .LaunchAsync(model, prompt, run.Request.RepoPath, attempt.Folder, left < AttemptTimeout ? left : AttemptTimeout, ct, attempt.Handle, key);
        attempt.TurnEnded = run.Clock.GetUtcNow().UtcDateTime;
        attempt.Add(launched);

        return launched;
    }

    /// <summary>The job was stopped (or ran out of time) while the attempt was between or inside a launch: the attempt is closed as <c>stopped</c> and the job ends.</summary>
    private Ended Interrupted(ModelRef model, AttemptRun attempt, RoleLaunched? launched)
    {
        var stop = new RoleStop("stopped", "the job was stopped while this model ran", "stopped", false, false);
        if (launched is not null)
        {
            LedgerTurn(model, attempt, launched, stop);
        }

        Record(model, attempt, stop, string.Empty);

        return new Ended.Stopped();
    }

    /// <summary>
    /// The role's part of an attempt after the first valid answer (<see cref="Follow"/>): the session of this model's turn 0 is handed over, and the attempt ends as the role says — with the job's outcome, or with a stop of a later turn that fails over. A job stopped
    /// while the role worked closes the attempt as <c>stopped</c>.
    /// </summary>
    private async Task<Ended> FollowedAsync(ModelRef model, AttemptRun attempt, CancellationToken ct)
    {
        var session = new RoleSession(model, attempt.FirstHandle, attempt.KeyId, attempt.Secret, attempt.CliVersion);
        Session = session;
        RoleFollowed followed;
        try
        {
            followed = await Follow!(session, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Interrupted(model, attempt, null);
        }

        attempt.TurnEnded = run.Clock.GetUtcNow().UtcDateTime;
        switch (followed)
        {
            case RoleFollowed.Ended { Outcome: RoleWorkOutcome.Stopped }:
                return Interrupted(model, attempt, null);
            case RoleFollowed.Ended ended:
                Record(model, attempt, null, string.Empty);

                return new Ended.Final(ended.Outcome);
            case RoleFollowed.FailsOver failsOver:
                Record(model, attempt, failsOver.Why, KeepEvidence(attempt.Folder, attempt.N, Redaction.SafeSource(failsOver.Why.Reason)));

                return new Ended.Stop(failsOver.Why);
            default:
                Record(model, attempt, null, string.Empty);

                return new Ended.Final(new RoleWorkOutcome.Failed(RoleStopWords.OutputInvalid, "the role gave no outcome for the attempt"));
        }
    }

    private Ended Finished(ModelRef model, AttemptRun attempt, RoleLaunched launched, RoleStop? stop, string result)
    {
        var evidence = stop is null ? string.Empty : Evidence(attempt.Folder, attempt.N, launched, stop);
        LedgerTurn(model, attempt, launched, stop);
        Record(model, attempt, stop, evidence);

        if (stop is null)
        {
            Session = new RoleSession(model, attempt.FirstHandle, attempt.KeyId, attempt.Secret, attempt.CliVersion);
        }

        return stop is null ? new Ended.Answer(result) : new Ended.Stop(stop);
    }

    /// <summary>Why a repair cannot be made, or null when it can: the turns are used up, or the vendor named no session to continue.</summary>
    private RoleStop? CannotRepair(RoleAnswerVerdict.Repair wanted, int turn, string handle)
    {
        if (turn >= MaxRepairTurns)
        {
            return new RoleStop(RoleStopWords.OutputInvalid, Redaction.SafeText($"{wanted.Reason} (still wrong after {turn} repair turn{(turn == 1 ? string.Empty : "s")})", Redaction.TitleLimit), "output-invalid", false, false);
        }

        return ConsultantHandle.IsWellFormed(handle)
            ? null
            : new RoleStop(RoleStopWords.OutputInvalid, Redaction.SafeText($"{wanted.Reason} (the vendor named no session, so the answer cannot be repaired in the same conversation)", Redaction.TitleLimit), "output-invalid", false, false);
    }

    /// <summary>Keeps the text of one turn's answer, when the role repairs at all — the evidence of an attempt that took more than one.</summary>
    private void KeepTurn(AttemptRun attempt, int turn, RoleLaunched launched)
    {
        if (MaxRepairTurns > 0 && launched.Launch?.Answer is { Length: > 0 } text)
        {
            EvidenceFile.Keep(attempt.Folder, $"turn-{turn}.txt", Redaction.SafeSource(text), e => run.Log.Warning(e, "role job {JobId}: the answer of turn {Turn} of attempt {N} could not be kept", run.Request.JobId, turn, attempt.N));
        }
    }

    /// <summary>What reading one finished launch gave: a stop; or the role's result; or the role's wish for a repair turn.</summary>
    private sealed record Read(RoleStop? Stop, string Result, RoleAnswerVerdict.Repair? Repair = null);

    /// <summary>The stop of a finished launch, or null with the role's result: the tree first (a changed tree ends the job whatever the model said), then the launch, then the answer.</summary>
    private async Task<Read> ReadAsync(ModelRef model, RoleLaunched launched, FilesystemSnapshot? before, string expectedSession, CancellationToken ct)
    {
        if (await ChangedAsync(before, ct) is { Length: > 0 } sentence)
        {
            return new Read(new RoleStop(RoleStopWords.TreeChanged, Redaction.SafeText(sentence, Redaction.TitleLimit), ConsultFailureKinds.TreeChanged, false, false), string.Empty);
        }

        if (StopOfLaunch(model, launched) is { } stopped)
        {
            return new Read(stopped, string.Empty);
        }

        // The check of the turn's own stream, before the role reads the answer and so before anything of the turn is committed (a coder only; the vendor's seam says what is wrong).
        if (Coder is not null && CoderTurnCheck.Stop(Vendors, model, launched, expectedSession) is { } wrong)
        {
            return new Read(wrong, string.Empty);
        }

        var launch = launched.Launch!;

        return judge(launched.Runtime.ReadAdvice(launch.Answer!)) switch
        {
            RoleAnswerVerdict.Valid valid => new Read(null, valid.ResultJson),
            RoleAnswerVerdict.Repair repair => new Read(null, string.Empty, repair),
            RoleAnswerVerdict.Refused refusal => new Read(new RoleStop(RoleStopWords.Refusal, Redaction.SafeText(refusal.Reason, Redaction.TitleLimit), "refused", false, false), string.Empty),
            RoleAnswerVerdict.Invalid invalid => new Read(new RoleStop(RoleStopWords.OutputInvalid, Redaction.SafeText(invalid.Reason, Redaction.TitleLimit), "output-invalid", false, false), string.Empty),
            _ => new Read(new RoleStop(RoleStopWords.OutputInvalid, "the role gave no verdict on the answer", "output-invalid", false, false), string.Empty),
        };
    }

    /// <summary>
    /// What a finished launch came to when it gave no answer the role can judge, or null when it did: the layer's own word first (a coder that was stopped from writing did not do its work, whatever its report claims, and the next model would meet the same
    /// layer), then a refused preparation, then a launch that ended with a terminal state or said nothing. One reading for every launch of a job — a coder's fix turns read theirs here too.
    /// </summary>
    internal static RoleStop? StopOfLaunch(ModelRef model, RoleLaunched launched)
    {
        // The text of the launch's own key was in what it printed: the key is dead and the job ends — another model would be handed a tree and a transcript that may hold it.
        if (launched.KeyExposed || launched.ExposedIds.Count > 0)
        {
            return new RoleStop(RoleStopWords.KeyInOutput, "the exact text of the launch's claude key was found in what the launch printed, so the key is dead (exposed) and the job stops; revoke the key at the vendor", "key-in-output", false, false);
        }

        // The key of a resumed session is gone: the turn stops as a limit does, and the loop goes on with the next key.
        if (launched.KeyStop is { } keyStop)
        {
            return keyStop;
        }

        if (launched.OsStop is { } osStop)
        {
            return new RoleStop(RoleStopWords.CoderBoundary, Redaction.SafeText(osStop.Sentence, Redaction.TitleLimit), osStop.Kind, false, false);
        }

        if (launched.Refused is { } refused)
        {
            return RoleStops.Of(refused, model.Vendor);
        }

        var launch = launched.Launch!;

        return launch.Terminal is not null || string.IsNullOrWhiteSpace(launch.Answer)
            ? RoleStops.Of(launched.Runtime, launch, model.Vendor, RoleModelLauncher.RuntimeOf(model.Vendor).Name, new ConsultFailure.Timeout())
            : null;
    }

    // ---------- the tree ----------

    private FilesystemInvariant Invariant() => new(new ProcessLauncher());

    /// <summary>The tree before the attempt, or null where it cannot be taken (not a git work tree) — said once in the log.</summary>
    private async Task<FilesystemSnapshot?> SnapshotAsync(CancellationToken ct)
    {
        if (Coder is not null)
        {
            // A coder's main checkout is held read-only by the OS layer and observed by it, turn by turn, as evidence (CoderTurns); this loop's invariant is the read-only roles'.
            return null;
        }

        try
        {
            return await Invariant().SnapshotAsync(run.Request.RepoPath, ct);
        }
        catch (Exception e) when (e is ContextException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            if (!_warnedNoSnapshot && e is not OperationCanceledException)
            {
                _warnedNoSnapshot = true;
                run.Log.Warning("role job {JobId} ({Role}): the repository's tree could not be snapshotted ({Why}); its attempts run without the tree check", run.Request.JobId, run.Request.Role, e.GetType().Name);
            }

            return null;
        }
    }

    /// <summary>The sentence for a tree that changed since <paramref name="before"/>, else empty. A snapshot that fails now is no change.</summary>
    private async Task<string> ChangedAsync(FilesystemSnapshot? before, CancellationToken ct)
    {
        if (before is null)
        {
            return string.Empty;
        }

        try
        {
            var changes = FilesystemSnapshot.Compare(before, await Invariant().SnapshotAsync(run.Request.RepoPath, ct));

            return changes.Count > 0 ? FilesystemSnapshot.Sentence(changes) : string.Empty;
        }
        catch (Exception e) when (e is ContextException or OperationCanceledException)
        {
            return string.Empty;
        }
    }

    // ---------- the record ----------

    private string Evidence(string folder, int n, RoleLaunched launched, RoleStop stop)
    {
        var text = launched.OsStop is not null ? stop.Reason
            : launched.Launch is { } launch
                ? string.IsNullOrWhiteSpace(launch.Answer) ? ConsultFailures.EvidenceOf(launch) : launch.Answer!
                : stop.Reason;

        return KeepEvidence(folder, n, Redaction.SafeSource(text));
    }

    private string KeepEvidence(string folder, int n, string text)
    {
        var file = EvidenceFile.Keep(folder, "evidence.txt", text, e => run.Log.Warning(e, "role job {JobId}: the evidence of attempt {N} could not be kept", run.Request.JobId, n));

        return file.Length == 0 ? string.Empty : $"attempts/{n}/evidence.txt";
    }

    private void Record(ModelRef model, AttemptRun attempt, RoleStop? stop, string evidence) =>
        run.Attempted(new RoleAttempt(attempt.N, model.Model, model.Vendor, stop?.Word ?? string.Empty, stop?.Reason ?? string.Empty, evidence)
        {
            StartedUtc = attempt.Begun,
            EndedUtc = attempt.TurnEnded,
            TokensIn = attempt.Total.TokensIn,
            TokensOut = attempt.Total.TokensOut,
            KeyId = attempt.KeyId,
            CliVersion = attempt.CliVersion,
            LastMeasuredVersion = Coder is null ? string.Empty : Vendors.LastMeasured(model.Vendor),
        });

    /// <summary>One ledger line for the launch of one turn — never for a preparation that refused before anything started. A cumulative vendor's report is cut to this turn's share. Never throws.</summary>
    private void LedgerTurn(ModelRef model, AttemptRun attempt, RoleLaunched launched, RoleStop? stop)
    {
        if (launched.Launch is not { } launch)
        {
            return;
        }

        var share = launched.Runtime.UsageIsCumulative ? ConsultationUsage.Less(launch.Usage, attempt.CounterBefore) : launch.Usage;
        RecordLedger(run, model, launched, stop, attempt.TurnEnded - attempt.TurnBegun, share);
    }

    /// <summary>
    /// One line of the usage ledger for one launch of a role job — the one writer, so a coder's fix turn is a line exactly as turn 0 is. Nothing for a preparation that refused before anything started. <paramref name="share"/> is what THIS launch
    /// consumed (a cumulative vendor's report is cut by the caller). Never throws.
    /// </summary>
    internal static void RecordLedger(IRoleRun run, ModelRef model, RoleLaunched launched, RoleStop? stop, TimeSpan duration, Usage share)
    {
        if (launched.Launch is not { } launch)
        {
            return;
        }

        var ranWith = (launched.Invocation?.RanWith ?? LaunchOptions.None).VersionedBy(launch.CliVersion);
        new UsageLedger(run.DataDir).RecordJob(
            string.Empty, model.Vendor, model.Model, run.Request.Role, stop is null ? "ok" : stop.Kind.Length > 0 ? stop.Kind : stop.Word,
            duration, share, UsageKinds.Role, "RoleJob", ranWith, resetsUtc: UsageLedger.StatedResetOf(launch.Terminal, DateTimeOffset.UtcNow), keyId: launched.KeyId, job: run.Request.JobId);

        // The launch that carried a pool key has ONE ledger line, and the same place tells the pool how it ended: a limit puts the key in cooldown, a refused key is dead, a good launch clears the cooldown.
        if (launched.KeyId.Length > 0)
        {
            RoleKeyRoad.For(run).Ended(launched, stop);
        }
    }

    /// <summary>The mutable state of ONE attempt across its turns: where its evidence goes, the session to resume, and what its launches consumed.</summary>
    private sealed class AttemptRun(int n, string folder, DateTime begun, FilesystemSnapshot? before)
    {
        public int N => n;

        public string Folder => folder;

        public DateTime Begun => begun;

        public FilesystemSnapshot? Before => before;

        public string Handle { get; set; } = string.Empty;

        /// <summary>The key this attempt's session runs on (<see cref="RoleKey.KeyId"/>): picked once for turn 0, kept for every later turn of the session.</summary>
        public string KeyId { get; init; } = string.Empty;

        /// <summary>The secret of the key, kept in memory for the life of the attempt so the coder's commit search never depends on a pool that may be busy, or on a key that was removed while its session ran (second judgement).</summary>
        public Server.Keys.ClaudeKeySecret? Secret { get; init; }

        /// <summary>The handle turn 0 reported: what a coder's fix turns resume, whatever a later turn answered with.</summary>
        public string FirstHandle { get; set; } = string.Empty;

        public DateTime TurnBegun { get; set; }

        public DateTime TurnEnded { get; set; }

        /// <summary>What the attempt's launches consumed together — a cumulative vendor's counter is not summed (<see cref="ConsultationUsage.OfTwoLaunches"/>).</summary>
        public Usage Total { get; private set; } = Usage.None;

        /// <summary>The cumulative vendor's counter as the last launch reported it: what the next report is measured against.</summary>
        public ConsultationBilled CounterBefore { get; private set; } = new();

        /// <summary>The CLI release the last launch of the attempt reported (empty until one did) — a record, never a condition.</summary>
        public string CliVersion { get; private set; } = string.Empty;

        public void Add(RoleLaunched launched)
        {
            if ((launched.CliVersion.Length > 0 ? launched.CliVersion : launched.Launch?.CliVersion) is { Length: > 0 } installed)
            {
                CliVersion = installed;
            }

            var reported = launched.Launch?.Usage ?? Usage.None;
            Total = ConsultationUsage.OfTwoLaunches(launched.Runtime.UsageIsCumulative, Total, reported);
            if (ConsultationUsage.CarriesACounter(reported) && launched.Runtime.UsageIsCumulative)
            {
                CounterBefore = ConsultationBilled.Of(reported);
            }
        }
    }

    /// <summary>One Information line and one notice for one switch — the line names the job, the role, both models, the stop and its reason; the notice names the vendor the work goes to.</summary>
    private void Switched(ModelRef from, ModelRef to, RoleStop stop)
    {
        run.Log.Information(
            "role job {JobId:l} ({Role:l}) switched {From:l} -> {To:l} after {Stop:l}: {Reason:l}",
            run.Request.JobId, run.Request.Role, RoleRoute.Format(from), RoleRoute.Format(to), stop.Word, stop.Reason);
        run.Notice(RoleJobNotices.Switched(run.Job, from, to, stop, _attempts, run.Clock.GetUtcNow().UtcDateTime));
    }

    private static IReadOnlyList<ModelRef> RouteOf(IReadOnlyList<string> route) =>
        [.. route.Select(RoleRoute.Parse).OfType<ModelRef>()];

    // ---------- the shapes of an attempt's end ----------

    private abstract record Ended
    {
        private Ended()
        {
        }

        public sealed record Answer(string ResultJson) : Ended;

        public sealed record Stop(RoleStop Why) : Ended;

        /// <summary>The attempt ended the JOB with this outcome (the role's own end of it, or a restore that failed) — nothing goes on to another model.</summary>
        public sealed record Final(RoleWorkOutcome Outcome) : Ended;

        public sealed record Stopped : Ended;

        /// <summary>The pool had no key to give the model — nothing was launched and no attempt was made: <see cref="Sentence"/> says why, <see cref="Exhausted"/> whether keys exist and all cool down.</summary>
        public sealed record NoKey(string Sentence, bool Exhausted) : Ended;
    }

    private abstract record Step
    {
        private Step()
        {
        }

        public sealed record Finished(RoleWorkOutcome Outcome) : Step;

        public sealed record MoveOn : Step;
    }
}
