using System.Text.Json;
using CoaiMcp.Core.Findings;

namespace CoaiMcp.Core.Api;

/// <summary>
/// One completion, read: what it said, what it consumed, how it stopped, how much of it was thought.
/// </summary>
/// <param name="Content">The message content, or null when the endpoint said nothing usable (an empty string is nothing).</param>
/// <param name="Usage">What the vendor billed — reasoning included in the output, whichever field it reported it in.</param>
/// <param name="FinishReason">The vendor's <c>finish_reason</c>, verbatim; empty when it gave none.</param>
/// <param name="ReasoningTokens"><c>completion_tokens_details.reasoning_tokens</c>, or zero when unreported.</param>
public sealed record ChatAnswer(string? Content, Usage Usage, string FinishReason, long ReasoningTokens)
{
    /// <summary>Text that is not a completion: no content, and a usage nobody reported — unknown, not zero.</summary>
    public static readonly ChatAnswer Nothing = new(null, Usage.Unknown, string.Empty, 0);

    /// <summary>Whether the vendor cut this generation at its token ceiling — a fragment, whatever it holds.</summary>
    public bool WasCut => string.Equals(FinishReason, CompletionReader.CutAtTheCeiling, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The one reader of an OpenAI-compatible completion — the local engine's and every hosted vendor's.
/// </summary>
/// <remarks>
/// <para>Moved here from <c>LocalAsk.ReadAnswer</c> with its behaviour intact when the vendor modules
/// were carved out (2026-09-27); <c>ApiVendorGoldensTests</c> holds the numbers it reads off recorded
/// xAI and Alibaba answers. Both conventions it knows were measured, not assumed — see
/// <see cref="Generated"/>.</para>
/// <para><b><see cref="ChatAnswer.FinishReason"/> is read because ignoring it cost a measurement</b>
/// (2026-09-26): on the Alibaba route the token ceiling bounds reasoning PLUS the answer, so a cut
/// arrives as <c>finish_reason: "length"</c> with a fragment — <c>{"</c>, or a whole
/// <c>{"findings":[]}</c> whose real findings were never written — and a reader that looked only at
/// the content took the fragment for the answer, or reported a vendor that "returned no message
/// content" when it had spent 16,382 tokens thinking.</para>
/// </remarks>
public static class CompletionReader
{
    /// <summary>The vendor's finish reason for a generation its own ceiling cut short.</summary>
    public const string CutAtTheCeiling = "length";

    /// <summary>
    /// The whole of what a completion said about itself, or <see cref="ChatAnswer.Nothing"/> for text
    /// that is not a completion.
    /// </summary>
    public static ChatAnswer Read(string response)
    {
        try
        {
            using var parsed = JsonDocument.Parse(response);

            return Read(parsed.RootElement);
        }
        catch (JsonException)
        {
            return ChatAnswer.Nothing;
        }
    }

    private static ChatAnswer Read(JsonElement root)
    {
        // Valid JSON is not the same as an answer. `[]`, `42`, `null` and a bare string all parse, and
        // `TryGetProperty` on a root that is not an object THROWS `InvalidOperationException` — which a
        // catch written for `JsonException` does not catch. An engine answering an array took the round
        // down with it.
        if (root.ValueKind != JsonValueKind.Object)
        {
            return ChatAnswer.Nothing;
        }

        var usage = ReadUsage(root);

        return FirstChoice(root) is { } choice
            ? new ChatAnswer(ContentOf(choice), usage, FinishOf(choice), usage.TokensReasoning)
            : new ChatAnswer(null, usage, string.Empty, usage.TokensReasoning);
    }

    /// <summary>
    /// The first choice when it is an OBJECT, or null — a <c>null</c>, a number or a string there is no choice.
    /// </summary>
    /// <remarks>
    /// Every read below is guarded by the value's KIND, not only its presence: <c>TryGetProperty</c> on a
    /// non-object and <c>TryGetInt64</c> on a non-number THROW <see cref="InvalidOperationException"/>, which
    /// the <c>JsonException</c> catch does not see — and a throw here killed the shim before it printed the
    /// usage of a call the vendor had billed (the calibration branch's code round, codex).
    /// </remarks>
    private static JsonElement? FirstChoice(JsonElement root) =>
        root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0
            && choices[0].ValueKind == JsonValueKind.Object
            ? choices[0]
            : null;

    /// <summary>The message content, or null when there is none — an empty string is none, and so is a message that is not an object.</summary>
    private static string? ContentOf(JsonElement choice) =>
        choice.TryGetProperty("message", out var message)
        && message.ValueKind == JsonValueKind.Object
        && message.TryGetProperty("content", out var text)
        && text.ValueKind == JsonValueKind.String
        && text.GetString() is { Length: > 0 } content
            ? content
            : null;

    private static string FinishOf(JsonElement choice) =>
        choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String
            ? reason.GetString() ?? string.Empty
            : string.Empty;

    private static Usage ReadUsage(JsonElement root)
    {
        // No usage object is a usage the vendor did not REPORT — unknown, never a zero a price turns into $0.
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return Usage.Unknown;
        }

        var prompt = Count(usage, "prompt_tokens");
        var completion = Count(usage, "completion_tokens");

        // Money is null on purpose: a local run has no bill, and 0 would read as free. The reasoning
        // share rides along for the record; the bill is the generated total.
        return new Usage(prompt, Generated(usage, prompt, completion), null, Detail(usage, "prompt_tokens_details", "cached_tokens"),
            TokensReasoning: Detail(usage, "completion_tokens_details", "reasoning_tokens"));
    }

    /// <summary>
    /// What was generated, and so billed as output: <c>completion_tokens</c> — or, when the vendor's own
    /// total says more was generated than the completion holds, <c>total_tokens − prompt_tokens</c>.
    /// </summary>
    /// <remarks>
    /// Two vendors, two conventions, measured 2026-09-26. The Alibaba route counts reasoning INSIDE
    /// <c>completion_tokens</c> (GLM-5.3: completion 20,815 of which reasoning 19,496). xAI counts it
    /// OUTSIDE: grok-4.7 answered <c>prompt 19,681 · completion 1,557 · reasoning 27,728 · total 48,966</c>
    /// and its own <c>cost_in_usd_ticks</c> priced all 29,285 generated tokens as output — so a ledger that
    /// read <c>completion_tokens</c> alone put that call at $0.047 against the vendor's $0.213. The total is
    /// the one number both vendors agree on: prompt plus everything generated.
    /// </remarks>
    private static long Generated(JsonElement usage, long prompt, long completion)
    {
        var total = Count(usage, "total_tokens");

        return total > 0 && total - prompt > completion ? total - prompt : completion;
    }

    /// <summary>A count inside a details object — the cached subset, the reasoning share — or zero when the vendor did not say.</summary>
    private static long Detail(JsonElement usage, string details, string field) =>
        usage.TryGetProperty(details, out var inner) && inner.ValueKind == JsonValueKind.Object ? Count(inner, field) : 0;

    /// <summary>A count the vendor reported as a NUMBER, or zero — a string there is no count.</summary>
    private static long Count(JsonElement element, string field) =>
        element.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var count) ? count : 0;
}

