using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CoaiMcp.FakeCli;

/// <summary>
/// The scriptable stand-in for an ACP agent (<c>kimi acp</c>): JSON-RPC over stdio, driven by a script file.
/// </summary>
/// <remarks>
/// <para>Verb <c>acp-agent &lt;script.json&gt;</c>, or <c>acp</c> with <c>FAKECLI_ACP_SCRIPT</c> naming the script — the
/// second is how a launch spelled by a real profile (<c>kimi acp</c>) reaches this stand-in through <c>--executable</c>.</para>
/// <para>The script:</para>
/// <code>
/// {
///   "log": "&lt;path&gt;",            every line in and out, as {"dir":"in"|"out","msg":…} JSONL — what a test reads
///   "sessionId": "fake-session",
///   "refuseModel": "…",           answer session/set_config_option with this JSON-RPC error
///   "promptError": "…",           answer session/prompt with this JSON-RPC error, after the steps
///   "stopReason": "end_turn",     the prompt's stopReason when nothing cancelled it
///   "omitStopReason": false,      true: the prompt is answered with an empty result, no stopReason at all
///   "omitSessionId": false,       true: session/new answers with no sessionId at all
///   "ignoreCancel": false,        true: a session/cancel changes nothing
///   "steps": [                    run in order while session/prompt is open
///     {"kind":"chunk","text":"…"}                      an agent_message_chunk
///     {"kind":"usage","used":1,"size":2}               a usage_update
///     {"kind":"toolCall","id":"t1","title":"…","toolKind":"read","status":"pending"}
///     {"kind":"toolCallUpdate","id":"t1","status":"completed"}
///     {"kind":"permission","title":"…","toolKind":"edit","options":[{"optionId":"a","kind":"allow_once"},…]}
///     {"kind":"read","path":"…","line":1,"limit":10}   an fs/read_text_file; the reply is logged
///     {"kind":"request","method":"fs/write_text_file","params":{…}}   any other client request; the reply is logged
///     {"kind":"stray","text":"not json"}               a line that is not JSON-RPC
///     {"kind":"spew","chars":N}                        one line of N characters
///     {"kind":"awaitCancel"}                           reads stdin until session/cancel arrives, then goes on (the prompt ends cancelled)
///     {"kind":"sleep","ms":100}
///     {"kind":"hang"}                                  never returns: the kill path
///     {"kind":"exit","code":3}                         leaves mid-turn: the premature EOF
///     {"kind":"grandchild","pidFile":"…"}              spawns a sleeping FakeCli and writes its pid there
///   ]
/// }
/// </code>
/// <para>Deliberately dumb, like the rest of this stand-in: it answers what the client sends and does what the
/// script says; it holds no opinion about the protocol beyond the shapes the real agent was measured to use
/// (research/RESULTS_kimi_confinement.md §2).</para>
/// </remarks>
internal static class AcpAgent
{
    private static readonly object Writing = new();
    private static string _log = string.Empty;
    private static string _sessionId = "fake-session";
    private static long _nextId = 1;
    private static bool _cancelled;

    public static int Run(string scriptPath)
    {
        var script = JsonDocument.Parse(File.ReadAllText(scriptPath)).RootElement.Clone();
        _log = Text(script, "log");
        _sessionId = Text(script, "sessionId") is { Length: > 0 } named ? named : "fake-session";

        while (Console.In.ReadLine() is { } line)
        {
            Log("in", line);
            if (!TryParse(line, out var message))
            {
                continue;
            }

            Answer(script, message);
        }

        // EOF on stdin: the client closed its end, and a well-behaved agent leaves.
        return 0;
    }

