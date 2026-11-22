using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CoaiMcp.Install;

/// <summary>
/// Claude Code's user-scope MCP entry: <c>mcpServers.coai</c> in <c>${CLAUDE_CONFIG_DIR:-$HOME}/.claude.json</c>, changed ONLY by <c>claude mcp add-json --scope user coai -</c>
/// (the JSON on stdin) and <c>claude mcp remove --scope user coai</c> — that file is live state the running client rewrites, so nothing here edits it.
/// </summary>
/// <remarks>
/// <para>Measured 2026-11-22 on claude 2.1.330: an existing name makes <c>add-json</c> exit 1 and change nothing, so an update is a remove and an add — the window a killed run
/// leaves, which the manifest's <c>removed</c> state closes. <c>claude mcp get</c> and <c>list</c> start the server for a health check and are never called here: the file is READ.</para>
/// </remarks>
internal sealed class ClaudeMcp : McpTarget
{
    private static readonly JsonWriterOptions Writer = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public override Client Cli => Client.Claude;

    public override string FileOf(InstallHost host) => host.ClaudeJson;

    protected override bool RemoveBeforeAdd => true;

    public override ConfigRead Read(string? text) => ReadFile(text);

    /// <summary>The <c>coai</c> entry of a <c>.claude.json</c> text, and the projects whose own <c>coai</c> entry wins over it.</summary>
    internal static ConfigRead ReadFile(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ConfigRead.None;
        }

        try
        {
            if (JsonNode.Parse(text) is not JsonObject root)
            {
                return new ConfigRead.Unreadable("the file is not a JSON object");
            }

            if (root["mcpServers"] is not null and not JsonObject)
            {
                return new ConfigRead.Unreadable("mcpServers is not an object");
            }

            var entry = root["mcpServers"] is JsonObject servers && servers["coai"] is { } found ? EntryOf(found) : null;

            return new ConfigRead.Found(entry, ShadowedBy(root));
        }
        catch (JsonException e)
        {
            return new ConfigRead.Unreadable($"it is not valid JSON ({e.Message})");
        }
    }

    private static List<string> ShadowedBy(JsonObject root) =>
        root["projects"] is JsonObject projects
            ? [.. projects.Where(project => project.Value is JsonObject { } body && body["mcpServers"] is JsonObject { } own && own.ContainsKey("coai")).Select(project => $"project:{project.Key}")]
            : [];

    private static McpEntry EntryOf(JsonNode node)
    {
        if (node is not JsonObject body)
        {
            return new McpEntry(string.Empty, [], new Dictionary<string, string>(), node.ToJsonString());
        }

        var args = body["args"] is JsonArray list ? [.. list.Select(item => item is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty)] : new List<string>();
        var env = body["env"] is JsonObject map
            ? map.ToDictionary(pair => pair.Key, pair => pair.Value is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        var command = body["command"] is JsonValue c && c.TryGetValue<string>(out var path) ? path : string.Empty;

        return new McpEntry(command, args, env, Text(body));
    }

    /// <summary>A new entry for <paramref name="command"/>: stdio, no arguments, no environment.</summary>
    internal static McpEntry NewEntry(string command) =>
        new(command, [], new Dictionary<string, string>(), Text(new JsonObject { ["type"] = "stdio", ["command"] = command, ["args"] = new JsonArray() }));

    /// <summary><paramref name="before"/> with <c>command</c> replaced and every other key kept as it was — the update of the contract.</summary>
    internal static McpEntry Updated(McpEntry before, string command)
    {
        var body = before.RawJson.Length > 0 && JsonNode.Parse(before.RawJson) is JsonObject raw ? raw : new JsonObject { ["type"] = "stdio", ["args"] = new JsonArray() };
        body["command"] = command;

        return new McpEntry(command, before.Args, before.Env, Text(body));
    }

    /// <summary>The entry as JSON text, written with the relaxed encoder so a <c>&lt;</c> or a non-ASCII letter is not turned into an escape the client did not write.</summary>
    private static string Text(JsonObject body)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, Writer))
        {
            body.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    protected override Task<VendorAnswer> AddAsync(McpEntry entry, ApplyContext context, CancellationToken ct) =>
        context.Vendor.RunAsync(context.CliPath, ["mcp", "add-json", "--scope", "user", "coai", "-"], entry.RawJson.Length > 0 ? entry.RawJson : NewEntry(entry.Command).RawJson, ct);

    protected override Task<VendorAnswer> RemoveAsync(ApplyContext context, CancellationToken ct) =>
        context.Vendor.RunAsync(context.CliPath, ["mcp", "remove", "--scope", "user", "coai"], string.Empty, ct);
}
