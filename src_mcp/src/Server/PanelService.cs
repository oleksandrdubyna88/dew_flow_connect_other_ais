using System.Collections.Immutable;
using System.Text.Json;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Worktrees;

namespace CoaiMcp.Server;

/// <summary>
/// The orchestrator behind every tool: sessions, fan-outs, verdicts. All RULES live in the core;
/// this class carries data between the wire, the runners and the state machine — and answers
/// every failure as a sentence in JSON, never as an exception up the stdio stack.
/// </summary>
public sealed class PanelService
{
    private readonly PanelSettings _settings;
    private readonly VaultKeys _keys;
    private readonly DateTime _vaultReadUtc;
    private readonly IProcessLauncher _launcher;
    private readonly Serilog.ILogger _log;
    private readonly SessionStore _store;
    private readonly WorktreeManager _worktrees;
    private readonly ContextAssembler _context;
    private readonly BoundedScheduler _scheduler;
    private readonly ReviewerExecutor _executor;
    private readonly RolePrompts _prompts;
    private readonly Escalations _escalations;
    private readonly UsageLedger _ledger;
    private readonly Runners.Processes.ProcessTracking _tracking;

    public PanelService(PanelSettings settings, VaultKeys keys, DateTime vaultReadUtc, IProcessLauncher launcher, Serilog.ILogger log)
    {
        _settings = settings;
        _keys = keys;
        _vaultReadUtc = vaultReadUtc;
        _launcher = launcher;
        _log = log;
        _store = new SessionStore(settings.DataDir);
        _worktrees = new WorktreeManager(launcher, Path.Combine(settings.DataDir, "worktrees"));
        _context = new ContextAssembler(launcher);
        _scheduler = new BoundedScheduler(
            settings.GlobalConcurrency,
            settings.PerProviderConcurrency,
            settings.RateLimitBackoff,
            settings.LocalConcurrency);
        // Unparseable answers are kept beside the sessions, so "it would not parse" can be read
        // rather than guessed at.
        _executor = new ReviewerExecutor(launcher, Path.Combine(settings.DataDir, "unparseable"));
        _prompts = new RolePrompts(settings.DataDir);
        _escalations = new Escalations(settings.DataDir);
        _ledger = new UsageLedger(settings.DataDir);

        // Rounds this server never finished cannot be running any more, whatever their file says.
        // A round left at "running" would sit in the panel forever; sweeping only rounds whose
        // recorded process is gone keeps a SECOND server sharing this directory out of the way.
        var swept = _store.SweepOrphanedRounds(ProcessIsAlive);
        if (swept > 0)
        {
            _log.Warning("swept {Count} round(s) abandoned by a dead process", swept);
        }

        // And the reviewers those rounds left RUNNING, which is the more expensive half of the
        // same failure. The timeout kill is performed by the parent, so a server that dies takes
        // no reviewers with it: reported from a macOS checkout, an Antigravity child started at
        // 00:03 was still alive at 10:00, hours after its round, its vendor removed from the
        // configuration, and its server long gone. A leaked directory costs disk; a leaked
        // reviewer holds a rate limit, a GPU, or a paid token budget.
        //
        // Startup is the right moment: a previous server's orphans are lying around exactly then,
        // and this process has no children of its own yet, so there is nothing of ours to get
        // wrong. What protects a SECOND live server's reviewers is `OrphanSweep`, not the timing.
        _tracking = new Runners.Processes.ProcessTracking(
            settings.DataDir,
            message => _log.Warning("process tracking: {Detail}", message));
        var killed = _tracking.Sweep();
        if (killed > 0)
        {
            _log.Warning("killed {Count} reviewer(s) left running by a server that is gone", killed);
        }
    }