    private static void Answer(JsonElement script, JsonElement message)
    {
        var method = Text(message, "method");
        var hasId = message.TryGetProperty("id", out var id);
        switch (method)
        {
            case "initialize":
                Reply(id, new JsonObject
                {
                    ["protocolVersion"] = 1,
                    ["agentCapabilities"] = new JsonObject { ["loadSession"] = true },
                    ["agentInfo"] = new JsonObject { ["name"] = "Fake ACP agent", ["version"] = "0" },
                    ["authMethods"] = new JsonArray(),
                });
                break;
            case "session/new":
                // `omitSessionId`: an agent that mints no session id at all — a shape the client must not go on with.
                Reply(id, script.TryGetProperty("omitSessionId", out var noId) && noId.ValueKind == JsonValueKind.True
                    ? new JsonObject { ["configOptions"] = new JsonArray() }
                    : new JsonObject { ["sessionId"] = _sessionId, ["configOptions"] = new JsonArray() });
                break;
            case "session/set_config_option":
                SetConfigOption(script, id);
                break;
            case "session/prompt":
                Prompt(script, id);
                break;
            case "session/cancel":
                _cancelled = true;
                break;
            default:
                if (hasId)
                {
                    Error(id, -32601, $"fake agent: no such method `{method}`");
                }

                break;
        }
    }

    private static void SetConfigOption(JsonElement script, JsonElement id)
    {
        if (Text(script, "refuseModel") is { Length: > 0 } refused)
        {
            Error(id, -32000, refused);

            return;
        }

        Reply(id, new JsonObject());
    }

    private static void Prompt(JsonElement script, JsonElement id)
    {
        if (script.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
        {
            foreach (var step in steps.EnumerateArray())
            {
                Step(step);
            }
        }

        if (Text(script, "promptError") is { Length: > 0 } failure)
        {
            Error(id, -32000, failure);

            return;
        }

        if (script.TryGetProperty("omitStopReason", out var omit) && omit.ValueKind == JsonValueKind.True)
        {
            // An agent that answers the prompt with no stopReason at all — a shape the client must not read as an answer.
            Reply(id, new JsonObject());

            return;
        }

        var ignoreCancel = script.TryGetProperty("ignoreCancel", out var ignore) && ignore.ValueKind == JsonValueKind.True;
        var stopReason = _cancelled && !ignoreCancel ? "cancelled" : Text(script, "stopReason") is { Length: > 0 } reason ? reason : "end_turn";
        Reply(id, new JsonObject { ["stopReason"] = stopReason });
    }

    private static void Step(JsonElement step)
    {
        switch (Text(step, "kind"))
        {
            case "chunk":
                Update(new JsonObject
                {
                    ["sessionUpdate"] = "agent_message_chunk",
                    ["content"] = new JsonObject { ["type"] = "text", ["text"] = Text(step, "text") },
                });
                break;
            case "usage":
                Update(new JsonObject { ["sessionUpdate"] = "usage_update", ["used"] = Number(step, "used"), ["size"] = Number(step, "size") });
                break;
            case "toolCall":
                Update(new JsonObject
                {
                    ["sessionUpdate"] = "tool_call",
                    ["toolCallId"] = Text(step, "id"),
                    ["title"] = Text(step, "title"),
                    ["kind"] = Text(step, "toolKind"),
                    ["status"] = Text(step, "status"),
                });
                break;
            case "toolCallUpdate":
                Update(new JsonObject { ["sessionUpdate"] = "tool_call_update", ["toolCallId"] = Text(step, "id"), ["status"] = Text(step, "status") });
                break;
            case "permission":
                Request("session/request_permission", new JsonObject
                {
                    ["sessionId"] = _sessionId,
                    ["toolCall"] = new JsonObject { ["toolCallId"] = "call-" + _nextId, ["title"] = Text(step, "title"), ["kind"] = Text(step, "toolKind") },
                    ["options"] = JsonNode.Parse(step.TryGetProperty("options", out var options) ? options.GetRawText() : "[]"),
                });
                break;
            case "read":
                Request("fs/read_text_file", ReadParams(step));
                break;
            case "request":
                Request(Text(step, "method"), (JsonObject)JsonNode.Parse(step.TryGetProperty("params", out var p) ? p.GetRawText() : "{}")!);
                break;
            case "stray":
                Write(Text(step, "text"));
                break;
            case "spew":
                // One line of N characters and no newline among them: the shape a line reader must bound.
                Write(new string('x', (int)Number(step, "chars")));
                break;
            case "awaitCancel":
                AwaitCancel();
                break;
            case "sleep":
                Thread.Sleep((int)Number(step, "ms"));
                break;
            case "hang":
                Thread.Sleep(Timeout.Infinite);
                break;
            case "exit":
                Console.Out.Flush();
                Environment.Exit((int)Number(step, "code"));
                break;
            case "grandchild":
                Grandchild(Text(step, "pidFile"));
                break;
        }
    }

    private static JsonObject ReadParams(JsonElement step)
    {
        var parameters = new JsonObject { ["sessionId"] = _sessionId, ["path"] = Text(step, "path") };
        if (step.TryGetProperty("line", out var line))
        {
            parameters["line"] = line.GetInt64();
        }

        if (step.TryGetProperty("limit", out var limit))
        {
            parameters["limit"] = limit.GetInt64();
        }

        return parameters;
    }

    /// <summary>
    /// A heartbeat that outlives this process unless the client's tree kill reaches it: this same binary, sleeping
    /// ten minutes, with its own pipes so it holds none of ours.
    /// </summary>
    private static void Grandchild(string pidFile)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath ?? "FakeCli")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("sleep");
        info.ArgumentList.Add("600000");
        var child = Process.Start(info)!;
        File.WriteAllText(pidFile, child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Reads stdin, logging every line, until a <c>session/cancel</c> arrives — the prompt then ends <c>cancelled</c>.</summary>
    private static void AwaitCancel()
    {
        while (Console.In.ReadLine() is { } line)
        {
            Log("in", line);
            if (TryParse(line, out var message) && Text(message, "method") == "session/cancel")
            {
                _cancelled = true;

                return;
            }
        }

        Environment.Exit(0);
    }

    /// <summary>Sends a request of the agent's own and waits for its reply, logging it; a cancel seen meanwhile is noted.</summary>
    private static void Request(string method, JsonObject parameters)
    {
        var id = _nextId++;
        Write(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters }.ToJsonString());
        while (Console.In.ReadLine() is { } line)
        {
            Log("in", line);
            if (!TryParse(line, out var message))
            {
                continue;
            }

            if (Text(message, "method") == "session/cancel")
            {
                _cancelled = true;
                continue;
            }

            if (message.TryGetProperty("id", out var replyId) && replyId.TryGetInt64(out var replied) && replied == id)
            {
                return;
            }
        }

        // The client went away mid-request: nothing left to answer to.
        Environment.Exit(0);
    }

