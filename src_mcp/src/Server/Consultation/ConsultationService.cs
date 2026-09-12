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
public sealed class ConsultationService
{
    private readonly PanelSettings _settings;
    private readonly ReviewerExecutor _executor;
    private readonly ContextAssembler _context;
    private readonly FilesystemInvariant _invariant;
    private readonly RolePrompts _prompts;
    private readonly UsageLedger _ledger;
    private readonly ConsultationStore _store;
    private readonly ConsultCallCounter _counter;
    private readonly Serilog.ILogger _log;
    private readonly Func<string, string?> _env;

    public ConsultationService(
        PanelSettings settings,
        IProcessLauncher launcher,
        ReviewerExecutor executor,
        ContextAssembler context,
        RolePrompts prompts,
        UsageLedger ledger,
        Serilog.ILogger log,
        Func<string, string?> env)
    {
        _settings = settings;
        _executor = executor;
        _context = context;
        _invariant = new FilesystemInvariant(launcher);
        _prompts = prompts;
        _ledger = ledger;
        _log = log;
        _env = env;
        _store = new ConsultationStore(settings.DataDir);
        _counter = new ConsultCallCounter(settings.DataDir);
    }

    public const string PromptId = "consult";

    public ConsultationStore Store => _store;

    public int Sweep(Func<int, bool> isAlive) =>
        _store.Sweep(isAlive, DateTime.UtcNow, _settings.ConsultIdle, ConsultationStore.Retention);

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