    private static bool ProcessIsAlive(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// The settings this instance was built from — how a caller checks WHICH configuration is
    /// actually serving a call, now that <see cref="PanelServiceHost"/> can swap the instance
    /// underneath the tools when the panel rewrites its file.
    /// </summary>
    public PanelSettings Settings => _settings;

    private static readonly ImmutableArray<ReviewRole> CodeRoles =
        [ReviewRole.Architecture, ReviewRole.SecurityReliability, ReviewRole.UxDxPerformance];

    // ---------- providers ----------

    public async Task<string> ProvidersAsync(CancellationToken ct = default)
    {
        var statuses = new List<ProviderStatus>();
        foreach (var provider in _settings.Providers)
        {
            statuses.Add(await ProbeAsync(provider, ct));
        }

        return Json(new ProvidersAnswer(
            statuses,
            _vaultReadUtc == default ? "never" : _vaultReadUtc.ToString("O"),
            _keys.Available ? $"{_keys.Keys.Count} vendor key(s) loaded" : _keys.Unavailability,
                _settings.Unrecognised),
            ServerJsonContext.Default.ProvidersAnswer);
    }

    private async Task<ProviderStatus> ProbeAsync(ProviderSettings provider, CancellationToken ct)
    {
        if (RuntimeFor(provider) is null)
        {
            return new ProviderStatus(provider.Provider, provider.Enabled, false, "",
                "unavailable", ReviewerRuntimeSelector.Default.RefusalFor(provider.Provider));
        }

        // A retired runtime is answered before the probe, not by it: `gemini --version` exits 0
        // without ever reaching Google, so a probe built on --version is structurally incapable of
        // seeing the retirement and reported "own auth" for a vendor that could not sign in at all.
        if (VendorDiagnosis.ForRuntime(RuntimeNameOf(provider)) is { } retired)
        {
            return new ProviderStatus(provider.Provider, provider.Enabled, false, "", "unavailable", retired);
        }

        // A local engine is not a CLI: there is nothing to run `--version` on, and the version
        // that matters is the ENGINE's, which the panel already probes over HTTP. Answering here
        // rather than falling through avoids a probe that would report a working endpoint as a
        // missing binary — the shape of a defect this file has already had once.
        if (RuntimeNameOf(provider) == "local")
        {
            var endpoint = provider.BaseUrl.Length > 0 ? provider.BaseUrl : LocalRuntime.DefaultEndpoint;

            return new ProviderStatus(provider.Provider, provider.Enabled, true, "", "own auth",
                $"a local engine at {endpoint} — no CLI, no key, no bill");
        }

        var (auth, authNote) = AuthFor(provider);
        if (!provider.Enabled)
        {
            return new ProviderStatus(provider.Provider, false, false, "", auth, "disabled in settings");
        }

        var exe = provider.ExecutablePath.Length > 0
            ? provider.ExecutablePath
            : RuntimeFor(provider)?.DefaultExecutable ?? provider.Provider;
        try
        {
            var result = await _launcher.RunAsync(
                new ProcessRequest(exe, ["--version"], Environment.CurrentDirectory) { Timeout = TimeSpan.FromSeconds(30) },
                ct);
            if (result.ExitCode == 0)
            {
                return new ProviderStatus(provider.Provider, true, true, result.StdOut.Trim(), auth, authNote);
            }

            // `providers` is the health probe a person reads before trusting a panel, so a known
            // closed door is named here too rather than only when a round has already failed.
            var cure = VendorDiagnosis.For(result.StdErr + result.StdOut);
            return new ProviderStatus(provider.Provider, true, true, "", auth,
                cure ?? $"--version exited {result.ExitCode}: {result.StdErr.Trim()}");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // A missing CLI is the one failure with a one-line answer, so the answer goes here
            // rather than on a vendor's docs page. This used to be a blanket "antigravity has no
            // Linux CLI" door in VendorDiagnosis.ForRuntime, which fired BEFORE this probe and so
            // told a machine with a working `agy` that it had none.
            var install = VendorDiagnosis.InstallCure(RuntimeNameOf(provider));
            var note = install is null
                ? $"'{exe}' was not found on this machine"
                : $"'{exe}' was not found on this machine — {install}";
            return new ProviderStatus(provider.Provider, true, false, "", auth, note);
        }
    }

    private (string Auth, string Note) AuthFor(ProviderSettings provider) =>
        AuthOf(provider, _keys.Keys.ContainsKey(provider.Provider));

    /// <summary>
    /// How a vendor authenticates — and therefore whether it can run at all.
    /// </summary>
    /// <remarks>
    /// <para><b>An "unavailable" answer removes the vendor from the round</b> (`BuildWork` keeps
    /// only what can run), so this is not a label for the panel. It decides who reviews.</para>
    /// <para><b>Local is answered through <see cref="RuntimeNameOf"/>, not by re-reading the base
    /// URL.</b> That is the fix for a real defect: a local vendor IS a vendor with a base URL, and
    /// this was the one of three readers of those fields that had never been told. It concluded a
    /// local engine needed a vault key, answered unavailable, and every round opened with zero
    /// reviewers — while `providers`, which has its own local arm, went on reporting the vendor as
    /// fine. Three copies of one decision is what allowed two of them to be right.</para>
    /// <para>Pure and internal so the decision is a unit test rather than a live round: the round
    /// that would have caught it needs a model, a machine and four minutes.</para>
    /// </remarks>
    internal static (string Auth, string Note) AuthOf(ProviderSettings provider, bool hasVaultKey) =>
        hasVaultKey
            ? ("vault key", "")
            : RuntimeNameOf(provider) == "local"
                ? ("own auth", "a local engine needs no key — it is reached over HTTP on this machine")
                : provider.BaseUrl.Length > 0 || provider.Provider is "deepseek"
                    ? ("unavailable", $"needs a key under '{provider.Provider}' and the vault holds none — see the creds config entry")
                    : ("own auth", "the CLI's own sign-in is used");

    /// <summary>
    /// The runtime for one configured reviewer: a built-in by name, or — when the operator gave it
    /// a base URL — the generic custom one. A vendor added in the panel is DATA, not a release.
    /// </summary>
    /// <summary>Which runtime a vendor actually drives, by the same order the launcher uses.</summary>
    private static string RuntimeNameOf(ProviderSettings provider) =>
        // `local` is checked first for the same reason it is in RuntimeFor: a local vendor IS a
        // vendor with a base url, and the base-url arm means "ride the Codex CLI".
        provider.Runtime == "local" ? "local"
        : provider.BaseUrl.Length > 0 ? "codex"
        : provider.Runtime.Length > 0 ? provider.Runtime
        : provider.Provider;

    internal static IReviewerRuntime? RuntimeFor(ProviderSettings provider) =>
        // Through RuntimeNameOf, which is the ONE place that answers "what is this vendor". It used
        // to ask `provider.Runtime == "local"` here as well, and a third copy of the same question
        // in AuthFor was never updated — so a local reviewer was silently dropped from every round.
        RuntimeNameOf(provider) == "local"
            ? new LocalRuntime(provider.Provider, provider.BaseUrl)
            : provider.BaseUrl.Length > 0
            ? new CustomCodexRuntime(provider.Provider, provider.BaseUrl)
            // An EXPLICIT runtime outranks the id, and that order is the fix for a real defect:
            // the id was consulted first, so a vendor called `claude` worked by accident while
            // `my-claude` — same runtime, different name — silently ran the Codex CLI.
            // The vendor's own id travels with the runtime — see ReviewerRuntimeSelector.Named for
            // what happened when it did not.
            : ReviewerRuntimeSelector.Named(provider.Runtime, provider.Provider)
                ?? ReviewerRuntimeSelector.Default.Find(provider.Provider);

    // ---------- open / status ----------

    public async Task<string> OpenAsync(string repoPath, string branch, CancellationToken ct = default)
    {
        if (!Directory.Exists(repoPath))
        {
            return Error($"'{repoPath}' is not a directory on this machine");
        }

        try
        {
            await _worktrees.ResolveShaAsync(repoPath, branch);
            await _worktrees.PruneOursAsync(repoPath);
        }
        catch (WorktreeException e)
        {
            return Error(e.Message);
        }

        var session = _store.Load(repoPath, branch)
            ?? new PersistedSession(
                new SessionState(Guid.NewGuid().ToString("N")[..8], repoPath, branch, _settings.Rounds),
                []);
        _store.Save(session);
        _log.Information("session {SessionId} open for {Branch}", session.State.SessionId, branch);
        return Json(SessionAnswerFor(session), ServerJsonContext.Default.SessionAnswer);
    }

    public Task<string> StatusAsync(string repoPath, string branch)
    {
        var session = _store.Load(repoPath, branch);
        return Task.FromResult(session is null
            ? Error("no session for this repo+branch — call open first")
            : Json(SessionAnswerFor(session), ServerJsonContext.Default.SessionAnswer));
    }

    // ---------- the two review stages ----------

    /// <summary>
    /// The plan gate — one reviewer per provider, over the document and NOTHING ELSE.
    /// </summary>
    /// <remarks>
    /// <para><b>No worktree here, and that is the fix for a ten-minute plan round.</b> This stage
    /// used to hand each reviewer a full checkout, exactly as the code stage does. Given a
    /// repository and a plan that mentions files, an agentic CLI goes and reads them — measured at
    /// eight minutes and still running, for a 15 KB document. The role is to judge the DOCUMENT;
    /// the repository is what the code stage is for.</para>
    /// <para>What that costs, said plainly: a reviewer can no longer check that a `file.cs:line`
    /// reference in the plan is real. That verification was never in the plan-critique prompt, and
    /// buying it back at an order of magnitude in wall-clock is the wrong trade for a gate anybody
    /// is expected to sit through.</para>
    /// </remarks>
    public Task<string> ReviewPlanAsync(string repoPath, string branch, string planText, CancellationToken ct = default) =>
        // No floor here on purpose. A three-line plan is a BAD plan, and saying so is the reviewers'
        // job — refusing it at the gate does their work for them and takes away the one round that
        // would have told the person why. The floor belongs to the code stage, where the scope has
        // something to be checked against.
        RunStageAsync(repoPath, branch, planText, RoundMachine.BeginPlanRound, needsWorktree: false,
            (session, workingDir, _) => Task.FromResult<IReadOnlyList<ReviewerWork>>(
                BuildWork([ReviewRole.PlanCritique], workingDir, $"## The plan under review\n\n{planText}",
                    session.State.RoundsRunThisStage + 1,
                    seed: StableSeed(session.State.SessionId, session.State.RoundsRunThisStage + 1),
                    planPrompts: _settings.DealPlanLenses ? UnspentPlanLenses(session) : null,
                    deal: _settings.DealPlanLenses)),
            ct);

    /// <summary>
    /// The code gate — three reviewers per provider, over the branch in a read-only tree, and
    /// never over a bare diff.
    /// </summary>
    /// <remarks>
    /// <para>An empty <c>planText</c> used to be accepted in silence. That quietly narrowed every
    /// reviewer's job from "is this what was asked for" to "is this diff reasonable" — two
    /// different questions, and only the first catches a change that solved the wrong problem
    /// well.</para>
    /// <para>The plan stage's own text is reused when the caller sends none: it is the same scope,
    /// it was already agreed by both halves, and asking for it twice is how a caller ends up
    /// sending nothing.</para>
    /// </remarks>
    public Task<string> ReviewCodeAsync(string repoPath, string branch, string baseRef, string planText, CancellationToken ct = default)
    {
        var scope = Scope(repoPath, branch, planText);
        // Only once the stage itself is reachable. "The plan stage has not passed" is the more
        // useful sentence for a caller who skipped it, and telling them to send a scope for a
        // round that could not have run either way sends them to fix the wrong thing.
        if (_store.Load(repoPath, branch) is { State.PlanProceeded: true } && !CodeScope.IsSubstantial(scope))
        {
            // Refused before any worktree, any launcher, any token: nothing has to run to know it.
            return Task.FromResult(Error(CodeScope.Refusal));
        }

        return RunStageAsync(repoPath, branch, scope, RoundMachine.BeginCodeRound, needsWorktree: true,
            async (session, workingDir, sha) =>
            {
                var files = await _context.CollectAsync(repoPath, baseRef, sha, ct: ct);
                var shaped = DiffShaper.Shape(files);
                var bundle = new ReviewBundle(scope, branch, baseRef, sha, shaped);

                // The project's OWN written conventions, read from the worktree so they are the
                // rules as of the commit under review rather than as of this afternoon. Without
                // them a reviewer can call a change well written by its own standards while it
                // breaks four rules the project enforces on its humans — and its silence reads as
                // approval, because a reviewer cannot flag what it was never told.
                var rules = RuleFiles.Collect(workingDir);
                var context =
                    $"## The plan this change implements\n\n{bundle.PlanText}\n\n" +
                    RulesSection(rules) +
                    $"## The change ({bundle.Branch} over {bundle.BaseRef}, at {bundle.Sha})\n\n{bundle.Diff.Text}";
                _log.Information("rules for review: {Count} file(s), {Bytes} bytes, {Omitted} omitted",
                    rules.Files.Count, rules.Bytes, rules.Omitted.Count);
                // Only the roles whose OWN budget reaches this round. The stage counts rounds once
                // and a role stops taking part when its rounds are spent, so architecture can be
                // worth two passes while performance is worth one.
                var round = session.State.RoundsRunThisStage + 1;
                var roles = _settings.Rounds
                    .RolesForRound(Stage.CodeReview, round)
                    .Select(Enum.Parse<ReviewRole>)
                    .ToList();
                _log.Information("round {Round} runs {Count} role(s): {Roles}", round, roles.Count, string.Join(", ", roles));
                return BuildWork(roles, workingDir, context, round, rules.HasRules,
                    seed: StableSeed(session.State.SessionId, round),
                    deal: _settings.DealCodeLenses);
            },
            ct);
    }

    /// <summary>
    /// The plan lenses this session has not spent yet, one for each vendor that can run.
    /// </summary>
    /// <remarks>
    /// <para>The plan role has a universal prompt and two narrow lenses. A round deals one to each
    /// vendor, so two vendors cover the pool in two rounds — instead of both being asked the
    /// universal question while the two lenses go unasked, which is what handing every vendor the
    /// same prompt did.</para>
    /// <para>When the pool is empty the whole list comes back: a fourth round asks the universal
    /// question again rather than nothing at all.</para>
    /// </remarks>
    private IReadOnlyList<string> UnspentPlanLenses(PersistedSession session)
    {
        var all = PromptCatalog.For(PromptCatalog.PlanRole)
            .OrderByDescending(p => p.Universal)
            .Select(p => p.Id)
            .ToList();
        var unspent = all.Where(id => !session.UsedPrompts.Contains(id)).ToList();
        var pool = unspent.Count > 0 ? unspent : all;
        var vendors = _settings.Providers.Count(p => p.Enabled);
        return [.. pool.Take(Math.Max(vendors, 1))];
    }

    /// <summary>
    /// A seed that is the same on every replay of one round, and different for the next.
    /// </summary>
    /// <remarks>
    /// FNV over the session id and the round number, not <c>string.GetHashCode</c>: that one is
    /// randomised per process, so the same round would deal a different hand on a restart and the
    /// audit log would name a seed nobody could reuse.
    /// </remarks>
    internal static int StableSeed(string sessionId, int round)
    {
        var hash = 2166136261u;
        foreach (var c in $"{sessionId}:{round}")
        {
            hash = (hash ^ c) * 16777619u;
        }

        return (int)(hash & 0x7FFFFFFF);
    }

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

    /// <summary>What the caller sent, or what the plan stage already agreed — in that order.</summary>
    private string Scope(string repoPath, string branch, string planText) =>
        planText.Trim().Length > 0 ? planText : _store.Load(repoPath, branch)?.PlanText ?? string.Empty;

    /// <param name="needsWorktree">
    /// Whether the reviewers get a checkout at all. Only the code stage does: a plan reviewer with a
    /// repository in front of it explores it, and a plan is text.
    /// </param>
    private async Task<string> RunStageAsync(
        string repoPath,
        string branch,
        string planText,
        Func<SessionState, Transition> begin,
        bool needsWorktree,
        Func<PersistedSession, string, string, Task<IReadOnlyList<ReviewerWork>>> makeWork,
        CancellationToken ct)
    {
        if (planText.Length == 0)
        {
            return Error("planText is required — a reviewer that cannot see the intent reviews its own guess");
        }

        var session = _store.Load(repoPath, branch);
        if (session is null)
        {
            return Error("no session for this repo+branch — call open first");
        }

        session = ApplyAnyHumanDecision(session);

        if (begin(session.State) is Transition.Refused refused)
        {
            return Error(refused.Sentence);
        }

        try
        {
            var sha = await _worktrees.ResolveShaAsync(repoPath, branch);
            // The plan stage gets an empty scratch directory instead of a checkout — there is
            // nothing there to wander into, which is the point.
            await using var lease = needsWorktree
                ? await _worktrees.AddAsync(repoPath, sha, session.State.SessionId, session.State.RoundsRunThisStage + 1)
                : null;
            using var scratch = needsWorktree ? null : new ScratchDirectory();
            var workingDir = lease?.Path ?? scratch!.Path;
            var work = await makeWork(session, workingDir, sha);

            // The round exists on disk BEFORE the first CLI starts: the panel shows "running" for
            // its whole duration instead of nothing at all, and a crash leaves something to sweep.
            // What the round is about, derived from the plan the caller passed — a file name if
            // they handed a path, its title otherwise. Nobody has to remember to name the work.
            var subject = RoundSubject.From(planText, File.Exists);
            var live = new LiveRound(_store, session, work, subject);
            var audit = new RoundAudit(_log, session.State.Stage.ToString(), session.State.RoundsRunThisStage + 1);
            audit.Opening(work, workingDir, _settings.ReviewerTimeout);
            var results = await _scheduler.RunAllAsync(work, _executor, ct, progress =>
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
            });
            var summary = ReviewerSummaryFactory.From(results);
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
                return Error("the round could not complete — this is a bug, report it");
            }

            var answer = AnswerFor(completed.Verdict, gate, summary, merged, reviews, StageGate(session).Threshold);
            var record = live.Finish(answer.Verdict, gate.GatingCount, summary.Sentence, results);
            // The operator's own switches, read for THIS call: the settings file is stamped and
            // reloaded per tool call, so a box ticked a second ago governs this round.
            var commands = Core.Commands.GateCommands.For(new Core.Commands.CommandContext(
                Autonomous: _settings.Autonomous,
                SplitPlan: _settings.SplitPlan,
                SplitWithFable: _settings.SplitWithFable,
                FableAvailable: FableIsUsable(),
                PlanText: session.PlanText,
                PlanStage: session.State.Stage == Stage.PlanReview));
            answer = answer with
            {
                Cost = new RoundCost(record.TokensIn, record.TokensOut, record.CostUsd),
                Commands = commands.Count == 0 ? null : commands,
                CommandsPreamble = commands.Count == 0 ? null : Core.Commands.GateCommands.Preamble,
            };
            _store.Save(session with
            {
                State = completed.State,
                Rounds = [.. session.Rounds, record],
                Pending = [.. merged],
                // The lenses this round spent, so the next one asks the ones nobody has yet.
                UsedPrompts = [.. session.UsedPrompts.Union(work.Select(w => w.Prompt).Where(p => p.Length > 0))],
                // The scope is kept with the session so the CODE stage has it without the caller
                // sending it twice. Asking for it again is how a caller ends up sending nothing,
                // and a reviewer handed a bare diff answers a different question than the one the
                // gate exists to ask.
                PlanText = planText,
            });
            audit.Closing(answer.Verdict, gate.GatingCount, summary.Sentence, record);
            audit.Findings(merged);
            NotifyIfAPersonMustDecide(completed.Verdict, session, merged);
            return Json(answer, ServerJsonContext.Default.ReviewAnswer);
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

    /// <summary>
    /// Which prompt each role gets THIS round — the panel's choice, the rotation, or the
    /// universal one. Resolved per round rather than per session, which is the whole point:
    /// round two can ask a different question instead of the same one louder.
    /// </summary>
    /// <summary>
    /// Removes yesterday's answer directories.
    /// </summary>
    /// <remarks>
    /// Each round takes a temp directory for the vendors' `-o` files and nothing ever removed it:
    /// an audit of this machine found 1384 of them, each holding review answers on disk with no
    /// expiry. Worktrees and the plan scratch were already leased properly; this was the one that
    /// leaked. Pruned on the way IN rather than in a finally, so a killed round is cleaned up by
    /// the next one instead of never.
    /// </remarks>
    /// <summary>
    /// Every temp directory a round creates, so the sweep cannot know one of them and miss two.
    /// </summary>
    /// <remarks>
    /// The answers directory was the one that leaked visibly (1384 of them on this machine) and so
    /// the only one the sweep knew. A round takes two more empty ones: the repair launch always,
    /// and — since Fast became the default — the review launch as well, which is now every code
    /// round rather than an opt-in. Empty directories are cheap; an unbounded count of them is not.
    /// </remarks>
    private static readonly string[] ScratchPrefixes =
        ["coai-answers-*", "coai-repair-*", "coai-noworkspace-*"];

    private static void PruneOldAnswerDirs() =>
        PruneOldScratchDirs(Path.GetTempPath(), DateTime.UtcNow.AddHours(-6));

    /// <summary>Removes this product's leftover scratch directories created before <paramref name="cutoff"/>.</summary>
    internal static void PruneOldScratchDirs(string tempRoot, DateTime cutoff)
    {
        try
        {
            foreach (var dir in ScratchPrefixes.SelectMany(p => Directory.EnumerateDirectories(tempRoot, p)))
            {
                try
                {
                    if (Directory.GetCreationTimeUtc(dir) < cutoff)
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                }
                catch (IOException)
                {
                    // In use by another server, or gone already. Either is fine.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    /// <summary>The gate for the stage this session is in — the only way this class reads it.</summary>
    private StageGate StageGate(PersistedSession session) => _settings.Rounds.For(session.State.Stage);

    private string ModelOf(string provider) =>
        _settings.Providers.FirstOrDefault(p => p.Provider == provider)?.Model ?? string.Empty;

    private PromptChoice ChoiceFor(ReviewRole role, int round, bool hasRules) =>
        PromptCatalog.ForRound(
            role.ToString(),
            round,
            _settings.PromptsPerRound.GetValueOrDefault(role.ToString(), []),
            hasRules);

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
    /// The rules block, or a sentence saying there is none.
    /// </summary>
    /// <remarks>
    /// Said out loud either way. A conventions reviewer handed nothing would judge against its own
    /// taste and report the result as compliance, which is the one answer this pass must not give.
    /// </remarks>
    private static string RulesSection(RuleBundle rules) =>
        rules.HasRules
            ? $"## The rules this project has written down\n\n{rules.Render()}\n"
            : "## The rules this project has written down\n\nThis repository has none " +
              "(no CLAUDE.md, AGENTS.md, GEMINI.md or .claude/rules). Do not invent a standard: " +
              "a conventions finding needs a rule to quote.\n\n";

    /// <summary>
    /// The round's work: one item per (role, prompt), DEALT across the vendors.
    /// </summary>
    /// <remarks>
    /// <para>Every vendor used to run every role's prompt — two vendors answering the same
    /// question, with the dedup merging what they agreed on. Dealing them out asks every lens once
    /// instead, at half the launches, and gives up cross-vendor agreement to do it. The trade is
    /// written out in <see cref="PromptDeal"/>.</para>
    /// <para>With ONE vendor the deal is the identity, and this is exactly what it always was.</para>
    /// </remarks>
    /// <remarks>Internal so a test can read the working directory a reviewer is actually given.</remarks>
    internal IReadOnlyList<ReviewerWork> BuildWork(
        IReadOnlyList<ReviewRole> roles,
        string worktreePath,
        string context,
        int round,
        bool hasRules = false,
        int seed = 0,
        IReadOnlyList<string>? planPrompts = null,
        bool deal = false)
    {
        var schemaFile = Path.Combine(_settings.DataDir, "finding-schema.json");
        Directory.CreateDirectory(_settings.DataDir);
        File.WriteAllText(schemaFile, FindingSchema.Json);
        var outputDir = Directory.CreateTempSubdirectory("coai-answers-").FullName;
        PruneOldAnswerDirs();

        // The REPAIR launch gets no workspace, whatever the stage. It is not asking for a better
        // review — it already asked for that — it is asking for the answer in the schema, and an
        // agentic CLI handed a checkout goes exploring instead. That is the same lesson the plan
        // stage learned the hard way, applied to the one launch whose whole job is to be brief.
        var repairDir = Directory.CreateTempSubdirectory("coai-repair-").FullName;

        // And the REVIEW launch can be given the same treatment on request. The prompt is identical
        // either way — the diff came from the repository and the rules from the worktree, both
        // above — so `none` removes only the exploring. It exists because the exploring is what
        // makes a hosted CLI cost 200k input tokens where a local reviewer costs 25k, which is a
        // difference in the QUESTION rather than in the models being compared.
        var launchDir = _settings.CodeWorkspace == "none" && planPrompts is null or { Count: 0 }
            ? Directory.CreateTempSubdirectory("coai-noworkspace-").FullName
            : worktreePath;

        // Only what can actually run: a vendor whose CLI is missing or whose key is absent is
        // reported by `providers` and left out of the deal rather than dealt work it cannot do.
        var runnable = _settings.Providers
            .Where(p => p.Enabled)
            .Where(p => RuntimeFor(p) is not null && AuthFor(p).Auth != "unavailable")
            .ToList();
        if (runnable.Count == 0)
        {
            return [];
        }

        // The items: one per role for a code round, or one per unspent lens for a plan round.
        var items = planPrompts is { Count: > 0 }
            ? planPrompts.Select(id => (Role: roles[0], PromptId: id)).ToList()
            : roles.Select(role => (Role: role, PromptId: ChoiceFor(role, round, hasRules).Id)).ToList();

        var work = new List<ReviewerWork>();
        if (!deal)
        {
            // The shipped behaviour: every vendor answers every question, so two vendors agreeing on
            // a finding is a fact the gate can use. Dealing is opt-in precisely because it gives
            // that up.
            foreach (var provider in runnable)
            {
                foreach (var item in items)
                {
                    Add(provider, item.Role, item.PromptId);
                }
            }

            return work;
        }

        foreach (var hand in PromptDeal.Deal(
            [.. items.Select(i => $"{i.Role}|{i.PromptId}")],
            [.. runnable.Select(p => p.Provider)],
            seed))
        {
            var parts = hand.Item.Split('|', 2);
            Add(runnable.First(p => p.Provider == hand.Vendor), Enum.Parse<ReviewRole>(parts[0]), parts[1]);
        }

        return work;

        void Add(ProviderSettings provider, ReviewRole role, string promptId)
        {
            var choice = PromptCatalog.ById(promptId) ?? PromptCatalog.UniversalFor(role.ToString());
            if (RuntimeFor(provider) is not { } runtime)
            {
                return;
            }

            var settings = new ReviewerSettings(provider.Provider)
            {
                ExecutablePath = provider.ExecutablePath,
                Model = provider.Model,
                ApiKey = _keys.Keys.GetValueOrDefault(provider.Provider, string.Empty),
                Timeout = _settings.ReviewerTimeout,
                ReasoningEffort = _settings.LocalReasoningEffort,
                MaxTokens = _settings.LocalMaxTokens,
            };
            var prompt = ComposePrompt(choice, context);
            var repairPrompt = prompt +
                "\n\nYOUR PREVIOUS ANSWER WAS NOT VALID JSON. Return ONLY the JSON object for the schema — no fences, no prose.";
            work.Add(new ReviewerWork(
                runtime.Build(role, prompt, launchDir, schemaFile, outputDir, settings),
                runtime.Build(role, repairPrompt, repairDir, schemaFile, outputDir, settings),
                choice.Id));
        }
    }

    /// <summary>
    /// Whether a Fable reviewer is here AND usable — never merely configured.
    /// </summary>
    /// <remarks>
    /// An instruction to switch to a model this machine has not got is an instruction that stops the
    /// work. A vendor that is disabled, or whose CLI the health probe cannot find, is not available,
    /// which is the distinction this change's plan round asked for by name.
    /// </remarks>
    private bool FableIsUsable() =>
        _settings.Providers.Any(p =>
            p.Enabled
            && (p.Provider.Contains("fable", StringComparison.OrdinalIgnoreCase)
                || p.Model.Contains("fable", StringComparison.OrdinalIgnoreCase)
                || p.Runtime.Contains("fable", StringComparison.OrdinalIgnoreCase)));

    private string ComposePrompt(PromptChoice choice, string context) =>
        $"{_prompts.ForChoice(choice)}\n\n## The finding contract\n\nReturn ONLY a JSON object matching this schema — no fences, no prose:\n\n{FindingSchema.Json}\n\n{context}";

    private ReviewAnswer AnswerFor(
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
                $"Findings gate. Resolve every finding with accept/reject + reason, fix the accepted ones, then run this review again ({r.RoundsLeft} round(s) left)."),
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

    // ---------- resolve ----------

    public Task<string> ResolveAsync(string repoPath, string branch, string decisionsJson, bool humanSaysProceed = false)
    {
        var session = _store.Load(repoPath, branch);
        if (session is null)
        {
            return Task.FromResult(Error("no session for this repo+branch — call open first"));
        }

        List<DecisionDto>? dtos;
        try
        {
            dtos = JsonSerializer.Deserialize(decisionsJson, ServerJsonContext.Default.ListDecisionDto);
        }
        catch (JsonException e)
        {
            return Task.FromResult(Error($"decisions is not valid JSON: {e.Message}"));
        }

        if (dtos is null or [])
        {
            return Task.FromResult(session.Pending.Count == 0
                ? Finish(WithHumanDecision(session), [], humanSaysProceed)
                : Error($"{session.Pending.Count} finding(s) await a decision — pass one per finding index"));
        }

        var decisions = new List<Decision>();
        foreach (var dto in dtos)
        {
            if (dto.Finding < 0 || dto.Finding >= session.Pending.Count)
            {
                return Task.FromResult(Error($"finding index {dto.Finding} does not exist — this round reported {session.Pending.Count}"));
            }

            var finding = session.Pending[dto.Finding];
            var action = dto.Action.ToLowerInvariant();
            if (action is not ("accept" or "reject"))
            {
                return Task.FromResult(Error($"action '{dto.Action}' is neither accept nor reject"));
            }

            decisions.Add(action == "accept"
                ? new Decision.Accepted(finding)
                : new Decision.Rejected(finding, dto.Reason));
        }

        return Task.FromResult(Finish(WithHumanDecision(session), decisions, humanSaysProceed));
    }

    /// <summary>
    /// Applies what the PERSON pressed on a <c>call_human</c> notice, if they pressed anything.
    /// </summary>
    /// <remarks>
    /// <para>Two of the three buttons mean "the stage is not finished, carry on": another set of
    /// rounds either way, differing only in whether the AI changes something first. So both reset
    /// the stage's round count — which is the whole unblocking, and it is the person's doing, not
    /// the AI's.</para>
    /// <para>None of the three advances a stage over open findings. A human override that means
    /// "ignore all this" would be an off switch on the gate, and it is deliberately not offered.
    /// <c>Discuss</c> leaves the session exactly where it is: the AI is meant to stop and talk.</para>
    /// </remarks>
    private PersistedSession WithHumanDecision(PersistedSession session)
    {
        var decision = _escalations.DecisionFor(session.State.SessionId);
        if (decision is not (HumanDecision.Continue or HumanDecision.Fix))
        {
            return session;
        }

        _log.Information("the person chose {Decision}; the stage gets a fresh set of rounds", decision);
        return session with { State = session.State with { RoundsRunThisStage = 0 } };
    }

    private string Finish(PersistedSession session, List<Decision> decisions, bool humanSaysProceed = false)
    {
        switch (RoundMachine.Resolve(session.State, decisions, humanSaysProceed))
        {
            case Transition.Refused refused:
                return Error(refused.Sentence);
            case Transition.Moved moved:
                _store.Save(session with { State = moved.State, Pending = [] });
                var instruction = moved.State switch
                {
                    { Stage: Stage.CodeReview, RoundsRunThisStage: 0 } when session.State.Stage == Stage.PlanReview =>
                        "The plan stage is complete. Implement the plan on the branch, then call review_code.",
                    { Stage: Stage.Done } => "The code stage is complete. This session is done.",
                    _ => "Decisions recorded. Apply the accepted findings, then run the review again.",
                };
                return Json(new ResolveAnswer(moved.State.Stage.ToString(), moved.State.AwaitingResolve, decisions.Count, instruction),
                    ServerJsonContext.Default.ResolveAnswer);
            default:
                return Error("unexpected transition — this is a bug, report it");
        }
    }

    // ---------- ask_human ----------

    /// <summary>
    /// Puts the question in front of a person and waits — through the data directory the extension
    /// watches, so no port is opened by either half.
    /// </summary>
    /// <remarks>
    /// The open findings ride with it: a person deciding "ship anyway?" needs to see what is still
    /// gating, and going to look for it elsewhere is how a decision gets made on a summary.
    /// </remarks>
    public async Task<string> AskHumanAsync(string repoPath, string branch, string question, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return Error("a question is required — an empty escalation tells a person nothing");
        }

        var session = _store.Load(repoPath, branch);
        var id = Guid.NewGuid().ToString("N")[..12];

        // English, as the caller wrote it. There used to be a translator here, and a set of
        // buttons replaced the prose it existed for: the question is one fixed sentence and the
        // answer is a choice, so a subprocess per escalation that can time out or answer in the
        // wrong language was a moving part earning nothing.
        var text = question.Trim();
        var asked = new EscalationQuestion(
            id,
            session?.State.SessionId ?? "no-session",
            repoPath,
            branch,
            text,
            text,
            "en",
            string.Empty,
            session?.Pending.Where(f => f.IsGating).ToList() ?? [],
            DateTime.UtcNow.ToString("O"));

        _log.Information("escalating {Id} to a person: {Question}", id, asked.Question);
        var outcome = await _escalations.AskAsync(asked, _settings.EscalationBudget, ct);

        return outcome switch
        {
            // Their own words, unchanged. Nothing stands between the person and the caller now.
            EscalationOutcome.Answered answered => Json(
                AnswerFor(answered.Text), ServerJsonContext.Default.HumanAnswer),

            // The family's `remote-ask` fallback, verbatim in shape: nobody answered in the budget,
            // so ASK IN THE CHAT rather than stalling or deciding alone. The question file stays.
            _ => Json(
                new HumanAnswer(
                    "no_answer_yet",
                    string.Empty,
                    string.Empty,
                    $"nobody answered in {_settings.EscalationBudget.TotalMinutes:0} minutes — ask the person " +
                    $"directly in this conversation and wait for their reply. The question is still open in " +
                    $"VS Code as escalation {id}."),
                ServerJsonContext.Default.HumanAnswer),
        };
    }

    /// <summary>The person's answer, exactly as they gave it.</summary>
    /// <remarks>
    /// It used to be translated back into the language the caller had asked in. The buttons
    /// removed the reason: a choice is not prose, and their free text — when they type any — is
    /// worth more unmediated than rendered into another language by a third model.
    /// </remarks>
    private static HumanAnswer AnswerFor(string answer) => new("answered", answer, answer, string.Empty);


    // ---------- plumbing ----------

    private SessionAnswer SessionAnswerFor(PersistedSession session) => new(
        session.State.SessionId,
        session.State.Stage.ToString(),
        session.State.RoundsRunThisStage,
        session.State.AwaitingResolve,
        session.State.PlanProceeded,
        _settings.Rounds.For(session.State.Stage).Threshold,
        _settings.Rounds.For(session.State.Stage).MaxRounds,
        session.Rounds)
    {
        HumanDecision = _escalations.DecisionFor(session.State.SessionId) switch
        {
            HumanDecision.Continue => "continue",
            HumanDecision.Fix => "fix",
            HumanDecision.Discuss => "discuss",
            _ => string.Empty,
        },
        HumanAnswer = _escalations.AnswerTextFor(session.State.SessionId),
    };

    private static string Json<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type) =>
        JsonSerializer.Serialize(value, type);

    private static string Error(string sentence) =>
        JsonSerializer.Serialize(new ErrorAnswer(sentence), ServerJsonContext.Default.ErrorAnswer);
}
