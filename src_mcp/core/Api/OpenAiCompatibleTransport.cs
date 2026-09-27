namespace CoaiMcp.Core.Api;

/// <summary>
/// The part of an OpenAI-compatible vendor that is the same for every one measured so far — the body
/// spelled from a row, the cache-routing header the row names, the one completion reader, the one
/// classification of refusals — composed into each module rather than inherited or branched on.
/// </summary>
/// <remarks>
/// A module adds what its vendor alone does (a thinking switch's spelling, its defaults, its
/// capabilities) and delegates the rest here. <c>ApiVendorGoldensTests</c> pins the bytes this produces
/// for each measured row, recorded before the modules existed.
/// </remarks>
public sealed class OpenAiCompatibleTransport(ApiDialect dialect)
{
    /// <summary>The measured row this transport speaks.</summary>
    public ApiDialect Dialect => dialect;

    /// <summary>The body for a turn, exactly as <c>ChatRequest.Body</c> spells it from the row.</summary>
    public string Body(ApiTurn turn) => Body(turn, dialect);

    /// <summary>The body for a turn from a variant of the row — how a module spells a switch the row has no field for.</summary>
    public string Body(ApiTurn turn, ApiDialect spelledAs) =>
        ChatRequest.Body(spelledAs, turn.Model, turn.Prompt, turn.SchemaJson, turn.Seed, turn.Effort, turn.MaxTokens);

    /// <summary>
    /// The conversation key in the header the row names — what routes every turn of one reviewer's
    /// conversation to the server holding its prompt cache (xAI, 2026-09-26) — or nothing, for a row that
    /// names no header or a launch with no key.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers(string conversation) =>
        dialect.CacheKeyHeader.Length > 0 && conversation.Length > 0
            ? new Dictionary<string, string>(StringComparer.Ordinal) { [dialect.CacheKeyHeader] = conversation }
            : new Dictionary<string, string>(StringComparer.Ordinal);

    public ChatAnswer Read(string response) => CompletionReader.Read(response);

    public ApiOutcome Classify(int status, string body) => ApiClassification.Of(status, body);
}
