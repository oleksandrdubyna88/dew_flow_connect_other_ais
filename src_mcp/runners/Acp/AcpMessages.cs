using System.Text.Json;
using System.Text.Json.Nodes;

namespace CoaiMcp.Runners.Acp;

/// <summary>
/// The JSON-RPC 2.0 lines of the Agent Client Protocol, as this client writes and reads them.
/// </summary>
/// <remarks>
/// <para>Built with <see cref="JsonObject"/> rather than interpolated: a prompt holds quotes, newlines and
/// backslashes, and every shell-quoting failure this project has had came from text composed by hand. Nodes
/// of primitives serialise without reflection, which is what the AOT build requires.</para>
/// <para>Ids are kept as the agent wrote them and echoed back verbatim — a number or a string, the spec
/// allows both — so the client never re-types an id into a shape the agent did not send.</para>
/// </remarks>
internal static class AcpMessages
{
    /// <summary>The protocol version this client speaks — the one Kimi Code 2.1.1 answered (RESULTS §2).</summary>
    public const int ProtocolVersion = 1;

    /// <summary>JSON-RPC: the method is not one this client offers.</summary>
    public const int MethodNotFound = -32601;

    /// <summary>JSON-RPC: the request's parameters were refused — the read boundary's answer.</summary>
    public const int InvalidParams = -32602;

    /// <summary>JSON-RPC: this client could not serve the request for a reason of its own — a handler that threw.</summary>
    public const int InternalError = -32603;

