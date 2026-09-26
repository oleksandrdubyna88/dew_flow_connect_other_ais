using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Worktrees;
using CoaiMcp.Store;

namespace CoaiMcp.Server;

/// <summary>
/// The one engine every stage runs through — claim, begin, deadline, assemble, schedule, dedup,
/// gate, verdict, save, project — and the answer the caller reads back.
/// </summary>
/// <remarks>
/// A stage hands it a <see cref="StageRun"/>. What only the MCP-facing service can say is handed
/// in as well rather than asked of it: which vendors this stage cannot run, and the sentence for a
/// round nobody could serve. Every other collaborator is a field of its own.
/// </remarks>
internal sealed class RoundEngine(
    PanelSettings settings,
    Serilog.ILogger log,
    Noticing noticing,
    SessionStore store,
    WorktreeManager worktrees,
    BoundedScheduler scheduler,
    ReviewerExecutor executor,
    RoundCommands commands,
    Escalations escalations,
    UsageLedger ledger,
    Store.Projection projection,
    CallerSessions callers,
    RemoteProbe remote,
    Func<Stage, IReadOnlyList<string>> excludedFrom,
    Func<Stage, RoundWork, string> noReviewerRefusal)
{
    private readonly PanelSettings _settings = settings;
    private readonly Serilog.ILogger _log = log;
    private readonly Noticing _noticing = noticing;
    private readonly SessionStore _store = store;
    private readonly WorktreeManager _worktrees = worktrees;
    private readonly BoundedScheduler _scheduler = scheduler;
    private readonly ReviewerExecutor _executor = executor;
    private readonly Escalations _escalations = escalations;
    private readonly UsageLedger _ledger = ledger;
    private readonly Store.Projection _projection = projection;
    private readonly CallerSessions _callers = callers;
    private readonly RemoteProbe _remote = remote;
    private readonly RoundCommands _commands = commands;
    private readonly Func<Stage, IReadOnlyList<string>> _excludedFrom = excludedFrom;
    private readonly Func<Stage, RoundWork, string> _noReviewerRefusal = noReviewerRefusal;

    /// <summary>The lenses a round spent: the prompt of every row that was asked.</summary>
    /// <remarks>
    /// A row that stood down (issue #485) was never asked, so its lens is still unspent — counted as
    /// spent it would be skipped until the pool reset (CodeRabbit, on the pull request). The results are
    /// in the work's own order, which is what <c>RunAllAsync</c>'s <c>Task.WhenAll</c> returns.
    /// </remarks>
    internal static IEnumerable<string> SpentPrompts(
        IReadOnlyList<ReviewerWork> work, IReadOnlyList<(ReviewerInvocation Invocation, ReviewerOutcome Outcome)> results) =>
        work.Zip(results)
            .Where(row => row.Second.Outcome is not ReviewerOutcome.StoodDown)
            .Select(row => row.First.Prompt)
            .Where(p => p.Length > 0);

    /// <summary>
    /// A held gate, opened by the answer a person actually gave — read at the last moment.
    /// </summary>
    /// <remarks>
    /// <para>The decision lives in the escalation answer file, keyed by session, because that is
    /// where the panel and the phone both write it. Reading it HERE, immediately before a round
    /// would begin, means the person can answer at any point during the wait and the next attempt
    /// simply works — no restart, no re-open, nothing to poll.</para>
    /// <para><c>Continue</c> and <c>Fix</c> grant a fresh set of rounds; <c>Discuss</c> and a typed
    /// answer with no button pressed leave the gate held, which is what both of them mean.</para>
    /// </remarks>
    private PersistedSession ApplyAnyHumanDecision(PersistedSession session)
    {
        if (!session.State.HumanGate)
        {
            return session;
        }

        var decision = _escalations.DecisionFor(session.State.SessionId);
        var opened = RoundMachine.ApplyHumanDecision(session.State, decision);
        if (ReferenceEquals(opened, session.State) || opened.HumanGate)
        {
            return session;
        }

        _log.Information(
            "a person chose {Decision}: the {Stage} stage gets a fresh set of rounds",
            decision,
            session.State.Stage);
        var next = session with { State = opened };
        _store.Save(next);
        return next;
    }

    /// <summary>
    /// Ask every configured Team server which roles it runs. EVERY round, without exception.
    /// </summary>
    /// <remarks>
    /// <para>One probe per SERVER, not per vendor: <c>Coai:ExtraRoles</c> is one setting on one box,
    /// so several vendors sharing a server collapse to one request.</para>
    /// <para><b>No server is ever skipped for having been asked before</b>, and the wording matters
    /// because the first version did skip and a maintainer following its doc would restore the
    /// defect: a transient failure became permanent for the process. <see cref="RemoteProbe"/> owns
    /// freshness — it answers from its cache inside the window and backs off after a failure — so
    /// the ordinary round pays nothing and a stale answer is the only thing that can be refreshed
    /// here. See <see cref="AskWhichRolesAsync"/>.</para>
    /// <para><b>Nothing here fails a round.</b> A server that cannot be reached is recorded as
    /// unreachable by the probe itself, which is a state the exclusion sentence can name; throwing
    /// would turn a network blip into a refused review. Cancellation is the exception: a round whose
    /// clock has run out should stop, not keep probing.</para>
    /// </remarks>
    private async Task WarmRemoteRolesAsync(CancellationToken ct)
    {
        // One task per distinct SERVER, run TOGETHER. Serially, an operator with five configured
        // servers of which three were down waited for three timeouts in a row before the round could
        // begin — out of the round's own budget, with nothing saying why. (gemini and codex, story
        // 4's code round, from three directions.)
        var asked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await Task.WhenAll(
            _settings.Providers
                .Where(p => p.Enabled && RosterBuilder.Remote(p) && asked.Add(TeamServerAuth.Normalise(p.BaseUrl)))
                .Select(p => AskWhichRolesAsync(p, ct)));
    }

    /// <summary>
    /// One server, asked. Never throws for the SERVER's sake; always throws for the ROUND's.
    /// </summary>
    /// <remarks>
    /// <para><b>There is deliberately no "already asked, skip it" test.</b> The first version had
    /// one, and it made a single transient failure permanent: a 502 during a restart recorded
    /// <c>Unreachable</c>, every later round saw a state that was no longer <c>NotAsked</c> and
    /// skipped the server, and that server's custom roles stayed off for the life of the process.
    /// The staleness ran the other way too — an operator adding a role to <c>Coai:ExtraRoles</c> was
    /// never heard again. <see cref="RemoteProbe"/> already owns freshness: it answers from its
    /// cache inside the window and backs off after a failure, so asking every round costs nothing
    /// when the answer is fresh and is the only thing that can refresh it when it is not. Four
    /// findings from two reviewers, all of them this. (Story 4's code round.)</para>
    /// <para>Cancellation is not a server failure: a round whose clock has run out must stop, not
    /// record every server as unreachable on its way out.</para>
    /// </remarks>
    private async Task AskWhichRolesAsync(ProviderSettings provider, CancellationToken ct)
    {
        try
        {
            await _remote.RunAsync(provider.Identity(), provider.Enabled, _settings.DataDir, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _log.Warning(e, "could not ask {Server} which review roles it runs", provider.BaseUrl);
        }
    }

    /// <remarks>
    /// <paramref name="from"/> is the ENTRYPOINT, filled by the compiler at each of the three callers
    /// — the plan round, the code round and the document round. Without it every refusal this method
    /// returns is written down as <c>RunStageAsync</c>, and the page cannot tell a plan round that
    /// found no reviewers from a code round that has no session. (CodeRabbit, on the pull request.)
    /// </remarks>
    internal async Task<string> RunStageAsync(
        string repoPath,
        string branch,
        string planText,
        StageRun stage,
        CancellationToken ct,
        [CallerMemberName] string from = "")
    {
        if (planText.Length == 0)
        {
            return Error(
                "planText is required — a reviewer that cannot see the intent reviews its own guess", from);
        }

        // One mutating call per session, held for the whole round (S4 of
        // research/PLAN_a_failed_round_can_be_retried.md). Taken BEFORE the session is read: two rounds
        // that both read it first would both compute the next round from the same file.
        using var claim = SessionClaim.TryTake(_settings.DataDir, repoPath, branch, stage.Document);
        if (claim is null)
        {
            return Error(SessionClaim.Busy(branch), from);
        }

        var session = _store.Load(repoPath, branch, stage.Document);
        if (session is null)
        {
            return Error("no session for this repo+branch — call open first", from);
        }

        session = ApplyAnyHumanDecision(session);
        // The session as read under the claim, BEFORE a begin moves it — what a refusal compares with.
        var loaded = session;

        switch (stage.Begin(session.State))
        {
            case Transition.Refused refused:
                return Error(refused.Sentence, from);
            // The state a begin MOVED to is the state the round runs from. Every begin but one
            // returns the state it was given; `BeginCodeRoundAgain` reopens a finished code stage,
            // and dropping that here would complete a round against `Done`. Nothing is saved until
            // the round ends, so a refusal further down still leaves the session as it was.
            case Transition.Moved { State: var begun }:
                session = session with { State = begun };
                break;
        }

        // ARMED BEFORE THE SETUP, not after it. The first build computed the budget from `work.Count`
        // and so could only start the timer once the work existed — which left resolving a sha,
        // mounting a worktree and shaping a diff outside the deadline entirely: a five-minute round
        // could spend ten minutes in setup and only then be told its time was up. Subtracting the
        // setup from what the reviewers get bounded the REVIEWING, not the round, and a reviewer
        // said so twice.
        //
        // The count comes from the CONFIGURED vendors and roles instead, which are known before any
        // of that happens and are the same numbers the panel derives its figure from. `work.Count`
        // can only be smaller — a vendor that serves the other stage, a repository with no rules —
        // so basing the budget on the configured shape is the generous direction, which is the one
        // to be wrong in.
        var budget = RoundDeadlineFor(ConfiguredReviewers(stage.Stage, stage.RolesPerVendor));
        using var clock = new CancellationTokenSource(budget);
        using var roundClock = CancellationTokenSource.CreateLinkedTokenSource(ct, clock.Token);

        // ASKED BEFORE THE ROUND IS ASSEMBLED, because assembling it is synchronous and cannot.
        //
        // `RolesOn` reads what a Team server last said; it does not fetch, so a round built before
        // anything had asked would see `NotAsked` and carry only the five this product ships —
        // silently leaving out a role the server accepts perfectly well. The panel probes every few
        // seconds while it is open, but the PANEL is the extension and this is `coai-mcp`: two
        // processes, two caches, and nothing guarantees this one has ever asked. So it asks here,
        // once, where there is still a Task to await on. (gemini and codex, story 4's plan round,
        // both Blocking.)
        await WarmRemoteRolesAsync(roundClock.Token);

        try
        {
            var sha = await _worktrees.ResolveShaAsync(repoPath, branch);
            if (await stage.RefuseBeforeBuilding(loaded, sha) is { Length: > 0 } nothingThere)
            {
                _log.Warning("{Stage} refused before building: {Reason}", stage.Stage, nothingThere);
                return Error(nothingThere, from);
            }

            // The number this round is written under: the next one in the session's journal for
            // this stage, allocated here — under the claim — and nowhere else. NOT the budget
            // counter plus one: `again`, the escalation ladder and a person's Continue/Fix all reset
            // that counter to zero, and the database upserts on (session, stage, number), so the
            // round after any of them REPLACED the round before it in the log (§9.6 of the
            // feature-review plan). The counter keeps counting rounds for the budget; this is the
            // round's name.
            var number = RoundNumber.Next(session.Rounds, stage.Stage);

            // The plan and epic this round is FOR go on the session BEFORE the live round first writes it,
            // so even a round a crash interrupts carries its epic (the epic-1-3 consultation, point 2).
            if (stage.Cadence.Call.Plan.Length > 0)
            {
                session = session with
                {
                    Plan = stage.Cadence.Call.Plan,
                    Epic = stage.Cadence.Call.Epic,
                    CadenceRepoId = stage.Cadence.Call.RepoId,
                };
            }

            // The plan stage gets an empty scratch directory instead of a checkout — there is
            // nothing there to wander into, which is the point.
            await using var lease = stage.NeedsWorktree
                ? await _worktrees.AddAsync(repoPath, sha, session.State.SessionId, number)
                : null;
            using var scratch = stage.NeedsWorktree ? null : new ScratchDirectory();
            var workingDir = lease?.Path ?? scratch!.Path;
            var roundWork = await stage.MakeWork(session, workingDir, sha);
            var work = roundWork.Reviewers;

            // A stage nobody serves is a REFUSAL, not an empty round. With no reviewer the round
            // runs nothing, merges nothing, and passes the gate — reporting `proceed` having
            // reviewed exactly nothing, which is worse than any verdict it could have given. Now
            // reachable on purpose: a vendor can be set to review plans and not code.
            if (work.Count == 0)
            {
                // And if a role was dropped on the way here, the refusal says which and why. This
                // path returns before any summary is built, so a round whose whole roster was
                // filtered out would otherwise be refused with a sentence about vendors — sending
                // somebody to check a configuration that is perfectly correct. (codex, the code
                // round, twice.)
                return Error(_noReviewerRefusal(session.State.Stage, roundWork), from);
            }

            // The round exists on disk BEFORE the first CLI starts: the panel shows "running" for
            // its whole duration instead of nothing at all, and a crash leaves something to sweep.
            // What the round is about, derived from the plan the caller passed — a file name if
            // they handed a path, its title otherwise. Nobody has to remember to name the work.
            var subject = RoundSubject.From(planText, File.Exists);
            var live = new LiveRound(_store, session, number, work, subject, _noticing);
            var audit = new RoundAudit(_log, session.State.Stage.ToString(), number);
            // Two kinds of exclusion, one list: a vendor this round cannot run AT ALL — no adapter,
            // no credential — and a vendor that cannot run one particular ROLE, which is what a Team
            // server does with a role a person defined. The second is only discovered while the work
            // is built, which is why it travels back on it.
            var excluded = (IReadOnlyList<string>)
                [.. _excludedFrom(stage.Stage), .. roundWork.Excluded.Select(e => e.Sentence)];
            audit.Opening(work, workingDir, _settings.ReviewerTimeout, excluded);
            // The ROUND's own deadline, derived from its shape unless somebody set one. A reviewer
            // is bounded; a round was not, and the round is what a person watches — so a round could
            // legitimately run for a long time while every reviewer inside it behaved, and the
            // operator asked for a bound after one passed ten minutes.
            //
            var results = await _scheduler.RunAllAsync(work, _executor, roundClock.Token, progress =>
            {
                live.Report(progress);
                audit.Moved(progress);
                // Every finished reviewer is recorded as it finishes, not at the end: a round that
                // is killed halfway still cost money, and a ledger that only writes on success
                // would under-report exactly the runs worth questioning.
                if (progress.Outcome is { } finished)
                {
                    _ledger.Record(
                        work.First(w => w.Invocation.Provider == progress.Provider && w.Invocation.Role == progress.Role).Invocation,
                        finished,
                        ModelOf(progress.Provider),
                        session.State.Stage.ToString(),
                        progress.Elapsed);
                }
            },
            // Issue #485: with the switch on, the local reviewer stands down once the cloud was quiet.
            _settings.StopLocalWhenQuiet ? StandDown.For(work) : null);
            // What ran, and — since 2026-09-07 — who was enabled for this stage and could not.
            //
            // The deadline is reported only when IT is what ended the round: `roundClock` fired and
            // the caller's own token did not. Inferring it from cancelled reviewers would have called
            // it a deadline the moment a PERSON cancelled a round, which is a different sentence and
            // a wrong one.
            var summary = ReviewerSummaryFactory.From(results, excluded, roundWork.NotAsked) with
            {
                // Read from the TIMER, and only when the caller did not also cancel. A person who
                // cancels at the moment the deadline strikes is reported as a person: the round was
                // going to end either way, and blaming the clock for their decision is the wrong
                // half of an ambiguity to keep.
                EndedByDeadline = WhatEndedIt(clock.Token, ct, budget),
            };
            var reviews = results.Select(r => r.Outcome).OfType<ReviewerOutcome.Ok>().Select(o => o.Review).ToList();
            // The ROLE is stamped here because this is the only place that holds both the invocation
            // and its answer. A threshold belongs to a role, so a finding has to remember whose it is.
            var merged = FindingDedup.Merge(results
                .Where(r => r.Outcome is ReviewerOutcome.Ok)
                .SelectMany(r => ((ReviewerOutcome.Ok)r.Outcome).Review.Findings
                    .Select(f => f with { Role = r.Invocation.Role.ToString() })));
            var gate = GateRule.Evaluate(
                merged,
                session.State.Rejections,
                role => _settings.Rounds.For(role).Threshold);

            if (RoundMachine.CompleteRound(session.State, gate, summary) is not Transition.Ok completed)
            {
                return Error("the round could not complete — this is a bug, report it", from);
            }

            var answer = AnswerFor(stage.Stage, completed.Verdict, gate, summary, merged, reviews, StageGate(session).Threshold);
            // And, on a document round whose work reached a Team server, where the document went.
            // Appended to the reviewer line rather than given a field of its own: it is a fact about
            // THIS round's reviewers, and it has to be read by an AI that was not told to look for a
            // new field. Empty for every other round, so nothing else changes by a byte.
            answer = answer with
            {
                Reviewers = answer.Reviewers
                    + WhereTheDocumentWent(stage.Stage, work.Select(w => w.Invocation.Provider))
                    // And what the round did not look at: a `proceed` must not cover an uncommitted
                    // tail nobody read. Empty for every round that has none.
                    + roundWork.Unreviewed,
            };
            // The commit it reviewed, kept with the round: what a later `again` compares the branch to.
            var record = live.Finish(answer.Verdict, gate.GatingCount, summary.Sentence, results) with
            {
                Sha = Stages.Of(stage.Stage).RecordsSha ? sha : string.Empty,
                // What the consultation cadence said about this round: met, or stood down and why.
                CadenceNote = stage.Cadence.Call.Note,
            };
            // The operator's own switches, read for THIS call: the settings file is stamped and
            // reloaded per tool call, so a box ticked a second ago governs this round.
            var caller = RoundCommands.CallerFor(session);
            var callerKind = CallerIdentity.KindFrom(Environment.GetEnvironmentVariable);
            var provisional = new Core.Commands.CommandContext(
                Autonomous: _settings.Autonomous,
                SplitPlan: _settings.SplitPlan,
                SplitWithFable: _settings.SplitWithFable,
                // The plan THIS round reviewed, not the one the session remembers. `session` was
                // loaded before the round and its PlanText is the PREVIOUS round's — empty on the
                // first plan round, which is the ordinary case — so every verdict was computed
                // from nothing and every plan came back "small enough to build as it stands".
                // Found end-to-end by SplitOrderTests: "Measured from the plan you sent: 0 lines,
                // 0 build step(s), 0 file(s) named" for a plan of four hundred.
                PlanText: planText,
                // A plan that did NOT pass is not a plan to go and build: with the switch on, a
                // `revise` or a `call_human` verdict was still telling the caller to split it into
                // stories and start committing. The order to build follows permission to build.
                PlanStage: session.State.Stage == Stage.PlanReview && RoundCommands.MayProceed(completed.Verdict),
                // Once per CALLER, not once per session: the epics a split produces come back as
                // their own sessions on their own branches, so a per-session memory would order
                // every one of them to split again — epics of epics, with no floor.
                FirstPlanRound: true)
            {
                // The CALLER carries the model order out, so it names the caller's own models: the
                // kind comes from the vendor's session variable — the same answer the consultant is
                // chosen by — and the pair from the panel (issue #117). A client nobody can identify
                // is `other`, and is named no model rather than another vendor's.
                Models = Core.Commands.CommandModels.For(_settings.CommandModels, callerKind),
                // Once per epic or once for the task, never per story (issue #131).
                GatePer = _settings.GatePer,
                // The words of the orders, read for THIS call like the switches: a file a person saved
                // a second ago rewords this round's order (issue #467).
                Texts = _commands.CommandTextsNow(),
                // The consultation cadence as it stands for this call — Off unless the operator switched it on
                // (todo/PLAN_consult_on_a_cadence.md).
                Cadence = stage.Cadence.Call.Facts,
            };
            // The caller's one order is CLAIMED, and only on a round that would actually give it —
            // a claim taken on a code round would spend it on a round that issues nothing. The
            // product's own predicate asks the question, so the condition cannot drift from what
            // `For` then does with it.
            var context = Core.Commands.GateCommands.OrdersSplit(provisional)
                ? provisional with
                {
                    FirstPlanRound = _callers.TryClaimSplitOrder(
                        caller, DateTime.UtcNow, message => _log.Warning("{Message}", message)),
                }
                : provisional;
            // The person's own commands come AFTER the built-in orders, which keep the positions every
            // caller and the bench already read them at (issue #467).
            var custom = _commands.CustomOrdersFor(stage.Stage, context.Texts);
            var commands = (IReadOnlyList<string>)[.. Core.Commands.GateCommands.For(context), .. custom.Orders];
            if (Core.Commands.GateCommands.OrdersSplit(context))
            {
                _log.Information(
                    "split ordered to caller {Caller} ({CallerKind}); {Models}", caller, callerKind, RoundCommands.ModelsInLog(context));
            }
            // Built HERE because this is the only place that holds both a reviewer's answer and the
            // invocation that produced it, which is the same reason the ROLE is stamped on a finding
            // twenty lines up. A note with no name on it is three accounts in a heap.
            var notes = (IReadOnlyList<ReviewerNote>)[.. results
                .Where(r => r.Outcome is ReviewerOutcome.Ok { Review.Notes.Length: > 0 })
                .Select(r => new ReviewerNote(
                    r.Invocation.Provider.ToString(),
                    r.Invocation.Role.ToString(),
                    ((ReviewerOutcome.Ok)r.Outcome).Review.Notes))];
            answer = answer with
            {
                Cost = new RoundCost(record.TokensIn, record.TokensOut, record.CostUsd),
                Commands = commands.Count == 0 ? null : commands,
                CommandsPreamble = commands.Count == 0 ? null : Core.Commands.GateCommands.PreambleFor(context),
                // Absent rather than empty when nobody wrote one, which is every code round: an
                // empty list in every reply would teach a reader to stop seeing the field.
                Notes = notes.Count == 0 ? null : notes,
                CommandsSkipped = custom.Skipped.Count == 0 ? null : custom.Skipped,
            };
            // REQUIRED, and the previous attempt at this was worse than the bug it fixed. Making it
            // best-effort stopped the round dying and started it LYING: the caller was handed
            // findings, numbered, and told to resolve them — while `resolve` reads the pending list
            // from a file that was never written, so the round sat at `running` with nothing to
            // decide on. An answer whose findings cannot be resolved is not an answer.
            _store.Save(session with
            {
                State = completed.State,
                Rounds = [.. session.Rounds, record],
                Pending = [.. merged],
                // The lenses this round spent, so the next one asks the ones nobody has yet.
                UsedPrompts = [.. session.UsedPrompts.Union(SpentPrompts(work, results))],
                // The scope is kept with the session so the CODE stage has it without the caller
                // sending it twice. Asking for it again is how a caller ends up sending nothing,
                // and a reviewer handed a bare diff answers a different question than the one the
                // gate exists to ask.
                PlanText = planText,
            });
            audit.Closing(answer.Verdict, gate.GatingCount, summary.Sentence, record);
            audit.Findings(merged);
            // And into the database, with what the round was ABOUT. Best-effort by construction:
            // the round is already answered and saved, and a projection that cannot be written must
            // never take it down.
            _projection.Write(db =>
            {
                db.RecordRound(
                    completed.State,
                    record,
                    merged,
                    // Which AI asked for it rides on `record` itself — see RoundContext's remarks.
                    new Store.RoundContext(
                        planText, sha, caller, [.. gate.Discounted], WhatTheCallerWasDoing(session, record))
                    {
                        // From the work, because the stage that assembled the diff is the only thing
                        // that resolved it. A plan round assembles none and leaves this empty.
                        BaseRef = roundWork.BaseRef,
                        // What this round ORDERED, and the size a split order was computed from —
                        // written down so "why five epics?" has an answer (issue #131).
                        Commands = [.. commands],
                        PlanShape = Core.Commands.GateCommands.ShapeOrdered(context),
                    });

                // Phase 2's instrument, and NOTHING is called: how many findings this round handed
                // back that the caller had already accepted and fixed. It is counted here because
                // this is where the decisions live — the session file holds what a round found, the
                // database holds what was done about it — and it is inside the projection because a
                // measurement must never be able to take a round down. A round the projection could
                // not ask about keeps `-1`, which is the absence of a measurement rather than a zero.
                var stuck = Core.Gate.StuckFindings.SurvivedAcceptance(
                    merged, db.DecidedEarlier(completed.State.SessionId, record.Stage, record.Number));
                db.RecordConsultMissed(completed.State.SessionId, record.Stage, record.Number, stuck.Count);
                if (stuck.Count > 0)
                {
                    audit.Stuck(stuck.Sentence);
                }
            });
            NotifyIfAPersonMustDecide(completed.Verdict, session, merged);
            return JsonSerializer.Serialize(answer, ServerJsonContext.Default.ReviewAnswer);
        }
        catch (SessionStoreException e)
        {
            // The round ran and its findings are in the log; what failed is writing the state
            // `resolve` needs. Saying so is the whole point — the caller re-runs the round instead
            // of resolving indices into a list nobody wrote.
            _log.Error(e, "the round completed but could not be recorded");

            return Error($"the round completed but could not be recorded, so its findings cannot be "
                + $"resolved — run it again. {e.Message}");
        }
        catch (Exception e) when (e is WorktreeException or ContextException)
        {
            return Error(e.Message);
        }
        catch (Exception e)
        {
            // Anything unforeseen becomes a SENTENCE, never an SDK-level "An error occurred
            // invoking 'review_plan'". That message is what the first real run got (2026-08-31)
            // when the prompts were missing from the release asset — it named the tool and
            // nothing else, so the cause had to be guessed. The log carries the stack; the caller
            // gets something it can act on.
            _log.Error(e, "unhandled failure in the {Stage} stage", session.State.Stage);
            return Error($"the round failed: {e.Message} (see the coai-mcp log for the stack)");
        }
    }

    /// <summary>The gate for the stage this session is in — the only way this class reads it.</summary>
    private StageGate StageGate(PersistedSession session) => _settings.Rounds.For(session.State.Stage);

    private string ModelOf(string provider) =>
        _settings.Providers.FirstOrDefault(p => p.Provider == provider)?.Model ?? string.Empty;

    /// <summary>
    /// The clause that says a document left this machine — or nothing, when it did not.
    /// </summary>
    /// <remarks>
    /// <para>Plan 4 confined a document to the repository the session was opened for and called that
    /// a security boundary rather than a tidiness one. Plan 5 lets it travel, to a box the company
    /// runs, on a subscription the company shares, and only where somebody ticked for it — all of
    /// which is fine, and none of which is visible in a round that reports three reviewers having
    /// answered. So it is said, once, in the reply the person asking is already reading.</para>
    /// <para><b>Built from the work that was ACTUALLY assembled, never from the settings.</b> The
    /// settings are the easy list and the wrong one: a Team server can be configured, ticked, and
    /// still carry nothing — no credential, a role that server does not run, a deal that fell
    /// elsewhere — and telling somebody their document reached a box it never reached is worse than
    /// silence, because it is the one claim here they cannot check.</para>
    /// <para><b>Distinct servers, not one.</b> Two vendors can sit on two different Team servers, and
    /// naming one of them is hiding the other. (gemini, plan 5's plan round.)</para>
    /// <para>Only for a DOCUMENT round. A diff going to a Team server is what a Team server is; a
    /// clause on every round is a clause nobody reads by the third one.</para>
    /// </remarks>
    /// <param name="carriers">The provider ids that were given work — one per reviewer, duplicates and all.</param>
    internal string WhereTheDocumentWent(Stage stage, IEnumerable<string> carriers)
    {
        if (stage != Stage.DocumentReview)
        {
            return string.Empty;
        }

        var servers = carriers
            .Select(id => _settings.Providers.FirstOrDefault(
                p => string.Equals(p.Provider, id, StringComparison.OrdinalIgnoreCase)))
            .Where(p => p is { IsRemote: true, BaseUrl.Length: > 0 })
            .Select(p => TeamServerAuth.Normalise(p!.BaseUrl))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // A NEWLINE rather than a space. This is a second statement, not a continuation of the
        // reviewer count, and joined by a space the two read as one run-on — a client rendering the
        // field compactly gets a server URL glued to the last reviewer's name. It stays on this
        // field deliberately, so an AI never told to look for a new one still sees it. (gemini, the
        // code round.)
        return servers.Count == 0
            ? string.Empty
            : $"{Environment.NewLine}The document was also sent to {string.Join(", ", servers)}, "
                + "where it is reviewed on the team's shared subscription rather than on this machine.";
    }

    /// <summary>
    /// A <c>call_human</c> verdict reaches the PERSON, not only the AI that asked.
    /// </summary>
    /// <remarks>
    /// The verdict is an instruction to the caller, and the caller decides what to do with it —
    /// so a gate that had exhausted its rounds could be answered by an AI writing a paragraph in
    /// a chat window while the panel stayed empty all day. That is what happened. The notice is
    /// the same shape as any escalation, so it shows up where a person is already looking and can
    /// be answered there; it does not block, because the round has already returned.
    /// </remarks>
    private void NotifyIfAPersonMustDecide(RoundVerdict verdict, PersistedSession session, ImmutableArray<Finding> merged)
    {
        if (verdict is not RoundVerdict.CallHuman human)
        {
            return;
        }

        _escalations.Notify(new EscalationQuestion(
            Guid.NewGuid().ToString("N")[..12],
            session.State.SessionId,
            session.State.RepoPath,
            session.State.Branch,
            $"The {RoundSubject.StageName(session.State.Stage.ToString())} gate needs your decision: {human.Reason}. " +
            "Proceed anyway, or fix the findings and review again?",
            string.Empty,
            "en",
            string.Empty,
            [.. merged.Where(f => f.IsGating)],
            DateTime.UtcNow.ToString("O")));
        _log.Information("a person was asked to decide: {Reason}", human.Reason);
    }

    /// <summary>
    /// How long this round may take: what was configured, or what its shape earns.
    /// </summary>
    /// <remarks>
    /// The derivation is the default because the honest number is not a constant — see
    /// <see cref="RoundBudget"/>. Shipping a fixed five or thirty minutes would cancel healthy
    /// rounds on a machine with more vendors than the person who chose the number had.
    /// </remarks>
    /// <summary>
    /// The round's own budget when the DEADLINE ended the round, and null for every other ending.
    /// </summary>
    /// <remarks>
    /// <para>Read from the timer's own token rather than from the linked one, so the answer comes
    /// from the thing that fired instead of a guess between two.</para>
    /// <para>Where both fired it resolves toward the PERSON deliberately: they were going to end the
    /// round either way, and blaming the clock for their decision is the wrong half of an ambiguity
    /// to keep. Two reviewers raised the race; this is which side of it to land on.</para>
    /// <para>The WHOLE budget, not what was left of it after setup — "the round reached its 30
    /// minute limit" when the limit is forty is a sentence nobody can act on.</para>
    /// </remarks>
    private static TimeSpan? WhatEndedIt(CancellationToken deadline, CancellationToken caller, TimeSpan budget) =>
        deadline.IsCancellationRequested && !caller.IsCancellationRequested ? budget : null;

    /// <summary>How many reviewers this stage is CONFIGURED to run, before any work is built.</summary>
    /// <remarks>
    /// The same arithmetic the panel shows: vendors that serve this stage, times the roles it
    /// schedules. It is an upper bound on the real fan-out — a repository with no written rules
    /// loses its Conventions reviewers, and dealing sends each lens to one vendor — and an upper
    /// bound is the right direction for a deadline to be wrong in.
    /// </remarks>
    private int ConfiguredReviewers(Stage stage, int rolesPerVendor) =>
        _settings.Providers.Count(p => p.Serves(stage)) * rolesPerVendor;

    private TimeSpan RoundDeadlineFor(int reviewers)
    {
        // `Expressible` on the explicit path too: a `CancellationTokenSource` takes an int of
        // milliseconds and refuses anything past about 24.8 days, so 80,000 minutes would have
        // thrown before a reviewer started. A number the timer cannot hold is not a longer deadline,
        // it is no deadline at all. Raised on the code round.
        var whole = RoundBudget.Expressible(_settings.RoundTimeout > TimeSpan.Zero
            ? _settings.RoundTimeout
            : RoundBudget.For(_settings.ReviewerTimeout, reviewers, _settings.GlobalConcurrency));

        // An explicit setting below one reviewer's own deadline cannot be honoured without
        // cancelling a reviewer that has not finished its FIRST attempt. Said out loud rather than
        // silently obeyed: a person who set five minutes against a ten-minute reviewer has made a
        // configuration mistake, and the round that follows would look like a bug in the gate.
        if (whole < _settings.ReviewerTimeout)
        {
            _log.Warning(
                "the round limit of {Limit:0} minute(s) is shorter than one reviewer's own "
                + "{Reviewer:0} — reviewers will be cancelled before they can finish",
                whole.TotalMinutes, _settings.ReviewerTimeout.TotalMinutes);
        }

        // NOT floored at a reviewer's own deadline. The first draft floored everything, which
        // quietly turned an explicit five minutes into ten while warning that reviewers would be cut
        // off: a setting ignored and a warning that lied about the same number. Two reviewers caught
        // it. The derived path has its floor already, inside `RoundBudget`, where it belongs — a
        // DERIVATION should never produce a budget too small to finish one reviewer, while a person
        // who types a smaller number has said what they want and is told what it costs.
        //
        // Nothing is subtracted here any more either. The timer is armed before the setup now, so
        // the setup spends the budget by simply taking time — which is what a deadline on a ROUND
        // has to mean, and is what subtracting-after-the-fact only approximated.
        return whole;
    }

    /// <param name="stage">
    /// Whose round this is: a <c>revise</c> verdict's instruction ends with what THIS stage does
    /// with accepted findings, read off the stage's own row.
    /// </param>
    private ReviewAnswer AnswerFor(
        Stage stage,
        RoundVerdict verdict,
        GateResult gate,
        ReviewerSummary summary,
        ImmutableArray<Finding> merged,
        List<NormalisedReview> reviews,
        int threshold)
    {
        var rejectedEntries = reviews.SelectMany(r => r.Rejected).Select(r => $"entry {r.Index}: {r.Reason}").ToList();
        var (name, step, instruction) = verdict switch
        {
            RoundVerdict.Proceed => ("proceed", (string?)null,
                "The gate passed. Record a decision for EVERY finding via resolve (rejections need reasons); the next stage opens after that."),
            RoundVerdict.Revise r => ("revise", null,
                $"Findings gate. Resolve every finding with accept/reject + reason, {Stages.Of(stage).ReviseInstruction} ({r.RoundsLeft} round(s) left)."),
            RoundVerdict.ContinueAnyway => ("continue_anyway", null,
                "Rounds exhausted; policy says proceed as-is. Record decisions via resolve and say in your summary that findings remain."),
            RoundVerdict.GoodEnough => ("good_enough", null,
                "Rounds exhausted; policy says good enough. READ the open findings and apply the ones " +
                "that are true and useful — that is the point of this setting, and it is what makes it " +
                "different from continue_anyway. Reject the rest with reasons via resolve, say in your " +
                "summary what you took and what you declined, then proceed."),
            RoundVerdict.CallHuman h => ("call_human", null,
                $"Rounds exhausted: {h.Reason}. A human decides — surface the open findings to them; do not proceed on your own."),
            RoundVerdict.Escalated e => ("escalated", e.Step.ToString(),
                $"Rounds exhausted; the ladder fires {e.Step}. Resolve the findings, apply the step, and run a fresh round."),
            // A verdict with no case here used to return ("unknown", null, "") — a name with no
            // instruction behind it, which is the same silence a button wired to nothing produces.
            // The union is closed, so reaching this is a programming error; the outer catch turns it
            // into a reported sentence rather than a crash, and the test below makes it not happen.
            _ => throw new InvalidOperationException($"no instruction for verdict {verdict.GetType().Name}"),
        };
        return new ReviewAnswer(
            name, step, gate.GatingCount, threshold, summary.Sentence,
            [.. merged], [.. gate.Discounted], rejectedEntries, instruction);
    }

    /// <summary>
    /// The caller's own work in the stretch this round closes.
    /// </summary>
    /// <remarks>
    /// <para>From the end of the PREVIOUS round to the start of this one — the operator's own
    /// framing: the session opened at 13:00 and the plan review ran at 13:39, so that stretch is the
    /// plan round's; the code was written between 13:39 and 15:03, so that stretch is the code
    /// round's. A first round is bounded by when the session was opened.</para>
    /// <para>Best-effort like everything else here, and read from the CLI's own transcripts, which
    /// this server only ever reads.</para>
    /// </remarks>
    private string WhatTheCallerWasDoing(PersistedSession session, RoundRecord record)
    {
        var previous = session.Rounds.Count > 0 ? session.Rounds[^1].CompletedUtc : DateTime.MinValue;
        var from = previous > session.OpenedUtc ? previous : session.OpenedUtc;
        var transcripts = _settings.AgentLogDir.Length > 0 ? _settings.AgentLogDir : Store.AgentLog.DefaultProjectsDir;

        return from == DateTime.MinValue
            ? string.Empty
            : Store.AgentLog.Slice(transcripts, from, record.StartedUtc, session.State.RepoPath);
    }

    /// <summary>
    /// A refusal, through the one boundary every refusal leaves by — <see cref="Refusal"/> — with
    /// the member that refused as its subject, exactly as the service's own helper does.
    /// </summary>
    private string Error(string sentence, [CallerMemberName] string from = "") =>
        Refusal.Answer(sentence, _noticing, from);
}
