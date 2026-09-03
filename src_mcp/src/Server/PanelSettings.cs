using CoaiMcp.Core.Context;
using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>One reviewer's configuration as the server sees it.</summary>
/// <param name="Provider">Its id: what names it in the panel, in the logs, and in the vault entry.</param>
public sealed record ProviderSettings(string Provider)
{
    public bool Enabled { get; init; } = true;

    public string Model { get; init; } = string.Empty;

    public string ExecutablePath { get; init; } = string.Empty;

    /// <summary>Which CLI shape drives it — `codex` or `gemini`.</summary>
    public string Runtime { get; init; } = string.Empty;

    /// <summary>An OpenAI-compatible endpoint, for a vendor riding the Codex CLI. Empty = built in.</summary>
    public string BaseUrl { get; init; } = string.Empty;
}

/// <summary>
/// Everything the extension's settings UI will eventually own. Until that loopback exists
/// (epic 05), the environment is the configuration surface — variables, not call sites, so
/// changing behaviour is a config edit and a client restart.
/// </summary>
public sealed record PanelSettings
{
    /// <remarks>
    /// Antigravity rather than Gemini since 2026-09-01: Google retired Code Assist for individual
    /// accounts, and the Gemini CLI now refuses before it reaches a model. The adapter for its
    /// replacement had shipped a day earlier and nothing used it — supporting a vendor and
    /// DEFAULTING to it are different changes, and only the first one had been made.
    /// </remarks>
    public IReadOnlyList<ProviderSettings> Providers { get; init; } =
        [new("codex"), new("antigravity"), new("deepseek") { Enabled = false }];

    public PanelConfig Rounds { get; init; } = new();

    public int GlobalConcurrency { get; init; } = 3;

    public int PerProviderConcurrency { get; init; } = 2;

