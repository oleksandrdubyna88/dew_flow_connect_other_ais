using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CoaiMcp.Install;

/// <summary>
/// Claude Code's SessionStart hook for the roles table (v2 E3.S7 t2): ONE handler of ours inside <c>hooks.SessionStart</c> of
/// <c>${CLAUDE_CONFIG_DIR:-$HOME/.claude}/settings.json</c> — <c>{type:"command", command:&lt;coai-mcp&gt;, args:["--roles","--format","claude-hook"], timeout:10}</c> — so a new session starts with the
/// role table in front of the model.
/// </summary>
/// <remarks>
/// <para><b>There is no vendor command for hooks</b>, so this file is edited here, and with the care a file another tool rewrites deserves: it is READ and parsed first (a file that is not a JSON object,
/// or whose <c>hooks</c> is not an object, is refused before anything is written), the change is SPLICED into the text — a new first item as whole new lines, a command replaced in place — so the lines
/// the installer did not mean to touch stay as their owner wrote them (escapes, number spellings, indentation, line ends, a byte-order mark), a result equal to the file is not written at all, the new
/// text goes to a temporary file BESIDE the real one (mode kept) and is renamed over it, and a file reached through a symbolic link is written at the link's target, the link left standing.</para>
/// <para><b>The file has a second writer</b> (the conventions' SessionStart hook rewrites it at a session start), so the bytes are compared again just before the rename; a file that changed since
/// they were read is read again, up to <see cref="FileEdit.Attempts"/> times, and after that the target is <c>refused</c> with nothing written. The window between that comparison and the rename is not closed
/// (a rename cannot be made conditional); a write in it is lost, and the next run sees the hook missing and puts it back.</para>
/// <para><b>Never</b> another group, another event, <c>permissions</c> or an effort key — and never a PreToolUse or AskUserQuestion hook (server.md §17.5): the only thing this class can add is the one handler.</para>
/// </remarks>
internal sealed class ClaudeHook : IInstallTarget
{
    internal const int TimeoutSeconds = 10;

    internal static readonly IReadOnlyList<string> HookArgs = ["--roles", "--format", "claude-hook"];

