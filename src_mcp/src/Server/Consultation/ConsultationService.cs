using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>Whether a new consultation could be had right now, and if not, the sentence the tool refuses with.</summary>
/// <param name="Available">A consultant is configured, resolvable and runnable for this caller.</param>
/// <param name="Reason">Empty when available; otherwise exactly the refusal <c>consult</c> would answer.</param>
public sealed record ConsultPreflight(bool Available, string Reason);

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
    Func<string, string?> env,
    Noticing noticing,
    // The vault, for the one kind of consultant that authenticates with a key (ConsultantTurnInputs.TakesAKey).
    VaultKeys keys)
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
    private readonly ConsultHealthStore _health = new(settings.DataDir, problem => log.Warning("{Problem}", problem));
    private readonly ConsultCheckSweeper _checks = new(settings.DataDir, Path.GetTempPath(), DateTime.UtcNow);

    /// <summary>
    /// A failed turn, applied (<see cref="ConsultationFailedTurns"/>) — built on first use, since a primary
    /// constructor's field initialisers cannot reach the store and counter above; its answer leaves through
    /// <see cref="Error"/>, so the refusal roads stay counted where they were.
    /// </summary>
    private ConsultationFailedTurns FailedTurns => field ??= new(_store, _counter, _health, ledger, settings.DataDir, log, Error);

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
                record.Kind,
                record.Vendor,
                record.Model,
                record.Status,
                record.Branch,
                record.Turns.Count,
                record.MaxTurns,
                record.StartedUtc,
                record.Alert))];
    }

    /// <summary>
    /// Records how a consultation ENDED, and answers a sentence either way.
    /// </summary>
    /// <remarks>
    /// <para>The verb the surface never had. Before it, a consultation lapsed: nine tools and none of
    /// them ended one, so it sat at <c>open</c> until a sweep took it, and `Reason` said why it
    /// stopped rather than whether it worked. (issue #309.)</para>
    /// <para><b>Under the same lock as a turn</b>, for the same reason: a close that read the record
    /// outside it could overwrite an answer being written at that moment.</para>
    /// <para><b>A repeat writes nothing.</b> <see cref="ConsultationClosing.Writes"/> decides that
    /// separately from the refusal, because a retry whose reply was lost must succeed — and a second
    /// <c>EndedUtc</c> would move the moment the consultation ended to whenever the network failed.</para>
    /// </remarks>
    /// <param name="byPerson">
    /// The panel's own door, which is not subject to the caller check. It reaches here through a
    /// one-shot CLI mode on the person's own machine against their own data directory — a stronger
    /// position than another AI's, not a weaker one — and the record keeps the difference.
    /// </param>
    public async Task<string> CloseAsync(string repoPath, string consultationId, string outcome, string note, bool byPerson, CancellationToken ct = default)
    {
        var repo = repoPath.Trim();
        var id = consultationId.Trim();
        if (!ConsultationStore.IsWellFormedId(id))
        {
            return Error($"'{id}' is not a consultation id");
        }

        // WHICH consultation, before which lock. This read is outside the lock and nothing is
        // decided from it: it answers one question — which checkout does this consultation belong to
        // — and everything below runs against the re-read under that checkout's own lock.
        var found = _store.Read(id);
        if (found is null)
        {
            return Error($"no consultation {id} — it may have been swept after {ConsultationStore.Retention.TotalDays:0} days");
        }

        // THE SUPPLIED PATH IS CHECKED, NEVER TRUSTED — and it is checked BEFORE any lock is taken.
        // The log lists consultations from every checkout a person has reviewed, so a close can
        // legitimately arrive naming the wrong one; refusing it from the record costs nothing, while
        // refusing it from behind a lock made somebody else's repository wait out this one's whole
        // budget for a question it was never asked. (codex, the second code round.)
        if (!SamePath(found.RepoPath, repo))
        {
            return Error($"consultation {id} belongs to {found.RepoPath} — close it from there");
        }

        // THE LOCK IS THE RECORD'S OWN, not the caller's spelling of it. Taking the supplied path's
        // lock and then comparing the two would rest the whole guarantee on two normalisers agreeing
        // about what one path is, and they do not: SamePath resolves links through
        // DocumentReader.CanonicalRoot, and RepositoryLock.Normalise is GetFullPath and a lowercase.
        // Where those differ — and SamePath's own remarks record that they DO, on macOS, where /var
        // is a link — two spellings of one checkout would pass the comparison above and take two
        // DIFFERENT locks, so this close would write while a turn in the same tree was running,
        // which is the one thing the lock exists to prevent. RepoPath is what git resolved: one
        // spelling, with nothing left for two normalisers to disagree about. (codex, the second
        // code round.)
        using var held = await RepositoryLock.TryTakeAsync(settings.DataDir, found.RepoPath, RepositoryLock.DefaultWait, ct);
        if (held is null)
        {
            return Error($"another consultation is running in {found.RepoPath} right now (waited {RepositoryLock.DefaultWait.TotalSeconds:0} s) — try again in a moment");
        }

        // RE-READ under the lock. The read above happened outside it, so a turn may have answered,
        // failed or been swept in between — and every decision below is about the record as it is
        // NOW. Only the checkout was taken from the earlier read, and a record's repository does
        // not change.
        var record = _store.Read(id);
        if (record is null)
        {
            return Error($"no consultation {id} — it may have been swept after {ConsultationStore.Retention.TotalDays:0} days");
        }

        var word = outcome.Trim();
        if (ConsultationClosing.Refusal(record, CallerOf(repo), word, byPerson) is { } refusal)
        {
            return Error(refusal);
        }
        if (!ConsultationClosing.Writes(record, word))
        {
            // A SUCCESS, not a refusal: the reply to the first attempt was lost, and the caller is
            // asking again with the same word. Nothing is written — a second EndedUtc would move the
            // moment the consultation ended to whenever the network failed.
            return Json(
                new CloseAnswer(id, word, Recorded: false, $"consultation {id} was already recorded as '{word}'"),
                ServerJsonContext.Default.CloseAnswer);
        }

        var nowUtc = ConsultationStore.Stamp(DateTime.UtcNow);
        _store.Write(record with
        {
            Outcome = word,
            OutcomeBy = byPerson ? ConsultationSources.Person : ConsultationSources.Caller,
            Status = ConsultationStatuses.Closed,
            // The REASON is never overwritten: it says why the consultation stopped, and a close says
            // how it ended. A record the sweep closed keeps its sentence about the budget and gains a
            // verdict beside it. Only a record that had none gets this one.
            Reason = record.Reason.Length > 0 ? record.Reason : Ended(word, note, byPerson),
            EndedUtc = record.EndedUtc.Length > 0 ? record.EndedUtc : nowUtc,
            UpdatedUtc = nowUtc,
        });

        return Json(
            new CloseAnswer(id, word, Recorded: true, $"consultation {id} is recorded as '{word}'"),
            ServerJsonContext.Default.CloseAnswer);
    }

    /// <summary>The sentence a close leaves behind when nothing had closed the consultation before.</summary>
    private static string Ended(string outcome, string note, bool byPerson) =>
        note.Trim().Length > 0
            ? $"{(byPerson ? "closed by hand" : "closed by the caller")} as {outcome}: {note.Trim()}"
            : $"{(byPerson ? "closed by hand" : "closed by the caller")} as {outcome}";

    /// <remarks>
    /// The retention of what a consultation leaves beside its record — orphaned health temporaries, month-old
    /// transcripts — rides the same beat (<see cref="ConsultationRetention"/>); its count is not the
    /// records', so it is logged here rather than returned.
    /// </remarks>
    public int Sweep(Func<int, bool> isAlive)
    {
        // The records FIRST: ending a stranded `asking` record is the sweep's job, and a retention pass
        // that cannot list a directory must never be the reason it did not happen (epic 2's review).
        var swept = _store.Sweep(isAlive, DateTime.UtcNow, settings.ConsultIdle, ConsultationStore.Retention);
        var expired = ConsultationRetention.Sweep(settings.DataDir, DateTime.UtcNow, problem => log.Warning("{Problem}", problem));
        if (expired > 0)
        {
            log.Information("consultations: removed {Count} file(s) past their retention", expired);
        }

        // The consultant check's share (E4.4), last and unable to stop the rest: abandoned states, stale scratch.
        var (abandoned, scratch) = _checks.Sweep(DateTime.UtcNow, problem => log.Warning("{Problem}", problem));
        if (abandoned + scratch > 0)
        {
            log.Information("consultant checks: {Abandoned} abandoned state(s) settled, {Scratch} leftover scratch director(y/ies) removed", abandoned, scratch);
        }

        return swept;
    }

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

    /// <summary>The sentence for a feature a person switched off — one copy, for the tool and the cadence gate.</summary>
    private const string SwitchedOff =
        "consulting another vendor is switched off in this installation "
        + "(COAI_CONSULT_ENABLED) — ConnectOtherAIs > Consultant turns it "
        + "back on. Nothing was sent anywhere; carry on with the person instead";

    /// <summary>The sentence for a routing setting that does not parse — one copy, for the tool and the gate.</summary>
    private const string RoutingUnreadable =
        "the consultant routing (COAI_CONSULTANTS) could not be read, so this installation "
        + "does not know which vendor you chose — and it will not pick one for you. Fix the "
        + "settings in ConnectOtherAIs > Consultant. Nothing was sent anywhere";

    /// <summary>
    /// Whether a NEW consultation could be had by THIS caller right now, and the sentence the tool would
    /// refuse with when it could not.
    /// </summary>
    /// <remarks>
    /// <para><c>research/PLAN_consult_on_a_cadence.md</c>, story 2.1. The cadence gate asks this before it refuses a
    /// code round for want of a consultation: a consultant that cannot be had must stand the refusal down,
    /// never deadlock the work (decision 12).</para>
    /// <para><b>Every</b> refusal the vendor path makes before the counter and the launch — switched off,
    /// routing unreadable, nothing resolved, no runtime, no answer schema — and in the SAME words, because
    /// each one is built by the method <see cref="AskAsync(string, string, string, string, CancellationToken)"/>
    /// uses. The first design listed three of the five (the epic-1-3 consultation, point 7). Launches
    /// nothing; reads settings and the schema's readiness only.</para>
    /// </remarks>
    public ConsultPreflight Preflight() => Preflight(CallerIdentity.KindFrom(env));

    /// <inheritdoc cref="Preflight()"/>
    public ConsultPreflight Preflight(string callerKind)
    {
        if (!settings.ConsultEnabled)
        {
            return new ConsultPreflight(false, SwitchedOff);
        }
        if (settings.ConsultantsUnreadable)
        {
            return new ConsultPreflight(false, RoutingUnreadable);
        }

        return ConsultantResolver.Resolve(ConsultantRouting.For(settings.Consultants, callerKind), callerKind, settings.Providers) switch
        {
            ResolvedConsultant.Unavailable no => new ConsultPreflight(false, no.Why),
            ResolvedConsultant.Definition definition => VendorRefusal(definition.Vendor) is { Length: > 0 } why
                ? new ConsultPreflight(false, why)
                : new ConsultPreflight(true, string.Empty),
            _ => throw new InvalidOperationException("the union is closed"),
        };
    }

    /// <summary>The two refusals that need the vendor row: no consultant runtime for it, or no answer schema.</summary>
    private string VendorRefusal(ProviderSettings row)
    {
        var runtime = ConsultantResolution.For(row.Identity());
        if (runtime is null)
        {
            return ConsultantResolution.CannotConsult(row.Identity());
        }

        // The one route that needs the schema is refused BY NAME when it is not there, rather than
        // launched to meet a shim's complaint about a file it was handed. Every other route is
        // unaffected by the same failure, which is why this is checked here and not at startup.
        return runtime.NeedsAnswerSchema && !_answerSchema.Ready ? _answerSchema.Problem : string.Empty;
    }

    public Task<string> AskAsync(string repoPath, string problem, string suspectedFilesJson, string consultationId, CancellationToken ct = default) =>
        AskAsync(repoPath, problem, suspectedFilesJson, consultationId, ConsultAim.Stuck, ct);

    /// <param name="aim">What the consultation is FOR — stuck, or a group or risk item the cadence ordered.
    /// Ignored on a follow-up, which keeps the kind its record was opened with.</param>
    public async Task<string> AskAsync(string repoPath, string problem, string suspectedFilesJson, string consultationId, ConsultAim aim, CancellationToken ct = default)
    {
        // FIRST, before the shape of the call is examined at all: somebody who switched the feature
        // off is owed the sentence saying so, not a complaint about an empty argument. And it is a
        // named refusal rather than a tool that disappears from the list — a caller that cannot see
        // the tool cannot be told why it is not there. (The Consultant tab of the Settings editor tab writes it.)
        if (!settings.ConsultEnabled)
        {
            return Error(SwitchedOff);
        }

        // And before the arguments too: a routing setting that does not PARSE is a person who meant
        // to choose a consultant and did not manage it. Falling back to the shipped map would send
        // their working tree to a vendor they never picked, which is the one thing
        // `ConsultantRouting`'s own doctrine forbids. (CodeRabbit, on the pull request.)
        if (settings.ConsultantsUnreadable)
        {
            return Error(RoutingUnreadable);
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
            return await WithConsultantAsync(repo, ConsultantPrompt.BoundedProblem(problem), ConsultantPrompt.BoundedFiles(files), consultationId.Trim(), aim, ct);
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

    private async Task<string> WithConsultantAsync(string repo, string problem, IReadOnlyList<string> files, string consultationId, ConsultAim aim, CancellationToken ct)
    {
        var kind = CallerIdentity.KindFrom(env);
        var caller = CallerOf(repo);
        // The repository, not the checkout: a cadence consultation taken in one worktree counts for the
        // plan in another. Asked only when it will be used — a stuck consultation records none.
        var repoId = aim.IsStuck ? string.Empty : await context.CommonDirAsync(repo, ct);

        // THE LOCK IS TAKEN BEFORE THE RECORD IS READ, and that order is the fix for a race the code
        // round found: two follow-ups could both read an `open` record, the second wait for the lock,
        // and then write its turn over the first one's answer from a stale turn list. Nothing about a
        // consultation is decided outside the lock now. (codex, code round.)
        using var held = await RepositoryLock.TryTakeAsync(settings.DataDir, repo, RepositoryLock.DefaultWait, ct);
        if (held is null)
        {
            return Error($"another consultation is running in {repo} right now (waited {RepositoryLock.DefaultWait.TotalSeconds:0} s) — try again in a moment");
        }

        return await UnderTheLockAsync(new TurnCaller(kind, caller, string.Empty), repo, problem, files, consultationId, new Aimed(aim, repoId), ct);
    }

    private async Task<string> UnderTheLockAsync(TurnCaller caller, string repo, string problem, IReadOnlyList<string> files, string consultationId, Aimed aimed, CancellationToken ct)
    {
        var (record, refusal) = consultationId.Length == 0
            ? (null, Duplicate(aimed))
            : Existing(consultationId, caller.Id, repo, problem);
        if (refusal is not null)
        {
            return Error(refusal);
        }

        // A NEW consultation runs on what the caller's entry RESOLVES to — a definition of its own, or
        // a legacy reference read through the reviewer rows. A RESUMED one runs on the vendor, model
        // and runtime FROZEN on its record, never on what the panel says now: the record claims they
        // are frozen, and a settings change between turns would otherwise hand a conversation opened
        // on one model to another — or hand one vendor's handle to a different vendor entirely.
        // (codex, code round.) Both are ONE rule, `ConsultantResolver`, mirrored from the panel's
        // `resolveConsultant`; the reviewer catalogue is read on the legacy path only, and every way
        // the rule can refuse is a sentence that names the cure.
        var resolved = record is null
            ? ConsultantResolver.Resolve(ConsultantRouting.For(settings.Consultants, caller.Kind), caller.Kind, settings.Providers)
            : ConsultantResolver.Resumed(record, settings.Consultants, settings.Providers);

        return resolved switch
        {
            ResolvedConsultant.Unavailable no => Error(no.Why),
            ResolvedConsultant.Definition definition => await OnTheVendorAsync(definition.Vendor, caller, record, repo, problem, files, aimed, ct),
            _ => throw new InvalidOperationException("the union is closed"),
        };
    }

    /// <summary>What a new consultation is for, with the repository it will be recorded under.</summary>
    private sealed record Aimed(ConsultAim Aim, string RepoId);

    /// <summary>
    /// A new cadence or risk consultation that is already had, or already under way — or null.
    /// </summary>
    /// <remarks>
    /// D5 of <c>research/PLAN_consult_on_a_cadence.md</c>: an ordered consultation spends none of the stuck budget,
    /// so "none" must not become "unlimited". The ceiling is structural — one per group and one per named
    /// item, per plan. Two sentences, because they are two situations (epic 2's plan round, gemini): one
    /// already CLOSED with a verdict needs nothing more; one still OPEN is followed up, not duplicated. A
    /// lapsed or failed one is neither, so a new one may be taken — or the lapsed one given its outcome.
    /// </remarks>
    private string? Duplicate(Aimed aimed)
    {
        if (aimed.Aim.IsStuck)
        {
            return null;
        }
        var same = _store.All().Where(record => Covers(record, aimed.RepoId, aimed.Aim)).ToList();
        if (same.FirstOrDefault(record => record.IsOver && ConsultationOutcomes.IsVerdict(record.Outcome)) is { } had)
        {
            return $"epics {aimed.Aim.Epics} of {aimed.Aim.Plan} already have a closed {aimed.Aim.Kind} consultation — {had.Id}, "
                   + $"closed as '{had.Outcome}' on {had.EndedUtc} — so nothing more is owed for them; carry on with the work";
        }

        return same.FirstOrDefault(record => !record.IsOver) is { } open
            ? $"a {aimed.Aim.Kind} consultation for epics {aimed.Aim.Epics} of {aimed.Aim.Plan} is still open — follow it up with "
              + $"consultationId {open.Id} rather than starting a second, and end it with close_consult and an outcome"
            : null;
    }

    /// <summary>Whether a record is a consultation for this aim: same kind, repository, plan key and epics.</summary>
    internal static bool Covers(ConsultationRecord record, string repoId, ConsultAim aim) =>
        record.Kind == aim.Kind
        && repoId.Length > 0
        && record.RepoId == repoId
        && record.Epics == aim.Epics
        && Core.Cadence.EpicRef.PlanKey(record.Plan) == aim.PlanKey;

    /// <summary>The checks that need the vendor row — adapter, schema, the cap — and then the launch.</summary>
    /// <remarks>
    /// Its own method since story B3, because <see cref="UnderTheLockAsync"/> was deciding six things
    /// at once and the resolution rule made it seven. Everything here reads the row the resolver built
    /// and nothing about how it was built: the same checks for a definition, a legacy reference and a
    /// resumed record.
    /// </remarks>
    private async Task<string> OnTheVendorAsync(ProviderSettings row, TurnCaller caller, ConsultationRecord? record, string repo, string problem, IReadOnlyList<string> files, Aimed aimed, CancellationToken ct)
    {
        // The same two refusals the preflight gives, from the same method — so the cadence gate stands
        // down on exactly the sentences this tool refuses with.
        if (VendorRefusal(row) is { Length: > 0 } refused)
        {
            return Error(refused);
        }
        var runtime = ConsultantResolution.For(row.Identity())!;

        // THE CALL IS COUNTED LAST, immediately before a consultant is launched, and that ordering is
        // the whole point: it used to be taken at the top of `WithConsultantAsync`, so every refusal
        // before this line — a lock somebody else held, an id belonging to another checkout, an entry
        // that resolves to nothing, a missing runtime, an absent answer schema — spent one of the
        // caller's calls without a consultant ever running. `ConsultCallCounter` has no refund, so the
        // cap could be exhausted entirely on refusals. Counting here means the number measures what
        // it is named after: consultations. (CodeRabbit, on the pull request.)
        //
        // And only a STUCK consultation is counted (decision 11 of research/PLAN_consult_on_a_cadence.md): the cap
        // was sized for the calls an agent makes on its own, and an ordered one — a group of epics, a risky
        // piece — is bounded by `Duplicate` instead. A follow-up is counted by the kind its record holds.
        var stuck = record is null ? aimed.Aim.IsStuck : record.Kind == ConsultKinds.Stuck;
        var counted = stuck
            ? _counter.TryTake(caller.Id, settings.ConsultCallsPerSession, DateTime.UtcNow)
            : new CounterOutcome(true, 0, string.Empty);
        if (!counted.Allowed)
        {
            return Error($"this caller session has made {counted.Used} consult calls, the cap (COAI_CONSULT_CALLS_PER_SESSION = {settings.ConsultCallsPerSession}) — "
                         + $"the window is {ConsultCallCounter.Window.TotalHours:0} hours from the first call; if you are still stuck, this is the moment to ask the person"
                         + (counted.Note.Length > 0 ? $" ({counted.Note})" : string.Empty));
        }

        // The model is already MATERIALISED on the row — the definition's own, the reviewer row's where
        // a legacy reference names none, the record's frozen one on a follow-up — so nothing is decided
        // about it here. The line that used to fall back to the CURRENT row's model on a resumed
        // consultation is the leak the record's "frozen" claim never allowed.
        var consultant = new TurnConsultant(runtime, row, row.Model, caller with { CounterNote = counted.Note, Take = counted.Take });

        return await RunTurnAsync(consultant, record ?? await NewRecordAsync(consultant, repo, aimed, DateTime.UtcNow, ct), repo, problem, files, ct);
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

    private async Task<ConsultationRecord> NewRecordAsync(TurnConsultant consultant, string repo, Aimed aimed, DateTime now, CancellationToken ct)
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
            // What it is FOR, in the canonical form the cadence gate matches on.
            Kind = aimed.Aim.Kind,
            Plan = aimed.Aim.Plan,
            Epics = aimed.Aim.Epics,
            RepoId = aimed.RepoId,
            // Frozen with the model: the row's own instruction as it stands now, for every turn of this conversation (C2).
            RowInstruction = consultant.Row.SystemPrompt,
        };
    }

    // ---------- the turn ----------

    private async Task<string> RunTurnAsync(TurnConsultant consultant, ConsultationRecord record, string repo, string problem, IReadOnlyList<string> files, CancellationToken ct)
    {
        var nonce = Guid.NewGuid().ToString("N")[..8];
        // An antigravity consultant cannot list or search in plan mode: coai does it for it, read-only, inside this
        // checkout (todo/PLAN_agy_searches_through_coai.md, S3) — the prompt then teaches the block, the turn serves it.
        consultant = consultant.Runtime is AntigravityConsultant agy ? consultant with { Runtime = agy.With(new WorkspaceLookup([repo])) } : consultant;
        var before = await _invariant.SnapshotAsync(repo, ct);
        // Prepared before it is built: the adapter learns what the INSTALLED CLI accepts (claude: --restricted or
        // not), every turn, so Build stays pure — and may REFUSE when it could not learn it (IConsultantRuntime.PrepareAsync).
        var prepared = await consultant.Runtime.PrepareAsync(new ConsultantLaunch(
            repo,
            await PromptAsync(record, consultant.Runtime.Toolbox, problem, files, nonce, repo, ct),
            record.Handle,
            AnswersDir,
            Settings(consultant),
            _answerSchema.Path), launcher, ct);
        if (prepared is ConsultantPreparation.Refused refused)
        {
            return FailedTurns.RefusedBeforeTheLaunch(record, consultant, refused.Failure);
        }

        var ready = (ConsultantPreparation.Ready)prepared;
        // The row's system prompt is redacted from what the child says, as a reviewer's is: a CLI echoes its prompt, and a
        // failing one quotes it into the reason that reaches the record and the reply (todo/PLAN_one_model_catalog.md, C2).
        var launch = consultant.Runtime.Build(ready.Launch) with { Redact = ConsultantTurnInputs.Redacted(record.RowInstruction) };
        // What the turn is SENT rides the record from here on, so every ending — answered or failed — carries it.
        var asking = record with { Status = ConsultationStatuses.Asking, RunnerPid = Environment.ProcessId, UpdatedUtc = ConsultationStore.Stamp(DateTime.UtcNow), Confinement = ready.Confinement };
        if (ready.Note.Length > 0)
        {
            log.Information("{Kind} consultation {Id}: {Vendor} launches {Note}", record.Kind, record.Id, consultant.Row.Provider, ready.Note);
        }
        _store.Write(asking);
        log.Information("{Kind} consultation {Id}: turn {Turn}/{Cap} on {Vendor} in {Repo}", record.Kind, record.Id, record.Budget.Turn, record.MaxTurns, consultant.Row.Provider, repo);

        // ONE deadline over everything after the record says `asking` — the launch, both snapshots and
        // the record write alike. It is a BACKSTOP longer than the launcher's own child timeout, so an
        // ordinary hung child is killed by the launcher (which yields the resumable `interrupted` path
        // below) and this fires only for a wait the launcher does not bound: a snapshot, the settle
        // write, or a launch that ignores its own timeout. Every exit from here on settles the record,
        // so the turn can no longer leave it stuck at `asking`. (The 2026-09-26 incident: the answer
        // arrived, the record write threw, and nothing after the launch was guarded — so the turn left
        // `asking` naming its own live pid, `close_consult` refused it as running, and the pid sweep
        // kept it for ever while the cadence gate refused the epic's code round.)
        // The whole turn's deadline, derived from the launch's own budget the way a round's is
        // (ConsultationDeadline / RoundBudget). Longer than the launch, so the launcher's own kill of a
        // hung child wins and that turn stays resumable; this fires only for what the launcher does not
        // bound.
        var deadline = ConsultationDeadline.For(ConsultantTurnInputs.TurnTimeout(consultant.Row, settings.ReviewerTimeout));
        using var turn = CancellationTokenSource.CreateLinkedTokenSource(ct);
        turn.CancelAfter(deadline);

        var started = Stopwatch.StartNew();
        List<ReviewerLaunch> launched = [];
        // The failure this turn DECIDED, once it has — so a record write that throws while applying it
        // does not get the turn classified a second time (epic 2's review: a second classification gave a
        // second refund, made a breach refundable and made an ending resumable). Null until then: absence
        // is the fact, "nothing decided yet".
        ConsultationFailed? decided = null;
        try
        {
            // One launch, or two: a follow-up in the SAME conversation when the first answered nothing in
            // a way the vendor can cure by being told (ConsultantTurn — the consultation's half of #504).
            // Each launch lands in `launched` the moment it returns, so the failure path below reads the
            // handle and the usage of whatever exists. The tree is asked once more before a follow-up;
            // a turn that stopped there reports THOSE changes, any other takes the final comparison —
            // which is what sees a write by the LAST launch.
            var looked = await ConsultationLookups.RunAsync(executor, consultant.Runtime, ready.Launch, launch, t => ChangesSinceAsync(before, repo, t), launched.Add, turn.Token);
            var result = looked.Result;
            var changes = await ChangesOverTheTurnAsync(result, () => ChangesSinceAsync(before, repo, turn.Token));
            decided = FailedTurns.Decided(asking, consultant, result, changes, ConsultationFailing.KilledAs(ct.IsCancellationRequested, turn.IsCancellationRequested, deadline));

            return decided is null
                ? Settle(asking, consultant, looked, problem, nonce, started.Elapsed)
                : FailedTurns.Failing(consultant, started.Elapsed, decided);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The CALLER withdrew the job — not a failure of the consultant, and told apart from our
            // own deadline by the token's STATE, never the exception's type (reliability.md). Settle so
            // the record cannot stick at `asking`, then let the cancellation fly as it always did.
            FailedTurns.Quietly(() => _store.Write(ConsultationFailing.Withdrawn(
                ConsultationFailedTurns.Facts(asking, consultant, ConsultantTurn.SurvivingHandle(consultant.Runtime, launched), Usage.None))));

            throw;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return FailedTurns.AfterTheLaunch(consultant, started.Elapsed, decided ?? ConsultationFailedTurns.Undecided(asking, consultant, launched, e,
                ConsultationFailing.KilledAs(ct.IsCancellationRequested, turn.IsCancellationRequested, deadline)), decided is null ? string.Empty : e.Message);
        }
    }

    /// <summary>The turn's prompt, composed from the RECORD alone.</summary>
    /// <remarks>
    /// It took a <c>Consultant</c> and never read it — residue from the fix the comment below
    /// describes, when the memory mode moved off the runtime adapter and onto the record. Its
    /// absence is now the statement: nothing about the vendor as it is configured TODAY may
    /// reach a conversation that was opened yesterday. (SonarCloud, Major, on the pull request.)
    /// </remarks>
    /// <param name="toolbox">
    /// What the adapter says its launch can read with (<see cref="IConsultantRuntime.Toolbox"/>) — the one
    /// fact about the vendor this method is handed, and it is the FROZEN runtime's: on a follow-up the
    /// consultant was resolved by <c>ConsultantResolver.Resumed</c>, which refuses unless today's
    /// definition runs on the runtime the record froze, so the adapter asked here is the one the
    /// conversation was opened on — never whatever the panel now names.
    /// </param>
    private async Task<string> PromptAsync(ConsultationRecord record, string toolbox, string problem, IReadOnlyList<string> files, string nonce, string repo, CancellationToken ct)
    {
        var resuming = record.Turns.Count > 0 || record.Status == ConsultationStatuses.Interrupted;
        // The RECORD's frozen mode, not the runtime's current one. An upgrade that changed an
        // adapter's memory mode would otherwise make an open consultation stop carrying its
        // transcript — or start carrying one to a vendor that already holds it.
        var remembers = !record.WeCarryTheConversation;

        return ConsultantPrompt.Compose(new ConsultantPromptInput(
            // The consultant is told what it is for: being stuck, a group of epics, or a risky piece.
            prompts.For(ConsultKinds.PromptId(record.Kind)),
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
            PreviousAnswerLost: record.Status == ConsultationStatuses.Interrupted,
            Toolbox: toolbox,
            // The consultant's row's own instruction, FROZEN when the consultation opened (C2) — from the record, like the rest.
            RowInstruction: record.RowInstruction));
    }

    private string Settle(ConsultationRecord record, TurnConsultant consultant, ConsultationLookups.Looked looked, string problem, string nonce, TimeSpan elapsed)
    {
        var turned = looked.Result;
        var handle = HandleOf(turned.SurvivingHandle, record.Handle);
        var launched = turned.Final;

        // The ADAPTER reads its own shape: prose for every CLI, an envelope for the one schema-bound
        // route. Unwrapping every answer here mangled a CLI's prose that happened to be JSON with an
        // `answer` property, which a consultant asked about a configuration file could return. When the
        // lookups decided the advice (a capped block's prose), that is the answer, with why they stopped.
        var advised = (looked.Advice ?? consultant.Runtime.ReadAdvice(launched.Answer ?? string.Empty)).Trim()
            + (looked.Note.Length > 0 && looked.Advice != looked.Note ? $"\n\n({looked.Note})" : string.Empty);
        var spent = consultant.ShareOf(record, turned.TurnUsage);
        var turn = new ConsultationTurn(ConsultationStore.Stamp(DateTime.UtcNow), problem, advised, Math.Round(elapsed.TotalSeconds, 1), spent.TokensIn, spent.TokensOut, spent.CostUsd, turned.FollowedUp, record.Confinement);
        _store.Write(ConsultationAnswering.Answered(ConsultationBilling.Billing(record, spent), turn, handle));
        ConsultantTurnBooks.Billed(ledger, consultant.Row, consultant.Model, ConsultantRoles.Consult, "ok", elapsed, spent);
        _health.Answered(ConsultHealth.AnswerOf(record, turn.Utc));
        log.Information("{Kind} consultation {Id}: answered turn {Turn} in {Seconds}s ({TokensIn}/{TokensOut} tokens)", record.Kind, record.Id, record.Budget.Turn, turn.Seconds, turn.TokensIn, turn.TokensOut);

        var advice = ConsultationFence.Advice(consultant.Row.Provider, consultant.Model, record.Budget, nonce, advised);
        var note = consultant.Caller.CounterNote;

        return Json(
            new ConsultAnswer(record.Id, record.Budget.Turn, record.MaxTurns, note.Length > 0 ? advice + "\n(" + note + ")" : advice,
                ConsultationBilling.ReplyCost(consultant.Runtime.UsageIsCumulative, record, turned)),
            ServerJsonContext.Default.ConsultAnswer);
    }

    // ---------- plumbing ----------

    private string AnswersDir => ConsultHealthPaths.AnswersDirectory(settings.DataDir);

    private ReviewerSettings Settings(TurnConsultant consultant) =>
        ConsultantTurnInputs.Settings(consultant.Row, consultant.Model, settings.ReviewerTimeout, settings.DataDir, keys, settings.ApiOverrides);

    private async Task<string> ShapedTreeAsync(string repo, CancellationToken ct)
    {
        Directory.CreateDirectory(AnswersDir);

        return await ConsultantTurnInputs.ShapedTreeAsync(context, repo, ct);
    }

    /// <summary>The working tree's changes since <paramref name="before"/> — the filesystem invariant, asked once.</summary>
    private async Task<IReadOnlyList<TreeChange>> ChangesSinceAsync(FilesystemSnapshot before, string repo, CancellationToken ct) =>
        FilesystemSnapshot.Compare(before, await _invariant.SnapshotAsync(repo, ct));

    /// <summary>
    /// What changed over the whole turn: the changes that stopped its follow-up, or else the final comparison.
    /// </summary>
    /// <remarks>
    /// A turn stopped at the tree check already holds the changes that stopped it, and a second snapshot could
    /// only disagree by losing one that was reverted in between — so it is not taken. Every other turn asks
    /// once more, because its last launch ran after anything was asked.
    /// </remarks>
    private static async Task<IReadOnlyList<TreeChange>> ChangesOverTheTurnAsync(ConsultantTurnResult turned, Func<Task<IReadOnlyList<TreeChange>>> finalComparison) =>
        turned.Breached ? turned.ChangesBeforeFollowUp : await finalComparison();

    /// <summary>The handle the turn's launches left (<see cref="ConsultantTurn.SurvivingHandle"/>), else the one already known.</summary>
    private static string HandleOf(string surviving, string known) =>
        surviving.Length > 0 ? surviving : known;

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
    private string CallerOf(string repoPath) => CallerOf(env, repoPath);

    /// <summary>The same owner rule, for the question consultant (S2) — one line with no twin, as the remark above says.</summary>
    internal static string CallerOf(Func<string, string?> env, string repoPath) =>
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

    /// <summary>A refusal, through the ONE place the wire shape is built. See <see cref="Refusal"/>.</summary>
    /// <remarks>
    /// <para>It used to build its own <c>ErrorAnswer</c>, and so did the other service — two
    /// boundaries for one promise. Story 2.1 made it one; story 2.2 writes the notice there, so this
    /// is a two-line wrapper in front of the instrumented point rather than a road past it.</para>
    /// <para><b>It is an instance method and takes a caller name, and neither cost a call site.</b>
    /// The logger is what says a notice was LOST — <c>Append</c> answers false for anything the disk
    /// gave, and a run where that happens otherwise looks exactly like one where it did not. The
    /// caller name becomes the notice's <c>subject</c>: the extension keys repeats on
    /// <c>(code, subject)</c>, so one <c>refused</c> code for every site would collapse every reason
    /// into a single row. The compiler fills it at each site, so nothing below changed.</para>
    /// </remarks>
    private string Error(string sentence, [CallerMemberName] string from = "") =>
        Refusal.Answer(sentence, noticing, from);
}