    public TimeSpan ReviewerTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>How long a rate-limited reviewer waits before its one retry.</summary>
    public TimeSpan RateLimitBackoff { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long an escalation waits for a person before answering "nobody answered yet".
    /// </summary>
    /// <remarks>
    /// Thirty minutes, and the fallback is the family's: the main AI then asks in the chat, the
    /// same shape `remote-ask.md` prescribes on `no_answer_yet`. Waiting forever would hand the
    /// decision to whichever MCP client's own timeout fires first, with nothing said about why.
    /// </remarks>
    public TimeSpan EscalationBudget { get; init; } = TimeSpan.FromMinutes(30);



    /// <summary>
    /// Which prompt each role uses on each round — <c>role -> [round1, round2, ...]</c>, by
    /// catalog id. An empty or unknown entry falls back to the rotation or the universal prompt.
    /// </summary>
    /// <summary>
    /// Deal the PLAN stage's lenses across the vendors instead of giving every vendor the same one.
    /// </summary>
    /// <remarks>
    /// <para>Opt-in, and off by default, because of what it trades. With it off — the shipped
    /// behaviour — every vendor answers the same question and <c>FindingDedup</c> merges what they
    /// agree on, which is the strongest signal this product produces. With it on every lens gets
    /// asked once instead, at half the launches, and that agreement is gone.</para>
    /// <para>Two flags rather than one because the stages are not alike: a plan has three lenses for
    /// one role, a code round has three roles.</para>
    /// </remarks>
    public bool DealPlanLenses { get; init; }

    public bool DealCodeLenses { get; init; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> PromptsPerRound { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>
    /// Spend the rounds on DIFFERENT lenses instead of asking the same broad question again.
    /// </summary>
    /// <remarks>
    /// Off by default: rotation changes what a second round means, and a person who has not asked
    /// for it should get the prompt they last read in the panel.
    /// </remarks>

    /// <summary>Where sessions, prompts overrides and round artifacts live.</summary>
    public string DataDir { get; init; } = DefaultDataDir;

    /// <summary>
    /// What a LOCAL reviewer is told about thinking: <c>none</c> by default, a level to ask for it,
    /// or <c>engine</c> to say nothing and take the engine's own default.
    /// </summary>
    /// <remarks>
    /// <para><b>Measured 2026-09-02.</b> Gemma4 26B on Ollama answered the planted-defect plan once
    /// in 171 s and, on the identical request, once filled a 64k context with 110 000 characters of
    /// <c>reasoning</c> and returned an empty <c>content</c> after 1056 s. Unbounded thinking that
    /// outruns the context is a review that never arrives, and one in two is not a reviewer.</para>
    /// <para>The escape was found in dew_flow_rag_qln first (<c>AiRuntimeOptions.ReasoningEffort</c>,
    /// 2026-08-11): on Ollama's OpenAI route <c>think:false</c> is ignored and <c>"low"</c> still burns
    /// the budget; only <c>"none"</c> returns <c>finish_reason: stop</c>. Re-verified here.</para>
    /// </remarks>
    public string LocalReasoningEffort { get; init; } = "none";

    /// <summary>
    /// What a CODE reviewer is launched in: <c>worktree</c> (the default) or <c>none</c>.
    /// </summary>
    /// <remarks>
    /// <para>A hosted CLI is agentic: handed a checkout it explores it, and the measurements put
    /// the cost at roughly 200 000 input tokens for one code round against about 25 000 for a local
    /// reviewer, which receives one composed prompt and has nowhere to go. That is not a fair
    /// comparison of models, it is a comparison of two different questions.</para>
    /// <para><c>none</c> launches the reviewers in an empty directory. The PROMPT does not change —
    /// the diff is assembled from the repository and the written rules are still read from the
    /// worktree, both by this server — so the only thing removed is the ability to go looking for
    /// more. The repair launch has always worked this way, and the plan stage too.</para>
    /// </remarks>
    public string CodeWorkspace { get; init; } = "worktree";

    /// <summary>
    /// Settings whose VALUE this build does not understand, each as a sentence for a person.
    /// </summary>
    /// <remarks>
    /// <para>The panel and the server ship separately and update separately, so a panel newer than
    /// the server writes values the server has never heard of. Falling back is right — refusing to
    /// start over a future policy would be worse — but falling back in SILENCE is what made a
    /// working configuration look broken: the setting was applied, the value was read, and the
    /// behaviour was the old one with nothing anywhere saying why.</para>
    /// <para>Empty is the normal state. A value nobody set is not a mismatch.</para>
    /// </remarks>
    public IReadOnlyList<string> Unrecognised { get; init; } = [];

    public static string DefaultDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "coai-mcp");

    public static PanelSettings FromEnvironment(Func<string, string?> env) => new PanelSettings
    {
        Rounds = new PanelConfig(
            // Three layers, widest first: a ROLE's own keys, then its stage's, then the legacy
            // single pair. Somebody who set a threshold once must not have their gate change under
            // them, and somebody who set a stage must not have to repeat it for three roles.
            Roles: RoleGates(env),
            OnExhausted: PolicyOf(env("COAI_ON_EXHAUSTED"))),
        Unrecognised = UnknownValues(env),
        GlobalConcurrency = IntVar(env, "COAI_MAX_CONCURRENCY", 3),
        PerProviderConcurrency = IntVar(env, "COAI_MAX_PER_PROVIDER", 2),
        ReviewerTimeout = TimeSpan.FromMinutes(IntVar(env, "COAI_REVIEWER_TIMEOUT_MINUTES", 10)),
        RateLimitBackoff = TimeSpan.FromSeconds(IntVar(env, "COAI_RATE_LIMIT_BACKOFF_SECONDS", 15)),
        // Seconds win when set: minutes are the setting a person configures, seconds are for a
        // short budget a test or a scripted run needs. One knob would have had to lie about one
        // of the two.
        EscalationBudget = env("COAI_ESCALATION_SECONDS") is { Length: > 0 }
            ? TimeSpan.FromSeconds(IntVar(env, "COAI_ESCALATION_SECONDS", 30))
            : TimeSpan.FromMinutes(IntVar(env, "COAI_ESCALATION_MINUTES", 30)),
        DataDir = env("COAI_DATA_DIR") is { Length: > 0 } dir ? dir : DefaultDataDir,
        LocalReasoningEffort = env("COAI_LOCAL_REASONING_EFFORT") is { Length: > 0 } effort
            ? effort.Trim().ToLowerInvariant()
            : "none",
        CodeWorkspace = WorkspaceOf(env("COAI_CODE_WORKSPACE")),
        DealPlanLenses = Flag(env, "COAI_DEAL_PLAN") || Flag(env, "COAI_ROTATE_PROMPTS"),
        DealCodeLenses = Flag(env, "COAI_DEAL_CODE") || Flag(env, "COAI_ROTATE_PROMPTS"),
        PromptsPerRound = ParsePromptRounds(env("COAI_PROMPTS_PER_ROUND")),
    }.WithProvidersFrom(env);

    /// <summary>Where a code reviewer runs, or the checkout when this build does not know the name.</summary>
    /// <remarks>
    /// The checkout is the fallback because it is the behaviour that loses nothing: a reviewer given
    /// MORE than it needs still answers the question.
    /// </remarks>
    private static string WorkspaceOf(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "none" => "none",
        _ => "worktree",
    };

    /// <summary>What a policy name means, or Human when this build does not know the name.</summary>
    /// <remarks>
    /// Human is the fallback because it is the end of the range that STOPS: a policy this build
    /// cannot honour must never resolve to proceeding over open findings.
    /// </remarks>
    private static StagePolicy PolicyOf(string? value) => value?.ToLowerInvariant() switch
    {
        "continue" => StagePolicy.Continue,
        "escalate" => StagePolicy.Escalate,
        "good_enough" or "goodenough" => StagePolicy.GoodEnough,
        _ => StagePolicy.Human,
    };

    /// <summary>
    /// The settings whose values this build does not understand, as sentences a person can act on.
    /// </summary>
    /// <remarks>
    /// It names the setting, the value, what happened instead, and that updating the server is the
    /// likely cure — because the likely cause is a panel newer than the server, and "unknown value"
    /// alone sends somebody back into the settings file where the answer is not.
    /// </remarks>
    private static IReadOnlyList<string> UnknownValues(Func<string, string?> env)
    {
        var unknown = new List<string>();
        if (env("COAI_ON_EXHAUSTED") is { Length: > 0 } policy
            && PolicyOf(policy) == StagePolicy.Human
            && !string.Equals(policy, "human", StringComparison.OrdinalIgnoreCase))
        {
            unknown.Add(
                $"COAI_ON_EXHAUSTED is '{policy}', which this server does not know — it is asking a "
                + "person instead. The panel is probably newer than this server: update it in the "
                + "panel's Server section.");
        }

        if (env("COAI_CODE_WORKSPACE") is { Length: > 0 } workspace
            && WorkspaceOf(workspace) == "worktree"
            && !string.Equals(workspace.Trim(), "worktree", StringComparison.OrdinalIgnoreCase))
        {
            unknown.Add(
                $"COAI_CODE_WORKSPACE is '{workspace}', which this server does not know — code "
                + "reviewers are getting the checkout, as they do by default. The values are "
                + "'worktree' and 'none'.");
        }

        return unknown;
    }

    /// <summary>
    /// <c>{"Architecture":["architecture","arch-boundaries"],...}</c> — the panel's per-round
    /// choice. Malformed JSON is no choice at all rather than a half-applied one.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ParsePromptRounds(string? json)
    {
        if (json is not { Length: > 0 })
        {
            return new Dictionary<string, IReadOnlyList<string>>();
        }

        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize(
                json, SettingsJsonContext.Default.DictionaryStringListString);
            // `{"Architecture": null}` is valid JSON and would put a NULL list in the map, which
            // the round then dereferences. Found by the gate reviewing this very commit: a
            // hand-edited settings file could crash every round with no useful message.
            return parsed?
                       .Where(e => e.Value is not null)
                       .ToDictionary(e => e.Key, e => (IReadOnlyList<string>)e.Value)
                   ?? new Dictionary<string, IReadOnlyList<string>>();
        }
        catch (System.Text.Json.JsonException)
        {
            return new Dictionary<string, IReadOnlyList<string>>();
        }
    }

    /// <summary>
    /// The reviewers, from `COAI_VENDORS` — a JSON array, because a comma-separated list cannot
    /// carry a runtime and a base URL, and a second encoding for those would be a format nobody
    /// could read in a config file. `COAI_PROVIDERS` still works for the simple case.
    /// </summary>
    private PanelSettings WithProvidersFrom(Func<string, string?> env)
    {
        if (env("COAI_VENDORS") is { Length: > 0 } json && ParseVendors(json) is { Count: > 0 } vendors)
        {
            // The env variable still answers for a vendor the list did not place. It predates the
            // panel, it is what a scripted or containerised run has, and dropping it the moment a
            // vendor list appeared is what made an executable path unsettable from either side.
            return this with { Providers = [.. vendors.Select(v => WithExecutable(v, env))] };
        }

        var listed = env("COAI_PROVIDERS");
        var providers = (listed is { Length: > 0 }
                ? listed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : ["codex", "antigravity"])
            .Select(p => new ProviderSettings(p.ToLowerInvariant())
            {
                Model = env($"COAI_MODEL_{p.ToUpperInvariant()}") ?? string.Empty,
                ExecutablePath = env($"COAI_EXE_{p.ToUpperInvariant()}") ?? string.Empty,
            })
            .ToList();
        return this with { Providers = providers };
    }

    /// <summary>Where this vendor's CLI is: the list first, then its own env variable.</summary>
    private static ProviderSettings WithExecutable(ProviderSettings vendor, Func<string, string?> env) =>
        vendor.ExecutablePath.Length > 0
            ? vendor
            : vendor with { ExecutablePath = env(ExecutableVariable(vendor.Provider)) ?? string.Empty };

    /// <summary><c>my-claude</c> → <c>COAI_EXE_MY_CLAUDE</c>, the same derivation the key uses.</summary>
    internal static string ExecutableVariable(string provider) =>
        $"COAI_EXE_{provider.ToUpperInvariant().Replace('-', '_').Replace('.', '_')}";

    /// <summary>Malformed JSON is no configuration at all — the caller falls back rather than
    /// running a review with a vendor list somebody half-wrote.</summary>
    internal static List<ProviderSettings> ParseVendors(string json)
    {
        try
        {
            var vendors = System.Text.Json.JsonSerializer.Deserialize(json, SettingsJsonContext.Default.ListVendorDto);
            return vendors is null
                ? []
                : [.. vendors
                    .Where(v => !string.IsNullOrWhiteSpace(v.Id))
                    .Select(v => new ProviderSettings(v.Id!.Trim().ToLowerInvariant())
                    {
                        Runtime = RuntimeOf(v.Runtime),
                        Model = v.Model?.Trim() ?? string.Empty,
                        BaseUrl = v.BaseUrl?.Trim() ?? string.Empty,
                        ExecutablePath = v.ExecutablePath?.Trim() ?? string.Empty,
                    })
                    // One id, one vendor — the extension already refuses a duplicate row, and a
                    // hand-edited settings file is how one reaches the server. The id is the
                    // provider/role key of every reviewer launch, so two rows sharing it would
                    // collide in the round's dictionary before any model ran.
                    .DistinctBy(v => v.Provider)];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Which runtime a configured vendor drives — every one this build knows, not two of them.
    /// </summary>
    /// <remarks>
    /// This used to read "gemini, else codex", which silently ran every <c>claude</c> vendor
    /// through the Codex CLI. It hid behind the id lookup — a vendor CALLED claude was matched by
    /// name before its runtime was consulted — so it only surfaced when someone named one
    /// <c>my-claude</c> and watched codex start. A reviewer that runs the wrong vendor's model is
    /// worse than one that refuses: the panel reports an answer from a model nobody chose.
    /// </remarks>
    private static string RuntimeOf(string? runtime)
    {
        // Unset stays unset, and that is the whole distinction: the id then decides, so a vendor
        // called `gemini` with no runtime field is still a gemini.
        var name = runtime?.Trim().ToLowerInvariant() ?? string.Empty;
        if (name.Length == 0)
        {
            return string.Empty;
        }

        // Membership, not a hand-written list. This WAS a hand-written list — gemini, claude,
        // antigravity, else codex — and `local` never got added to it, so a local vendor became a
        // codex vendor with a base URL, failed the key check that base URLs imply, and was dropped
        // from every round. The panel showed a configured reviewer; the round opened with zero.
        // The extension had the identical defect in its own copy of this set, days earlier.
        //
        // An unknown runtime is still a custom vendor riding the Codex CLI against its own base
        // URL — a deliberate decision kept from the vendor-settings tests, not a fallthrough.
        return Runners.Reviewers.ReviewerRuntimeSelector.RuntimeNames.Contains(name) ? name : "codex";
    }

    /// <summary>
    /// Every role's gate, read widest-first: the role's own keys, its stage's, then the legacy pair.
    /// </summary>
    /// <remarks>
    /// <c>COAI_ROUNDS_ARCHITECTURE</c> / <c>COAI_THRESHOLD_SECURITYRELIABILITY</c> name a role;
    /// <c>COAI_MAX_ROUNDS_CODE</c> / <c>COAI_THRESHOLD_PLAN</c> name a stage; <c>COAI_MAX_ROUNDS</c>
    /// and <c>COAI_GATE_THRESHOLD</c> are the originals and still fill in for everything.
    /// </remarks>
    private static Dictionary<string, RoleGate> RoleGates(Func<string, string?> env)
    {
        var gates = new Dictionary<string, RoleGate>();
        foreach (var role in PanelConfig.AllRoles)
        {
            var isPlan = role == "PlanCritique";
            var stage = isPlan ? "PLAN" : "CODE";
            var shipped = isPlan ? PanelConfig.PlanDefault : PanelConfig.CodeDefault;
            var key = role.ToUpperInvariant();

            var rounds = IntVar(env, $"COAI_ROUNDS_{key}",
                IntVar(env, $"COAI_MAX_ROUNDS_{stage}",
                    IntVar(env, "COAI_MAX_ROUNDS", shipped.MaxRounds)));
            var threshold = CountVar(env, $"COAI_THRESHOLD_{key}",
                CountVar(env, $"COAI_THRESHOLD_{stage}",
                    CountVar(env, "COAI_GATE_THRESHOLD", shipped.Threshold)));
            gates[role] = new RoleGate(rounds, threshold);
        }

        return gates;
    }

    private static bool Flag(Func<string, string?> env, string name) =>
        env(name) is "1" or "true" or "TRUE" or "True";

    private static int IntVar(Func<string, string?> env, string name, int fallback) =>
        int.TryParse(env(name), out var value) && value > 0 ? value : fallback;

    /// <summary>
    /// A count where ZERO is a legitimate value — a threshold, unlike a round budget.
    /// </summary>
    /// <remarks>
    /// <see cref="IntVar"/> requires a positive number, which is right for rounds and concurrency and
    /// wrong for a threshold: zero means "any gating finding blocks", the panel has always accepted
    /// it and has a test saying so, and the server silently substituted its own default. The two
    /// halves disagreed about a number a person had deliberately set to nothing.
    /// </remarks>
    private static int CountVar(Func<string, string?> env, string name, int fallback) =>
        int.TryParse(env(name), out var value) && value >= 0 ? value : fallback;
}
