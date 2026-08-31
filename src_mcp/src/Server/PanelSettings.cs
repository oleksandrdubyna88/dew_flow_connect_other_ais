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
    public IReadOnlyList<ProviderSettings> Providers { get; init; } =
        [new("codex"), new("gemini"), new("deepseek") { Enabled = false }];

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
    /// Gemini Flash by default because the Gemini CLI is usually already signed in and a flash
    /// model answers a one-sentence job while a person waits. `none` switches translation off and
    /// shows the original — which is also what happens, with a note, when the CLI cannot run.
    /// </remarks>
    public TranslatorSettings Translator { get; init; } = new("gemini") { Model = "gemini-flash-latest" };

    /// <summary>Where sessions, prompts overrides and round artifacts live.</summary>
    public string DataDir { get; init; } = DefaultDataDir;

    public static string DefaultDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "coai-mcp");

    public static PanelSettings FromEnvironment(Func<string, string?> env) => new PanelSettings
    {
        Rounds = new PanelConfig(
            MaxRounds: IntVar(env, "COAI_MAX_ROUNDS", 3),
            Threshold: IntVar(env, "COAI_GATE_THRESHOLD", 2),
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
        Translator = new TranslatorSettings(env("COAI_TRANSLATOR_PROVIDER") is { Length: > 0 } tp ? tp : "gemini")
        {
            Model = env("COAI_TRANSLATOR_MODEL") ?? "gemini-flash-latest",
            ExecutablePath = env("COAI_TRANSLATOR_EXE") ?? string.Empty,
        },
        DataDir = env("COAI_DATA_DIR") is { Length: > 0 } dir ? dir : DefaultDataDir,
    }.WithProvidersFrom(env);

    /// <summary>
    /// The reviewers, from `COAI_VENDORS` — a JSON array, because a comma-separated list cannot
    /// carry a runtime and a base URL, and a second encoding for those would be a format nobody
    /// could read in a config file. `COAI_PROVIDERS` still works for the simple case.
    /// </summary>
    private PanelSettings WithProvidersFrom(Func<string, string?> env)
    {
        if (env("COAI_VENDORS") is { Length: > 0 } json && ParseVendors(json) is { Count: > 0 } vendors)
        {
            return this with { Providers = vendors };
        }

        var listed = env("COAI_PROVIDERS");
        var providers = (listed is { Length: > 0 }
                ? listed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : ["codex", "gemini"])
            .Select(p => new ProviderSettings(p.ToLowerInvariant())
            {
                Model = env($"COAI_MODEL_{p.ToUpperInvariant()}") ?? string.Empty,
                ExecutablePath = env($"COAI_EXE_{p.ToUpperInvariant()}") ?? string.Empty,
            })
            .ToList();
        return this with { Providers = providers };
    }

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
                        Runtime = string.Equals(v.Runtime, "gemini", StringComparison.OrdinalIgnoreCase) ? "gemini" : "codex",
                        Model = v.Model?.Trim() ?? string.Empty,
                        BaseUrl = v.BaseUrl?.Trim() ?? string.Empty,
                    })];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private static int IntVar(Func<string, string?> env, string name, int fallback) =>
        int.TryParse(env(name), out var value) && value > 0 ? value : fallback;
}