    private static readonly JsonWriterOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly JsonWriterOptions PrettyOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = true, IndentSize = 2, NewLine = "\n" };

    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    public Client Cli => Client.Claude;

    public string Kind => "hook";

    public bool FollowsBinary => true;

    public string FileOf(InstallHost host) => host.ClaudeSettings;

    public string Via(PlanAction action) => action == PlanAction.None ? string.Empty : "edit of settings.json (hooks.SessionStart)";

    public ConfigRead Read(string? text) => ReadText(text);

    private static ConfigRead ReadText(string? text) => string.IsNullOrWhiteSpace(text) ? ConfigRead.None : ReadBytes(Encoding.UTF8.GetBytes(text));

    // ---------- reading ----------

    private static ConfigRead ReadBytes(byte[] bytes)
    {
        try
        {
            var root = JsonSpan.Parse(bytes);
            if (!root.IsObject)
            {
                return new ConfigRead.Unreadable("the file is not a JSON object");
            }

            var hooks = root.Get("hooks");
            if (hooks is not null && !hooks.IsObject)
            {
                return new ConfigRead.Unreadable("hooks is not an object");
            }

            var sessionStart = hooks?.Get("SessionStart");
            if (sessionStart is not null && !sessionStart.IsArray)
            {
                return new ConfigRead.Unreadable("hooks.SessionStart is not a list");
            }

            return new ConfigRead.Found(Ours(sessionStart).Select(one => EntryOf(one.Handler, bytes)).FirstOrDefault(), []);
        }
        catch (JsonException e)
        {
            return new ConfigRead.Unreadable($"it is not valid JSON ({e.Message})");
        }
    }

    /// <summary>The handlers of ours in a <c>SessionStart</c> list, each with its group and where it sits: a command whose file is <c>coai-mcp</c> and whose first argument is <c>--roles</c>.</summary>
    private static List<(JsonSpan Group, int GroupIndex, JsonSpan Handler, int HandlerIndex)> Ours(JsonSpan? sessionStart)
    {
        var found = new List<(JsonSpan, int, JsonSpan, int)>();
        for (var group = 0; sessionStart is not null && group < sessionStart.Items.Count; group++)
        {
            var handlers = sessionStart.Items[group].IsObject ? sessionStart.Items[group].Get("hooks") : null;
            for (var handler = 0; handlers is { IsArray: true } && handler < handlers.Items.Count; handler++)
            {
                if (IsOurs(handlers.Items[handler]))
                {
                    found.Add((sessionStart.Items[group], group, handlers.Items[handler], handler));
                }
            }
        }

        return found;
    }

    private static bool IsOurs(JsonSpan handler) =>
        handler.IsObject
        && handler.Get("command") is { IsString: true } command
        && new McpEntry(command.Text ?? string.Empty, [], new Dictionary<string, string>()).IsOurs
        && handler.Get("args") is { IsArray: true } args
        && args.Items.Count > 0
        && args.Items[0].Text == "--roles";

    private static McpEntry EntryOf(JsonSpan handler, byte[] bytes) => new(
        handler.Get("command")?.Text ?? string.Empty,
        [.. handler.Get("args")!.Items.Select(item => item.Text ?? string.Empty)],
        new Dictionary<string, string>(),
        Encoding.UTF8.GetString(bytes, handler.Start, handler.End - handler.Start));

    // ---------- the entry ----------

    public McpEntry NewEntry(string binary) => NewEntryOf(binary);

    private static McpEntry NewEntryOf(string binary)
    {
        var handler = new JsonObject
        {
            ["type"] = "command",
            ["command"] = binary,
            ["args"] = new JsonArray([.. HookArgs.Select(arg => (JsonNode)JsonValue.Create(arg)!)]),
            ["timeout"] = TimeoutSeconds,
        };

        return new McpEntry(binary, HookArgs, new Dictionary<string, string>(), Written(handler, Compact));
    }

    public McpEntry WithCommand(McpEntry before, string binary)
    {
        if (before.RawJson.Length == 0 || JsonNode.Parse(before.RawJson) is not JsonObject handler)
        {
            return NewEntry(binary);
        }

        handler["command"] = binary;

        return new McpEntry(binary, before.Args, before.Env, Written(handler, Compact));
    }

    private static string Written(JsonNode node, JsonWriterOptions options)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, options))
        {
            node.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public Task<TargetOutcome> ApplyAsync(PlannedTarget plan, ApplyContext context, CancellationToken ct) => FileEdit.ApplyAsync(this, plan, context, Transform, ct);

    // ---------- the text ----------

    /// <summary>The file's new bytes: <paramref name="original"/> with our handler added, its command replaced, or taken away — whichever the plan wants.</summary>
    private static byte[] Transform(byte[]? original, PlannedTarget plan)
    {
        var after = plan.After;
        if (original is null || original.All(@byte => @byte is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
        {
            return after is null ? original ?? [] : Encoding.UTF8.GetBytes(Pretty(new JsonObject { ["hooks"] = new JsonObject { ["SessionStart"] = new JsonArray(Group(after)) } }) + "\n");
        }

        var hasBom = original.AsSpan().StartsWith(Bom);
        var text = hasBom ? original[Bom.Length..] : original;
        text = after is null ? RemoveOurs(text) : (OursIn(JsonSpan.Parse(text)).Count == 0 ? AddOurs(text, after) : UpdateOurs(text, after));

        return hasBom ? [.. Bom, .. text] : text;
    }

    private static List<(JsonSpan Group, int GroupIndex, JsonSpan Handler, int HandlerIndex)> OursIn(JsonSpan root) =>
        Ours(root.IsObject ? root.Get("hooks")?.Get("SessionStart") : null);

    private static byte[] AddOurs(byte[] text, McpEntry after)
    {
        var root = JsonSpan.Parse(text);
        if (!root.IsObject)
        {
            throw new JsonException("the file is not a JSON object");
        }

        var group = Pretty(Group(after));
        var hooks = root.Get("hooks");
        var sessionStart = hooks?.Get("SessionStart");
        if (sessionStart is { IsArray: true })
        {
            return JsonSplice.InsertFirst(text, sessionStart, group);
        }

        if (hooks is { IsObject: true })
        {
            return JsonSplice.InsertFirst(text, hooks, $"\"SessionStart\": {Pretty(new JsonArray(Group(after)))}");
        }

        return JsonSplice.InsertFirst(text, root, $"\"hooks\": {Pretty(new JsonObject { ["SessionStart"] = new JsonArray(Group(after)) })}");
    }

    private static byte[] UpdateOurs(byte[] text, McpEntry after)
    {
        for (var pass = 0; pass < 50; pass++)
        {
            var next = OursIn(JsonSpan.Parse(text)).Select(one => one.Handler).FirstOrDefault(handler => !SameAs(handler, after));
            if (next is null)
            {
                return text;
            }

            text = !(next.Get("command")?.Text ?? string.Empty).Equals(after.Command, StringComparison.Ordinal)
                ? JsonSplice.Replace(text, next.Get("command")!, JsonSplice.StringLiteral(after.Command))
                : JsonSplice.Replace(text, next.Get("args")!, Written(new JsonArray([.. after.Args.Select(arg => (JsonNode)JsonValue.Create(arg)!)]), Compact));
        }

        return text;
    }

    private static bool SameAs(JsonSpan handler, McpEntry after) =>
        (handler.Get("command")?.Text ?? string.Empty).Equals(after.Command, StringComparison.Ordinal)
        && handler.Get("args")!.Items.Select(item => item.Text ?? string.Empty).SequenceEqual(after.Args, StringComparer.Ordinal);

    private static byte[] RemoveOurs(byte[] text)
    {
        var removed = false;
        for (var pass = 0; pass < 50; pass++)
        {
            var root = JsonSpan.Parse(text);
            var sessionStart = root.IsObject ? root.Get("hooks")?.Get("SessionStart") : null;
            if (Ours(sessionStart).FirstOrDefault() is not { Group: { } group } one)
            {
                break;
            }

            var handlers = group.Get("hooks")!;
            text = handlers.Items.Count == 1 ? JsonSplice.Remove(text, sessionStart!, one.GroupIndex) : JsonSplice.Remove(text, handlers, one.HandlerIndex);
            removed = true;
        }

        return removed ? WithoutEmptyContainers(text) : text;
    }

    /// <summary>After the last handler of ours went, an emptied <c>SessionStart</c> list and an emptied <c>hooks</c> object go too: a removal leaves the file as it would be had the hook never been added.</summary>
    private static byte[] WithoutEmptyContainers(byte[] text)
    {
        var root = JsonSpan.Parse(text);
        var hooks = root.Get("hooks");
        if (hooks is { IsObject: true } && hooks.Get("SessionStart") is { IsArray: true, Items.Count: 0 })
        {
            text = JsonSplice.Remove(text, hooks, hooks.Members.FindIndex(member => member.Name == "SessionStart"));
            root = JsonSpan.Parse(text);
            hooks = root.Get("hooks");
        }

        return hooks is { IsObject: true, Members.Count: 0 } ? JsonSplice.Remove(text, root, root.Members.FindIndex(member => member.Name == "hooks")) : text;
    }

    /// <summary>The group of the contract — the one new element of <c>SessionStart</c>.</summary>
    private static JsonObject Group(McpEntry entry) =>
        new() { ["hooks"] = new JsonArray(entry.RawJson.Length > 0 && JsonNode.Parse(entry.RawJson) is { } handler ? handler : JsonNode.Parse(NewEntryOf(entry.Command).RawJson)) };

    private static string Pretty(JsonNode node) => Written(node, PrettyOptions);
}
