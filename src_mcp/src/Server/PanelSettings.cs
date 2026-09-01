using CoaiMcp.Core.Context;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Translation;

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

    /// <summary>The language a person is asked in, and answers in. Defaults to English.</summary>
    public Language Language { get; init; } = Language.English;

    /// <summary>
    /// Which small, fast model translates when the AI did not already write in that language.
    /// </summary>
    /// <remarks>
    /// Antigravity by default: its CLI is usually already signed in, and a flash model answers a
    /// one-sentence job while a person waits. The model is left unset so the CLI picks its own.
    /// This ran through the Gemini CLI until the retirement, which meant the one path a person
    /// actually SEES — the question in their own language — went through a CLI that had stopped
    /// answering. `none` switches translation off and shows the original, which is also what
    /// happens, with a note, when the CLI cannot run.
    /// </remarks>
    public TranslatorSettings Translator { get; init; } = new("antigravity");

    /// <summary>
    /// Which prompt each role uses on each round — <c>role -> [round1, round2, ...]</c>, by
    /// catalog id. An empty or unknown entry falls back to the rotation or the universal prompt.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> PromptsPerRound { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>
    /// Spend the rounds on DIFFERENT lenses instead of asking the same broad question again.
    /// </summary>
    /// <remarks>
    /// Off by default: rotation changes what a second round means, and a person who has not asked
    /// for it should get the prompt they last read in the panel.
    /// </remarks>
    public bool RotatePrompts { get; init; }

    /// <summary>Where sessions, prompts overrides and round artifacts live.</summary>
    public string DataDir { get; init; } = DefaultDataDir;

    public static string DefaultDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "coai-mcp");

    public static PanelSettings FromEnvironment(Func<string, string?> env) => new PanelSettings
    {
        Rounds = new PanelConfig(
            // The legacy keys become the DEFAULT for both stages rather than being dropped:
            // somebody who set a threshold once must not have their gate silently change under them.
            Plan: new StageGate(
                IntVar(env, "COAI_MAX_ROUNDS_PLAN", IntVar(env, "COAI_MAX_ROUNDS", PanelConfig.PlanDefault.MaxRounds)),
                IntVar(env, "COAI_THRESHOLD_PLAN", IntVar(env, "COAI_GATE_THRESHOLD", PanelConfig.PlanDefault.Threshold))),
            Code: new StageGate(
                IntVar(env, "COAI_MAX_ROUNDS_CODE", IntVar(env, "COAI_MAX_ROUNDS", PanelConfig.CodeDefault.MaxRounds)),
                IntVar(env, "COAI_THRESHOLD_CODE", IntVar(env, "COAI_GATE_THRESHOLD", PanelConfig.CodeDefault.Threshold))),
            OnExhausted: env("COAI_ON_EXHAUSTED")?.ToLowerInvariant() switch
            {
                "continue" => StagePolicy.Continue,
                "escalate" => StagePolicy.Escalate,
                _ => StagePolicy.Human,
            }),
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
        Language = Language.For(env("COAI_LANGUAGE")),
        Translator = new TranslatorSettings(env("COAI_TRANSLATOR_PROVIDER") is { Length: > 0 } tp ? tp : "antigravity")
        {
            Model = env("COAI_TRANSLATOR_MODEL") ?? string.Empty,
            ExecutablePath = env("COAI_TRANSLATOR_EXE") ?? string.Empty,
        },
        DataDir = env("COAI_DATA_DIR") is { Length: > 0 } dir ? dir : DefaultDataDir,
        RotatePrompts = env("COAI_ROTATE_PROMPTS") is "1" or "true" or "TRUE",
        PromptsPerRound = ParsePromptRounds(env("COAI_PROMPTS_PER_ROUND")),
    }.WithProvidersFrom(env);

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
                    })];
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
    private static string RuntimeOf(string? runtime) => runtime?.Trim().ToLowerInvariant() switch
    {
        // Unset stays unset, and that is the whole distinction: the id then decides, so a vendor
        // called `gemini` with no runtime field is still a gemini.
        null or "" => string.Empty,
        "gemini" => "gemini",
        "claude" => "claude",
        "antigravity" => "antigravity",
        // An unknown runtime is a custom vendor riding the Codex CLI against its own base URL —
        // a deliberate decision kept from the vendor-settings tests, not a fallthrough.
        _ => "codex",
    };

    private static int IntVar(Func<string, string?> env, string name, int fallback) =>
        int.TryParse(env(name), out var value) && value > 0 ? value : fallback;
}