        var (repo, refusal) = await _context.TopLevelAsync(repoPath, ct);
        return refusal.Length > 0
            ? Error(refusal)
            : await WithConsultantAsync(repo, problem.Trim(), files, consultationId.Trim(), ct);
    }

    private async Task<string> WithConsultantAsync(string repo, string problem, IReadOnlyList<string> files, string consultationId, CancellationToken ct)
    {
        var kind = CallerIdentity.KindFrom(_env);
        var caller = CallerIdentity.From(_env) is { Length: > 0 } id ? id : $"repo:{repo}";
        var choice = ConsultantRouting.For(_settings.Consultants, kind);
        var row = _settings.Providers.FirstOrDefault(p => string.Equals(p.Provider, choice.Vendor, StringComparison.OrdinalIgnoreCase));
        if (row is null || !row.Enabled)
        {
            return Error($"the consultant for a '{kind}' caller is the vendor '{choice.Vendor}', which is "
                         + (row is null ? "not configured" : "switched off")
                         + " — pick an enabled vendor row for this caller in the Consultant section of the ConnectOtherAIs panel (COAI_CONSULTANTS)");
        }

        var runtime = ConsultantResolution.For(row.Identity());
        return runtime is null
            ? Error(ConsultantResolution.CannotConsult(row.Identity()))
            : await WithRecordAsync(new Consultant(runtime, row, choice.Model.Length > 0 ? choice.Model : row.Model, kind, caller), repo, problem, files, consultationId, ct);
    }

    private async Task<string> WithRecordAsync(Consultant consultant, string repo, string problem, IReadOnlyList<string> files, string consultationId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var (record, refusal) = consultationId.Length == 0
            ? (await NewRecordAsync(consultant, repo, now, ct), null)
            : Existing(consultationId, consultant.Caller, now, problem);
        if (refusal is not null)
        {
            return Error(refusal);
        }

        var counted = _counter.TryTake(consultant.Caller, _settings.ConsultCallsPerSession, now);
        if (!counted.Allowed)
        {
            return Error($"this caller session has made {counted.Used} consult calls, the cap (COAI_CONSULT_CALLS_PER_SESSION = {_settings.ConsultCallsPerSession}) — "
                         + $"the window is {ConsultCallCounter.Window.TotalHours:0} hours from the first call; if you are still stuck, this is the moment to ask the person");
        }

        using var held = await RepositoryLock.TryTakeAsync(_settings.DataDir, repo, RepositoryLock.DefaultWait, ct);
        return held is null
            ? Error($"another consultation is running in {repo} right now (waited {RepositoryLock.DefaultWait.TotalSeconds:0} s) — try again in a moment")
            : await RunTurnAsync(consultant, record!, repo, problem, files, counted.Note, ct);
    }

    private (ConsultationRecord? Record, string? Refusal) Existing(string consultationId, string caller, DateTime now, string problem)
    {
        var record = _store.Read(consultationId);
        if (record is null)
        {
            return (null, $"no consultation {consultationId} is known to this server — the id is misspelt, it belongs to another machine's data directory, or its file expired; start a new consultation");
        }

        return ConsultationRules.Refusal(record, caller, now, _settings.ConsultIdle, problem) is { } refusal
            ? (null, refusal)
            : (record, null);
    }

    private async Task<ConsultationRecord> NewRecordAsync(Consultant consultant, string repo, DateTime now, CancellationToken ct)
    {
        var (sha, branch) = await _context.HeadAsync(repo, ct);

        return new ConsultationRecord(
            ConsultationStore.NewId(),
            consultant.Caller,
            consultant.Kind,
            "no-session",
            repo,
            branch,
            sha,
            consultant.Row.Provider,
            consultant.Model,
            RuntimeResolution.NameOf(consultant.Row.Identity()),
            consultant.Runtime.Memory is ConsultantMemory.VendorRemembers ? "vendorRemembers" : "weRemember",
            _settings.ConsultTurns,
            ConsultationStore.Stamp(now));
    }

    // ---------- the turn ----------

    private async Task<string> RunTurnAsync(Consultant consultant, ConsultationRecord record, string repo, string problem, IReadOnlyList<string> files, string counterNote, CancellationToken ct)
    {
        var nonce = Guid.NewGuid().ToString("N")[..8];
        var resuming = record.Turns.Count > 0 || record.Status == ConsultationStatuses.Interrupted;
        var before = await _invariant.SnapshotAsync(repo, ct);
        var prompt = ConsultantPrompt.Compose(new ConsultantPromptInput(
            _prompts.For(PromptId),
            record.Budget,
            nonce,
            problem,
            files,
            record.Branch,
            record.HeadSha,
            WorkingTree: resuming && consultant.Runtime.Memory is ConsultantMemory.VendorRemembers ? string.Empty : await ShapedTreeAsync(repo, ct),
            TreeUnchangedSinceTurnOne: resuming,
            PreviousAnswerLost: record.Status == ConsultationStatuses.Interrupted));

        var launch = consultant.Runtime.Build(new ConsultantLaunch(repo, prompt, record.Handle, AnswersDir, Settings(consultant)));
        var asking = record with { Status = ConsultationStatuses.Asking, RunnerPid = Environment.ProcessId, UpdatedUtc = ConsultationStore.Stamp(DateTime.UtcNow) };
        _store.Write(asking);
        _log.Information("consultation {Id}: turn {Turn}/{Cap} on {Vendor} in {Repo}", record.Id, record.Budget.Turn, record.MaxTurns, consultant.Row.Provider, repo);

        var started = Stopwatch.StartNew();
        ReviewerLaunch launched;
        try
        {
            launched = await _executor.LaunchAsync(launch, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _store.Write(Ended(asking, ConsultationStatuses.Failed, $"the launch threw: {e.Message}"));
            throw;
        }
        catch (OperationCanceledException)
        {
            _store.Write(Ended(asking, ConsultationStatuses.Failed, "the call was cancelled while the consultant was running"));
            throw;
        }

        var changes = FilesystemSnapshot.Compare(before, await _invariant.SnapshotAsync(repo, ct));
        return changes.Count > 0
            ? Breach(asking, changes, consultant, launched, started.Elapsed)
            : Settle(asking, consultant, launched, launch, problem, nonce, started.Elapsed, counterNote);
    }

    private string Breach(ConsultationRecord record, IReadOnlyList<TreeChange> changes, Consultant consultant, ReviewerLaunch launched, TimeSpan elapsed)
    {
        var sentence = FilesystemSnapshot.Sentence(changes);
        _log.Error("consultation {Id}: {Alert}", record.Id, sentence);
        _store.Write(Ended(record, ConsultationStatuses.Failed, "the consultant's process changed the working tree") with { Alert = sentence, Handle = HandleOf(consultant, launched, record.Handle) });
        Record(consultant, "tree-changed", elapsed, launched.Usage);

        return Error(sentence);
    }

    private string Settle(ConsultationRecord record, Consultant consultant, ReviewerLaunch launched, ReviewerInvocation launch, string problem, string nonce, TimeSpan elapsed, string counterNote)
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
            // setting's name — rather than leaving the caller to find it. (Its own scenario test
            // read the exhausted-branch wording and met this one, which is how the gap showed.)
            Reason = record.Budget.IsLast ? $"all {record.MaxTurns} of its turns are used (COAI_CONSULT_TURNS sets the cap)" : string.Empty,
            EndedUtc = record.Budget.IsLast ? turn.Utc : string.Empty,
            UpdatedUtc = turn.Utc,
        };
        _store.Write(answered);
        Record(consultant, "ok", elapsed, launched.Usage);
        _log.Information("consultation {Id}: answered turn {Turn} in {Seconds}s ({TokensIn}/{TokensOut} tokens)", record.Id, record.Budget.Turn, turn.Seconds, turn.TokensIn, turn.TokensOut);

        var advice = ConsultationFence.Advice(consultant.Row.Provider, consultant.Model, record.Budget, nonce, launched.Answer);
        return Json(new ConsultAnswer(record.Id, record.Budget.Turn, record.MaxTurns, counterNote.Length > 0 ? advice + "\n(" + counterNote + ")" : advice, launched.Usage.CostUsd), ServerJsonContext.Default.ConsultAnswer);
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

    private sealed record Consultant(IConsultantRuntime Runtime, ProviderSettings Row, string Model, string Kind, string Caller);

    private string AnswersDir => Path.Combine(_settings.DataDir, "consultations", "answers");

    private ReviewerSettings Settings(Consultant consultant) => new(consultant.Row.Provider)
    {
        ExecutablePath = consultant.Row.ExecutablePath,
        Model = consultant.Model,
        Timeout = _settings.ReviewerTimeout,
        DataDir = _settings.DataDir,
    };

    private async Task<string> ShapedTreeAsync(string repo, CancellationToken ct)
    {
        Directory.CreateDirectory(AnswersDir);
        var files = await _context.CollectWorkingTreeAsync(repo, ct: ct);
        return files.Count == 0
            ? "(the working tree has no uncommitted change — everything is committed at HEAD)"
            : DiffShaper.Shape(files, ConsultantPrompt.DiffBudget).Text;
    }

    private static string HandleOf(Consultant consultant, ReviewerLaunch launched, string known)
    {
        var read = launched.Process is { } process ? consultant.Runtime.ReadHandle(process) : string.Empty;
        return read.Length > 0 ? read : known;
    }

    private static ConsultationRecord Ended(ConsultationRecord record, string status, string reason)
    {
        var now = ConsultationStore.Stamp(DateTime.UtcNow);
        return record with { Status = status, Reason = reason, EndedUtc = now, UpdatedUtc = now };
    }

    private void Record(Consultant consultant, string outcome, TimeSpan elapsed, Usage usage) =>
        _ledger.RecordJob(string.Empty, consultant.Row.Provider, consultant.Model, ConsultantRoles.Consult, outcome, elapsed,
            usage.TokensIn, usage.TokensOut, usage.CostUsd, UsageKinds.Consult, stage: "Consultation");

    private string KeepEvidence(Consultant consultant, string evidence)
    {
        var dir = Path.Combine(_settings.DataDir, "unparseable");
        var path = Path.Combine(dir, $"consult-{Core.Rounds.FileName.Safe(consultant.Row.Provider)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, evidence);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Warning("evidence: could not keep the consultant's transcript at {Path}: {Reason}", path, e.Message);
        }

        return path;
    }

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
