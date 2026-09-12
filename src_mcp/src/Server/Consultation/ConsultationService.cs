using System.Diagnostics;
using System.Text.Json;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// The `consult` tool's whole flow: who is calling, which consultant they get, the record, the caps,
/// the lock, the snapshot, the launch, the snapshot again, the fence — and a sentence for every way it
/// can refuse, decided BEFORE anything is launched wherever the fact is ours to know.
/// </summary>
/// <remarks>
/// A collaborator of <see cref="PanelService"/> rather than three hundred more lines in it: the file
/// was already past the family's ceiling, and a consultation shares none of the round machine.
/// </remarks>
public sealed class ConsultationService(
    PanelSettings settings,
    IProcessLauncher launcher,
    ReviewerExecutor executor,
    ContextAssembler context,
    RolePrompts prompts,
    UsageLedger ledger,
    Serilog.ILogger log,
    Func<string, string?> env)
{
    public const string PromptId = "consult";

    private readonly FilesystemInvariant _invariant = new(launcher);
    private readonly ConsultationStore _store = new(
        settings.DataDir,
        problem => log.Warning("consultations: {Problem}", problem));
    private readonly ConsultCallCounter _counter = new(settings.DataDir);

    public ConsultationStore Store => _store;

    public int Sweep(Func<int, bool> isAlive) =>
        _store.Sweep(isAlive, DateTime.UtcNow, settings.ConsultIdle, ConsultationStore.Retention);

    public async Task<string> AskAsync(string repoPath, string problem, string suspectedFilesJson, string consultationId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(problem))
        {
            return Error("a problem statement is required — say what is stuck and what already broke");
        }

        var files = SuspectedFiles(suspectedFilesJson);
        if (files is null)
        {
            return Error("suspectedFiles must be a JSON array of repository-relative paths, e.g. [\"src/A.cs\"] — or [] when you do not know");
        }

        var (repo, refusal) = await context.TopLevelAsync(repoPath, ct);
        if (refusal.Length > 0)
        {
            return Error(refusal);
        }

        try
        {
            return await WithConsultantAsync(repo, ConsultantPrompt.BoundedProblem(problem), ConsultantPrompt.BoundedFiles(files), consultationId.Trim(), ct);
        }
        catch (ContextException e)
        {
            // git refused something — a status, a diff, a rev-parse. Every failure of this tool is a
            // SENTENCE, never an exception up the stdio stack: `PanelService`'s rule, and the one path
            // where the snapshot's own throw would otherwise reach the client as a protocol error.
            // (local, code round.)
            log.Warning("consultation: git refused a command in {Repo}: {Reason}", repo, e.Message);

            return Error($"the working tree at {repo} could not be read: {e.Message} — no consultant was launched");
        }
    }

    // ---------- choosing the consultant ----------

    private async Task<string> WithConsultantAsync(string repo, string problem, IReadOnlyList<string> files, string consultationId, CancellationToken ct)
    {
        var kind = CallerIdentity.KindFrom(env);
        var caller = CallerIdentity.From(env).Id is { Length: > 0 } id ? id : $"repo:{repo}";
        var counted = _counter.TryTake(caller, settings.ConsultCallsPerSession, DateTime.UtcNow);
        if (!counted.Allowed)
        {
            return Error($"this caller session has made {counted.Used} consult calls, the cap (COAI_CONSULT_CALLS_PER_SESSION = {settings.ConsultCallsPerSession}) — "
                         + $"the window is {ConsultCallCounter.Window.TotalHours:0} hours from the first call; if you are still stuck, this is the moment to ask the person");
        }

        // THE LOCK IS TAKEN BEFORE THE RECORD IS READ, and that order is the fix for a race the code
        // round found: two follow-ups could both read an `open` record, the second wait for the lock,
        // and then write its turn over the first one's answer from a stale turn list. Nothing about a
        // consultation is decided outside the lock now. (codex, code round.)
        using var held = await RepositoryLock.TryTakeAsync(settings.DataDir, repo, RepositoryLock.DefaultWait, ct);
        if (held is null)
        {
            return Error($"another consultation is running in {repo} right now (waited {RepositoryLock.DefaultWait.TotalSeconds:0} s) — try again in a moment");
        }

        return await UnderTheLockAsync(new Caller(kind, caller, counted.Note), repo, problem, files, consultationId, ct);
    }

    private async Task<string> UnderTheLockAsync(Caller caller, string repo, string problem, IReadOnlyList<string> files, string consultationId, CancellationToken ct)
    {
        var (record, refusal) = consultationId.Length == 0
            ? (null, null)
            : Existing(consultationId, caller.Id, repo, problem);
        if (refusal is not null)
        {
            return Error(refusal);
        }

        // A RESUMED consultation runs on the vendor and model FROZEN on its record, never on what the
        // panel says now: the record claims they are frozen, and a settings change between turns would
        // otherwise hand a conversation opened on one model to another — or hand one vendor's handle
        // to a different vendor entirely. (codex, code round.)
        var choice = record is null
            ? ConsultantRouting.For(settings.Consultants, caller.Kind)
            : new ConsultantChoice(record.Vendor, record.Model);
        var row = settings.Providers.FirstOrDefault(p => string.Equals(p.Provider, choice.Vendor, StringComparison.OrdinalIgnoreCase));
        if (row is null || !row.Enabled)
        {
            return Error(record is null
                ? $"the consultant for a '{caller.Kind}' caller is the vendor '{choice.Vendor}', which is "
                  + (row is null ? "not configured" : "switched off")
                  + " — pick an enabled vendor row for this caller in the Consultant section of the ConnectOtherAIs panel (COAI_CONSULTANTS)"
                : $"consultation {record.Id} was opened on the vendor '{choice.Vendor}', which is no longer "
                  + (row is null ? "configured" : "enabled")
                  + " — a consultation stays on the vendor it started with, so this one cannot go on; start a new consultation");
        }

        var runtime = ConsultantResolution.For(row.Identity());
        if (runtime is null)
        {
            return Error(ConsultantResolution.CannotConsult(row.Identity()));
        }

        var model = choice.Model.Length > 0 ? choice.Model : row.Model;
        var consultant = new Consultant(runtime, row, model, caller);

        return await RunTurnAsync(consultant, record ?? await NewRecordAsync(consultant, repo, DateTime.UtcNow, ct), repo, problem, files, ct);
    }

    private (ConsultationRecord? Record, string? Refusal) Existing(string consultationId, string caller, string repo, string problem)
    {
        var record = _store.Read(consultationId);
        if (record is null)
        {
            return (null, $"no consultation {consultationId} is known to this server — the id is misspelt, it belongs to another machine's data directory, or its file expired; start a new consultation");
        }

        // The id names a conversation about ONE working tree. Resuming it against another repository
        // would launch that vendor thread in a checkout it has never seen while the record went on
        // describing the first one's branch and commit — advice about a mixture of two. Two reviewers,
        // independently, on the code round.
        if (!SamePath(record.RepoPath, repo))
        {
            return (null, $"consultation {consultationId} belongs to {record.RepoPath}, not to {repo} — a consultation is about ONE working tree; start a new consultation here");
        }

        return ConsultationRules.Refusal(record, caller, DateTime.UtcNow, settings.ConsultIdle, problem) is { } refusal
            ? (null, refusal)
            : (record, null);
    }

    private async Task<ConsultationRecord> NewRecordAsync(Consultant consultant, string repo, DateTime now, CancellationToken ct)
    {
        var (sha, branch) = await context.HeadAsync(repo, ct);

        return new ConsultationRecord(
            ConsultationStore.NewId(),
            consultant.Caller.Id,
            consultant.Caller.Kind,
            "no-session",
            repo,
            branch,
            sha,
            consultant.Row.Provider,
            consultant.Model,
            RuntimeResolution.NameOf(consultant.Row.Identity()),
            consultant.Runtime.Memory is ConsultantMemory.VendorRemembers ? "vendorRemembers" : "weRemember",
            settings.ConsultTurns,
            ConsultationStore.Stamp(now));
    }

    // ---------- the turn ----------

    private async Task<string> RunTurnAsync(Consultant consultant, ConsultationRecord record, string repo, string problem, IReadOnlyList<string> files, CancellationToken ct)
    {
        var nonce = Guid.NewGuid().ToString("N")[..8];
        var before = await _invariant.SnapshotAsync(repo, ct);
        var launch = consultant.Runtime.Build(new ConsultantLaunch(
            repo, await PromptAsync(consultant, record, problem, files, nonce, repo, ct), record.Handle, AnswersDir, Settings(consultant)));
        var asking = record with { Status = ConsultationStatuses.Asking, RunnerPid = Environment.ProcessId, UpdatedUtc = ConsultationStore.Stamp(DateTime.UtcNow) };
        _store.Write(asking);
        log.Information("consultation {Id}: turn {Turn}/{Cap} on {Vendor} in {Repo}", record.Id, record.Budget.Turn, record.MaxTurns, consultant.Row.Provider, repo);

        var started = Stopwatch.StartNew();
        ReviewerLaunch launched;
        try
        {
            launched = await executor.LaunchAsync(launch, ct);
        }
        catch (Exception e)
        {
            // A turn the vendor may already have ACCEPTED must not be closed as failed: with a handle
            // on the record it stays resumable and the turn stays uncounted, which is the whole point
            // of the interrupted state. Without one there is nothing to resume. The write is guarded
            // so a second failure here cannot mask the first. (codex + gemini, code round.)
            Quietly(() => _store.Write(asking.Handle.Length > 0
                ? asking with { Status = ConsultationStatuses.Interrupted, Reason = Interrupted(e), UpdatedUtc = ConsultationStore.Stamp(DateTime.UtcNow) }
                : Ended(asking, ConsultationStatuses.Failed, Interrupted(e))));

            throw;
        }

        var changes = FilesystemSnapshot.Compare(before, await _invariant.SnapshotAsync(repo, ct));
        return changes.Count > 0
            ? Breach(asking, changes, consultant, launched, started.Elapsed)
            : Settle(asking, consultant, launched, problem, nonce, started.Elapsed);
    }

    private async Task<string> PromptAsync(Consultant consultant, ConsultationRecord record, string problem, IReadOnlyList<string> files, string nonce, string repo, CancellationToken ct)
    {
        var resuming = record.Turns.Count > 0 || record.Status == ConsultationStatuses.Interrupted;
        var remembers = consultant.Runtime.Memory is ConsultantMemory.VendorRemembers;

        return ConsultantPrompt.Compose(new ConsultantPromptInput(
            prompts.For(PromptId),
            record.Budget,
            nonce,
            problem,
            files,
            record.Branch,
            record.HeadSha,
            // A vendor that keeps the conversation is not re-sent the tree; one that keeps none is.
            WorkingTree: resuming && remembers ? string.Empty : await ShapedTreeAsync(repo, ct),
            TreeUnchangedSinceTurnOne: resuming,
            // The arm that had a shape and no behaviour until the code round asked for it: without
            // this, the first forgetful vendor would answer every follow-up with no memory of the one
            // before, and nothing would say so.
            CarriedTranscript: resuming && !remembers
                ? ConsultantPrompt.Transcript([.. record.Turns.Select(t => (t.Problem, t.Advice))])
                : string.Empty,
            PreviousAnswerLost: record.Status == ConsultationStatuses.Interrupted));
    }

    private string Breach(ConsultationRecord record, IReadOnlyList<TreeChange> changes, Consultant consultant, ReviewerLaunch launched, TimeSpan elapsed)
    {
        var sentence = FilesystemSnapshot.Sentence(changes);
        log.Error("consultation {Id}: {Alert}", record.Id, sentence);
        _store.Write(Ended(record, ConsultationStatuses.Failed, "the consultant's process changed the working tree") with
        {
            Alert = sentence,
            Handle = HandleOf(consultant, launched, record.Handle),
        });
        Record(consultant, "tree-changed", elapsed, launched.Usage);

        return Error(sentence);
    }

    private string Settle(ConsultationRecord record, Consultant consultant, ReviewerLaunch launched, string problem, string nonce, TimeSpan elapsed)
    {
        var handle = HandleOf(consultant, launched, record.Handle);
        if (launched.Terminal is { } terminal)
        {
            return Terminal(record, consultant, launched, terminal, handle, elapsed);
        }

        if (string.IsNullOrWhiteSpace(launched.Answer))
        {
            var kept = KeepEvidence(consultant, launched.Evidence);
            _store.Write(Ended(record, ConsultationStatuses.Failed, "the consultant answered nothing") with { Handle = handle });
            Record(consultant, "empty", elapsed, launched.Usage);

            return Error($"the consultant ({consultant.Row.Provider}) exited cleanly but answered nothing; its transcript is kept at {kept} — try once more with a sharper problem statement");
        }

        var turn = new ConsultationTurn(ConsultationStore.Stamp(DateTime.UtcNow), problem, launched.Answer.Trim(), Math.Round(elapsed.TotalSeconds, 1), launched.Usage.TokensIn, launched.Usage.TokensOut, launched.Usage.CostUsd);
        var answered = record with
        {
            Turns = [.. record.Turns, turn],
            Handle = handle,
            Status = record.Budget.IsLast ? ConsultationStatuses.Closed : ConsultationStatuses.Open,
            // The sentence a LATER call is refused with is this one, so it carries the cure — the
            // setting's name — rather than leaving the caller to find it.
            Reason = record.Budget.IsLast ? $"all {record.MaxTurns} of its turns are used (COAI_CONSULT_TURNS sets the cap)" : string.Empty,
            EndedUtc = record.Budget.IsLast ? turn.Utc : string.Empty,
            UpdatedUtc = turn.Utc,
        };
        _store.Write(answered);
        Record(consultant, "ok", elapsed, launched.Usage);
        log.Information("consultation {Id}: answered turn {Turn} in {Seconds}s ({TokensIn}/{TokensOut} tokens)", record.Id, record.Budget.Turn, turn.Seconds, turn.TokensIn, turn.TokensOut);

        var advice = ConsultationFence.Advice(consultant.Row.Provider, consultant.Model, record.Budget, nonce, launched.Answer);
        var note = consultant.Caller.CounterNote;

        return Json(
            new ConsultAnswer(record.Id, record.Budget.Turn, record.MaxTurns, note.Length > 0 ? advice + "\n(" + note + ")" : advice, launched.Usage.CostUsd),
            ServerJsonContext.Default.ConsultAnswer);
    }

    private string Terminal(ConsultationRecord record, Consultant consultant, ReviewerLaunch launched, ReviewerOutcome terminal, string handle, TimeSpan elapsed)
    {
        var reason = ReviewerSummaryFactory.Describe(terminal);
        Record(consultant, reason, elapsed, launched.Usage);
        if (launched.Process is { } process && consultant.Runtime.DroppedTheConversation(process))
        {
            _store.Write(Ended(record, ConsultationStatuses.Failed, "the vendor no longer holds this conversation") with { Handle = string.Empty });
            return Error($"the consultant ({consultant.Row.Provider}) no longer holds conversation {record.Id} — its own store dropped the thread; start a new consultation");
        }

        if (handle.Length > 0)
        {
            _store.Write(record with { Status = ConsultationStatuses.Interrupted, Handle = handle, Reason = reason, UpdatedUtc = ConsultationStore.Stamp(DateTime.UtcNow) });
            return Error($"the consultant ({consultant.Row.Provider}) accepted the turn but the answer did not arrive ({reason}); the turn was NOT counted — call consult again with consultationId {record.Id} and the consultant will pick the conversation up");
        }

        _store.Write(Ended(record, ConsultationStatuses.Failed, reason));
        return Error($"the consultant ({consultant.Row.Provider}) did not answer: {reason} — check `providers`, then start a new consultation");
    }

    // ---------- plumbing ----------

    /// <param name="CounterNote">Empty ordinarily; a sentence when the call cap is being held in memory.</param>
    private sealed record Caller(string Kind, string Id, string CounterNote);

    private sealed record Consultant(IConsultantRuntime Runtime, ProviderSettings Row, string Model, Caller Caller);

    private string AnswersDir => Path.Combine(settings.DataDir, "consultations", "answers");

    private ReviewerSettings Settings(Consultant consultant) => new(consultant.Row.Provider)
    {
        ExecutablePath = consultant.Row.ExecutablePath,
        Model = consultant.Model,
        Timeout = settings.ReviewerTimeout,
        DataDir = settings.DataDir,
    };

    private async Task<string> ShapedTreeAsync(string repo, CancellationToken ct)
    {
        Directory.CreateDirectory(AnswersDir);
        var files = await context.CollectWorkingTreeAsync(repo, ct: ct);

        return files.Count == 0
            ? "(the working tree has no uncommitted change — everything is committed at HEAD)"
            : DiffShaper.Shape(files, ConsultantPrompt.DiffBudget).Text;
    }

    private static string HandleOf(Consultant consultant, ReviewerLaunch launched, string known)
    {
        var read = launched.Process is { } process ? consultant.Runtime.ReadHandle(process) : string.Empty;

        return read.Length > 0 ? read : known;
    }

    private static string Interrupted(Exception e) =>
        e is OperationCanceledException
            ? "the call was cancelled while the consultant was running"
            : $"the launch failed: {e.Message}";

    private static ConsultationRecord Ended(ConsultationRecord record, string status, string reason)
    {
        var now = ConsultationStore.Stamp(DateTime.UtcNow);

        return record with { Status = status, Reason = reason, EndedUtc = now, UpdatedUtc = now };
    }

    /// <summary>A best-effort write on a path that is already failing — it must not mask what failed.</summary>
    private void Quietly(Action write)
    {
        try
        {
            write();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Warning("consultations: the record could not be updated on the failure path: {Reason}", e.Message);
        }
    }

    private void Record(Consultant consultant, string outcome, TimeSpan elapsed, Usage usage) =>
        ledger.RecordJob(string.Empty, consultant.Row.Provider, consultant.Model, ConsultantRoles.Consult, outcome, elapsed,
            usage.TokensIn, usage.TokensOut, usage.CostUsd, UsageKinds.Consult, stage: "Consultation");

    private string KeepEvidence(Consultant consultant, string evidence)
    {
        var dir = Path.Combine(settings.DataDir, "unparseable");
        var path = Path.Combine(dir, $"consult-{Core.Rounds.FileName.Safe(consultant.Row.Provider)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, evidence);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Warning("evidence: could not keep the consultant's transcript at {Path}: {Reason}", path, e.Message);
        }

        return path;
    }

    private static bool SamePath(string one, string other) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(one)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(other)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static IReadOnlyList<string>? SuspectedFiles(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize(json, ServerJsonContext.Default.ListString) ?? [];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Json<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type) => JsonSerializer.Serialize(value, type);

    private static string Error(string sentence) => JsonSerializer.Serialize(new ErrorAnswer(sentence), ServerJsonContext.Default.ErrorAnswer);
}
