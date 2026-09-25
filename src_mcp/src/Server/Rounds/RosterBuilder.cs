using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// A round's roster: which vendors are dealt which roles with which prompts, and who could not
/// be given one and why.
/// </summary>
/// <remarks>
/// What it cannot decide on its own is handed to it rather than looked up: whether a vendor can
/// run at all and on which runtime (the one predicate the round's exclusion sentence reads too),
/// and the sweep of this product's scratch directories.
/// </remarks>
internal sealed class RosterBuilder(
    PanelSettings settings,
    VaultKeys keys,
    RolePrompts prompts,
    ReviewerPrompt reviewerPrompt,
    RemoteProbe remote,
    Func<ProviderSettings, bool> canRun,
    Func<ProviderSettings, IReviewerRuntime?> runtimeFor,
    Action pruneOldAnswerDirs)
{
    private readonly PanelSettings _settings = settings;
    private readonly VaultKeys _keys = keys;
    private readonly RolePrompts _prompts = prompts;
    private readonly ReviewerPrompt _reviewerPrompt = reviewerPrompt;
    private readonly RemoteProbe _remote = remote;
    private readonly Func<ProviderSettings, bool> _canRun = canRun;
    private readonly Func<ProviderSettings, IReviewerRuntime?> _runtimeFor = runtimeFor;
    private readonly Action _pruneOldAnswerDirs = pruneOldAnswerDirs;

    /// <summary>
    /// Every local ROW leads the round, as one group in the shuffle's order.
    /// </summary>
    /// <remarks>
    /// <para><b>Why it leads (issue #155).</b> A local engine is the slowest reviewer in any round —
    /// minutes where a hosted vendor takes tens of seconds — so a round that asks it last spends its
    /// wall-clock watching the hosted reviewers finish and then waiting for this one to begin. The
    /// shuffle above cannot know that: it exists to spread load across shared Team accounts, and it
    /// puts the local row wherever the seed says.</para>
    ///
    /// <para><b>Rows, not providers — and that distinction is the whole correctness of this.</b> The
    /// first version reordered the PROVIDER list, and <c>Everyone</c> then expands each provider into
    /// one row per role, vendor-major. A local vendor serving four roles therefore put four local
    /// rows at the head while every reviewer took a machine slot first, filling every slot with
    /// reviewers that could not run. Found on the code round — and the test could not see it, because
    /// it read the row list through <c>Distinct()</c> on the provider.</para>
    ///
    /// <para><b>ALL of them lead, since 2026-09-23.</b> Until then only one did and the rest went to
    /// the tail, because a waiting local row held a machine slot. That tail is what made
    /// <c>local/2..4</c> wait behind every hosted reviewer while the card sat idle — the half of
    /// issue #155 the operator restated. <see cref="BoundedScheduler"/> now gives a local reviewer a
    /// lane of its own, bounded only by its engine, so there is no slot to hold; and at the head the
    /// local rows meet the engine's FIFO queue together, with no hosted launch between
    /// <c>local/1</c> and <c>local/2</c> (plan round, gemini). See
    /// <c>research/PLAN_the_local_reviewers_have_their_own_lane.md</c>.</para>
    ///
    /// <para>Stable in both directions: the local rows and the hosted rows each keep the shuffle's
    /// relative order, so the fairness it buys is untouched and a replayed seed still replays.</para>
    ///
    /// <para>"Local" is asked of the invocation — <see cref="ReviewerInvocation.IsOnEngine"/> — rather
    /// than re-derived from the settings, so there is one authority: <c>RuntimeResolution</c> chose
    /// the adapter, the adapter said what it contends on, and the scheduler picks its lane by the same
    /// property.</para>
    /// </remarks>
    private static IReadOnlyList<ReviewerWork> LocalRowsFirst(IReadOnlyList<ReviewerWork> rows) =>
        [.. rows.Where(r => r.Invocation.IsOnEngine), .. rows.Where(r => !r.Invocation.IsOnEngine)];

    /// <summary>
    /// The role's own name as the start of a sentence about it — <c>“My role” — </c> — or nothing when it
    /// has none but its id. Issue #338.
    /// </summary>
    private static string NamedAs(RoleCatalog catalog, string role) =>
        catalog.ById(role)?.Name is { } name && !string.IsNullOrWhiteSpace(name) && name != role ? $"“{name.Trim()}” — " : string.Empty;

    /// <summary>Whether a vendor's reviews are run by somebody else's server.</summary>
    internal static bool Remote(ProviderSettings provider) => provider.IsRemote;

    /// <summary>
    /// The prompt this round of this role gets — from the session's CATALOG, not from the compiled
    /// list this product used to have.
    /// </summary>
    /// <remarks>
    /// The rule is unchanged and deliberately so: the person's explicit choice for that round, else
    /// the role's general prompt. What changed is where the roles come from, which is what lets a
    /// role somebody defined be asked anything at all.
    /// </remarks>
    private PromptChoice ChoiceFor(string role, int round) =>
        _settings.Rounds.Catalog.ForRound(
            role,
            round,
            _settings.PromptsPerRound.GetValueOrDefault(role, []));

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
    internal RoundWork BuildWork(
        IReadOnlyList<string> roles,
        string worktreePath,
        string context,
        int round,
        // REQUIRED, and deliberately not last: the review gate pointed out that an optional stage
        // defaults a future caller into code-stage routing with no compile error, which is exactly
        // the class of silent mistake this parameter was introduced to end. A caller that forgets it
        // does not compile. TWO of them since plan 4 — see StageRun, which records why one flag for
        // two questions stopped being honest the moment there were three stages.
        Stage stage,
        bool readsCheckout,
        int seed = 0,
        IReadOnlyList<string>? planPrompts = null,
        bool deal = false)
    {
        // The CATALOG's spelling, and nothing else, from here on. `RolesForRound` already answers
        // with catalog ids, so this changes nothing today — it is the boundary the Team server has
        // at its endpoint and the local path did not: a caller that schedules `architecture` would
        // otherwise carry that spelling into the invocation, the live round, the usage rows, the
        // evidence file and the session record, and a later `Architecture` run would be a second
        // identity for one role. A role the catalog does not know keeps what it was given, which is
        // what the refusals downstream quote back. (codex, this story's code round.)
        roles = [.. roles.Select(r => _settings.Rounds.Catalog.ById(r)?.Id ?? r)];

        var schemaFile = SchemaFile.Ensure(_settings.DataDir);
        var outputDir = Directory.CreateTempSubdirectory("coai-answers-").FullName;
        _pruneOldAnswerDirs();

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
        // The STAGE is told to this method, not guessed inside it. Two guesses were tried and both
        // were wrong in a way tests did not see: `planPrompts is { Count: > 0 }` is empty on an
        // ordinary plan round (the lenses are only dealt when asked for), and reading the ROLES
        // works today only because no code round happens to carry PlanCritique — a coincidence, and
        // the review gate said so. The caller knows which stage it is running; it passes it.
        var fastCode = _settings.CodeWorkspace == "none" && readsCheckout;
        var launchDir = fastCode
            ? Directory.CreateTempSubdirectory("coai-noworkspace-").FullName
            : worktreePath;

        // What the reviewer is standing in, which is NOT the same question as which directory it
        // was pointed at. A plan round's `worktreePath` is an empty scratch directory — the stage
        // checks out nothing at all (RunStageAsync, `needsWorktree: false`) — so a flag derived
        // from the launch directory alone would tell a plan reviewer a repository was there. That
        // is the exact lie this change removed from the prompt files; only a mounted worktree is a
        // checkout.
        var hasCheckout = readsCheckout && !fastCode;

        // Only what can actually run: a vendor whose CLI is missing or whose key is absent is
        // reported by `providers` and left out of the deal rather than dealt work it cannot do.
        //
        // And only what serves THIS stage. Measured over fourteen judged runs
        // (research/RESULTS_vendor_overlap_2026-09-06.md): a local model was 19 % useful on a plan
        // and 3 % on code while writing more findings than both hosted vendors together, so "on for
        // the plan, off for the code" is a setting somebody actually wants.
        var eligible = _settings.Providers.Where(p => p.Serves(stage)).Where(_canRun).ToList();
        if (eligible.Count == 0)
        {
            return new RoundWork([], []);
        }

        // The ORDER the vendors are offered in, which is not a detail once a Team server is in the
        // list. This method builds the round vendor-major and `BoundedScheduler` starts one task per
        // row against a single semaphore, which hands out its slots in the order they were asked
        // for — so this list's order IS the order reviewers reach a shared server. Every client
        // ships the same vendor list, so ten people starting a round at nine in the morning all
        // queue for the first vendor's shared accounts while the second vendor's sit idle.
        //
        // The server's queue is not what needs fixing: `JobStore.TryClaim` is FIFO and a job it
        // never received cannot be claimed early. A fair queue fed in a biased order is fixed at the
        // feeding end.
        //
        // Seeded with the round's own seed rather than freshly random, so two SESSIONS differ while
        // one session replays — the property the deal below already depends on, and the reason an
        // audit log can name a seed somebody is able to reuse.
        //
        // How far the ordering actually reaches, narrowed by the code round: the slots that are FREE
        // when the round opens are taken in list order, deterministically, because each task runs
        // synchronously to its first await. That prefix is what decides which vendor is asked first,
        // which is the whole point. Past it, who gets a RELEASED slot is SemaphoreSlim's business and
        // .NET documents no order for it — so the tail is best-effort rather than a promise.
        var runnable = SeededShuffle.Of(eligible, seed);

        var items = Items(roles, round, planPrompts);
        var work = new List<ReviewerWork>();
        var notAsked = new List<SkippedRole>();
        var excluded = new List<ExcludedRole>();
        // Sets beside the lists rather than a scan of them: one round can carry a role per vendor
        // per lens, and the scan was the round's own quadratic. (codex, story B2's code round.)
        var skipped = new HashSet<string>(StringComparer.Ordinal);
        var refused = new HashSet<(string Provider, string Role)>();
        Assemble(runnable, items, deal, seed, Add, CanCarry);

        return new RoundWork(LocalRowsFirst(work), notAsked, excluded);

        // One sentence per ROLE however many vendors would have carried it: a person reading a round
        // needs to know the role did not run, not that four vendors each did not run it.
        void Skip(string role, string reason)
        {
            if (!skipped.Add(role))
            {
                return;
            }

            notAsked.Add(new SkippedRole(role, reason));
        }

        // And one per (vendor, role), for the same reason one step down: this list is per vendor
        // because the same Team server runs the shipped roles, but a role dealt four lenses was
        // refused four times in identical words. (gemini, story B2's code round.)
        void Exclude(string provider, string role, string reason)
        {
            if (!refused.Add((provider, role)))
            {
                return;
            }

            excluded.Add(new ExcludedRole(provider, role, reason));
        }

        void Add(ProviderSettings provider, string role, string promptId)
        {
            var catalog = _settings.Rounds.Catalog;
            var choice = catalog.PromptById(promptId) ?? catalog.UniversalFor(role);
            if (_runtimeFor(provider) is not { } runtime)
            {
                return;
            }

            // A prompt with no text at all: a role somebody added and never wrote the prompt for.
            // The round runs without it and SAYS so, because a reviewer that silently does not run
            // is a round that reviewed less than it reported. The shipped prompts cannot reach this
            // — their text is embedded in the binary.
            //
            // FIRST, before the vendor question below it. A role with no text has nothing to say to
            // any vendor, and asking the vendor question first meant a person whose only vendor was
            // a Team server was told the server did not know their role — true, and not the thing
            // they could fix. (codex, story B2's code round.)
            if (!_prompts.Has(choice))
            {
                // BY NAME as well as by id (issue #338): a new role's id is minted before it has a name —
                // `Role2` — so the id alone names nothing the person recognises.
                Skip(role, $"{NamedAs(catalog, role)}its prompt '{choice.Id}' has no text — write it at {_prompts.FileToWrite(choice.Id)}");
                return;
            }

            // A role a person defined cannot be sent to a Team server: that server validates the
            // name against the catalog IT was compiled with, so the request comes back a 400 naming
            // roles the person never asked for. Said here, before the launch, rather than read out
            // of a refusal afterwards — and said per (vendor, role), because the same vendor runs
            // the shipped roles perfectly well. Widening the server is plan 3 of this feature.
            //
            // The question is about the ROLE's provenance and is asked of the CATALOG. Asking the
            // prompt — `!choice.BuiltIn` — answered the same today only because composition refuses
            // a custom role a shipped prompt id, which is a second rule holding up the first.
            // (codex and gemini, story B2's code round.)
            // The server's OWN words where it gave any: "this Team server runs A, B — not C" is
            // something a person can act on, and it is true of THAT server rather than of Team
            // servers in general. The sentence used to say "it accepts the five this product ships"
            // for every one of them, which stopped being true the day an operator could add a role.
            if (WhyNotCarried(provider, role) is { } why)
            {
                Exclude(provider.Provider, role, why);
                return;
            }

            var settings = new ReviewerSettings(provider.Provider)
            {
                ExecutablePath = provider.ExecutablePath,
                Model = provider.Model,
                ApiKey = _keys.Keys.GetValueOrDefault(provider.Provider, string.Empty),
                // Only ApiRuntime reads it: which row of shared/api-dialects.json spells the request.
                Dialect = provider.Dialect,
                Timeout = _settings.ReviewerTimeout,
                ReasoningEffort = _settings.LocalReasoningEffort,
                MaxTokens = _settings.LocalMaxTokens,
                // Only RemoteRuntime uses it, to find this machine's token for its Team server.
                DataDir = _settings.DataDir,
                // A reviewer starts no MCP server (issue #514); read per round, so a server added to
                // config.toml is switched off from the next round on.
                McpServersToSwitchOff = NoMcpServers.CodexConfigured(Environment.GetEnvironmentVariable),
            };
            var prompt = _reviewerPrompt.ComposePrompt(choice, context, hasCheckout);
            // The repair is composed with hasCheckout: FALSE always, because the repair launch always
            // runs in repairDir — an empty temp directory, whatever the review was given (see above).
            // Composing it with the REVIEW's mode is what shipped on 2026-09-06: in worktree mode the
            // repair opened by promising a read-only checkout and closed by saying there were no
            // tools, in one prompt, to the reviewer that had already failed once. Found by codex at
            // that change's own code round — which diagnosed it the other way round, as a repair that
            // should promise the tree. The code says otherwise: the repair never has one.
            //
            // The paragraph itself is built before anybody knows which way the first attempt failed,
            // so it covers both. Its second sentence exists because a refused tool produces NO answer
            // at all, and telling that model its JSON was malformed describes a failure it never had.
            var repairPrompt = _reviewerPrompt.ComposePrompt(choice, context, hasCheckout: false) +
                "\n\nYOUR PREVIOUS ATTEMPT DID NOT PRODUCE A USABLE ANSWER."
                + " If it returned text that was not the schema's JSON: return ONLY the JSON object — no fences, no prose."
                + " If it returned nothing because a command or a file read was refused: there are no tools here"
                + " and none are needed — answer from the text above.";
            work.Add(new ReviewerWork(
                runtime.Build(role, prompt, launchDir, schemaFile, outputDir, settings),
                runtime.Build(role, repairPrompt, repairDir, schemaFile, outputDir, settings),
                choice.Id,
                System.Text.Encoding.UTF8.GetByteCount(prompt)));
        }
    }

    /// <summary>
    /// What this round asks: one item per role, or one per unspent lens when the plan stage deals.
    /// </summary>
    /// <remarks>
    /// Pure, and extracted out of `BuildWork` on the code round — twice, by two different reviewers,
    /// for exceeding the doctrine's complexity ceiling. This half was always a value rather than a
    /// step, and reading it as one makes the method above shorter by a branch.
    /// </remarks>
    private List<(string Role, string PromptId)> Items(
        IReadOnlyList<string> roles,
        int round,
        IReadOnlyList<string>? planPrompts) =>
        planPrompts is { Count: > 0 } && roles.Count > 0
            ? [.. planPrompts.SelectMany(id => Lens(roles, id))]
            : [.. roles.Select(role => (Role: role, PromptId: ChoiceFor(role, round).Id))];

    /// <summary>One dealt lens, under the role that owns it — or nothing.</summary>
    /// <remarks>
    /// <para>It went to <c>roles[0]</c>, which was true for exactly as long as a plan round had one
    /// role in it. The round after a person adds a second plan role, the first role is asked every
    /// lens, including the other role's, whose questions it then answers under its own name.</para>
    /// <para>Three outcomes, not two. A lens the catalog gives to a role this round IS running goes
    /// to that role. A lens the catalog does not know at all falls back to the first role, because a
    /// stale pick must never leave a round with nothing to ask. A lens the catalog knows and gives
    /// to a role this round is NOT running is DROPPED — reassigning it would have a reviewer answer
    /// a question written for somebody else and the round report the wrong role as having asked it.
    /// (codex and gemini, story B2's second code round.)</para>
    /// </remarks>
    private IEnumerable<(string Role, string PromptId)> Lens(IReadOnlyList<string> roles, string promptId)
    {
        if (_settings.Rounds.Catalog.PromptById(promptId)?.Role is not { Length: > 0 } owner)
        {
            return [(roles[0], promptId)];
        }

        return roles.FirstOrDefault(r => string.Equals(r, owner, StringComparison.OrdinalIgnoreCase)) is { } scheduled
            ? [(scheduled, promptId)]
            : [];
    }

    /// <summary>
    /// Who is asked what: every vendor every question, or one hand dealt across them.
    /// </summary>
    /// <remarks>
    /// <para>Not dealing is the shipped behaviour, and the reason is worth keeping beside the
    /// branch: every vendor answering every question is what makes two vendors agreeing on a finding
    /// a fact the gate can use. Dealing is opt-in precisely because it gives that up — every lens
    /// gets asked instead of one lens being asked twice, at half the launches.</para>
    /// <para>The `add` callback belongs to the caller because building one reviewer needs a dozen
    /// things this method has no business holding — a schema file, two directories, a vault key.
    /// What is extracted here is the SHAPE of the fan-out, which is the part with the branches.</para>
    /// </remarks>
    private static void Assemble(
        IReadOnlyList<ProviderSettings> runnable,
        IReadOnlyList<(string Role, string PromptId)> items,
        bool deal,
        int seed,
        Action<ProviderSettings, string, string> add,
        Func<ProviderSettings, string, bool> canCarry)
    {
        if (!deal)
        {
            Everyone(runnable, items, add);

            return;
        }

        // Dealt WITHIN the vendors that can carry the role, and the grouping is by that set rather
        // than per item, so items every vendor can take are still spread across all of them.
        // Dealing before asking cost a custom role its whole round: the hand fell to the Team
        // server, the leaf that builds a launch excluded it there, and the local vendor sitting
        // beside it was never offered the work. (gemini, story B2's second code round.)
        foreach (var group in items.GroupBy(i => Carriers(runnable, i.Role, canCarry), StringComparer.Ordinal))
        {
            Hand(runnable, [.. group], group.Key.Split('\0', StringSplitOptions.RemoveEmptyEntries), seed, add);
        }
    }

    /// <summary>Every vendor offered every question — the shipped fan-out.</summary>
    private static void Everyone(
        IReadOnlyList<ProviderSettings> runnable,
        IReadOnlyList<(string Role, string PromptId)> items,
        Action<ProviderSettings, string, string> add)
    {
        foreach (var (provider, item) in runnable.SelectMany(p => items.Select(i => (p, i))))
        {
            add(provider, item.Role, item.PromptId);
        }
    }

    /// <summary>
    /// One group of items, dealt across the vendors that can carry them.
    /// </summary>
    /// <remarks>
    /// With NO carrier every vendor is offered the work anyway, so the leaf records why each of them
    /// could not take it: a round that says nothing about a role is the defect this whole story is
    /// about, and a deal is no excuse for one. Split out of <see cref="Assemble"/> so both stay
    /// inside the complexity the family's C# doctrine allows. (CodeRabbit, this plan's pull request.)
    /// </remarks>
    private static void Hand(
        IReadOnlyList<ProviderSettings> runnable,
        IReadOnlyList<(string Role, string PromptId)> items,
        string[] vendors,
        int seed,
        Action<ProviderSettings, string, string> add)
    {
        if (vendors.Length == 0)
        {
            Everyone(runnable, items, add);

            return;
        }

        foreach (var hand in PromptDeal.Deal([.. items.Select(i => $"{i.Role}|{i.PromptId}")], vendors, seed))
        {
            var parts = hand.Item.Split('|', 2);
            add(runnable.First(p => p.Provider == hand.Vendor), parts[0], parts[1]);
        }
    }

    /// <summary>
    /// Whether this vendor may be given this role at all.
    /// </summary>
    /// <remarks>
    /// One rule, asked in two places: before the deal, so a role is dealt only among the vendors
    /// that can run it, and inside the leaf, so the non-dealing fan-out — where every vendor is
    /// offered everything — still records why one of them was not used. The question is about the
    /// ROLE's provenance, from the catalog: a Team server validates the name against the catalog IT
    /// was compiled with, and a role a person defined is not in it.
    /// </remarks>
    private bool CanCarry(ProviderSettings provider, string role) =>
        WhyNotCarried(provider, role) is null;

    /// <summary>
    /// Why this vendor cannot run this role, or null when it can.
    /// </summary>
    /// <remarks>
    /// <para>A vendor this machine runs itself carries anything: the roles are composed here and the
    /// CLI is told what to ask.</para>
    /// <para>A TEAM SERVER is somebody else's boundary, and it used to be guessed at —
    /// <c>Catalog.ById(role)?.BuiltIn == true</c>, which is "the five this product ships" written as
    /// though it were a fact about the server. It has been true of every Team server until now, and
    /// it stops being true the moment an operator sets <c>Coai:ExtraRoles</c>. So the server is ASKED,
    /// through the catalog the panel already fetches to draw its health, and the answer decides —
    /// with the shipped five as the fallback for a server that has not said, which is exactly the
    /// old behaviour.</para>
    /// </remarks>
    private string? WhyNotCarried(ProviderSettings provider, string role)
    {
        if (!Remote(provider))
        {
            return null;
        }

        var known = _settings.Rounds.Catalog.ById(role);

        // The id is what the server matches; the NAME is what the sentence says. A person who called
        // a role "Requirements we wrote" reads that back rather than the `Requirements` the wire uses.
        return _remote.RolesOn(provider.BaseUrl).WhyNot(role, known?.Name ?? role, known?.BuiltIn == true);
    }

    /// <summary>The vendors that can carry this role, as one key so items group by capability.</summary>
    /// <remarks>
    /// Joined on NUL because a provider name is a person's own word and may hold any punctuation a
    /// separator could have been — a space, a comma, a pipe, even a line break. Written as the
    /// ESCAPE: an edit on this branch put the BYTE itself into the source, where it is invisible,
    /// makes `grep` report the file as binary, and cannot be reviewed by reading it.
    /// </remarks>
    private static string Carriers(
        IReadOnlyList<ProviderSettings> runnable, string role, Func<ProviderSettings, string, bool> canCarry) =>
        string.Join('\0', runnable.Where(p => canCarry(p, role)).Select(p => p.Provider));

    /// <summary>
    /// The roles a code round runs, once the repository has been asked whether it wrote any rules.
    /// </summary>
    /// <remarks>
    /// <para>A conventions pass with nothing to judge against would invent a standard, which is worse
    /// than the review it displaced. That reasoning is older than the role — it used to gate a
    /// round-1 prompt substitution, and it gates the ROLE now.</para>
    /// <para><b>Pure, and extracted for two reasons that arrived together.</b> It was written inline
    /// as `roles.Remove(...)`, which reads tidily — one call that both filters and answers whether it
    /// filtered — and is the exact mutate-in-place shape coding-style.md names as wrong; two gate
    /// reviewers said so. Then a review of the fix pointed out that the branch had no test at all,
    /// which was true and worse: the behaviour is only reachable through a method that needs a
    /// session, a checkout and a git repository. A function is the answer to both.</para>
    /// <para>Derived rather than removed, so nothing observes a list changing under it.</para>
    /// </remarks>
    /// <summary>Why the Conventions reviewers are dropped, in the one place both readers of it look.</summary>
    /// <remarks>
    /// The server's own log line and the sentence the calling AI receives are built from THIS string,
    /// so the two cannot come to describe one decision in two ways — which the plan round asked for
    /// after noticing they were about to be written twice.
    /// </remarks>
    internal const string NoWrittenRules =
        "this repository has no written rules to judge against";

    internal static IReadOnlyList<string> RolesWithRulesInMind(
        IReadOnlyList<string> scheduled,
        bool hasRules) =>
        hasRules ? scheduled : [.. scheduled.Where(r => r != RoleCatalog.ConventionsRole)];

    /// <summary>The roles this round will not ask for, each carrying ITS OWN reason.</summary>
    /// <remarks>
    /// <para>Paired with the filter above rather than mapped over its output, and the code round is
    /// why: taking the difference and giving every omitted role <see cref="NoWrittenRules"/> means
    /// that the day a second filter drops a role for some other cause, the caller is told the wrong
    /// thing with complete confidence. A second reason belongs HERE, beside the rule that produces
    /// it.</para>
    /// <para>Still DERIVED from the difference, so the sentence a caller reads and the roles a round
    /// actually ran cannot disagree — a list written out by hand could.</para>
    /// </remarks>
    internal static IReadOnlyList<SkippedRole> RolesNotAsked(
        IReadOnlyList<string> scheduled,
        bool hasRules)
    {
        var kept = RolesWithRulesInMind(scheduled, hasRules);

        return [.. scheduled
            .Where(r => !kept.Contains(r))
            .Select(r => new SkippedRole(r, NoWrittenRules))];
    }
}