    public static string Request(long id, string method, JsonObject parameters) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters }.ToJsonString();

    public static string Notification(string method, JsonObject parameters) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters }.ToJsonString();

    public static string Result(JsonElement id, JsonObject result) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = JsonNode.Parse(id.GetRawText()), ["result"] = result }.ToJsonString();

    public static string Error(JsonElement id, int code, string message) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonNode.Parse(id.GetRawText()),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        }.ToJsonString();

    /// <summary>
    /// <c>initialize</c>: reads offered, writes and terminals NOT — the three blocks coai owns (D15). The
    /// offer is mandatory: without it Kimi read an outside file itself and quoted it (RESULTS §2.2).
    /// </summary>
    public static JsonObject Initialize() => new()
    {
        ["protocolVersion"] = ProtocolVersion,
        ["clientCapabilities"] = new JsonObject
        {
            ["fs"] = new JsonObject { ["readTextFile"] = true, ["writeTextFile"] = false },
            ["terminal"] = false,
        },
    };

    /// <summary><c>session/new</c>: the working folder, and no MCP server (the #514 rule, D8b).</summary>
    public static JsonObject NewSession(string cwd) => new() { ["cwd"] = cwd, ["mcpServers"] = new JsonArray() };

    public static JsonObject SetConfigOption(string sessionId, string configId, string value) =>
        new() { ["sessionId"] = sessionId, ["configId"] = configId, ["value"] = value };

    public static JsonObject Prompt(string sessionId, string text) => new()
    {
        ["sessionId"] = sessionId,
        ["prompt"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
    };

    public static JsonObject Cancel(string sessionId) => new() { ["sessionId"] = sessionId };

    public static JsonObject Selected(string optionId) =>
        new() { ["outcome"] = new JsonObject { ["outcome"] = "selected", ["optionId"] = optionId } };

    public static JsonObject CancelledOutcome() => new() { ["outcome"] = new JsonObject { ["outcome"] = "cancelled" } };

    public static JsonObject FileContent(string text) => new() { ["content"] = text };

    /// <summary>
    /// The values <c>session/new</c> announced for one config option: <c>configOptions: [{ id, options: [{ value, name }] }]</c>
    /// (the shape the probe read, RESULTS §10). Empty when the result carries no such option, or none of its options has a value —
    /// the caller reads "no list" and "an empty list" as the same refusal, which is what they are.
    /// </summary>
    public static IReadOnlyList<string> AnnouncedValues(JsonElement sessionResult, string optionId) =>
        Child(sessionResult, "configOptions") is { ValueKind: JsonValueKind.Array } options
            ? [.. options.EnumerateArray().Where(option => Text(option, "id") == optionId).SelectMany(Values)]
            : [];

    private static IEnumerable<string> Values(JsonElement option) =>
        Child(option, "options") is { ValueKind: JsonValueKind.Array } offered
            ? offered.EnumerateArray().Select(value => Text(value, "value")).Where(value => value.Length > 0)
            : [];

    /// <summary>A string property, or empty — the agent's optional fields are read without a null in the caller.</summary>
    public static string Text(JsonElement element, string name) =>
        Child(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? string.Empty : string.Empty;

    /// <summary>The most of a JSON-RPC error's <c>data</c> member a reason keeps.</summary>
    public const int ErrorDataCap = 2_000;

    /// <summary>
    /// A JSON-RPC error's words: its <c>message</c>, then its <c>data</c> member when there is one — a string as it is, anything else as compact JSON — as <c>(data: …)</c>,
    /// redacted through the product's one redactor and capped (K5 of the code gate). A server may keep the detail its message leaves out in <c>data</c>: a bare
    /// "Authentication required" carrying the 429 there was read as a lost sign-in.
    /// </summary>
    public static string ErrorText(JsonElement error) =>
        DataText(error) is { Length: > 0 } data ? $"{Text(error, "message")} (data: {Core.Notices.Redaction.SafeText(data, ErrorDataCap)})" : Text(error, "message");

    private static string DataText(JsonElement error) => Child(error, "data") switch
    {
        { ValueKind: JsonValueKind.Undefined or JsonValueKind.Null } => string.Empty,
        { ValueKind: JsonValueKind.String } text => text.GetString() ?? string.Empty,
        var other => other.GetRawText(),
    };

    /// <summary>A number property, or zero — a string where a number was expected is zero too, never a throw in the pump.</summary>
    public static long Number(JsonElement element, string name) =>
        Child(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var number) ? number : 0;

    /// <summary>An object property, or an undefined element.</summary>
    public static JsonElement Child(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
}

/// <summary>One line the agent wrote, classified. The elements are clones, so the line's document is already gone.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Id">The id of a response or a request; undefined for a notification.</param>
/// <param name="Method">The method of a request or a notification; empty for a response.</param>
/// <param name="Params">The params of a request or a notification.</param>
/// <param name="Result">A response's result.</param>
/// <param name="Error">A response's error.</param>
internal sealed record AcpIncoming(AcpIncomingKind Kind, JsonElement Id, string Method, JsonElement Params, JsonElement Result, JsonElement Error)
{
    /// <summary>The line read as a message — or <see cref="AcpIncomingKind.Stray"/> for anything that is not one.</summary>
    public static AcpIncoming Parse(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement.Clone();

            return root.ValueKind == JsonValueKind.Object ? Classified(root) : Stray;
        }
        catch (JsonException)
        {
            return Stray;
        }
    }

    /// <summary>A JSON object read as a message by what it carries: a method and an id, a method alone, an id alone — else stray.</summary>
    private static AcpIncoming Classified(JsonElement root)
    {
        var method = AcpMessages.Text(root, "method");

        return (method.Length > 0, HasId(root, out var id)) switch
        {
            (true, true) => new AcpIncoming(AcpIncomingKind.Request, id, method, AcpMessages.Child(root, "params"), default, default),
            (true, false) => new AcpIncoming(AcpIncomingKind.Notification, default, method, AcpMessages.Child(root, "params"), default, default),
            (false, true) => new AcpIncoming(AcpIncomingKind.Response, id, string.Empty, default, AcpMessages.Child(root, "result"), AcpMessages.Child(root, "error")),
            _ => Stray,
        };
    }

    /// <summary>Whether the message carries an id that is not <c>null</c> — a JSON-RPC notification has none.</summary>
    private static bool HasId(JsonElement root, out JsonElement id) =>
        root.TryGetProperty("id", out id) && id.ValueKind != JsonValueKind.Null;

    private static readonly AcpIncoming Stray = new(AcpIncomingKind.Stray, default, string.Empty, default, default, default);
}

internal enum AcpIncomingKind
{
    /// <summary>Not JSON-RPC: a banner, a log line on the wrong stream. Counted, never fatal.</summary>
    Stray,

    Response,

    Request,

    Notification,
}
