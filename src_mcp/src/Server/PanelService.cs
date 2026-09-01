using System.Collections.Immutable;
using System.Text.Json;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Gate;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Translation;
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
    private readonly ITranslator _translator;
    private readonly UsageLedger _ledger;

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
            settings.GlobalConcurrency, settings.PerProviderConcurrency, settings.RateLimitBackoff);
        // Unparseable answers are kept beside the sessions, so "it would not parse" can be read
        // rather than guessed at.
        _executor = new ReviewerExecutor(launcher, Path.Combine(settings.DataDir, "unparseable"));
        _prompts = new RolePrompts(settings.DataDir);
        _escalations = new Escalations(settings.DataDir);
        _translator = new CliTranslator(launcher, settings.Translator);
        _ledger = new UsageLedger(settings.DataDir);

        // Rounds this server never finished cannot be running any more, whatever their file says.
        // A round left at "running" would sit in the panel forever; sweeping only rounds whose
        // recorded process is gone keeps a SECOND server sharing this directory out of the way.
        var swept = _store.SweepOrphanedRounds(ProcessIsAlive);
        if (swept > 0)
        {
            _log.Warning("swept {Count} round(s) abandoned by a dead process", swept);
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
            _keys.Available ? $"{_keys.Keys.Count} vendor key(s) loaded" : _keys.Unavailability),
            ServerJsonContext.Default.ProvidersAnswer);
    }

    private async Task<ProviderStatus> ProbeAsync(ProviderSettings provider, CancellationToken ct)
    {
        if (RuntimeFor(provider) is null)
        {
            return new ProviderStatus(provider.Provider, provider.Enabled, false, "",
                "unavailable", ReviewerRuntimeSelector.Default.RefusalFor(provider.Provider));
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
            return result.ExitCode == 0
                ? new ProviderStatus(provider.Provider, true, true, result.StdOut.Trim(), auth, authNote)
                : new ProviderStatus(provider.Provider, true, true, "", auth,
                    $"--version exited {result.ExitCode}: {result.StdErr.Trim()}");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new ProviderStatus(provider.Provider, true, false, "", auth, $"'{exe}' was not found on this machine");
        }
    }

    private (string Auth, string Note) AuthFor(ProviderSettings provider) =>
        _keys.Keys.ContainsKey(provider.Provider)
            ? ("vault key", "")
            : provider.BaseUrl.Length > 0 || provider.Provider is "deepseek"
                ? ("unavailable", $"needs a key under '{provider.Provider}' and the vault holds none — see the creds config entry")
                : ("own auth", "the CLI's own sign-in is used");

    /// <summary>
    /// The runtime for one configured reviewer: a built-in by name, or — when the operator gave it
    /// a base URL — the generic custom one. A vendor added in the panel is DATA, not a release.
    /// </summary>
    private static IReviewerRuntime? RuntimeFor(ProviderSettings provider) =>
        provider.BaseUrl.Length > 0
            ? new CustomCodexRuntime(provider.Provider, provider.BaseUrl)
            // An EXPLICIT runtime outranks the id, and that order is the fix for a real defect:
            // the id was consulted first, so a vendor called `claude` worked by accident while
            // `my-claude` — same runtime, different name — silently ran the Codex CLI.
            : Named(provider.Runtime) ?? ReviewerRuntimeSelector.Default.Find(provider.Provider);

    private static IReviewerRuntime? Named(string runtime) => runtime switch
    {
        "gemini" => new GeminiRuntime(),
        "claude" => new ClaudeRuntime(),
        "antigravity" => new AntigravityRuntime(),
        "codex" => new CodexRuntime(),
        _ => null,
    };

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
        RunStageAsync(repoPath, branch, planText, RoundMachine.BeginPlanRound, needsWorktree: false,
            (session, workingDir, _) => Task.FromResult<IReadOnlyList<ReviewerWork>>(
                BuildWork([ReviewRole.PlanCritique], workingDir, $"## The plan under review\n\n{planText}",
                    session.State.RoundsRunThisStage + 1)),
            ct);

    /// <summary>The code gate — three reviewers per provider, over the branch in a read-only tree.</summary>
    public Task<string> ReviewCodeAsync(string repoPath, string branch, string baseRef, string planText, CancellationToken ct = default) =>
        RunStageAsync(repoPath, branch, planText, RoundMachine.BeginCodeRound, needsWorktree: true,
            async (session, workingDir, sha) =>
            {
                var files = await _context.CollectAsync(repoPath, baseRef, sha, ct: ct);
                var shaped = DiffShaper.Shape(files);
                var bundle = new ReviewBundle(planText, branch, baseRef, sha, shaped);
                var context =
                    $"## The plan this change implements\n\n{bundle.PlanText}\n\n" +
                    $"## The change ({bundle.Branch} over {bundle.BaseRef}, at {bundle.Sha})\n\n{bundle.Diff.Text}";
                return BuildWork(CodeRoles, workingDir, context, session.State.RoundsRunThisStage + 1);
            },
            ct);

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
            var live = new LiveRound(_store, session, work);
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
            var merged = FindingDedup.Merge(reviews.SelectMany(r => r.Findings));
            var gate = GateRule.Evaluate(merged, session.State.Rejections, _settings.Rounds.Threshold);

            if (RoundMachine.CompleteRound(session.State, gate, summary) is not Transition.Ok completed)
            {
                return Error("the round could not complete — this is a bug, report it");
            }

            var answer = AnswerFor(completed.Verdict, gate, summary, merged, reviews);
            var record = live.Finish(answer.Verdict, gate.GatingCount, summary.Sentence, results);
            answer = answer with { Cost = new RoundCost(record.TokensIn, record.TokensOut, record.CostUsd) };
            _store.Save(session with
            {
                State = completed.State,
                Rounds = [.. session.Rounds, record],
                Pending = [.. merged],
            });
            audit.Closing(answer.Verdict, gate.GatingCount, summary.Sentence, record);
            audit.Findings(merged);
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
    private string ModelOf(string provider) =>
        _settings.Providers.FirstOrDefault(p => p.Provider == provider)?.Model ?? string.Empty;

    private PromptChoice ChoiceFor(ReviewRole role, int round) =>
        PromptCatalog.ForRound(
            role.ToString(),
            round,
            _settings.PromptsPerRound.GetValueOrDefault(role.ToString(), []),
            _settings.RotatePrompts);

    private IReadOnlyList<ReviewerWork> BuildWork(IReadOnlyList<ReviewRole> roles, string worktreePath, string context, int round)
    {
        var schemaFile = Path.Combine(_settings.DataDir, "finding-schema.json");
        Directory.CreateDirectory(_settings.DataDir);
        File.WriteAllText(schemaFile, FindingSchema.Json);
        var outputDir = Directory.CreateTempSubdirectory("coai-answers-").FullName;

        var work = new List<ReviewerWork>();
        foreach (var provider in _settings.Providers.Where(p => p.Enabled))
        {
            if (RuntimeFor(provider) is not { } runtime ||
                AuthFor(provider).Auth == "unavailable")
            {
                continue; // reported by `providers`; a fan-out is built only from what can run
            }

            var settings = new ReviewerSettings(provider.Provider)
            {
                ExecutablePath = provider.ExecutablePath,
                Model = provider.Model,
                ApiKey = _keys.Keys.GetValueOrDefault(provider.Provider, string.Empty),
                Timeout = _settings.ReviewerTimeout,
            };
            foreach (var role in roles)
            {
                var prompt = ComposePrompt(ChoiceFor(role, round), context);
                var repairPrompt = prompt +
                    "\n\nYOUR PREVIOUS ANSWER WAS NOT VALID JSON. Return ONLY the JSON object for the schema — no fences, no prose.";
                work.Add(new ReviewerWork(
                    runtime.Build(role, prompt, worktreePath, schemaFile, outputDir, settings),
                    runtime.Build(role, repairPrompt, worktreePath, schemaFile, outputDir, settings)));
            }
        }

        return work;
    }

    private string ComposePrompt(PromptChoice choice, string context) =>
        $"{_prompts.ForChoice(choice)}\n\n## The finding contract\n\nReturn ONLY a JSON object matching this schema — no fences, no prose:\n\n{FindingSchema.Json}\n\n{context}";

    private ReviewAnswer AnswerFor(
        RoundVerdict verdict,
        GateResult gate,
        ReviewerSummary summary,
        ImmutableArray<Finding> merged,
        List<NormalisedReview> reviews)
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
            RoundVerdict.CallHuman h => ("call_human", null,
                $"Rounds exhausted: {h.Reason}. A human decides — surface the open findings to them; do not proceed on your own."),
            RoundVerdict.Escalated e => ("escalated", e.Step.ToString(),
                $"Rounds exhausted; the ladder fires {e.Step}. Resolve the findings, apply the step, and run a fresh round."),
            _ => ("unknown", null, ""),
        };
        return new ReviewAnswer(
            name, step, gate.GatingCount, _settings.Rounds.Threshold, summary.Sentence,
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
                ? Finish(session, [], humanSaysProceed)
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

        return Task.FromResult(Finish(session, decisions, humanSaysProceed));
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

        // Into the person's language. A model that already wrote in it returns the text unchanged;
        // one that cannot be reached returns it with a note, and the note is shown rather than
        // swallowed — a question in the wrong language is a nuisance, a missing one stops a review.
        var shown = await _translator.TranslateAsync(question.Trim(), _settings.Language, "question", ct);
        var asked = new EscalationQuestion(
            id,
            session?.State.SessionId ?? "no-session",
            repoPath,
            branch,
            shown.Text,
            shown.Original,
            _settings.Language.Code,
            shown.Note,
            session?.Pending.Where(f => f.IsGating).ToList() ?? [],
            DateTime.UtcNow.ToString("O"));

        _log.Information("escalating {Id} to a person in {Language}: {Question}", id, _settings.Language.Code, asked.Question);
        var outcome = await _escalations.AskAsync(asked, _settings.EscalationBudget, ct);

        return outcome switch
        {
            // And back: the caller asked in its own language, so it is answered in that language,
            // with the person's own words kept beside it. Translating only one direction would
            // hand an English-speaking model a Russian answer and call it done.
            EscalationOutcome.Answered answered => Json(
                await AnswerFor(answered.Text, question.Trim(), ct), ServerJsonContext.Default.HumanAnswer),

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

    /// <summary>
    /// The person's answer, rendered for the caller: their words translated back into the language
    /// the question arrived in, with the original kept beside it.
    /// </summary>
    /// <remarks>
    /// The target is inferred from the ASKING text rather than configured — the model that asked
    /// is the one that has to act on the reply, and it wrote the question itself. When the two
    /// languages are the same the translator returns the text unchanged, which costs one fast call
    /// and removes a guess.
    /// </remarks>
    private async Task<HumanAnswer> AnswerFor(string answer, string originalQuestion, CancellationToken ct)
    {
        var back = await _translator.TranslateAsync(
            answer,
            LanguageOfCaller(originalQuestion),
            "answer",
            ct);
        return new HumanAnswer(
            "answered",
            back.Text,
            answer,
            back.Note.Length == 0
                ? string.Empty
                : $"the answer is shown in the person's own words: {back.Note}");
    }

    /// <summary>
    /// Which language to answer the CALLER in. Latin script → English; otherwise the configured
    /// language, because a caller writing Cyrillic is not asking to be answered in English.
    /// </summary>
    /// <remarks>
    /// Deliberately crude, and cheap: the translator itself decides whether anything needs doing —
    /// asked to render Russian text in Russian, it returns it unchanged. A wrong guess here costs
    /// a no-op call, never a wrong answer.
    /// </remarks>
    private Language LanguageOfCaller(string question) =>
        question.Any(c => c >= 'Ѐ' && c <= 'ӿ') ? _settings.Language : Language.English;

    // ---------- plumbing ----------

    private SessionAnswer SessionAnswerFor(PersistedSession session) => new(
        session.State.SessionId,
        session.State.Stage.ToString(),
        session.State.RoundsRunThisStage,
        session.State.AwaitingResolve,
        session.State.PlanProceeded,
        _settings.Rounds.Threshold,
        _settings.Rounds.MaxRounds,
        session.Rounds);

    private static string Json<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type) =>
        JsonSerializer.Serialize(value, type);

    private static string Error(string sentence) =>
        JsonSerializer.Serialize(new ErrorAnswer(sentence), ServerJsonContext.Default.ErrorAnswer);
}
