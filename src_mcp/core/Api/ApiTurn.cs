namespace CoaiMcp.Core.Api;

/// <summary>One completion turn, as a vendor module is asked to spell it.</summary>
/// <param name="Model">Empty is legal — the endpoint picks.</param>
/// <param name="Prompt">The whole review prompt: role, rules, plan or diff, and a follow-up's tail.</param>
/// <param name="SchemaJson">The finding schema, verbatim.</param>
/// <param name="Seed">Per prompt (<c>LocalAsk.SeedFor</c>), sent only by a row that sends seeds.</param>
/// <param name="Effort">What was configured; the module's row decides what is sent.</param>
/// <param name="MaxTokens">The configured ceiling; the module's row may raise it to its floor.</param>
/// <param name="ThinkingOn">Whether the model is to think — off only on a module that has a switch, and only when a row said so.</param>
/// <param name="Stream">Whether the answer is asked for as a stream (todo/PLAN_api_streaming.md) — only when a row said so.</param>
public sealed record ApiTurn(
    string Model,
    string Prompt,
    string SchemaJson,
    int Seed,
    string Effort = "",
    int MaxTokens = ApiDefaults.PanelMaxTokens,
    bool ThinkingOn = true,
    bool Stream = false);

/// <summary>What an HTTP answer means to the shim — the four things it can do about one.</summary>
public enum ApiOutcome
{
    /// <summary>A 2xx: a billed completion, to be read.</summary>
    Answered,

    /// <summary>The key was refused — 401, 403, or a 400 whose error says the key is wrong (xAI).</summary>
    KeyRefused,

    /// <summary>A 429 or a 503: try again later, in the words <c>RateLimit.Hit</c> reads.</summary>
    RateLimited,

    /// <summary>Anything else: a failed request.</summary>
    Failed,
}
