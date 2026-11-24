using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CoaiMcp.Install;

/// <summary>
/// Claude Code's user-scope MCP entry: <c>mcpServers.coai</c> in <c>${CLAUDE_CONFIG_DIR:-$HOME}/.claude.json</c>, changed ONLY by <c>claude mcp add-json --scope user coai -</c>
/// (the JSON on stdin; or, on a claude whose help says nothing of stdin and for an entry that holds no value, the JSON as the argument) and <c>claude mcp remove --scope user coai</c> — that file is live
/// state the running client rewrites, so nothing here edits it.
/// </summary>
/// <remarks>
/// <para>Measured 2026-11-24 on claude 2.1.310 (the owner's Windows) and 2.1.331: only the newer one reads the JSON from stdin — the older one says "&lt;name&gt; &lt;json&gt;" in its help and a run that
/// sent <c>-</c> was rolled back four times without a word. The capability is therefore READ (<see cref="ReadsStdinAsync"/>) before anything is written.</para>
/// <para>Measured 2026-11-22 on claude 2.1.330: an existing name makes <c>add-json</c> exit 1 and change nothing, so an update is a remove and an add — the window a killed run
/// leaves, which the manifest's <c>removed</c> state closes. <c>claude mcp get</c> and <c>list</c> start the server for a health check and are never called here: the file is READ.</para>
/// </remarks>
internal sealed class ClaudeMcp : McpTarget
{
    /// <summary>What a person is told when the installer runs inside a Claude Code session: the vendor's own refusal, met BEFORE the lock and the backup, not got round.</summary>
    internal const string SessionRefusal = "inside a Claude Code session the claude CLI refuses this registration; run the install from the extension or a normal terminal";

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
    public override McpEntry NewEntry(string command) =>
        new(command, [], new Dictionary<string, string>(), Text(new JsonObject { ["type"] = "stdio", ["command"] = command, ["args"] = new JsonArray() }));

    /// <summary><paramref name="before"/> with <c>command</c> replaced and every other key kept as it was — the update of the contract.</summary>
    public override McpEntry WithCommand(McpEntry before, string command)
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

    /// <summary>Whether this claude reads <c>add-json</c>'s JSON from stdin — asked once per run, of the CLI itself. <see langword="null"/> until asked.</summary>
    private bool? _readsStdin;

    /// <summary>
    /// Reads the capability from <c>claude mcp add-json --help</c> (bounded like every vendor command, starts no server) rather than from a version number: 2.1.310 prints
    /// "&lt;name&gt; &lt;json&gt;" and nothing of stdin, 2.1.331 adds "or - to read it from stdin". A help that cannot be read is "no stdin" — the road that never puts a value on argv, or refuses.
    /// </summary>
    private async Task<bool> ReadsStdinAsync(ApplyContext context, CancellationToken ct)
    {
        if (_readsStdin is { } known)
        {
            return known;
        }

        var help = await context.Vendor.ReadAsync(context.CliPath, ["mcp", "add-json", "--help"], ct);
        _readsStdin = help.Ok && help.Text.Contains("stdin", StringComparison.OrdinalIgnoreCase);

        return _readsStdin.Value;
    }

    private static readonly string[] OnlyTheProgram = ["type", "command", "args", "env"];

    /// <summary>
    /// Whether the entry is only the program path: no environment value, NO argument (an argument can carry a secret too — a token after a flag) and no other key (a header) that could hold one. The one
    /// condition under which the JSON may be an ARGUMENT of <c>claude mcp add-json</c> — a command line is readable by every process of the account, so nothing that could be a value goes there.
    /// </summary>
    private static bool CarriesNoValue(McpEntry entry)
    {
        if (entry.Env.Count > 0 || entry.Args.Count > 0)
        {
            return false;
        }

        if (entry.RawJson.Length == 0)
        {
            return true;
        }

        try
        {
            return JsonNode.Parse(entry.RawJson) is JsonObject body
                && body.All(pair => OnlyTheProgram.Contains(pair.Key, StringComparer.Ordinal))
                && (body["env"] is null || body["env"] is JsonObject { Count: 0 })
                && (body["args"] is null || body["args"] is JsonArray { Count: 0 });
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>The sentence of a refusal: the version asked of the CLI (its first word, empty when it said none), the thing it cannot do, and the cure.</summary>
    private static string StdinRefusal(ApplyContext context)
    {
        var version = context.CliVersion.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

        return $"claude {(version.Length > 0 ? version + " " : string.Empty)}cannot read `mcp add-json` from stdin; update Claude Code (claude update)";
    }

    /// <summary>
    /// A <c>.cmd</c> or <c>.bat</c> file is run by Windows through cmd.exe, which re-parses the JSON argument and can run shell syntax (<c>&amp;</c>, <c>%VAR%</c>, <c>^</c>) BEFORE the read-back sees anything —
    /// so the argument road is not taken through one, whatever the entry holds.
    /// </summary>
    private static bool IsShell(string cliPath) =>
        Path.GetExtension(cliPath.Trim()) is { } extension && (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase));

    /// <summary>Why the argument road may not carry <paramref name="entry"/> to this claude, or empty when it may. A shim's reason comes first: it holds for any entry.</summary>
    private static string ArgumentRoadRefusal(McpEntry entry, ApplyContext context) =>
        IsShell(context.CliPath)
            ? $"claude at {context.CliPath} is a .cmd/.bat shim; coai will not pass the entry on its command line; update Claude Code (claude update) so it reads stdin"
            : CarriesNoValue(entry) ? string.Empty : StdinRefusal(context);

    protected override async Task<string> RefusalBeforeAnyStepAsync(PlannedTarget plan, ApplyContext context, CancellationToken ct) =>
        plan.After is { } after && !await ReadsStdinAsync(context, ct) ? ArgumentRoadRefusal(after, context) : string.Empty;

    /// <summary>
    /// The three roads: stdin when the CLI says it reads it (the road measured on 2.1.330, unchanged); the JSON as the one <c>&lt;json&gt;</c> argument when it does not,
    /// (<see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/> quotes it — the launcher never joins a string) the entry is only the program path (no env, no args) and claude is not a <c>.cmd</c>/<c>.bat</c> shim;
    /// otherwise nothing is run, and the answer says why.
    /// </summary>
    protected override async Task<VendorAnswer> AddAsync(McpEntry entry, ApplyContext context, CancellationToken ct)
    {
        var json = entry.RawJson.Length > 0 ? entry.RawJson : NewEntry(entry.Command).RawJson;
        if (await ReadsStdinAsync(context, ct))
        {
            return await context.Vendor.RunAsync(context.CliPath, ["mcp", "add-json", "--scope", "user", "coai", "-"], json, ct);
        }

        return ArgumentRoadRefusal(entry, context) is { Length: > 0 } refusal
            ? new VendorAnswer(false, refusal)
            : await context.Vendor.RunAsync(context.CliPath, ["mcp", "add-json", "--scope", "user", "coai", json], string.Empty, ct);
    }

    public override string Via(PlanAction action) => action switch
    {
        PlanAction.Add => "claude mcp add-json",
        PlanAction.Update or PlanAction.Restore => "claude mcp remove + claude mcp add-json",
        PlanAction.Remove => "claude mcp remove",
        _ => string.Empty,
    };

    protected override Task<VendorAnswer> RemoveAsync(ApplyContext context, CancellationToken ct) =>
        context.Vendor.RunAsync(context.CliPath, ["mcp", "remove", "--scope", "user", "coai"], string.Empty, ct);
}