    private static void Update(JsonObject update) =>
        Write(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "session/update",
            ["params"] = new JsonObject { ["sessionId"] = _sessionId, ["update"] = update },
        }.ToJsonString());

    private static void Reply(JsonElement id, JsonObject result) =>
        Write(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = JsonNode.Parse(id.GetRawText()), ["result"] = result }.ToJsonString());

    private static void Error(JsonElement id, int code, string message) =>
        Write(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonNode.Parse(id.GetRawText()),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        }.ToJsonString());

    private static void Write(string line)
    {
        Log("out", line);
        Console.Out.WriteLine(line);
        Console.Out.Flush();
    }

    private static void Log(string direction, string line)
    {
        if (_log.Length == 0)
        {
            return;
        }

        var entry = IsJson(line)
            ? $"{{\"dir\":\"{direction}\",\"msg\":{line}}}"
            : $"{{\"dir\":\"{direction}\",\"raw\":{JsonValue.Create(line).ToJsonString()}}}";
        lock (Writing)
        {
            File.AppendAllText(_log, entry + "\n");
        }
    }

    private static bool IsJson(string line) => TryParse(line, out _);

    private static bool TryParse(string line, out JsonElement message)
    {
        try
        {
            message = JsonDocument.Parse(line).RootElement.Clone();

            return message.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            message = default;

            return false;
        }
    }

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static long Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;
}
