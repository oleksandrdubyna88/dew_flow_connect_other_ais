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
    /// <summary>The most one attempt may take; a test shortens it.</summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>The waits of the last model's ladder.</summary>
    public IReadOnlyList<TimeSpan> Ladder { get; init; } = RetryLadder.Default;

    /// <summary>The jitter's roll, 0 to 1 — a test fixes it.</summary>
    public Func<double> Roll { get; init; } = Random.Shared.NextDouble;

    private readonly HashSet<string> _skipped = new(StringComparer.Ordinal);
    private readonly List<string> _history = [];
    private int _attempts;
    private bool _warnedNoSnapshot;

    public async Task<RoleWorkOutcome> RunAsync(string prompt, CancellationToken ct)
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

            var step = await ModelAsync(route, index, prompt, started, ct);
            if (step is Step.Finished finished)
            {
                return finished.Outcome;
            }
        }

        return new RoleWorkOutcome.Failed("models-exhausted", $"every model of the route stopped: {string.Join("; ", _history)}");
    }

    /// <summary>One model, with the last model's ladder: finished (success, a stop that ends the job, the exhausted list) or move on to the next model.</summary>
    private async Task<Step> ModelAsync(IReadOnlyList<ModelRef> route, int index, string prompt, long started, CancellationToken ct)
    {
        var model = route[index];
        for (var wait = 0; ; wait++)
        {
            var end = await AttemptAsync(model, prompt, started, ct);
            if (end is Ended.Stopped)
            {
                return new Step.Finished(new RoleWorkOutcome.Stopped());
            }

            if (end is Ended.Answer answer)
            {
                return new Step.Finished(new RoleWorkOutcome.Succeeded(answer.ResultJson));
            }

            var stop = ((Ended.Stop)end).Why;
            _history.Add($"{RoleRoute.Format(model)}: {stop.Word} — {stop.Reason}");
            if (!stop.FailsOver)
            {
                return new Step.Finished(new RoleWorkOutcome.Failed(stop.Word, stop.Reason));
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

    private async Task<Ended> AttemptAsync(ModelRef model, string prompt, long started, CancellationToken ct)
    {
        var left = Budget() - run.Clock.GetElapsedTime(started);
        if (ct.IsCancellationRequested || left <= TimeSpan.Zero)
        {
            return new Ended.Stopped();
        }

        var n = ++_attempts;
        run.Phase("attempt", n);
        if (!run.Trying(RoleRoute.Format(model)))
        {
            return new Ended.Stopped();
        }

        var folder = Path.Combine(run.Folder, "attempts", n.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var begun = run.Clock.GetUtcNow().UtcDateTime;
        var before = await SnapshotAsync(ct);
        var launcher = new RoleModelLauncher(run.Settings, run.Children);
        var launched = await launcher.LaunchAsync(model, prompt, run.Request.RepoPath, folder, left < AttemptTimeout ? left : AttemptTimeout, ct);
        var ended = run.Clock.GetUtcNow().UtcDateTime;
        if (ct.IsCancellationRequested)
        {
            Record(model, n, begun, ended, launched, new RoleStop("stopped", "the job was stopped while this model ran", "stopped", false, false), string.Empty);

            return new Ended.Stopped();
        }

        var (stop, result) = await ReadAsync(model, launched, before, ct);
        var evidence = stop is null ? string.Empty : Evidence(folder, n, launched, stop);
        Record(model, n, begun, ended, launched, stop, evidence);

        return stop is null ? new Ended.Answer(result) : new Ended.Stop(stop);
    }

    /// <summary>The stop of a finished launch, or null with the role's result: the tree first (a changed tree ends the job whatever the model said), then the launch, then the answer.</summary>
    private async Task<(RoleStop? Stop, string Result)> ReadAsync(ModelRef model, RoleLaunched launched, FilesystemSnapshot? before, CancellationToken ct)
    {
        if (await ChangedAsync(before, ct) is { Length: > 0 } sentence)
        {
            return (new RoleStop(RoleStopWords.TreeChanged, Redaction.SafeText(sentence, Redaction.TitleLimit), ConsultFailureKinds.TreeChanged, false, false), string.Empty);
        }

        var name = RoleModelLauncher.RuntimeOf(model.Vendor).Name;
        if (launched.Refused is { } refused)
        {
            return (RoleStops.Of(refused, model.Vendor), string.Empty);
        }

        var launch = launched.Launch!;
        if (launch.Terminal is not null || string.IsNullOrWhiteSpace(launch.Answer))
        {
            return (RoleStops.Of(launched.Runtime, launch, model.Vendor, name, new ConsultFailure.Timeout()), string.Empty);
        }

        return judge(launched.Runtime.ReadAdvice(launch.Answer)) switch
        {
            RoleAnswerVerdict.Valid valid => (null, valid.ResultJson),
            RoleAnswerVerdict.Refused refusal => (new RoleStop(RoleStopWords.Refusal, Redaction.SafeText(refusal.Reason, Redaction.TitleLimit), "refused", false, false), string.Empty),
            RoleAnswerVerdict.Invalid invalid => (new RoleStop(RoleStopWords.OutputInvalid, Redaction.SafeText(invalid.Reason, Redaction.TitleLimit), "output-invalid", false, false), string.Empty),
            _ => (new RoleStop(RoleStopWords.OutputInvalid, "the role gave no verdict on the answer", "output-invalid", false, false), string.Empty),
        };
    }

    // ---------- the tree ----------

    private FilesystemInvariant Invariant() => new(new ProcessLauncher());

    /// <summary>The tree before the attempt, or null where it cannot be taken (not a git work tree) — said once in the log.</summary>
    private async Task<FilesystemSnapshot?> SnapshotAsync(CancellationToken ct)
    {
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
        var text = launched.Launch is { } launch
            ? string.IsNullOrWhiteSpace(launch.Answer) ? ConsultFailures.EvidenceOf(launch) : launch.Answer!
            : stop.Reason;
        var file = EvidenceFile.Keep(folder, "evidence.txt", Redaction.SafeSource(text), e => run.Log.Warning(e, "role job {JobId}: the evidence of attempt {N} could not be kept", run.Request.JobId, n));

        return file.Length == 0 ? string.Empty : $"attempts/{n}/evidence.txt";
    }

    private void Record(ModelRef model, int n, DateTime begun, DateTime ended, RoleLaunched launched, RoleStop? stop, string evidence)
    {
        var usage = launched.Launch?.Usage ?? Usage.None;
        run.Attempted(new RoleAttempt(n, model.Model, model.Vendor, stop?.Word ?? string.Empty, stop?.Reason ?? string.Empty, evidence)
        {
            StartedUtc = begun,
            EndedUtc = ended,
            TokensIn = usage.TokensIn,
            TokensOut = usage.TokensOut,
        });
        if (launched.Launch is { } launch)
        {
            Ledger(model, begun, ended, launched, launch, stop);
        }
    }

    /// <summary>One ledger line for the launch — never for a preparation that refused before anything started. Never throws.</summary>
    private void Ledger(ModelRef model, DateTime begun, DateTime ended, RoleLaunched launched, ReviewerLaunch launch, RoleStop? stop)
    {
        var ranWith = (launched.Invocation?.RanWith ?? LaunchOptions.None).VersionedBy(launch.CliVersion);
        new UsageLedger(run.DataDir).RecordJob(
            string.Empty, model.Vendor, model.Model, run.Request.Role, stop is null ? "ok" : stop.Kind.Length > 0 ? stop.Kind : stop.Word,
            ended - begun, launch.Usage, UsageKinds.Role, "RoleJob", ranWith, resetsUtc: UsageLedger.StatedResetOf(launch.Terminal, DateTimeOffset.UtcNow));
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

        public sealed record Stopped : Ended;
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
