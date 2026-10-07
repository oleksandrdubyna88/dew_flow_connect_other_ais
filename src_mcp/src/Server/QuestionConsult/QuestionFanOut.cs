using System.Diagnostics;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Outlining;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Feature;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>Everything one fan-out is given beside the record it fills.</summary>
/// <param name="HeadSha">The checkout's HEAD, for the outline and the api rows' source turns; empty on a repository with no commit.</param>
/// <param name="FollowUps">How many source turns an api row may have (<c>COAI_FEATURE_SOURCE_FOLLOWUPS</c>).</param>
/// <param name="Nonce">The question's fence nonce — one per question, on every row's material and every answer's fence.</param>
public sealed record FanOutInput(
    QuestionConsultSettings Settings,
    IReadOnlyList<ProviderSettings> Reviewers,
    Core.Api.ApiOverrides Overrides,
    string Question,
    string Context,
    string Repo,
    string HeadSha,
    int FollowUps,
    string Nonce);

/// <summary>
/// The fan-out (PLAN_question_consultant.md, §4 and S2): every active row admitted through the matrix,
/// given what its capability allows and nothing more (D10), launched in PARALLEL under its own deadline
/// — no scheduler cap, no repository lock (D6) — and persisted as it settles, so the sidebar sees
/// per-model progress; a heartbeat every thirty seconds while anything runs (D14 d); the filesystem
/// invariant around any disk row, a breach withholding the disk rows' advice alone.
/// </summary>
/// <remarks>
/// <para><b>What each row is given</b> is decided here and in one place each: a <c>web</c> row the
/// question through <see cref="WebQuestionSanitiser"/> and nothing else (A2 — <see cref="QuestionPrompt"/>
/// refuses anything more by name); a <c>none</c> or <c>disk</c> row the context through
/// <see cref="SecretCheck"/> (refused, never redacted); a <c>none</c> row the OUTLINE of what exists at
/// HEAD, built ONCE per question and timed, since six rows would otherwise build six; an <c>api</c> row
/// its source turns on top (A9).</para>
/// <para><b>The record is written before the first launch</b> with the pid and a heartbeat, and the
/// writes of six rows settling at once are serialised: the record in memory is replaced whole under one
/// gate and written through the store's one road, so the projection sees every state.</para>
/// </remarks>
public sealed class QuestionFanOut(
    IProcessLauncher launcher,
    ReviewerExecutor executor,
    QuestionConsultStore store,
    RolePrompts prompts,
    UsageLedger ledger,
    VaultKeys keys,
    PanelSettings panel,
    ISourceOutliner outliner,
    string schemaFile,
    Func<string, string?> env,
    Serilog.ILogger log,
    TimeSpan? heartbeatEvery = null,
    QuestionOutlineCache? outlines = null,
    Func<string, string, CancellationToken, Task<string>>? buildOutline = null)
{
    private readonly QuestionRowLaunch _launch = new(executor, ledger, log);

    /// <summary>How often the heartbeat is rewritten — the store's thirty seconds, or a test's seam (D14 d).</summary>
    private readonly TimeSpan _heartbeatEvery = heartbeatEvery ?? QuestionConsultStore.HeartbeatEvery;
    private readonly FilesystemInvariant _invariant = new(launcher);

    /// <summary>Where outlines are kept — the process's one cache, or a test's (S4b item 11).</summary>
    private readonly QuestionOutlineCache _outlines = outlines ?? QuestionOutlineCache.Shared;

    /// <summary>Runs every row of <paramref name="consulting"/>'s question and answers the settled record.</summary>
    public async Task<QuestionConsultRecord> RunAsync(QuestionConsultRecord consulting, FanOutInput input, CancellationToken ct)
    {
        var admissions = input.Settings.Rows.Select(row => QuestionAdmission.Admit(row, input.Settings, input.Reviewers, input.Overrides)).ToList();
        var admitted = admissions.OfType<RowAdmission.Admitted>().ToList();
        var inputs = admitted.Select(row => RowInputFor(row, input)).ToList();

        var current = new Current(store, consulting with
        {
            Status = QuestionConsultStatuses.Consulting,
            RunnerPid = Environment.ProcessId,
            HeartbeatUtc = QuestionConsultStore.Stamp(DateTime.UtcNow),
            UpdatedUtc = QuestionConsultStore.Stamp(DateTime.UtcNow),
            Rows = [.. admissions.Select(a => StartingRow(a, inputs))],
        });
        // The record says `consulting`, with the pid, BEFORE anything is launched (S2 acceptance 3).
        store.Write(current.Record);
        // Where a codex row's -o file and the shims' prompt and answer files land — swept on the record's clock.
        Directory.CreateDirectory(store.AnswersDir);

        var launches = inputs.Where(i => i is not RowInput.Refused).ToList();
        // Started now and awaited by the api none rows alone (S4b item 11): every other row launches without it.
        var outline = launches.Any(i => i is RowInput.AfterOutline) ? OutlineAsync(input, ct) : Task.FromResult(string.Empty);
        var invariant = await SnapshotsAsync(launches, ct);
        await LaunchAllAsync(current, launches, outline, ct);
        var breach = await BreachAsync(invariant.Watched, ct);

        var settled = Settled(current.Record, launches, breach, invariant.Unwatched);
        store.Write(settled);

        return settled;
    }

    // ---------- admission → what each row is given ----------

    /// <summary>
    /// The outline of what exists at HEAD for the api <c>none</c> rows (A9) — from the cache when this repository and
    /// HEAD were outlined already, built otherwise (S4b item 11) — and timed, in the log.
    /// </summary>
    /// <remarks>
    /// <b>Material, not a precondition</b>, and awaited by nobody but the api rows: whatever stops it — a git that
    /// refuses, a build another question started and its caller cancelled — leaves those rows the context and the
    /// question, said in the log. Only this question's own cancellation travels on.
    /// </remarks>
    private async Task<string> OutlineAsync(FanOutInput input, CancellationToken ct)
    {
        if (input.HeadSha.Length == 0)
        {
            return string.Empty;
        }

        var started = Stopwatch.StartNew();
        try
        {
            var outline = await _outlines.GetAsync(input.Repo, input.HeadSha, () => (buildOutline ?? BuildOutlineAsync)(input.Repo, input.HeadSha, ct));
            log.Information("question outline of {Repo} at {Head}: {Bytes} bytes, ready in {Ms} ms",
                input.Repo, input.HeadSha[..Math.Min(8, input.HeadSha.Length)], outline.Length, started.ElapsedMilliseconds);

            return outline;
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            log.Warning(e, "question outline of {Repo} could not be built; the api none rows answer without it", input.Repo);

            return string.Empty;
        }
    }

    /// <summary>The outline of what exists at HEAD, as the feature gate's outline builder makes it — what the cache keeps.</summary>
    private async Task<string> BuildOutlineAsync(string repo, string head, CancellationToken ct)
    {
        var started = Stopwatch.StartNew();
        var outline = await new FeatureOutlineBuilder(launcher, outliner).BuildAtHeadAsync(repo, head, ct: ct);
        log.Information("question outline of {Repo} at {Head}: {Files} file(s), {Bytes} bytes, built in {Ms} ms",
            repo, head[..Math.Min(8, head.Length)], outline.Files.Count, outline.Section.Length, started.ElapsedMilliseconds);

        return outline.Section;
    }

    /// <summary>What one admitted row is given, by its capability — or the refusal that stops it before any launch.</summary>
    private RowInput RowInputFor(RowAdmission.Admitted row, FanOutInput input)
    {
        var instruction = prompts.Written(row.Prompt.Id) is { Length: > 0 } written ? written : row.Prompt.Text;

        // A web row is given strictly the question (A2): the person's instruction is not the sanitiser's to vet, so it
        // reaches every OTHER row only — after the base prompt, before what the row is shown (C2).
        return row.Prompt.Capability == Capability.Web
            ? WebInput(row, input, instruction)
            : CheckedInput(row, input, instruction.TrimEnd() + "\n\n" + Core.Catalog.PersonInstruction.ConsultantSection(row.Provider.SystemPrompt.Trim()));
    }

    /// <summary>A web row: the sanitised question and NOTHING else (A2).</summary>
    private static RowInput WebInput(RowAdmission.Admitted row, FanOutInput input, string instruction) =>
        WebQuestionSanitiser.Check(input.Question, new WebQuestionContext(input.Repo, input.Settings.Roots)) switch
        {
            WebQuestion.Clean clean => new RowInput.Launch(row, QuestionPrompt.Compose(new QuestionPromptInput(
                instruction, Capability.Web, clean.Text, CheckedContext.Empty, string.Empty, [], input.Nonce))),
            WebQuestion.Refused refused => new RowInput.Refused(row, $"the web row was given nothing ({refused.Class}): {refused.Reason} — {refused.Cure}"),
            _ => throw new InvalidOperationException("the union is closed"),
        };

    /// <summary>
    /// A none or disk row: the question AND the context after the secret check (S4b item 1 — the question reaches
    /// these rows too), the roots (disk) — and an api row through its own composer, a <c>none</c> one WITH the outline
    /// of what exists, composed once that outline is ready (A9, S4b item 11).
    /// </summary>
    private RowInput CheckedInput(RowAdmission.Admitted row, FanOutInput input, string instruction)
    {
        if (SecretCheck.Inspect(input.Question, "question") is SecretCheckResult.Refused question)
        {
            return new RowInput.Refused(row, $"the question was not sent ({question.Class}): {question.Reason} — {question.Cure}");
        }

        if (SecretCheck.Inspect(input.Context) is not SecretCheckResult.Clean clean)
        {
            var refused = (SecretCheckResult.Refused)SecretCheck.Inspect(input.Context);

            return new RowInput.Refused(row, $"the context was not sent ({refused.Class}): {refused.Reason} — {refused.Cure}");
        }

        if (row.Runtime is ApiConsultant api)
        {
            return row.Prompt.Capability == Capability.None
                ? new RowInput.AfterOutline(row, outline => ApiLaunch(row, api, input, instruction, clean.Context, outline))
                : ApiLaunch(row, api, input, instruction, clean.Context, string.Empty);
        }

        // A CLI row is given no outline (A9 names the api rows): building one cost every question six seconds before
        // ANY row launched (S4b item 11).
        return new RowInput.Launch(row, QuestionPrompt.Compose(new QuestionPromptInput(
            instruction, row.Prompt.Capability, input.Question, clean.Context, string.Empty,
            row.Prompt.Capability == Capability.Disk ? input.Settings.Roots : [], input.Nonce)));
    }

    /// <summary>An api row's launch: its material (the outline, the source turns) on the runtime, and the prompt that names them.</summary>
    private RowInput.Launch ApiLaunch(RowAdmission.Admitted row, ApiConsultant api, FanOutInput input, string instruction, CheckedContext context, string outline) =>
        new(row with { Runtime = api.With(new QuestionMaterial(outline, SourceTurnsFor(input))) },
            ApiQuestionPrompt.Compose(new ApiQuestionInput(instruction, input.Question, context, outline, input.FollowUps, input.Nonce)));

    private SourceTurns SourceTurnsFor(FanOutInput input) =>
        input.HeadSha.Length > 0 && input.FollowUps > 0
            ? new SourceTurns.On(new SourceResolver(new GitHistory(launcher), outliner, input.Repo, input.HeadSha), input.FollowUps)
            : SourceTurns.None;

    /// <summary>The row as the record first carries it: consulting when it will launch, terminal with its word when it will not.</summary>
    private static QuestionRowRecord StartingRow(RowAdmission admission, IReadOnlyList<RowInput> inputs)
    {
        var row = admission switch
        {
            RowAdmission.Admitted a => Shape(a.Row, a.Prompt, a.Provider, RuntimeResolution.NameOf(a.Provider.Identity()), a.Plan.Flag),
            RowAdmission.Refused r => Shape(r.Row, r.Prompt, null, r.Row.Runtime, AdmissionFlag.None) with { Status = r.Status, Reason = r.Reason, EndedUtc = QuestionConsultStore.Stamp(DateTime.UtcNow) },
            _ => throw new InvalidOperationException("the union is closed"),
        };

        return inputs.OfType<RowInput.Refused>().FirstOrDefault(refused => refused.Admitted.Row.Id == row.RowId) is { } stopped
            ? row with { Status = RowOutcomes.Refused, Reason = stopped.Reason, EndedUtc = QuestionConsultStore.Stamp(DateTime.UtcNow) }
            : row;
    }

    private static QuestionRowRecord Shape(QuestionRow row, QuestionPromptDefinition? prompt, ProviderSettings? provider, string runtime, AdmissionFlag flag) =>
        new(row.Id, row.Vendor, provider?.Model ?? row.Model, runtime, row.Prompt, prompt?.Title ?? string.Empty,
            prompt?.Capability.Spelled() ?? string.Empty, flag.Spelled());

    // ---------- the launches, in parallel ----------

    private async Task LaunchAllAsync(Current current, IReadOnlyList<RowInput> launches, Task<string> outline, CancellationToken ct)
    {
        if (launches.Count == 0)
        {
            return;
        }

        using var beating = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeat = HeartbeatAsync(current, _heartbeatEvery, beating.Token);
        try
        {
            // Every admitted row at once (D6): no BoundedScheduler, no RepositoryLock — the rows read, and a
            // cap would turn six rows into two waves and break the five-minute promise.
            await Task.WhenAll(launches.Select(launch => RunOneAsync(current, launch, outline, ct)));
        }
        finally
        {
            await beating.CancelAsync();
            await heartbeat;
        }
    }

    private async Task RunOneAsync(Current current, RowInput planned, Task<string> outline, CancellationToken ct)
    {
        // An api none row waits here, for its outline alone — the row's budget starts at its launch, as before.
        var launch = planned is RowInput.AfterOutline after ? after.Compose(await outline) : (RowInput.Launch)planned;
        var start = current.Record.Rows.First(r => r.RowId == launch.Row.Row.Id);
        var input = new RowLaunchInput(launch.Row, launch.Prompt, SettingsFor(launch.Row), current.Record.RepoPath, store.AnswersDir, schemaFile, panel.QuestionConsult.RowBudget);
        var settled = await _launch.RunAsync(input, start, ct);
        // Persisted as it settles, so the sidebar sees per-model progress (S2 acceptance 2).
        await current.UpdateAsync(record => record.WithRow(settled) with { UpdatedUtc = QuestionConsultStore.Stamp(DateTime.UtcNow) });
        log.Information("question {Id}: row {Row} ({Vendor}, {Prompt}) ended {Status} in {Seconds}s", current.Record.Id, settled.RowId, settled.Vendor, settled.PromptId, settled.Status, settled.Seconds);
    }

    /// <summary>D14 (d): the heartbeat the sweep reads, rewritten every thirty seconds while anything runs.</summary>
    private static async Task HeartbeatAsync(Current current, TimeSpan every, CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(every, stop);
                await current.UpdateAsync(record => record with { HeartbeatUtc = QuestionConsultStore.Stamp(DateTime.UtcNow) });
            }
        }
        catch (OperationCanceledException)
        {
            // The rows settled, which is how the heartbeat ends.
        }
    }

    /// <summary>One row's launch settings — the roster's shape (<c>RosterBuilder.SettingsFor</c>), with the row's own budget.</summary>
    private ReviewerSettings SettingsFor(RowAdmission.Admitted row)
    {
        var provider = row.Provider;
        var settings = new ReviewerSettings(provider.Provider)
        {
            ExecutablePath = provider.ExecutablePath,
            Model = provider.Model,
            // By the row's KEY NAME — its id unless it names another (S3.6); the api row's VaultKeyName is the same name.
            ApiKey = keys.Keys.GetValueOrDefault(provider.KeyName, string.Empty),
            Dialect = provider.Dialect,
            Price = provider.Price,
            // The question consultant's own per-row budget, not the catalog row's timeout: the store's sweep and the fan-out's
            // deadline are both derived from it, so a row that outlived it would be swept as stale while it still ran.
            Timeout = panel.QuestionConsult.RowBudget,
            // The row's CLI effort by the reviewers' own rule (todo/PLAN_one_model_catalog.md, C2); an api row's is its module's.
            ReasoningEffort = RosterBuilder.EffortFor(provider, string.Empty),
            DataDir = panel.DataDir,
            // A question row starts no MCP server either (issue #514).
            McpServersToSwitchOff = NoMcpServers.CodexConfigured(env),
            // The row's fast mode, as for a reviewer (todo/PLAN_fast_mode.md).
            Fast = provider.Fast,
        };
        if (row.Runtime is not ApiConsultant)
        {
            return settings;
        }

        var api = ApiRowView.Of(provider, panel.ApiOverrides).Effective;

        return settings.WithApi(api);
    }

    // ---------- the invariant, around the disk rows ----------

    /// <summary>
    /// One snapshot per distinct root of the disk rows that is a git checkout, taken before any launch, once per
    /// fan-out — and the roots that are NOT one, which the invariant cannot fingerprint (S4b item 5: said on every
    /// disk row that reads one, never only in the log).
    /// </summary>
    private async Task<InvariantPlan> SnapshotsAsync(IReadOnlyList<RowInput> launches, CancellationToken ct)
    {
        var watched = new List<Watched>();
        var unwatched = new List<string>();
        foreach (var root in DiskRoots(launches).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                watched.Add(new Watched(root, await _invariant.SnapshotAsync(root, ct)));
            }
            catch (ContextException e)
            {
                log.Warning("question disk root {Root} is not watched by the filesystem invariant: {Reason}", root, e.Message);
                unwatched.Add(root);
            }
        }

        return new InvariantPlan(watched, unwatched);
    }

    private static IEnumerable<string> DiskRoots(IEnumerable<RowInput> launches) =>
        launches.Where(l => l.Admitted.Plan.Grant.Capability == Capability.Disk).SelectMany(l => l.Admitted.Plan.Grant.Roots);

    /// <summary>The sentence a disk row carries for a root the invariant could not watch — on the row, so the record, the reply and the log say it beside the answer.</summary>
    public static string NotWatched(string root) => $"root {root} is not a git checkout: changes there are not watched";

    /// <summary>The second snapshot of every watched root, compared — the sentence when any changed, else empty.</summary>
    private async Task<string> BreachAsync(IReadOnlyList<Watched> watched, CancellationToken ct)
    {
        var changes = new List<TreeChange>();
        foreach (var one in watched)
        {
            try
            {
                changes.AddRange(FilesystemSnapshot.Compare(one.Before, await _invariant.SnapshotAsync(one.Root, ct))
                    .Select(c => new TreeChange($"{one.Root}: {c.Path}", c.What)));
            }
            catch (ContextException e)
            {
                changes.Add(new TreeChange(one.Root, $"the second snapshot could not be taken: {e.Message}"));
            }
        }

        return changes.Count == 0 ? string.Empty : FilesystemSnapshot.Sentence(changes);
    }

    // ---------- the settled record ----------

    /// <summary>
    /// The record as the question ends: a breach withholds the DISK rows' advice alone; a disk row over a root the
    /// invariant could not watch says so; the status is what the rows came to.
    /// </summary>
    private static QuestionConsultRecord Settled(QuestionConsultRecord record, IReadOnlyList<RowInput> launches, string breach, IReadOnlyList<string> unwatched)
    {
        var rows = (breach.Length == 0 ? record.Rows : [.. record.Rows.Select(row => Withheld(row, launches, breach))])
            .Select(row => SaidUnwatched(row, launches, unwatched)).ToList();
        var launched = rows.Where(row => launches.Any(l => l.Admitted.Row.Id == row.RowId)).ToList();
        var answered = launched.Count(r => r.Answered);
        var stamp = QuestionConsultStore.Stamp(DateTime.UtcNow);

        return record with
        {
            Rows = rows,
            Alert = breach,
            Status = answered == 0 ? QuestionConsultStatuses.Failed
                : answered == launched.Count ? QuestionConsultStatuses.Answered
                : QuestionConsultStatuses.Partial,
            Outcome = answered > 0 ? QuestionOutcomes.AnsweredByConsultants : string.Empty,
            UpdatedUtc = stamp,
            EndedUtc = stamp,
        };
    }

    private static QuestionRowRecord Withheld(QuestionRowRecord row, IReadOnlyList<RowInput> launches, string breach) =>
        row.Answered && launches.Any(l => l.Admitted.Row.Id == row.RowId && l.Admitted.Plan.Grant.Capability == Capability.Disk)
            ? row with { Status = RowOutcomes.Failed, Advice = string.Empty, Reason = breach }
            : row;

    /// <summary>A disk row's note for every one of ITS roots the invariant could not watch — its answer is then never read as one the invariant stood behind.</summary>
    private static QuestionRowRecord SaidUnwatched(QuestionRowRecord row, IReadOnlyList<RowInput> launches, IReadOnlyList<string> unwatched)
    {
        var said = DiskRoots(launches.Where(l => l.Admitted.Row.Id == row.RowId))
            .Where(root => unwatched.Contains(root, StringComparer.OrdinalIgnoreCase))
            .Select(NotWatched)
            .ToList();

        return said.Count == 0 ? row : row with { Note = string.Join("; ", row.Note.Length > 0 ? said.Prepend(row.Note) : said) };
    }

    /// <summary>A root under the invariant, with its first snapshot.</summary>
    private sealed record Watched(string Root, FilesystemSnapshot Before);

    /// <summary>What the invariant could fingerprint before the launches, and the disk roots it could not.</summary>
    private sealed record InvariantPlan(IReadOnlyList<Watched> Watched, IReadOnlyList<string> Unwatched);

    /// <summary>The record in memory, replaced whole under one gate and written through the store's one road — six rows settle at once.</summary>
    private sealed class Current(QuestionConsultStore store, QuestionConsultRecord record)
    {
        private readonly SemaphoreSlim _gate = new(1, 1);

        public QuestionConsultRecord Record { get; private set; } = record;

        public async Task UpdateAsync(Func<QuestionConsultRecord, QuestionConsultRecord> change)
        {
            await _gate.WaitAsync();
            try
            {
                Record = change(Record);
                store.Write(Record);
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}

/// <summary>
/// What one admitted row is given: a prompt to launch, a launch composed once the outline is ready (an api <c>none</c>
/// row, S4b item 11), or the refusal that stops it (the sanitiser's, the secret check's).
/// </summary>
public abstract record RowInput
{
    public sealed record Launch(RowAdmission.Admitted Row, string Prompt) : RowInput;

    /// <summary>An api <c>none</c> row: composed with the outline when it arrives — the other rows do not wait for it.</summary>
    public sealed record AfterOutline(RowAdmission.Admitted Row, Func<string, Launch> Compose) : RowInput;

    public sealed record Refused(RowAdmission.Admitted Row, string Reason) : RowInput;

    private RowInput() { }

    /// <summary>The admitted row this input is for, whichever case it is.</summary>
    public RowAdmission.Admitted Admitted => this switch
    {
        Launch launch => launch.Row,
        AfterOutline after => after.Row,
        Refused refused => refused.Row,
        _ => throw new InvalidOperationException("the union is closed"),
    };
}