/// <summary>
/// What an HTTP status and body mean, for every OpenAI-compatible vendor — one rule, composed into each
/// module, because every module measured so far answers the same shapes.
/// </summary>
/// <remarks>
/// <para><b>A 400 whose error says the key is wrong is a refused key</b> (PLAN_feature_review.md §9.11).
/// Measured, not documented: S0.5's probe sent a key that was wrong on purpose on 2026-09-26 and xAI
/// answered <c>400</c> with <c>"error": "Incorrect API key provided: …"</c>, where OpenAI answers 401. Read
/// as a plain 400, a revoked Grok key was reported as a malformed request and never sent anybody to the
/// vault. General rather than xAI-only, because the test is narrow enough to be safe everywhere: only the
/// body's own error field is read — <c>error</c> as a string, or <c>error.message</c> / <c>error.code</c> —
/// never the whole text.</para>
/// </remarks>
public static class ApiClassification
{
    /// <summary>What a vendor says when the key itself is wrong — each read off a real answer or a vendor's error code.</summary>
    private static readonly string[] WrongKeyPhrases = ["incorrect api key", "invalid api key", "invalid_api_key"];

    /// <summary>
    /// What a vendor says in a 5xx that clears on a retry — each read off a real answer, never imagined.
    /// </summary>
    /// <remarks>
    /// <b>"Auth context expired."</b> — xAI, 2026-09-27, phase 2 of the reviewer-models measurement: HTTP 500
    /// <c>{"code":"internal","error":"Auth context expired."}</c> on 8 of 20 grok-4.7 reviews, on first calls and
    /// later ones alike, 74–262 s into the generation, with the same conversation key, body and concurrency as
    /// the calls that succeeded before and after it (the key's first use failed once; the same key served three
    /// turns half an hour earlier and later). Nothing on our side correlated; a 500 was a plain failure and the
    /// review was lost. Read the way <see cref="WrongKeyPhrases"/> is: the body's own error field, never the
    /// whole text.
    /// </remarks>
    private static readonly string[] TransientPhrases = ["auth context expired"];

    public static ApiOutcome Of(int status, string body) =>
        IsKeyRefused(status, body) ? ApiOutcome.KeyRefused
        : IsTransient(status, body) ? ApiOutcome.RateLimited
        : status is >= 200 and <= 299 ? ApiOutcome.Answered
        : ApiOutcome.Failed;

    private static bool IsKeyRefused(int status, string body) =>
        status is 401 or 403 || (status == 400 && SaysTheKeyIsWrong(body));

    /// <summary>429 and 503 always; any other 5xx only when its own error field says it is a transient.</summary>
    private static bool IsTransient(int status, string body) =>
        status is 429 or 503 || (status is >= 500 and <= 599 && ErrorFieldSays(body, TransientPhrases));

    /// <summary>Whether a body's own ERROR field says the key is wrong.</summary>
    public static bool SaysTheKeyIsWrong(string body) => ErrorFieldSays(body, WrongKeyPhrases);

    /// <summary>Whether a body's own ERROR field (<c>error</c> as a string, or <c>error.message</c> / <c>error.code</c>) carries one of the phrases.</summary>
    private static bool ErrorFieldSays(string body, string[] phrases)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            return ErrorField(document.RootElement) is { } error
                && ErrorTexts(error).Any(text => phrases.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase)));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static JsonElement? ErrorField(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error) ? error : null;

    /// <summary>The error field's own words: the string itself, or an object's message and code.</summary>
    private static IEnumerable<string> ErrorTexts(JsonElement error) => error.ValueKind switch
    {
        JsonValueKind.String => [error.GetString() ?? string.Empty],
        JsonValueKind.Object =>
            ((string[])["message", "code"])
                .Where(name => error.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
                .Select(name => error.GetProperty(name).GetString() ?? string.Empty),
        _ => [],
    };
}
