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

    /// <summary>A string property, or empty — the agent's optional fields are read without a null in the caller.</summary>
    public static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>A number property, or zero — a string where a number was expected is zero too, never a throw in the pump.</summary>
    public static long Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : 0;

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
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Stray;
            }

            var method = AcpMessages.Text(root, "method");
            var hasId = root.TryGetProperty("id", out var id) && id.ValueKind != JsonValueKind.Null;

            return (method.Length > 0, hasId) switch
            {
                (true, true) => new AcpIncoming(AcpIncomingKind.Request, id, method, AcpMessages.Child(root, "params"), default, default),
                (true, false) => new AcpIncoming(AcpIncomingKind.Notification, default, method, AcpMessages.Child(root, "params"), default, default),
                (false, true) => new AcpIncoming(AcpIncomingKind.Response, id, string.Empty, default, AcpMessages.Child(root, "result"), AcpMessages.Child(root, "error")),
                _ => Stray,
            };
        }
        catch (JsonException)
        {
            return Stray;
        }
    }

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
