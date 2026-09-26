using System.Runtime.CompilerServices;
using System.Text.Json;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Worktrees;
using CoaiMcp.Store;

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

    private readonly Noticing _noticing;
    private readonly SessionStore _store;
    private readonly ArtifactStore _artifacts;
    private readonly WorktreeManager _worktrees;
    private readonly ContextAssembler _context;
    private readonly RosterBuilder _roster;
    private readonly RoundCommands _commands;
    private readonly RoundEngine _engine;
    private readonly FeatureStage _feature;
    private readonly Escalations _escalations;
    private readonly Store.Projection _projection;
    private readonly Runners.Processes.ProcessTracking _tracking;
    private readonly RemoteProbe _remote;
    private readonly ConsultationService _consultations;
    private readonly CadenceDesk _cadence;

    public PanelService(
        PanelSettings settings,
        VaultKeys keys,
        DateTime vaultReadUtc,
        IProcessLauncher launcher,
        Serilog.ILogger log,
        Noticing noticing)
    {
        _settings = settings;
        _keys = keys;
        _vaultReadUtc = vaultReadUtc;
        _launcher = launcher;
        _log = log;
        _noticing = noticing;
        _store = new SessionStore(settings.DataDir, settings.Rounds.Catalog);
        _artifacts = new ArtifactStore(settings.DataDir);
        _worktrees = new WorktreeManager(
            launcher,
            settings.WorktreeRoot,
            message => log.Warning("worktrees: {Detail}", message));
        _context = new ContextAssembler(launcher);
        var scheduler = new BoundedScheduler(
            settings.GlobalConcurrency,
            settings.PerProviderConcurrency,
            settings.RateLimitBackoff,
            settings.LocalConcurrency,
            settings.RetryLadder);
        // Unparseable answers are kept beside the sessions, so "it would not parse" can be read
        // rather than guessed at.
        var executor = new ReviewerExecutor(
            launcher,
            Path.Combine(settings.DataDir, "unparseable"),
            // Its own directory beside that one: "it said nothing" and "it said something
            // I could not read" are different questions, and a person chasing one must not
            // have to wade through the other.
            Path.Combine(settings.DataDir, "empty"),
            // A failure to keep evidence is reported rather than swallowed. It cannot fail the
            // round — a review the vendor answered must not become a failure because a disk was
            // full — but an empty evidence directory that says nothing is one a reader later
            // takes as proof that no round was ever silent.
            problem => _log.Warning("evidence: {Problem}", problem));
        var prompts = new RolePrompts(settings.DataDir);
        _escalations = new Escalations(settings.DataDir);
        var ledger = new UsageLedger(settings.DataDir);
        var callers = new CallerSessions(settings.DataDir);
        _projection = new Store.Projection(settings.DataDir, log);
        _consultations = new ConsultationService(
            settings, launcher, executor, _context, prompts, ledger, log,
            Environment.GetEnvironmentVariable, noticing);
        // The consultation cadence (research/PLAN_consult_on_a_cadence.md): its record, its gate over the
        // consultation records, and every decision a round makes about it — kept out of this file.
        _cadence = new CadenceDesk(
            settings,
            new CadenceStore(settings.DataDir),
            new CadenceGate(_consultations.Store, () => _consultations.Preflight()),
            _context,
            new Runners.Collecting.GitHistory(launcher),
            log,
            noticing);

        // One client for every Team server this configuration names. A probe and a cancellation are
        // both short requests to the same handful of hosts, so a shared handler is the whole point.
        _remote = new RemoteProbe(new HttpClient());

        // The roster is dealt from the same settings, keys and prompts, and asks this service only
        // what it also answers for the round's exclusion sentence: can a vendor run, and on what.
        _roster = new RosterBuilder(
            settings, keys, prompts, new ReviewerPrompt(prompts), _remote, CanRun, RuntimeFor, PruneOldAnswerDirs);

        // The orders a round carries, read by the engine for its reply and by the cadence check before a
        // code round; and the engine every stage runs through. The engine is handed what only this service
        // can say — who a stage excludes, and the sentence for a round nobody could serve — and holds no
        // reference back.
        _commands = new RoundCommands(settings, prompts, log);
        _engine = new RoundEngine(
            settings, log, noticing, _store, _worktrees, scheduler, executor, _commands, _escalations, ledger,
            _projection, callers, _remote, ExcludedFrom, NoReviewerRefusal);
        // The fourth gate, in a file of its own (S2.2b): its inputs, its refs and its pack, over the same
        // engine. The outliner is the in-process tree-sitter one the pack is measured with.
        _feature = new FeatureStage(settings, launcher, _engine, _roster, new Normalizer.TreeSitterOutliner(), log, noticing);

        // Rounds this server never finished cannot be running any more, whatever their file says.
        // A round left at "running" would sit in the panel forever; sweeping only rounds whose
        // recorded process is gone keeps a SECOND server sharing this directory out of the way.
        var swept = _store.SweepOrphanedRounds(ProcessIsAlive);
        if (swept > 0)
        {
            _log.Warning("swept {Count} round(s) abandoned by a dead process", swept);
        }

        // The same sweep for consultations: one left `asking` by a dead server, one left open past
        // its idle budget, one finished long ago. Same pid rule, same reason.
        var consultationsSwept = _consultations.Sweep(ProcessIsAlive);
        if (consultationsSwept > 0)
        {
            _log.Warning("swept {Count} consultation(s): interrupted by a dead process, idle past their budget, or expired", consultationsSwept);
        }

        // And the LOG catches up with the records, because the projection is allowed to fail: a
        // database locked or full when a consultation wrote its last state leaves that consultation
        // missing from the log for ever, since a terminal record is never written again. Bounded by
        // the same retention the sweep above applies, and idempotent — each row is recomputed from
        // its record rather than accumulated. (codex, story 4's plan round.)
        var reprojected = _consultations.Reproject();
        _log.Debug("re-projected {Count} consultation(s) into the rounds database", reprojected);

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

    /// <summary>Is the process that owned a running turn still there?</summary>
    /// <remarks>
    /// <para><b>Every failure to ask is NOT ALIVE, and none of them may throw.</b> This is called
    /// from the consultation sweep, which runs on startup — so an exception here does not fail a
    /// sweep, it takes the whole binary down before it has answered anything. Found by the
    /// <c>--close-consult</c> scenario on issue #309: a record left at <c>asking</c> with pid 0,
    /// which is what a torn write or an older build leaves, made Windows answer
    /// <c>Win32Exception (5): Access is denied</c> — pid 0 is the idle process and nobody may open
    /// it. A pid belonging to another user does the same.</para>
    /// <para>A pid this process cannot open is one it cannot vouch for, and the sweep's question is
    /// "may I reclaim this record". Answering "not alive" reclaims it, which is the safe direction:
    /// the alternative is a consultation stuck at <c>asking</c> for ever because nobody could ask.</para>
    /// </remarks>
    private static bool ProcessIsAlive(int pid)
    {
        // A pid nothing could ever own. Windows answers "access denied" for 0 rather than "no such
        // process", so it is refused here rather than through an exception handler.
        if (pid <= 0)
        {
            return false;
        }

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Gone, never existed, or not ours to open. All three mean the same thing to a sweep.
            return false;
        }
    }

    /// <summary>
    /// The settings this instance was built from — how a caller checks WHICH configuration is
    /// actually serving a call, now that <see cref="PanelServiceHost"/> can swap the instance
    /// underneath the tools when the panel rewrites its file.
    /// </summary>
    public PanelSettings Settings => _settings;

    /// <summary>
    /// The roster this service deals its rounds from — internal so a test can build a round's work
    /// without running it.
    /// </summary>
    internal RosterBuilder Roster => _roster;

    /// <summary>
    /// The engine this service's stages run through — internal so a test can ask it a round-time
    /// question without running a round.
    /// </summary>
    internal RoundEngine Engine => _engine;

    /// <summary>Every sentence this round says instead of reviewing — see <see cref="RoundRefusals"/>.</summary>
    /// <remarks>
    /// A delegation rather than a field on the settings, because the catalog is per SESSION: the
    /// panel rewrites its settings file while this server runs, and a refusal built once at
    /// construction would name the roles that were configured at startup.
    /// </remarks>
    private RoundRefusals Refusals => new(_settings.Rounds.Catalog);

    internal string NoCodeRolesRefusal => Refusals.NoCodeRoles;

    internal string NoRolesRefusal(Stage stage) => Refusals.NoRoles(stage);

    internal string NoReviewerRefusal(Stage stage, RoundWork work) => Refusals.NoReviewer(
        stage,
        work.NotAsked.Select(r => $"{r.Role} was not asked: {r.Reason}"),
        work.Excluded.Select(e => $"{e.Role} could not go to {e.Provider}: {e.Reason}"));

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

    /// <summary>
    /// One vendor's health, through the library's probe — the same question the Team server asks of
    /// the same vendors, asked once rather than twice.
    /// </summary>
    private async Task<ProviderStatus> ProbeAsync(ProviderSettings provider, CancellationToken ct)
    {
        var health = await VendorProbe.RunAsync(
            _launcher,
            provider.Identity(),
            provider.Enabled,
            provider.ExecutablePath,
            provider.Model,
            _keys.Keys.ContainsKey(provider.Provider),
            ct,
            // A Team server vendor is asked over HTTP, and only this binary can ask: the token is
            // on this machine. Passed as a delegate so the probe itself stays a process probe.
            remoteHealth: (vendor, enabled, token) =>
                _remote.RunAsync(vendor, enabled, _settings.DataDir, token));

        return new ProviderStatus(
            provider.Provider, health.Enabled, health.CliFound, health.Version, health.Auth, health.Note);
    }

    /// <summary>
    /// How a vendor authenticates, and therefore whether it may review at all.
    /// </summary>
    /// <remarks>
    /// A delegation, and the tests still call it here because this is where they have always called
    /// it. The decision itself lives in <see cref="RuntimeResolution"/> — one answer for both
    /// binaries, after two separate incidents where a second copy of it was the one that was wrong.
    /// </remarks>
    private (string Auth, string Note) AuthFor(ProviderSettings provider) =>
        AuthOf(provider, _keys.Keys.ContainsKey(provider.Provider), HasServerToken(provider));

    /// <param name="hasServerToken">
    /// Whether this machine has signed into the Team server this vendor points at. Only a
    /// <c>remote</c> vendor consults it, and it is a PARAMETER rather than a lookup so this stays
    /// static and pure — which is also why the tests that predate Team servers still call it with
    /// two arguments and still pass unedited.
    /// </param>
    internal static (string Auth, string Note) AuthOf(
        ProviderSettings provider, bool hasVaultKey, bool hasServerToken = false) =>
        RuntimeResolution.AuthOf(provider.Identity(), hasVaultKey, hasServerToken);

    /// <summary>Has this machine signed into the Team server this vendor points at?</summary>
    /// <remarks>
    /// A file check rather than a request: `providers` must answer whether or not a server is
    /// reachable, and "am I signed in" is a fact about this machine that no network call improves.
    /// </remarks>
    internal bool HasServerToken(ProviderSettings provider) =>
        TeamServerAuth.ReadToken(
            TeamServerAuth.TokenPath(_settings.DataDir, provider.BaseUrl)).Length > 0;

    internal static IReviewerRuntime? RuntimeFor(ProviderSettings provider) =>
        RuntimeResolution.For(provider.Identity());

    /// <summary>
    /// Can this vendor actually review — an adapter for its runtime, and a credential that exists?
    /// </summary>
    /// <remarks>
    /// ONE predicate, read from two directions. `BuildWork` asks who to deal work to;
    /// <see cref="ExcludedFrom"/> asks who was left out and why, so that a round can say it. Two
    /// predicates that agree today is how three copies of the runtime decision got away with it
    /// twice in this file's own history.
    /// </remarks>
    private bool CanRun(ProviderSettings provider) =>
        RuntimeFor(provider) is not null && AuthFor(provider).Auth != "unavailable";

    /// <summary>
    /// The reviewers the operator ENABLED for this stage that this round cannot run, each as
    /// <c>name: reason</c>.
    /// </summary>
    /// <remarks>
    /// <para>The stage filter runs FIRST, in both directions: a vendor turned off for plans has not
    /// been lost, and reporting it on every plan round would train a person to ignore the sentence —
    /// which is the one thing it cannot afford, because it exists to be read the once it matters.</para>
    /// <para>The reason is the vendor's own note, never a summary of it. "needs a key under 'x' and
    /// the vault holds none" and "not signed in to the Team server at …" have different cures, and
    /// the whole point of naming the exclusion is that somebody can act on it.</para>
    /// </remarks>
    internal IReadOnlyList<string> ExcludedFrom(Stage stage) =>
        [.. _settings.Providers
            .Where(p => p.Serves(stage))
            .Where(p => !CanRun(p))
            .Select(p => $"{p.Provider}: {ReasonFor(p)}")];

    /// <summary>
    /// Why a reviewer was left out, in words THIS side wrote.
    /// </summary>
    /// <remarks>
    /// Not the vendor's own note, and that is a security decision rather than an editorial one: the
    /// note can carry a remote server's text — <c>RemoteAsk.UnexpectedMessage</c> interpolates a
    /// response body — and this string travels into the round summary that reaches the calling AI. A
    /// remote service must not be able to put instruction-shaped text into an AI's instruction
    /// stream, and a review gate is precisely where that would be worth doing. The full note is
    /// still what <c>providers</c> answers and what the panel's card shows — the surfaces a PERSON
    /// reads. Raised on epic 3's code round.
    /// </remarks>
    private string ReasonFor(ProviderSettings provider) =>
        RuntimeFor(provider) is null
            ? $"no adapter for a runtime called '{provider.Runtime}'"
            : RuntimeResolution.ExclusionReason(
                provider.Identity(), _keys.Keys.ContainsKey(provider.Provider), HasServerToken(provider));

    // ---------- open / status ----------

    /// <param name="callerModel">
    /// The model the CALLING AI says it is running — <c>claude-opus-5</c>, <c>codex-astra</c>.
    /// </param>
    /// <param name="client">
    /// What the client called itself in the MCP handshake, and its version. Passed as plain strings
    /// rather than the SDK's <c>Implementation</c> so this service stays testable without a wire.
    /// </param>
    /// <remarks>
    /// Re-stamped on EVERY open of the same repo and branch, which is the whole reason the model is
    /// declared per call: a <c>/model</c> switch mid-session reaches the next round rather than
    /// leaving the log naming the model somebody stopped using.
    /// </remarks>
    public async Task<string> OpenAsync(
        string repoPath,
        string branch,
        string callerModel = "",
        string client = "",
        string clientVersion = "",
        CancellationToken ct = default)
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
                [])
            {
                // Stamped once, here, and carried by every later save. It bounds the first round's
                // window over the caller's transcript, and the file system cannot be asked for it:
                // a save moves a scratch file over this one.
                OpenedUtc = DateTime.UtcNow,
            };
        session = session with
        {
            Caller = CallerDeclaration.From(CallerIdentity.Current(), client, clientVersion, callerModel),
        };
        _store.Save(session);
        _log.Information(
            "session {SessionId} open for {Branch}, asked by {Caller}",
            session.State.SessionId, branch, session.Caller.Phrase);
        return Json(SessionAnswerFor(session), ServerJsonContext.Default.SessionAnswer);
    }

    /// <param name="document">
    /// Which document's review, when the session is one: the same <c>documentPath</c> or
    /// <c>documentName</c> that was passed to <c>review_document</c>. Empty asks about the BRANCH's
    /// own session, which is what every caller before plan 4 meant and still means.
    /// </param>
    public Task<string> StatusAsync(string repoPath, string branch, string document = "") =>
        StatusAsync(repoPath, branch, document, string.Empty);

    /// <param name="plan">A plan to report the consultation cadence of — or empty for the one this session holds,
    /// if any (<c>research/PLAN_consult_on_a_cadence.md</c>). Read from the plan's own record, so it answers the same
    /// on every branch the plan is built on.</param>
    /// <param name="feature">
    /// Which plan's FEATURE review, when the session is one: the same <c>planPath</c> that was passed to
    /// <c>review_feature</c>. The branch is then not read — a feature session is keyed by its plan.
    /// </param>
    public async Task<string> StatusAsync(string repoPath, string branch, string document, string plan, string feature = "", CancellationToken ct = default)
    {
        if (Both(document, feature) is { Length: > 0 } both)
        {
            return Error(both);
        }

        var at = AddressOf(repoPath, branch, document, feature);
        var session = _store.Load(repoPath, at.Branch, at.Document, at.Feature);
        if (session is null)
        {
            return Error(NoSession(document, feature));
        }

        var cadence = _settings.CadenceMode == Core.Cadence.CadenceMode.Off || document.Length > 0 || feature.Length > 0
            ? null
            : await _cadence.AnswerAsync(session, plan, await _worktrees.ShaOrNoneAsync(repoPath, branch), ct);

        return Json(SessionAnswerFor(session) with { Cadence = cadence }, ServerJsonContext.Default.SessionAnswer);
    }

    /// <summary>
    /// The session identity a caller's <paramref name="document"/> means — the LATEST review of it.
    /// </summary>
    /// <remarks>
    /// <para>A caller passes what they passed to <c>review_document</c>: a path, or the name they
    /// gave raw text. It has to become the SAME identity, which is why the resolution is
    /// <see cref="DocumentReader.IdentityOf"/> and not a second copy of it — a rooted-paths-only,
    /// no-symlinks, wrong-case reconstruction lived here for one code round, and the consequence was
    /// that a caller who passed a relative path could never resolve their own round. Five findings
    /// across two vendors.</para>
    /// <para>The NEWEST review of that document is the one they mean: asking somebody to remember an
    /// ordinal they never typed would be asking them to know how this file numbers things.</para>
    /// </remarks>
    private string DocumentKeyFor(string repoPath, string branch, string document)
    {
        var identity = DocumentReader.IdentityOf(repoPath, document, DocumentReader.FollowLink);

        return identity.Length == 0
            ? string.Empty
            : DocumentSessions.Which(identity, newReview: false, id => _store.Exists(repoPath, branch, id));
    }

    private static string NoSession(string document, string feature = "") =>
        feature.Trim().Length > 0
            ? $"no feature review of '{feature}' in this repository — call review_feature for it first, "
              + "and pass the same planPath here as feature"
            : document.Trim().Length == 0
            ? "no session for this repo+branch — call open first"
            : $"no review of '{document}' on this branch — call review_document for it first. If you "
            + "meant the branch's own session, leave document empty.";

    /// <summary>Which session a <c>resolve</c>, <c>status</c> or <c>ask_human</c> is about: the three key segments it resolves to.</summary>
    private sealed record SessionAddress(string Branch, string Document, string Feature);

    /// <summary>
    /// The session a caller's arguments name — a FEATURE review's when <paramref name="feature"/> is set,
    /// keyed by the plan's identity through the same <see cref="DocumentReader.IdentityOf"/> the review was
    /// opened with (D13), under the constant branch no git ref can spell; otherwise the branch's or a
    /// document's, exactly as before.
    /// </summary>
    private SessionAddress AddressOf(string repoPath, string branch, string document, string feature) =>
        feature.Trim().Length > 0
            ? new SessionAddress(SessionKey.FeatureBranch, string.Empty, DocumentReader.IdentityOf(repoPath, feature, DocumentReader.FollowLink))
            : new SessionAddress(branch, DocumentKeyFor(repoPath, branch, document), string.Empty);

    /// <summary>A document review and a feature review are two different sessions — naming both is naming neither.</summary>
    private static string Both(string document, string feature) =>
        document.Trim().Length > 0 && feature.Trim().Length > 0
            ? "pass document OR feature, not both — a document review and a feature review are separate sessions"
            : string.Empty;
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
        ReviewPlanAsync(repoPath, branch, planText, CadenceArgs.None, ct);

    /// <param name="cadence">What the caller declared about the consultation cadence — the plan, the epic, the
    /// risk answer (<c>research/PLAN_consult_on_a_cadence.md</c>). Checked under the claim; a false declaration is
    /// refused, and the facts shape the orders the reply carries.</param>
    public Task<string> ReviewPlanAsync(string repoPath, string branch, string planText, CadenceArgs cadence, CancellationToken ct = default)
    {
        var trace = new CadenceTrace();
        // The same guard the code stage has had, for the same reason: a round that launches no
        // reviewer is not an empty round, it is an unresolved one, and it sits open for ever. It
        // became reachable here when the plan roster started coming from the catalog.
        if (_settings.Rounds.EnabledRolesOf(Stage.PlanReview).Count == 0)
        {
            _log.Warning("review_plan refused: every plan role is switched off");
            return Task.FromResult(Error(NoRolesRefusal(Stage.PlanReview)));
        }

        // No floor here on purpose. A three-line plan is a BAD plan, and saying so is the reviewers'
        // job — refusing it at the gate does their work for them and takes away the one round that
        // would have told the person why. The floor belongs to the code stage, where the scope has
        // something to be checked against.
        return _engine.RunStageAsync(repoPath, branch, planText, new StageRun(RoundMachine.BeginPlanRound,
            NeedsWorktree: false, Stage: Stage.PlanReview, ReadsCheckout: false,
            (session, workingDir, _) =>
            {
                // The rules a PLAN is judged against, from `repoPath` and never from `workingDir` —
                // this stage runs `NeedsWorktree: false`, so its working directory is an empty
                // scratch folder on purpose, and collecting from it would gather nothing while
                // looking like it worked. `Collect` reads the WORKING TREE, which is the honest
                // input at plan time: the plan under review describes work about to happen in it.
                var rules = RuleFiles.Collect(repoPath, RuleOrder.Staged(StageRules.Plan));

                // The same sentence the code stage writes, carrying this stage's own numbers. The
                // tier COVERAGE is here because this gate reviews other repositories: a target on an
                // older conventions pin may carry only some of the tier, and a round that matched
                // none of it must not read like a round that matched all of it.
                _log.Information(
                    "context for review: plan {PlanBytes} bytes; rules {RulesBytes} bytes, "
                    + "{Matched} of {Tier} tier rule(s), {Omitted} omitted",
                    System.Text.Encoding.UTF8.GetByteCount(planText),
                    rules.Bytes,
                    rules.MatchedCount(StageRules.Plan),
                    StageRules.Plan.Length,
                    rules.Omitted.Count);

                // A plan round skips no role for a RULE's sake: it has one, and there is nothing for
                // a rule file to decide about it. What BuildWork itself could not ask — a role
                // whose prompt has no text — travels on the work it returns.
                //
                // The roster comes from the CATALOG, as the code stage's has since story B1. It was
                // a hardcoded `[PlanRole]` here, so a plan-stage role a person added was composed,
                // given its own budget and its own enable switch — the one the shipped plan role
                // deliberately does not have — and then never asked anything. (gemini, twice on
                // story B2's code round.)
                var round = session.State.RoundsRunThisStage + 1;
                var roles = _settings.Rounds.RolesForRound(Stage.PlanReview, round);

                return Task.FromResult(WithNothingSkippedByRule(
                    _roster.BuildWork(
                        roles, workingDir,
                        RulesText.Section(rules, rules.TierCoverage(StageRules.Plan))
                            + $"## The plan under review\n\n{planText}",
                        round,
                        stage: Stage.PlanReview, readsCheckout: false,
                        seed: StableSeed(session.State.SessionId, round),
                        planPrompts: _settings.DealPlanLenses ? UnspentPlanLenses(session, roles) : null,
                        deal: _settings.DealPlanLenses)));
            })
        {
            RolesPerVendor = 1,
            Cadence = trace,
            // The plan stage refuses only what the declaration itself gets wrong; which groups are owed
            // is an ORDER here, never a refusal — nothing is built yet.
            RefuseBeforeBuilding = async (loaded, sha) =>
            {
                trace.Call = await _cadence.PrepareAsync(loaded, cadence, repoPath, sha, ct);
                return trace.Call.Refusal;
            },
        },
            ct);
    }

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
    public Task<string> ReviewCodeAsync(
        string repoPath, string branch, string baseRef, string planText, bool again = false, CancellationToken ct = default) =>
        ReviewCodeAsync(repoPath, branch, baseRef, planText, again, CadenceArgs.None, ct);

    /// <param name="cadence">The plan and epic this code round is for, and any risk answer — what the
    /// consultation cadence refuses on in <c>require</c> (<c>research/PLAN_consult_on_a_cadence.md</c>).</param>
    public Task<string> ReviewCodeAsync(
        string repoPath, string branch, string baseRef, string planText, bool again, CadenceArgs cadence, CancellationToken ct = default)
    {
        var trace = new CadenceTrace();
        // Before the scope check and before anything is built, because it needs nothing to be true:
        // a code round with every role switched off would launch no reviewer, and that is not an
        // empty round — the session counts a round nobody answered as unresolved, so it would sit
        // open for ever and the next `review_code` would be refused for the wrong reason.
        if (_settings.Rounds.EnabledRolesOf(Stage.CodeReview).Count == 0)
        {
            // Named in the log as well as in the answer. The caller gets a sentence about the panel;
            // whoever is reading a log afterwards is usually holding a `mcpServers` block instead,
            // and "which four" is the question they have.
            _log.Warning(
                "review_code refused: every code role is switched off ({Roles})",
                string.Join(", ", PanelConfig.CodeRoleNames));
            return Task.FromResult(Error(NoCodeRolesRefusal));
        }

        var scope = Scope(repoPath, branch, planText);
        var before = _store.Load(repoPath, branch);
        // Only once the stage itself is reachable. "The plan stage has not passed" is the more
        // useful sentence for a caller who skipped it, and telling them to send a scope for a
        // round that could not have run either way sends them to fix the wrong thing.
        if (before is { State.PlanProceeded: true } && !ReviewScope.IsSubstantial(scope))
        {
            // Refused before any worktree, any launcher, any token: nothing has to run to know it.
            return Task.FromResult(Error(ReviewScope.Refusal));
        }


        return _engine.RunStageAsync(repoPath, branch, scope, new StageRun(
            again ? RoundMachine.BeginCodeRoundAgain : RoundMachine.BeginCodeRound,
            NeedsWorktree: true, Stage: Stage.CodeReview, ReadsCheckout: true,
            async (session, workingDir, sha) =>
            {
                var collected = await _context.CollectAsync(repoPath, baseRef, sha, ct: ct);
                var shaped = DiffShaper.Shape(collected.Files);
                var bundle = new ReviewBundle(scope, branch, baseRef, sha, shaped);

                // WHICH commit this round is a diff of, said out loud. Findings re-read a week later
                // cannot be re-checked without it — and each fallback is worth its own sentence,
                // because those are the states in which the base's own commits can still arrive as
                // deletions the branch never made.
                _log.Information(
                    collected.Kind switch
                    {
                        DiffBase.MergeBase =>
                            "diffed against the merge base {Against} of {BaseRef} and {Sha}",
                        DiffBase.ShallowHistory =>
                            "diffed against {Against} directly: {BaseRef} and {Sha} share no ancestor IN THIS CHECKOUT, "
                            + "which is shallow — deepen it, or this diff can show the base's commits as deletions",
                        _ =>
                            "diffed against {Against} directly: {BaseRef} and {Sha} have no common ancestor, "
                            + "so this diff can show the base's own commits as deletions",
                    },
                    collected.ComparedAgainst, baseRef, sha);

                // The project's OWN written conventions, read from the worktree so they are the
                // rules as of the commit under review rather than as of this afternoon. Without
                // them a reviewer can call a change well written by its own standards while it
                // breaks four rules the project enforces on its humans — and its silence reads as
                // approval, because a reviewer cannot flag what it was never told.
                // Ordered FOR THIS BRANCH. The tier fills the budget, so a fixed order would show a
                // fixed set and the rest of the corpus to nobody; rotating the tail by the branch
                // keeps every rule reachable across a team's work while this branch's own answer
                // never changes between the rounds of one fix — which is the whole point.
                var rules = RuleFiles.Collect(workingDir, RuleOrder.ForBranch(branch));
                var context =
                    $"## The plan this change implements\n\n{bundle.PlanText}\n\n" +
                    RulesText.Section(rules) +
                    // The resolved base is named to the REVIEWER as well as to the log. Two reviewers
                    // asked for it on the plan round, and the reason is theirs: a reviewer that
                    // checks the branch against the tip of `main` sees a diff that does not match,
                    // and the discrepancy is indistinguishable from the phantom deletions this whole
                    // change exists to stop.
                    $"## The change ({bundle.Branch} over {bundle.BaseRef}, at {bundle.Sha}; "
                    + $"compared against {collected.ComparedAgainst})\n\n"
                    // And when there is no common ancestor, the reviewer is TOLD, not left to
                    // discover it: this is the one state in which a deletion in the diff below may
                    // be somebody else's commit rather than anything this branch did, and a reviewer
                    // reading it cannot tell the two apart on its own.
                    + (collected.Kind == DiffBase.MergeBase
                        ? string.Empty
                        : "> The base and this branch share no common ancestor that could be found, so what follows "
                          + "compares the two tips. Deletions in it may be commits the base has and this branch "
                          + "never removed — weigh them accordingly.\n\n")
                    + bundle.Diff.Text;
                _log.Information(
                    "rules for review: {Count} file(s), {Bytes} bytes, {Omitted} omitted, {Missing} mount(s) not in the tree",
                    rules.Files.Count, rules.Bytes, rules.Omitted.Count, rules.MissingMounts.Count);
                // What the reviewers were actually sent, which the line above says nothing about.
                // The diff IS a reviewer's world at this stage, and on 2026-09-08 eight of them
                // answered with nothing while the only way to ask whether they had seen the change
                // was to subtract this rules byte count from a token total in the ledger. Every
                // number here was already computed and thrown away.
                //
                // UTF-8 bytes throughout, so a diff of Cyrillic identifiers and one of ASCII are
                // measured the same way — and `elided` because a partial view is exactly the state
                // in which a reviewer's silence means nothing at all.
                _log.Information(
                    "context for review: diff {DiffBytes} bytes over {DiffFiles} file(s), {Elided} elided; "
                    + "plan {PlanBytes} bytes; rules {RulesBytes} bytes",
                    System.Text.Encoding.UTF8.GetByteCount(bundle.Diff.Text),
                    bundle.Diff.TotalFiles,
                    bundle.Diff.Elided.Length,
                    System.Text.Encoding.UTF8.GetByteCount(bundle.PlanText),
                    rules.Bytes);
                // Only the roles whose OWN budget reaches this round. The stage counts rounds once
                // and a role stops taking part when its rounds are spent, so architecture can be
                // worth two passes while performance is worth one.
                var round = session.State.RoundsRunThisStage + 1;
                var scheduled = _settings.Rounds.RolesForRound(Stage.CodeReview, round);

                var roles = RosterBuilder.RolesWithRulesInMind(scheduled, rules.HasRules);
                // Each with the reason its own rule gives it — see `RolesNotAsked`, which is where a
                // second rule would put a second reason rather than inheriting this one.
                var notAsked = RosterBuilder.RolesNotAsked(scheduled, rules.HasRules);
                if (notAsked.Count > 0)
                {
                    _log.Warning(
                        "round {Round}: the Conventions reviewers are skipped — {Reason} ({Sources})",
                        round, RosterBuilder.NoWrittenRules, string.Join(", ", RuleFiles.SourceNames));
                }
                _log.Information("round {Round} runs {Count} role(s): {Roles}", round, roles.Count, string.Join(", ", roles));
                var built = WithSkippedByRule(
                    _roster.BuildWork(roles, workingDir, context, round,
                        stage: Stage.CodeReview, readsCheckout: true,
                        seed: StableSeed(session.State.SessionId, round),
                        deal: _settings.DealCodeLenses),
                    notAsked);

                // The RESOLVED base, not `baseRef`: when the two differ the reviewers are told so a
                // few lines above, and it is the resolved one the diff was actually taken against.
                // Recorded here because this local is the only place it exists — by the time the
                // round is written down the call that computed it has returned.
                return built with
                {
                    BaseRef = collected.ComparedAgainst,
                    Unreviewed = NothingToReview.UnreviewedTail(await _context.UncommittedAsync(repoPath, sha, ct)),
                };
            })
        {
            RolesPerVendor = PanelConfig.CodeRoleNames.Length,
            // Nothing to review is SAID, never passed. Asked from the resolved commit before any
            // tree or reviewer exists, so a refusal costs two numstats and leaves the session as it
            // was — `again` included: its reopening is in memory until a round actually completes.
            //
            // What `again` would reopen a finished code stage FOR is the last code round's commit, read
            // from the session as it stands UNDER the claim — never from a read taken before it, which
            // a round finishing in between would make stale (code round, gemini). Only a finished stage
            // is compared: asking again of a stage still open changes nothing.
            Cadence = trace,
            RefuseBeforeBuilding = async (loaded, sha) =>
                await NothingSince(repoPath, sha, again ? SinceWhenFinished(loaded) : NoCodeRound, ct) is { Length: > 0 } unchanged
                    ? unchanged
                    : await NothingOver(repoPath, branch, baseRef, sha, ct) is { Length: > 0 } empty
                        ? empty
                        : await CadenceBeforeTheCode(loaded, cadence, trace, repoPath, branch, sha, ct),
        },
            ct);
    }

    /// <summary>
    /// The consultation cadence's word on this code round — after the checks that need nothing but the commit,
    /// because a round with nothing to review owes no consultation (research/PLAN_consult_on_a_cadence.md, story 3.3).
    /// </summary>
    private async Task<string> CadenceBeforeTheCode(
        PersistedSession loaded, CadenceArgs cadence, CadenceTrace trace, string repoPath, string branch, string sha, CancellationToken ct)
    {
        var prepared = await _cadence.PrepareAsync(loaded, cadence, repoPath, sha, ct);
        var (refusal, call) = _cadence.BeforeTheCode(loaded, prepared, _commands.CommandTextsNow(), branch);
        trace.Call = call;

        return refusal;
    }

    /// <summary>A code round's number and the commit it reviewed; number 0 is "none".</summary>
    private sealed record CodeRound(int Number, string Sha);

    private static readonly CodeRound NoCodeRound = new(0, string.Empty);

    private static CodeRound SinceWhenFinished(PersistedSession session) =>
        session.State.Stage == Stage.Done ? LastCodeRound(session) : NoCodeRound;

    private static CodeRound LastCodeRound(PersistedSession session) =>
        session.Rounds.LastOrDefault(r => r.Stage == nameof(Stage.CodeReview)) is { } last
            ? new CodeRound(last.Number, last.Sha)
            : NoCodeRound;

    /// <summary>
    /// Why `again` has nothing new to review, or an empty sentence: the same commit as the last code
    /// round, or new commits that changed only files a reviewer is never shown (plan round, codex).
    /// A round with no recorded commit — one from before the field existed — proves nothing, so it
    /// never refuses.
    /// </summary>
    private async Task<string> NothingSince(string repoPath, string sha, CodeRound since, CancellationToken ct)
    {
        if (since.Sha.Length == 0)
        {
            return string.Empty;
        }

        if (sha.Equals(since.Sha, StringComparison.OrdinalIgnoreCase))
        {
            return NothingToReview.NoNewCommit(since.Number, sha);
        }

        var change = await _context.ReviewableAsync(repoPath, since.Sha, sha, ct: ct);
        return change.IsEmpty ? NothingToReview.NothingSince(since.Number, change.ChangedButExcluded) : string.Empty;
    }

    /// <summary>Why the branch has nothing to review over its base, or an empty sentence (S1).</summary>
    private async Task<string> NothingOver(string repoPath, string branch, string baseRef, string sha, CancellationToken ct)
    {
        var change = await _context.ReviewableAsync(repoPath, baseRef, sha, ct: ct);
        return change.IsEmpty
            ? NothingToReview.Refusal(
                branch, baseRef, sha, change.ChangedButExcluded, await _context.UncommittedAsync(repoPath, sha, ct))
            : string.Empty;
    }

    /// <summary>
    /// The document gate — the roles a person wrote, over a document rather than a diff.
    /// </summary>
    /// <remarks>
    /// <para>What plans 1, 2 and 3 were building towards, and what the person who asked for this
    /// series actually wanted: they are not a programmer, their work product is a specification or a
    /// policy, and what they want is several vendors' models reading it independently.</para>
    /// <para><b>It opens its own session</b>, keyed by the document rather than by the branch — a
    /// person with ten documents does not have ten branches. <c>open</c> is still required first,
    /// because a document review happens IN a repository and the branch session is what proves the
    /// caller named a real checkout.</para>
    /// <para><b>No plan gate before it.</b> There is no plan before a document; the document is the
    /// work. Requiring one would be asking a person to write a plan about the thing they wanted
    /// read.</para>
    /// </remarks>
    /// <param name="purposeText">
    /// What the document is FOR. Required, and held to the same substantiality rule a code round's
    /// scope is: a specification can be clear, complete, internally consistent and about the wrong
    /// project, and only the purpose catches that.
    /// </param>
    /// <param name="newReview">
    /// Start a FRESH review of this document rather than continuing the current one. Never automatic
    /// — see <see cref="DocumentSessions"/>.
    /// </param>
    public Task<string> ReviewDocumentAsync(
        string repoPath,
        string branch,
        string purposeText,
        string? documentPath = null,
        string? documentText = null,
        string? documentName = null,
        bool newReview = false,
        CancellationToken ct = default)
    {
        // Before anything is read, resolved or written: a round with no reviewer in it is not an
        // empty round, it is an unresolved one that sits open for ever. The same guard the other two
        // stages have, for the same reason.
        // Read ONCE, and carried: the guard and the deadline both need the roster, and reading it
        // twice is how the two could come to disagree about whether it is empty.
        var roles = _settings.Rounds.EnabledRolesOf(Stage.DocumentReview).Count;

        return WhatToReview(repoPath, purposeText, roles, new DocumentRequest(documentPath, documentText, documentName)) switch
        {
            DocumentOutcome.Refused no => Task.FromResult(Refused(no.Sentence)),
            DocumentOutcome.Ready document =>
                ReviewTheDocumentAsync(repoPath, branch, purposeText, document, roles, newReview, ct),
            _ => throw new InvalidOperationException("the union is closed"),
        };
    }

    /// <summary>The guards that need nothing on disk, then the document itself.</summary>
    private DocumentOutcome WhatToReview(string repoPath, string purposeText, int roles, DocumentRequest request) =>
        WhyNotADocumentRound(purposeText, roles) is { } no
            ? new DocumentOutcome.Refused(no)
            : DocumentReader.Read(repoPath, request, DocumentReader.FollowLink);

    private Task<string> ReviewTheDocumentAsync(
        string repoPath,
        string branch,
        string purposeText,
        DocumentOutcome.Ready document,
        int roles,
        bool newReview,
        CancellationToken ct) =>
        OpenDocumentSession(repoPath, branch, document, purposeText, newReview) switch
        {
            DocumentTurn.Refused no => Task.FromResult(Refused(no.Sentence)),
            DocumentTurn.Open open => RunDocumentStageAsync(
                repoPath, branch, purposeText, document, open.Session, roles, ct),
            _ => throw new InvalidOperationException("the union is closed"),
        };

    /// <summary>
    /// A refusal a person may have to act on is worth a line in the log as well.
    /// </summary>
    /// <remarks>
    /// It forwards its OWN caller. Without that, every refusal that comes through this wrapper is
    /// written down as <c>Refused</c> — one subject for every document refusal there is, which is the
    /// single collapsed row story 2.2 introduced <c>[CallerMemberName]</c> to avoid. (CodeRabbit, on
    /// the pull request.)
    /// </remarks>
    private string Refused(string sentence, [CallerMemberName] string from = "")
    {
        _log.Warning("review_document refused: {Why}", sentence);

        return Error(sentence, from);
    }

    /// <summary>Why this cannot be a document round at all, or null.</summary>
    private string? WhyNotADocumentRound(string purposeText, int roles) =>
        roles == 0
            ? NoRolesRefusal(Stage.DocumentReview)
            : ReviewScope.IsSubstantial(purposeText) ? null : ReviewScope.DocumentRefusal;

    /// <summary>
    /// The document session this call is about, or the sentence saying why there is none.
    /// </summary>
    /// <remarks>
    /// A closed union rather than two nullable fields, which is how every other outcome in this
    /// codebase says the same thing — <c>Transition</c>, <c>ParseOutcome</c>, <c>DocumentOutcome</c>.
    /// Two nullables can be BOTH null or both set, and neither state means anything; a union has no
    /// way to spell them. (codex, the code round.)
    /// </remarks>
    private abstract record DocumentTurn
    {
        public sealed record Open(PersistedSession Session) : DocumentTurn;

        public sealed record Refused(string Sentence) : DocumentTurn;

        private DocumentTurn() { }
    }

    private DocumentTurn OpenDocumentSession(
        string repoPath, string branch, DocumentOutcome.Ready document, string purposeText, bool newReview)
    {
        if (_store.Load(repoPath, branch) is null)
        {
            return new DocumentTurn.Refused("no session for this repo+branch — call open first");
        }

        if (DocumentSessions.Reserved(document.Id) is { } reserved)
        {
            return new DocumentTurn.Refused(reserved);
        }

        // A FRESH review may not be started over a round nobody has decided on: the old session
        // would sit awaiting a resolve that now has no way to reach it, which is the orphaning this
        // whole design was rewritten to prevent — one ordinal further along. (codex, the code round.)
        var current = _store.Load(repoPath, branch, DocumentSessions.Which(
            document.Id, newReview: false, id => _store.Exists(repoPath, branch, id)));
        if (newReview && current is { State.AwaitingResolve: true })
        {
            return new DocumentTurn.Refused(DocumentSessions.StillOpen(document.Id));
        }

        var identity = DocumentSessions.Which(
            document.Id, newReview, id => _store.Exists(repoPath, branch, id));

        return identity.Length == 0
            ? new DocumentTurn.Refused(DocumentSessions.TooMany(document.Id))
            : ContinuingOrNew(repoPath, branch, identity, purposeText);
    }

    /// <summary>
    /// The session at this identity: the one that is there, or a new one.
    /// </summary>
    /// <remarks>
    /// The load is repeated rather than carried down from the caller, and deliberately: between the
    /// two, another server sharing this data directory may have created it. Losing that race writes
    /// a fresh session over somebody's open round, and re-reading here narrows the window to the
    /// width of one call. (codex, on concurrent claims.)
    /// </remarks>
    private DocumentTurn ContinuingOrNew(string repoPath, string branch, string identity, string purposeText) =>
        _store.Load(repoPath, branch, identity) is { } existing
            ? Continuing(existing, purposeText)
            : new DocumentTurn.Open(NewDocumentSession(repoPath, branch, identity, purposeText));

    /// <summary>
    /// A purpose that has changed is a different review, not a second round of this one.
    /// </summary>
    /// <remarks>
    /// The same document read for "check the security controls" and later for "check it is
    /// complete" is two questions. Carrying round 1's scope into round 2 silently is what the first
    /// draft of the plan would have done, and codex named it: the second round's reviewers would
    /// have been told to answer the first round's question with the second one's rounds.
    /// </remarks>
    private static DocumentTurn Continuing(PersistedSession session, string purposeText) =>
        string.Equals(session.PlanText.Trim(), purposeText.Trim(), StringComparison.Ordinal)
            ? new DocumentTurn.Open(session)
            : new DocumentTurn.Refused(
                "this review of the document was opened with a different purpose, and a different "
              + "purpose is a different review rather than another round of this one. Pass the "
              + "original purposeText to continue, or newReview: true to start a fresh review.");

    /// <param name="purposeText">
    /// Written at CREATION, not only after the first round completes. It used to be empty until the
    /// round's own save, so a round interrupted before that — a crash, a cancelled tool call — left a
    /// session whose stored purpose was blank; the retry then compared blank against the same purpose
    /// the caller sent again and refused it as a DIFFERENT one. A trap that only fires after
    /// something else has already gone wrong. (gemini, the code round.)
    /// </param>
    private PersistedSession NewDocumentSession(string repoPath, string branch, string identity, string purposeText)
    {
        var session = new PersistedSession(
            new SessionState(Guid.NewGuid().ToString("N")[..8], repoPath, branch, _settings.Rounds)
            {
                Document = identity,
                // Its own stage from the first moment. A document session never passes through the
                // plan stage — there is nothing to plan — and leaving it at the default would make
                // its first round refuse for a reason that is not true about it.
                Stage = Stage.DocumentReview,
            },
            [])
        {
            OpenedUtc = DateTime.UtcNow,
            PlanText = purposeText,
        };
        _store.Save(session);
        _log.Information(
            "session {SessionId} open for document {Document} on {Branch}",
            session.State.SessionId, identity, branch);

        return session;
    }

    private Task<string> RunDocumentStageAsync(
        string repoPath,
        string branch,
        string purposeText,
        DocumentOutcome.Ready document,
        PersistedSession session,
        int roles,
        CancellationToken ct)
    {
        // The snapshot is kept BEFORE the round, so a round that is then killed still leaves the
        // text somebody can open. It never fails the round: the review is the product and the
        // snapshot is a record of it.
        if (_artifacts.Keep(document.ArtifactId, document.Text) is { } notKept)
        {
            _log.Warning("{Sentence}", notKept);
        }

        return _engine.RunStageAsync(repoPath, branch, purposeText,
            // A document reviewer is served by the vendor's PLAN switch — a person who set a vendor
            // to read documents rather than diffs ticked that one — and is handed no checkout,
            // because its job is the document it was given. Neither says it is the plan stage.
            new StageRun(RoundMachine.BeginDocumentRound,
                NeedsWorktree: false, Stage: Stage.DocumentReview, ReadsCheckout: false,
                (running, workingDir, _) =>
                {
                    // From `repoPath`, not `workingDir` — this stage is handed no checkout on
                    // purpose, so its working directory is empty and collecting there would gather
                    // nothing while looking like it worked.
                    var rules = RuleFiles.Collect(repoPath, RuleOrder.Staged(StageRules.Document));

                    // The same sentence the other two stages write, carrying this one's own numbers.
                    _log.Information(
                        "context for review: document {Name} ({DocumentBytes} bytes, snapshot {Artifact}), "
                        + "purpose {PurposeBytes} bytes; rules {RulesBytes} bytes, "
                        + "{Matched} of {Tier} tier rule(s), {Omitted} omitted",
                        document.Name,
                        System.Text.Encoding.UTF8.GetByteCount(document.Text),
                        document.ArtifactId,
                        System.Text.Encoding.UTF8.GetByteCount(purposeText),
                        rules.Bytes,
                        rules.MatchedCount(StageRules.Document),
                        StageRules.Document.Length,
                        rules.Omitted.Count);

                    var round = running.State.RoundsRunThisStage + 1;
                    var roles = _settings.Rounds.RolesForRound(Stage.DocumentReview, round);

                    return Task.FromResult(WithNothingSkippedByRule(
                        _roster.BuildWork(
                            roles, workingDir, DocumentContext(purposeText, document, rules), round,
                            stage: Stage.DocumentReview, readsCheckout: false,
                            seed: StableSeed(running.State.SessionId, round))));
                })
            {
                Document = session.State.Document,
                // The real number, not the plan stage's one: a document round runs one reviewer per
                // document ROLE per vendor, and a deadline derived from one would be short. It is
                // the count the GUARD read, passed down rather than read again — an empty roster is
                // refused before this line, and a second read is how that could stop being true.
                RolesPerVendor = roles,
            },
            ct);
    }

    /// <summary>
    /// What a document reviewer is handed: the purpose FIRST, then the document.
    /// </summary>
    /// <remarks>
    /// The order is deliberate and it is the one thing about this prompt worth arguing over. A model
    /// reading a long document before it is told what to look for spends the document forming its
    /// own idea of what the document is for — which is exactly the judgement the purpose exists to
    /// replace.
    /// </remarks>
    /// <remarks>
    /// The rules sit between the purpose and the document, where the code stage puts them: what a
    /// thing is judged against comes before the thing under review, so one reader learns one shape
    /// whichever stage they are looking at.
    /// </remarks>
    private static string DocumentContext(string purposeText, DocumentOutcome.Ready document, RuleBundle rules) =>
        $"## What this document is for\n\n{purposeText}\n\n"
        + RulesText.Section(rules, rules.TierCoverage(StageRules.Document))
        + $"## The document under review — {document.Name}\n\n{document.Text}";

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
    private IReadOnlyList<string> UnspentPlanLenses(PersistedSession session, IReadOnlyList<string> roles)
    {
        // Every role in the round contributes, and the pool was read from the shipped plan role
        // alone — which was the whole plan stage until a person could add a second role to it. With
        // dealing switched on, every question in the hand then belonged to PlanCritique, so a round
        // configured for two plan roles asked one of them twice and the other nothing.
        //
        // One lens each first, so no role is starved by the take below; then the first role tops the
        // hand up to one question per vendor, which is what the take has always been for.
        var hand = new List<string>();
        foreach (var role in roles)
        {
            if (Pool(session, role) is [var first, ..])
            {
                hand.Add(first);
            }
        }

        var vendors = Math.Max(_settings.Providers.Count(p => p.Enabled), 1);

        return [.. hand, .. roles.Count > 0 ? Pool(session, roles[0]).Skip(1).Take(Math.Max(vendors - hand.Count, 0)) : []];
    }

    /// <summary>One role's lenses, the unspent ones first and its general prompt first of those.</summary>
    private IReadOnlyList<string> Pool(PersistedSession session, string role)
    {
        var all = _settings.Rounds.Catalog.For(role)
            .OrderByDescending(p => p.Universal)
            .Select(p => p.Id)
            .ToList();
        var unspent = all.Where(id => !session.UsedPrompts.Contains(id)).ToList();

        return unspent.Count > 0 ? unspent : all;
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

    /// <summary>What the caller sent, or what the plan stage already agreed — in that order.</summary>
    private string Scope(string repoPath, string branch, string planText) =>
        planText.Trim().Length > 0 ? planText : _store.Load(repoPath, branch)?.PlanText ?? string.Empty;

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

    /// <summary>
    /// Deletes a directory that git has been in.
    /// </summary>
    /// <remarks>
    /// <c>Directory.Delete(recursive: true)</c> refuses a READ-ONLY file with
    /// <c>UnauthorizedAccessException</c>, and git marks every object file read-only — so a scratch
    /// directory that ever held a clone could not be swept, the exception was caught, and the
    /// leftovers accumulated in silence. Measured 2026-09-05: 5,476 undeletable directories, almost
    /// all of them a test's clone, the oldest five days old.
    /// </remarks>
    private static void DeleteEvenIfReadOnly(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
        }

        Directory.Delete(dir, recursive: true);
    }

    /// <summary>A downloaded server kept under its version — not scratch, and not ours to remove.</summary>
    private static bool VersionCache(string dir) =>
        Path.GetFileName(dir) is { Length: > 5 } name && char.IsDigit(name["coai-".Length]);

    /// <summary>A version cache, or a directory whose name starts with one of <paramref name="except"/>.</summary>
    private static bool Kept(string dir, IReadOnlyList<string> except) =>
        VersionCache(dir) || except.Any(owned => Path.GetFileName(dir).StartsWith(owned, StringComparison.Ordinal));

    /// <summary>How often one process will walk the temp directory looking for its own leftovers.</summary>
    /// <remarks>
    /// <para><b>A sweep is cheap and a WALK is not, and this used to pay the walk on every
    /// <c>BuildWork</c>.</b> The cost is the number of directories in temp, which on a machine
    /// running this suite grows all day: measured here 2026-09-13, <b>81,986</b> <c>coai-*</c>
    /// directories, and <c>SubmissionOrderTests</c> — which calls <c>BuildWork</c> a hundred times in
    /// a loop — went from 8 seconds to over four minutes and read, from the outside, as a deadlock in
    /// whatever had just been changed.</para>
    /// <para>Ten minutes is far below the six-hour window it sweeps, so nothing survives longer than
    /// it did; what changes is that a round pays for the walk at most once every ten minutes instead
    /// of twice per reviewer.</para>
    /// </remarks>
    private static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(10);

    private static DateTime _sweptUtc = DateTime.MinValue;

    /// <summary>
    /// Sweeps this product's leftovers, at most once per <see cref="SweepEvery"/> per process.
    /// </summary>
    /// <remarks>
    /// The clock is read and written under a lock rather than with <c>Interlocked</c> on purpose: two
    /// rounds starting in the same millisecond should produce ONE walk, and a compare-and-swap on a
    /// <c>DateTime</c> is not a thing this runtime offers without a struct wider than a word.
    /// </remarks>
    private static void PruneOldAnswerDirs()
    {
        lock (SweepLock)
        {
            var now = DateTime.UtcNow;
            if (now - _sweptUtc < SweepEvery)
            {
                return;
            }

            _sweptUtc = now;
        }

        PruneOldScratchDirs(Path.GetTempPath(), DateTime.UtcNow.AddHours(-6));
    }

    private static readonly System.Threading.Lock SweepLock = new();

    /// <summary>Lets a test drive the throttle rather than wait ten minutes for it.</summary>
    internal static void ForgetTheLastSweep()
    {
        lock (SweepLock)
        {
            _sweptUtc = DateTime.MinValue;
        }
    }

    /// <summary>
    /// Removes leftover scratch directories created before <paramref name="cutoff"/>.
    /// </summary>
    /// <remarks>
    /// The prefixes are a parameter because the TEST suite leaks the same way and for the same
    /// reason — a directory whose delete lost a race with a file still held open — and a second
    /// sweeper would be a second thing to get wrong. Measured on this machine, 2026-09-05: the
    /// product's own three prefixes had 3,395 directories and NOT ONE older than its six-hour
    /// window, while the test prefixes had 6,220 with the oldest at five days.
    /// <para><paramref name="except"/> is for the test sweep, which matches <c>coai-*</c> on a ten-minute
    /// window and must leave the product's own working directories alone — a live chat's can sit for an
    /// hour with nothing written into it (<c>shared/temp-sweep.json</c>).</para>
    /// </remarks>
    internal static void PruneOldScratchDirs(
        string tempRoot, DateTime cutoff, IReadOnlyList<string>? prefixes = null, IReadOnlyList<string>? except = null)
    {
        try
        {
            foreach (var dir in (prefixes ?? ScratchPrefixes).SelectMany(p => Directory.EnumerateDirectories(tempRoot, p)))
            {
                try
                {
                    // The LAST WRITE, not the creation: Windows file tunnelling hands a recreated
                    // name its predecessor's creation time, so a directory made this minute under a
                    // name used yesterday reads as a day old — and, the other way round, a long
                    // campaign's directory looks fresh for as long as anything writes into it.
                    if (Directory.GetLastWriteTimeUtc(dir) < cutoff && !Kept(dir, except ?? []))
                    {
                        DeleteEvenIfReadOnly(dir);
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

    /// <summary>
    /// A round's work with the roles a RULE decided not to ask for added to the ones the build
    /// itself could not.
    /// </summary>
    /// <remarks>
    /// Two different reasons a role is absent, from two different places: the conventions pass is
    /// dropped because the repository wrote no rules, and a role a person added is dropped because
    /// its prompt has no text. Both reach the caller through one list, and neither is allowed to
    /// overwrite the other — which is what a plain `new RoundWork(work, notAsked)` was doing.
    /// </remarks>
    private static RoundWork WithSkippedByRule(RoundWork built, IReadOnlyList<SkippedRole> byRule) =>
        built with { NotAsked = [.. byRule, .. built.NotAsked] };

    /// <summary>The same, for a stage where no rule skips anything.</summary>
    private static RoundWork WithNothingSkippedByRule(RoundWork built) => built;

    // ---------- the feature stage ----------

    /// <summary>
    /// The feature gate — once a whole plan of three or more epics is built, before release: the plan,
    /// the epics, the implementer's lessons, the gate's history of this work, and an outline of every
    /// changed file with the changed hunks, to the vendors ticked to review features.
    /// </summary>
    /// <remarks>
    /// A thin delegation to <see cref="FeatureStage"/>, which owns the inputs, the refs and the pack. It
    /// needs no <c>open</c>: the session is keyed by the plan and made under the engine's own claim, and
    /// the caller is recorded from this call's handshake as <c>open</c> would record it.
    /// </remarks>
    public Task<string> ReviewFeatureAsync(
        string repoPath,
        string planPath,
        string baseRef,
        string epics,
        string lessons,
        bool again = false,
        string callerModel = "",
        string client = "",
        string clientVersion = "",
        CancellationToken ct = default) =>
        _feature.ReviewAsync(
            new FeatureRequest(repoPath, planPath, baseRef, epics, lessons, again,
                CallerDeclaration.From(CallerIdentity.Current(), client, clientVersion, callerModel)),
            ct);

    // ---------- resolve ----------

    /// <param name="document">
    /// Which document's review is being resolved, when the round was a document round: the same
    /// <c>documentPath</c> or <c>documentName</c> that was passed to <c>review_document</c>. Empty
    /// resolves the BRANCH's session, which is what plan and code rounds have always meant.
    /// </param>
    /// <param name="feature">
    /// Which plan's feature review is being resolved: the same <c>planPath</c> passed to
    /// <c>review_feature</c>. The branch is then not read.
    /// </param>
    public Task<string> ResolveAsync(
        string repoPath, string branch, string decisionsJson, bool humanSaysProceed = false, string document = "", string feature = "")
    {
        if (Both(document, feature) is { Length: > 0 } both)
        {
            return Task.FromResult(Error(both));
        }

        // One mutating call per session (S4): a resolve decides the pending list a running round of
        // another call is about to replace. The body is synchronous, so the claim covers all of it.
        var at = AddressOf(repoPath, branch, document, feature);
        using var claim = SessionClaim.TryTake(_settings.DataDir, repoPath, at.Branch, at.Document, at.Feature);
        return claim is null
            ? Task.FromResult(Error(SessionClaim.Busy(branch)))
            : ResolveUnderClaim(repoPath, at, decisionsJson, humanSaysProceed, NoSession(document, feature));
    }

    private Task<string> ResolveUnderClaim(
        string repoPath, SessionAddress at, string decisionsJson, bool humanSaysProceed, string noSession)
    {
        var session = _store.Load(repoPath, at.Branch, at.Document, at.Feature);
        if (session is null)
        {
            return Task.FromResult(Error(noSession));
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

        // Each decision keeps the NUMBER the caller made it by. The projection used to take that
        // number from the decision's position here, which is the same number only while the caller
        // resolves top to bottom — and nothing makes it.
        var decisions = new List<DecisionAt>();
        // One decision per finding. Two entries for the same index used to pass every check and
        // then disagree with themselves: the projection wrote both in order, so the LAST one won
        // silently, while RecordClosing counted both — a round with one finding closing as one
        // accepted and one rejected. (Code round, codex and gemini.)
        var decided = new HashSet<int>();
        foreach (var dto in dtos)
        {
            if (dto.Finding < 0 || dto.Finding >= session.Pending.Count)
            {
                // "this round reported 0" is a true sentence and a useless one: when there is
                // nothing pending at all, the caller is almost never holding a bad index — they are
                // holding the right findings and the WRONG session, because a document round lives
                // in its own. Say that instead of sending them to recount.
                return Task.FromResult(Error(session.Pending.Count == 0
                    ? "this session has no findings awaiting decisions. If you are resolving a "
                      + "DOCUMENT round, pass its document: a document review is its own session, "
                      + "keyed by the document rather than by the branch. A FEATURE round is keyed by its "
                      + "plan: pass the planPath as feature."
                    : $"finding index {dto.Finding} does not exist — this round reported {session.Pending.Count}"));
            }

            if (!decided.Add(dto.Finding))
            {
                return Task.FromResult(Error(
                    $"finding {dto.Finding} was decided twice in one call — pass one entry per finding index"));
            }

            var action = dto.Action.ToLowerInvariant();
            if (action is not ("accept" or "reject"))
            {
                return Task.FromResult(Error($"action '{dto.Action}' is neither accept nor reject"));
            }

            // The factory reads the finding OUT of Pending by this number, so the number and the
            // finding cannot disagree. The range was checked above for the sentence it produces;
            // this is what makes an inconsistent pair unconstructible anywhere.
            var made = action == "accept"
                ? DecisionAt.Accept(session.Pending, dto.Finding)
                : DecisionAt.Reject(session.Pending, dto.Finding, dto.Reason);
            if (made is null)
            {
                return Task.FromResult(Error($"finding index {dto.Finding} does not exist"));
            }

            decisions.Add(made);
        }

        return Task.FromResult(Finish(WithHumanDecision(session), decisions, humanSaysProceed));
    }

    /// <summary>
    /// A write to the projection, which is never allowed to matter.
    /// </summary>
    /// <remarks>
    /// The session files are the source of truth and the round is what somebody is waiting for. A
    /// database that is locked, full or corrupt is a line in the log, not a failed review.
    /// </remarks>
    private void Project(Action<Store.RoundsDb> write) => _projection.Write(write);

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

    private string Finish(PersistedSession session, List<DecisionAt> decisions, bool humanSaysProceed = false)
    {
        // The state machine judges DECISIONS and has no use for the numbers they were made by; the
        // projection needs both. One projection here rather than two lists carried side by side.
        var judged = decisions.Select(d => d.Decision).ToList();
        switch (RoundMachine.Resolve(session.State, judged, humanSaysProceed))
        {
            case Transition.Refused refused:
                return Error(refused.Sentence);
            case Transition.Moved moved:
                // An epic through its code gate is recorded BEFORE the session moves, so the commoner
                // failure — this save — cannot lose it; its own failure is logged and reconciled by the
                // next call, never allowed to fail the resolve (research/PLAN_consult_on_a_cadence.md, D3).
                if (session.State.Stage == Stage.CodeReview && moved.State.Stage == Stage.Done && session.Rounds.Count > 0)
                {
                    _cadence.Close(session, session.Rounds[^1].Verdict);
                }
                _store.Save(session with { State = moved.State, Pending = [] });
                // How the caller closed this gate: how many findings it took, which it argued with
                // and why. The one thing this data is for — an accepted finding is something the
                // caller had not seen and then agreed was worth having.
                Project(db => db.RecordDecisions(
                    session.State.SessionId,
                    session.Rounds.Count == 0 ? session.State.Stage.ToString() : session.Rounds[^1].Stage,
                    session.Rounds.Count == 0 ? 0 : session.Rounds[^1].Number,
                    decisions));
                // A stage that ADVANCED says so in its own words — the stage that just completed
                // owns the sentence. It read `Done => "The code stage is complete"`, which told a
                // document review a code round had finished (§9.2 of the feature-review plan).
                var instruction = moved.State.Stage != session.State.Stage
                    ? Stages.Of(session.State.Stage).CompletedSentence
                    : "Decisions recorded. Apply the accepted findings, then run the review again.";
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
    /// <param name="document">
    /// Which document's review is asking, when it is a document round: the same <c>documentPath</c>
    /// or <c>documentName</c> that was passed to <c>review_document</c>, as <c>resolve</c> and
    /// <c>status</c> take it. Empty asks on behalf of the BRANCH's session, which is what plan and
    /// code rounds have always meant. It always loaded the branch session, so a document review's
    /// question carried the branch's pending findings and was filed under the branch session's id —
    /// and the person's answer, looked up by that id, never reached the document session (§9.3 of
    /// the feature-review plan).
    /// </param>
    /// <param name="feature">
    /// Which plan's feature review is asking: the same <c>planPath</c> passed to <c>review_feature</c>, so
    /// the question is filed under that session and the person's answer reaches it.
    /// </param>
    public async Task<string> AskHumanAsync(
        string repoPath, string branch, string question, string document = "", string feature = "", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return Error("a question is required — an empty escalation tells a person nothing");
        }

        if (Both(document, feature) is { Length: > 0 } both)
        {
            return Error(both);
        }

        var at = AddressOf(repoPath, branch, document, feature);
        var session = _store.Load(repoPath, at.Branch, at.Document, at.Feature);
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

    /// <summary>
    /// The ninth tool: another vendor's model, consulted about the LIVE working tree — a thin
    /// delegation, the way the review half's vendor questions are delegations since 2026-09-05.
    /// </summary>
    public Task<string> ConsultAsync(string repoPath, string problem, string suspectedFiles, string consultationId, CancellationToken ct = default) =>
        _consultations.AskAsync(repoPath, problem, suspectedFiles, consultationId, ct);

    /// <summary>
    /// The same, FOR something: a group of epics or a risky piece the cadence ordered
    /// (<c>research/PLAN_consult_on_a_cadence.md</c>). The three arguments are read into ONE canonical aim before
    /// anything else, so a spelling the gate would not match is refused rather than paid for.
    /// </summary>
    public Task<string> ConsultAsync(string repoPath, string problem, string suspectedFiles, string consultationId, string kind, string plan, string epics, CancellationToken ct = default)
    {
        var (aim, refusal) = Core.Consultation.ConsultAim.Parse(kind, plan, epics);

        return refusal.Length > 0
            ? Task.FromResult(Error(refusal))
            : _consultations.AskAsync(repoPath, problem, suspectedFiles, consultationId, aim, ct);
    }

    /// <summary>Whether a consultation could be had right now — what the cadence gate asks before it refuses.</summary>
    public ConsultPreflight ConsultPreflight() => _consultations.Preflight();

    /// <summary>The consultation records, for the cadence gate and the tests that read what was written.</summary>
    public ConsultationStore Consultations => _consultations.Store;

    /// <summary>
    /// The tenth tool: how a consultation ENDED, recorded by whoever knows.
    /// </summary>
    /// <remarks>
    /// Its counterpart above opens and continues one; nothing closed one until this existed, so a
    /// consultation sat at <c>open</c> until a sweep took it and nothing anywhere said whether the
    /// advice had worked. (issue #309.)
    /// </remarks>
    public Task<string> CloseConsultAsync(string repoPath, string consultationId, string outcome, string note, CancellationToken ct = default) =>
        _consultations.CloseAsync(repoPath, consultationId, outcome, note, byPerson: false, ct);

    /// <summary>The PERSON closing one from the panel, through the one-shot CLI mode.</summary>
    /// <remarks>
    /// Not subject to the caller check, deliberately: this runs on their own machine against their
    /// own data directory, which is a stronger position than another AI's rather than a weaker one.
    /// A consultation whose session has gone is exactly the one nobody else can close. (issue #309.)
    /// </remarks>
    public Task<string> CloseConsultByHandAsync(string repoPath, string consultationId, string outcome, string note, CancellationToken ct = default) =>
        _consultations.CloseAsync(repoPath, consultationId, outcome, note, byPerson: true, ct);


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
        Consultations = _consultations.OpenIn(session.State.RepoPath),
        Pending = session.State.AwaitingResolve ? session.Pending : [],
    };

    private static string Json<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type) =>
        JsonSerializer.Serialize(value, type);

    /// <summary>
    /// A refusal, through the ONE place the wire shape is built.
    /// </summary>
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
        Refusal.Answer(sentence, _noticing, from);
}
