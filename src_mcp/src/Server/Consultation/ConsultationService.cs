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
        problem => log.Warning("consultations: {Problem}", problem),
        // Every state change reaches the log, and none of them can fail because of it: the record
        // file is the source of truth and `Projection` swallows a database that is locked or full.
        // A consultation must never refuse somebody who is stuck over a view of itself.
        record => Project(settings, log, record));
    private readonly ConsultCallCounter _counter = new(settings.DataDir);

    /// <summary>
    /// The answer schema, on disk once when this service is built rather than on every launch.
    /// </summary>
    /// <remarks>
    /// Its own directory, never <c>consultations/</c>: that one holds a file per consultation keyed
    /// by id, and story 2's live check found this schema being read back as a record with no id.
    /// </remarks>
    private readonly ConsultSchemaFile.Provisioned _answerSchema = Provision(settings, log);

    /// <summary>
    /// The projection, as a static so the field initialiser above can reach it.
    /// </summary>
    /// <remarks>
    /// A primary constructor's parameters are in scope for initialisers, but <c>this</c> is not — so
    /// the store, which is itself a field, cannot be handed a lambda that touches another field. It
    /// opens its own <see cref="Store.Projection"/> per write, which is what that type does anyway:
    /// the database is opened, written and closed per projection, never held.
    /// </remarks>
    private static void Project(PanelSettings settings, Serilog.ILogger log, ConsultationRecord record) =>
        new Store.Projection(settings.DataDir, log)
            .Write(db => db.RecordConsultation(ConsultationRows.From(record)), "the consultation");

    private static ConsultSchemaFile.Provisioned Provision(PanelSettings settings, Serilog.ILogger log)
    {
        var provisioned = ConsultSchemaFile.Ensure(Path.Combine(settings.DataDir, "schemas"));
        if (!provisioned.Ready)
        {
            // Said WHERE it happened, at startup, rather than surfacing minutes later as a child
            // process complaining about a missing file. It does not stop the service: one route of
            // four needs this, and a consultation on codex must not be prevented by a directory the
            // local engine would have used.
            log.Warning("consultations: {Problem}", provisioned.Problem);
        }

        return provisioned;
    }

    public ConsultationStore Store => _store;

    /// <summary>
    /// This caller's consultations still open in a checkout, newest first — what `status` reports.
    /// </summary>
    /// <remarks>
    /// <para>Re-orientation, which is what <c>status</c> is for, pointed at the one thing a compacted
    /// conversation loses that costs money: the <c>consultationId</c> its first reply carried. Without
    /// it a follow-up opens a SECOND consultation — the working tree collected again, a model that has
    /// already answered asked from scratch, the caller's own budget spent twice.</para>
    /// <para>Filtered by CALLER as well as by repository. A consultation another session opened is not
    /// this one's to resume — <see cref="ConsultationRules"/> refuses a follow-up whose caller differs
    /// — so listing it would offer an id that comes back as a refusal. (gemini, plan round.)</para>
    /// <para>Compared as a resolved PATH rather than as text, because the caller's spelling of a
    /// checkout is not the server's: the tool resolves <c>repoPath</c> to the repository's top level
    /// and the record holds that, so a call made from a subdirectory would match nothing.</para>
    /// </remarks>
    public IReadOnlyList<OpenConsultation> OpenIn(string repoPath)
    {
        var caller = CallerOf(repoPath);

        return [.. _store.All()
            .Where(record => !record.IsOver
                && string.Equals(record.Caller, caller, StringComparison.Ordinal)
                && SamePath(record.RepoPath, repoPath))
            .OrderByDescending(record => record.StartedUtc, StringComparer.Ordinal)
            .Select(record => new OpenConsultation(
                record.Id,
                record.Vendor,
                record.Model,
                record.Status,
                record.Branch,
                record.Turns.Count,
                record.MaxTurns,
                record.StartedUtc,
                record.Alert))];
    }

    public int Sweep(Func<int, bool> isAlive) =>
        _store.Sweep(isAlive, DateTime.UtcNow, settings.ConsultIdle, ConsultationStore.Retention);

    /// <summary>
    /// Re-projects every record the store still holds, and answers how many.
    /// </summary>
    /// <remarks>
    /// <para><b>Because the projection is allowed to fail.</b> A database that is locked or full when
    /// a consultation writes its LAST state leaves that consultation absent from the log for ever —
    /// a terminal record gets no further writes, so nothing would ever carry it across. The records
    /// are the source of truth and this is the pass that lets the view catch up with them.</para>
    /// <para>Cheap and bounded: the store already reads every file for its sweep, and the sweep reaps
    /// a terminal record 7 days after it ended — so this is a handful of upserts of rows that are
    /// almost always identical to what is there. Idempotent by construction, since the row is
    /// recomputed from the record rather than accumulated. (codex, this story's plan round.)</para>
    /// </remarks>
    public int Reproject()
    {
        var records = _store.All();
        if (records.Count == 0)
        {
            return 0;
        }

        // ONE open for the whole pass. A projection per record opened, stepped and closed the file
        // once per consultation — which is what `Projection.Write` is for, but it was being asked the
        // wrong question. (codex and gemini, code round.)
        new Store.Projection(settings.DataDir, log).Write(
            db =>
            {
                foreach (var record in records)
                {
                    db.RecordConsultation(ConsultationRows.From(record));
                }
            },
            "the consultations");

        return records.Count;
    }

    public async Task<string> AskAsync(string repoPath, string problem, string suspectedFilesJson, string consultationId, CancellationToken ct = default)
    {
        // FIRST, before the shape of the call is examined at all: somebody who switched the feature
        // off is owed the sentence saying so, not a complaint about an empty argument. And it is a
        // named refusal rather than a tool that disappears from the list — a caller that cannot see
        // the tool cannot be told why it is not there. (The panel's Consultant section writes it.)
        if (!settings.ConsultEnabled)
        {
            return Error("consulting another vendor is switched off in this installation "
                         + "(COAI_CONSULT_ENABLED) — the Consultant section of the ConnectOtherAIs panel turns it "
                         + "back on. Nothing was sent anywhere; carry on with the person instead");
        }

        // And before the arguments too: a routing setting that does not PARSE is a person who meant
        // to choose a consultant and did not manage it. Falling back to the shipped map would send
        // their working tree to a vendor they never picked, which is the one thing
        // `ConsultantRouting`'s own doctrine forbids. (CodeRabbit, on the pull request.)
        if (settings.ConsultantsUnreadable)
        {
            return Error("the consultant routing (COAI_CONSULTANTS) could not be read, so this installation "
                         + "does not know which vendor you chose — and it will not pick one for you. Fix the "
                         + "Consultant section of the ConnectOtherAIs panel. Nothing was sent anywhere");
        }

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
        var caller = CallerOf(repo);

        // THE LOCK IS TAKEN BEFORE THE RECORD IS READ, and that order is the fix for a race the code
        // round found: two follow-ups could both read an `open` record, the second wait for the lock,
        // and then write its turn over the first one's answer from a stale turn list. Nothing about a
        // consultation is decided outside the lock now. (codex, code round.)
        using var held = await RepositoryLock.TryTakeAsync(settings.DataDir, repo, RepositoryLock.DefaultWait, ct);
        if (held is null)
        {
            return Error($"another consultation is running in {repo} right now (waited {RepositoryLock.DefaultWait.TotalSeconds:0} s) — try again in a moment");
        }

        return await UnderTheLockAsync(new Caller(kind, caller, string.Empty), repo, problem, files, consultationId, ct);
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
            return Error(NoSuchVendor(choice.Vendor, caller.Kind, row is null, record?.Id));
        }

        var runtime = ConsultantResolution.For(row.Identity());
        if (runtime is null)
        {
            return Error(ConsultantResolution.CannotConsult(row.Identity()));
        }

        // The one route that needs the schema is refused BY NAME when it is not there, rather than
        // launched to meet a shim's complaint about a file it was handed. Every other route is
        // unaffected by the same failure, which is why this is checked here and not at startup.
        if (runtime.NeedsAnswerSchema && !_answerSchema.Ready)
        {
            return Error(_answerSchema.Problem);
        }

        // THE CALL IS COUNTED LAST, immediately before a consultant is launched, and that ordering is
        // the whole point: it used to be taken at the top of `WithConsultantAsync`, so every refusal
        // below — a lock somebody else held, an id belonging to another checkout, a vendor row
        // switched off, a missing runtime, an absent answer schema — spent one of the caller's calls
        // without a consultant ever running. `ConsultCallCounter` has no refund, so the cap could be
        // exhausted entirely on refusals. Counting here means the number measures what it is named
        // after: consultations. (CodeRabbit, on the pull request.)
        var counted = _counter.TryTake(caller.Id, settings.ConsultCallsPerSession, DateTime.UtcNow);
        if (!counted.Allowed)
        {
            return Error($"this caller session has made {counted.Used} consult calls, the cap (COAI_CONSULT_CALLS_PER_SESSION = {settings.ConsultCallsPerSession}) — "
                         + $"the window is {ConsultCallCounter.Window.TotalHours:0} hours from the first call; if you are still stuck, this is the moment to ask the person");
        }

        var model = choice.Model.Length > 0 ? choice.Model : row.Model;
        var consultant = new Consultant(runtime, row, model, caller with { CounterNote = counted.Note });

        return await RunTurnAsync(consultant, record ?? await NewRecordAsync(consultant, repo, DateTime.UtcNow, ct), repo, problem, files, ct);
    }

    /// <summary>
    /// Why this vendor cannot be consulted — and the sentence differs for a NEW consultation.
    /// </summary>
    /// <remarks>
    /// A new one is a configuration problem the caller can fix by choosing another row. A RESUMED one
    /// cannot be fixed at all: a consultation stays on the vendor it started with, so the cure is to
    /// start a new one. Pure, and lifted out of a method that was deciding six things at once.
    /// (CodeRabbit, on the pull request.)
    /// </remarks>
    private static string NoSuchVendor(string vendor, string callerKind, bool absent, string? resuming) =>
        resuming is null
            ? $"the consultant for a '{callerKind}' caller is the vendor '{vendor}', which is "
              + (absent ? "not configured" : "switched off")
              + " — pick an enabled vendor row for this caller in the Consultant section of the ConnectOtherAIs panel (COAI_CONSULTANTS)"
            : $"consultation {resuming} was opened on the vendor '{vendor}', which is no longer "
              + (absent ? "configured" : "enabled")
              + " — a consultation stays on the vendor it started with, so this one cannot go on; start a new consultation";

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
            consultant.Runtime.Memory is ConsultantMemory.VendorRemembers
                ? ConsultationMemories.VendorRemembers
                : ConsultationMemories.WeRemember,
            settings.ConsultTurns,
            ConsultationStore.Stamp(now))
        {
            // Frozen with the rest: the budget the adapter declared when this conversation opened is
            // what every turn of it carries, whatever a later build declares.
            CarryBudget = consultant.Runtime.Memory is ConsultantMemory.WeRemember carried ? carried.CarryBudget : 0,
        };
    }

    // ---------- the turn ----------

    private async Task<string> RunTurnAsync(Consultant consultant, ConsultationRecord record, string repo, string problem, IReadOnlyList<string> files, CancellationToken ct)
    {
        var nonce = Guid.NewGuid().ToString("N")[..8];
        var before = await _invariant.SnapshotAsync(repo, ct);
        var launch = consultant.Runtime.Build(new ConsultantLaunch(
            repo,
            await PromptAsync(record, problem, files, nonce, repo, ct),
            record.Handle,
            AnswersDir,
            Settings(consultant),
            _answerSchema.Path));
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

    /// <summary>The turn's prompt, composed from the RECORD alone.</summary>
    /// <remarks>
    /// It took a <c>Consultant</c> and never read it — residue from the fix the comment below
    /// describes, when the memory mode moved off the runtime adapter and onto the record. Its
    /// absence is now the statement: nothing about the vendor as it is configured TODAY may
    /// reach a conversation that was opened yesterday. (SonarCloud, Major, on the pull request.)
    /// </remarks>
    private async Task<string> PromptAsync(ConsultationRecord record, string problem, IReadOnlyList<string> files, string nonce, string repo, CancellationToken ct)
    {
        var resuming = record.Turns.Count > 0 || record.Status == ConsultationStatuses.Interrupted;
        // The RECORD's frozen mode, not the runtime's current one. An upgrade that changed an
        // adapter's memory mode would otherwise make an open consultation stop carrying its
        // transcript — or start carrying one to a vendor that already holds it.
        var remembers = !record.WeCarryTheConversation;

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
                ? ConsultantPrompt.Transcript([.. record.Turns.Select(t => (t.Problem, t.Advice))], record.CarryBudget)
                : string.Empty,
            PreviousAnswerLost: record.Status == ConsultationStatuses.Interrupted));
    }

    private string Breach(ConsultationRecord record, IReadOnlyList<TreeChange> changes, Consultant consultant, ReviewerLaunch launched, TimeSpan elapsed)
    {
        var sentence = FilesystemSnapshot.Sentence(changes);
        log.Error("consultation {Id}: {Alert}", record.Id, sentence);
        _store.Write(Ended(record, ConsultationStatuses.Failed, "the working tree changed while the consultant was running") with
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

        // The ADAPTER reads its own shape: prose for every CLI, an envelope for the one schema-bound
        // route. Unwrapping every answer here mangled a CLI's prose that happened to be JSON with an
        // `answer` property, which a consultant asked about a configuration file could return.
        var advised = consultant.Runtime.ReadAdvice(launched.Answer).Trim();
        var spent = ThisTurnsShare(consultant, record, launched.Usage);
        var turn = new ConsultationTurn(ConsultationStore.Stamp(DateTime.UtcNow), problem, advised, Math.Round(elapsed.TotalSeconds, 1), spent.TokensIn, spent.TokensOut, spent.CostUsd);
        _store.Write(Answered(record, turn, handle));
        Record(consultant, "ok", elapsed, spent);
        log.Information("consultation {Id}: answered turn {Turn} in {Seconds}s ({TokensIn}/{TokensOut} tokens)", record.Id, record.Budget.Turn, turn.Seconds, turn.TokensIn, turn.TokensOut);

        var advice = ConsultationFence.Advice(consultant.Row.Provider, consultant.Model, record.Budget, nonce, advised);
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

    /// <summary>The record this turn leaves behind — closed when the budget is spent, open otherwise.</summary>
    /// <remarks>
    /// Pure, and its own method because the three <c>IsLast</c> decisions are ONE decision wearing
    /// three hats: whether this was the last turn. Reading them apart, inside an object initialiser
    /// inside a method that also launches and logs, was the complexity the gate objected to.
    /// (CodeRabbit, on the pull request.)
    /// </remarks>
    private static ConsultationRecord Answered(ConsultationRecord record, ConsultationTurn turn, string handle)
    {
        var last = record.Budget.IsLast;

        return record with
        {
            Turns = [.. record.Turns, turn],
            Handle = handle,
            Status = last ? ConsultationStatuses.Closed : ConsultationStatuses.Open,
            // The sentence a LATER call is refused with is this one, so it carries the cure — the
            // setting's name — rather than leaving the caller to find it.
            Reason = last ? $"all {record.MaxTurns} of its turns are used (COAI_CONSULT_TURNS sets the cap)" : string.Empty,
            EndedUtc = last ? turn.Utc : string.Empty,
            UpdatedUtc = turn.Utc,
        };
    }

    /// <summary>
    /// What THIS turn consumed, for a vendor that reports the whole conversation's total every time.
    /// </summary>
    /// <remarks>
    /// The running total lives on the RECORD, which is why the subtraction happens here and the
    /// adapter only declares that it reports cumulatively. The arithmetic itself is
    /// <see cref="ConsultationUsage"/>, in the core, so the test exercises the rule rather than a
    /// copy of it.
    /// </remarks>
    private static Usage ThisTurnsShare(Consultant consultant, ConsultationRecord record, Usage reported)
    {
        if (!consultant.Runtime.UsageIsCumulative)
        {
            return reported;
        }

        var share = ConsultationUsage.ThisTurnsShare(
            [.. record.Turns.Select(t => (t.TokensIn, t.TokensOut, t.CostUsd))],
            (reported.TokensIn, reported.TokensOut, reported.CostUsd));

        return new Usage(share.TokensIn, share.TokensOut, share.CostUsd);
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

    /// <summary>
    /// Two spellings of one checkout, or not.
    /// </summary>
    /// <remarks>
    /// Guarded since it gained a second caller in story 4: this compares a path out of a RECORD —
    /// written by a server that may have run on another machine — against one the tool resolved here,
    /// and `GetFullPath` throws on a string no filesystem here can make sense of. A `status` call must
    /// not fail over somebody else's record, and a path this machine cannot resolve is not the one
    /// being asked about.
    /// </remarks>
    /// <remarks>
    /// <para><b>Links are followed, because the two sides come from different places.</b> The record
    /// holds git's answer — <c>TopLevelAsync</c>, which reports the REAL path — and the query holds
    /// whatever the session was opened with. On macOS those are routinely two spellings of one
    /// directory: a checkout under <c>/var/folders/…</c> is <c>/private/var/folders/…</c> to git,
    /// because <c>/var</c> is a link. <c>GetFullPath</c> normalises separators and <c>..</c> and
    /// stops there, so <c>status</c> answered "nothing open" about a consultation that was open —
    /// and the next call, not knowing better, opened a SECOND one: the tree collected again, a model
    /// that had already answered asked from scratch, the caller's budget spent twice. That is the
    /// exact loss this method exists to prevent, so it has to resolve what git resolved.</para>
    /// </remarks>
    /// <summary>
    /// Who owns a consultation — the calling session, or the CHECKOUT when no session names itself.
    /// </summary>
    /// <remarks>
    /// <para>The ID rather than the whole <see cref="CallerIdentity"/>, since the VENDOR half is a
    /// display fact and a consultation is owned by a session rather than by a brand.</para>
    /// <para><b>One function because two copies of this were the bug.</b> Writing a consultation and
    /// listing one had a line each, and they were not the same line: the write had already resolved
    /// the repository to its top level through git, the read used whatever the session was opened
    /// with. The same directory therefore produced two owners, so <c>status</c> filtered out the
    /// consultation it was asked about — on macOS, where <c>/var</c> is a link and those two
    /// spellings always differ. The fix for one such pair is not a second careful line; it is one
    /// line with no twin.</para>
    /// <para>The path is canonicalised for the same reason <see cref="SamePath"/> canonicalises:
    /// whoever calls this may hold either spelling, and an identity that depends on which one is
    /// not an identity.</para>
    /// </remarks>
    private string CallerOf(string repoPath) =>
        CallerIdentity.From(env).Id is { Length: > 0 } id ? id : RepoIdentity(repoPath, DocumentReader.FollowLink);

    /// <summary>The owner a checkout is, told the same way from either spelling of it.</summary>
    internal static string RepoIdentity(string repoPath, Func<string, string> followLink)
    {
        try
        {
            return $"repo:{Path.TrimEndingDirectorySeparator(DocumentReader.CanonicalRoot(repoPath, followLink))}";
        }
        catch (Exception e) when (e is ArgumentException or PathTooLongException or NotSupportedException)
        {
            // A path this machine cannot resolve still has to name SOMETHING, and naming it after
            // itself keeps the two sides agreeing — which is the whole job here.
            return $"repo:{repoPath}";
        }
    }

    internal static bool SamePath(string one, string other, Func<string, string> followLink)
    {
        try
        {
            return one.Length > 0 && other.Length > 0
                && string.Equals(
                    Path.TrimEndingDirectorySeparator(DocumentReader.CanonicalRoot(one, followLink)),
                    Path.TrimEndingDirectorySeparator(DocumentReader.CanonicalRoot(other, followLink)),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception e) when (e is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool SamePath(string one, string other) =>
        SamePath(one, other, DocumentReader.FollowLink);

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
