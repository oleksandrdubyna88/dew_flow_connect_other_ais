using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Runners.Files;

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
///   "models": ["m-1"],            the values of the "model" config option session/new announces; absent: ["m-1"]; []: no config option at all (E4.3)
///   "usageReply": "…",            the text of the one chunk a session/prompt whose text is exactly "/usage" is answered with (RESULTS §5);
///                                 absent: that prompt is answered end_turn at once, with no chunk and none of the steps — as a real agent answers it itself
///   "promptErrorOnUsage": "…",    answer the "/usage" prompt with this JSON-RPC error
///   "usageHang": false,           true: the "/usage" prompt is never answered (the bound's arm)
///   "startDelayMs": 0,            on start, before ANYTHING is read (the environment record included): sleeps this long — a cold start, which a test then races a deadline against
///   "envFile": "&lt;path&gt;",         on start, before reading anything: this process's whole environment, NAME=value lines sorted, and then
///   "probes": ["&lt;path&gt;",…]       one "PROBE &lt;path&gt; Held|Free|Unknown" line per lock file named — what a tree hold looks like from INSIDE the agent
///   "steps": [                    run in order while session/prompt is open
///     {"kind":"chunk","text":"…"}                      an agent_message_chunk
///     {"kind":"usage","used":1,"size":2}               a usage_update
///     {"kind":"toolCall","id":"t1","title":"…","toolKind":"read","status":"pending"}
///     {"kind":"toolCallUpdate","id":"t1","status":"completed"}
///     {"kind":"permission","title":"…","toolKind":"edit","options":[{"optionId":"a","kind":"allow_once"},…]}
///     {"kind":"read","path":"…","line":1,"limit":10}   an fs/read_text_file; the reply is logged ("echo":true: and said back as a message chunk, whole — E4.2 S2.45)
///     {"kind":"request","method":"fs/write_text_file","params":{…}}   any other client request; the reply is logged
///     {"kind":"stray","text":"not json"}               a line that is not JSON-RPC
///     {"kind":"spew","chars":N}                        one line of N characters
///     {"kind":"stderr","chars":N}                      N characters on STDERR, no newline — what drives the client's stderr capture and its truncation flag
///     {"kind":"stderrText","text":"…"}             the text on STDERR, as given (put its own line break in it) — what a vendor's own error line is
///     {"kind":"awaitCancel"}                           reads stdin until session/cancel arrives, then goes on (the prompt ends cancelled)
///     {"kind":"sleep","ms":100}
///     {"kind":"hang"}                                  never returns: the kill path
///     {"kind":"exit","code":3}                         leaves mid-turn: the premature EOF
///     {"kind":"grandchild","pidFile":"…"}              spawns a sleeping FakeCli and writes its pid there
///     {"kind":"grandchildSetsid","pidFile":"…"}        the same, but the sleeper calls setsid() first — it leaves the agent's session and group (what `rg` does, L-K4)
///     {"kind":"grandchildLateSetsid","pidFile":"…","trigger":"…"}   a child that polls for the file `trigger` and only THEN starts the setsid sleeper and writes its pid
///     {"kind":"grandchildExits","pidFile":"…"}         a child that writes its pid and exits 0 at once
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
        StartSlowly(script);
        WriteEnvironment(script);

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

    /// <summary><c>startDelayMs</c>: a start that takes this long before the first line is read — the cold start of a loaded machine, made exact.</summary>
    private static void StartSlowly(JsonElement script)
    {
        if (Number(script, "startDelayMs") is > 0 and var ms)
        {
            Thread.Sleep((int)ms);
        }
    }

    /// <summary>
    /// The script beside the work folder — <c>&lt;cwd&gt;/../agent.json</c>, the place <c>FakeAcpAgent</c> writes it — else the one the steering file beside the fake's binary (<see cref="MinimalSteeringFile"/>) names
    /// (<c>FAKECLI_ACP_SCRIPT</c>, the same file a minimal-environment vendor launch reads: a round's snapshot is a folder no test can name beforehand, and a launch that builds the agent's
    /// environment from scratch carries no variable) — or empty when there is none.
    /// </summary>
    public static string ScriptBesideTheWorkFolder()
    {
        var beside = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "agent.json"));

        return File.Exists(beside) ? beside : SteeredScript();
    }

    private static string SteeredScript()
    {
        var steering = MinimalSteeringFile.Location;
        if (!File.Exists(steering))
        {
            return string.Empty;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(steering));
        var named = document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("FAKECLI_ACP_SCRIPT", out var script) ? script.GetString() ?? string.Empty : string.Empty;

        return File.Exists(named) ? named : string.Empty;
    }

    /// <summary>
    /// <c>envFile</c>: what this process was started with, before a line of the protocol — and what it can see of the locks named in
    /// <c>probes</c>. A tree hold taken by the shim is visible here only if the agent inherited it, which is the point.
    /// </summary>
    private static void WriteEnvironment(JsonElement script)
    {
        if (Text(script, "envFile") is not { Length: > 0 } path)
        {
            return;
        }

        var lines = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .Select(pair => $"{pair.Key}={pair.Value}")
            .Order(StringComparer.Ordinal)
            .Concat(Probed(script));
        File.WriteAllLines(path, lines);
        // The folder the agent was started in, beside the environment record (not in it: that file is read as a set of variables).
        File.WriteAllText(path + ".cwd", Directory.GetCurrentDirectory());
    }

    private static IEnumerable<string> Probed(JsonElement script) =>
        script.TryGetProperty("probes", out var probes) && probes.ValueKind == JsonValueKind.Array
            ? probes.EnumerateArray().Select(probe => probe.GetString() ?? string.Empty).Select(path => $"PROBE {path} {HeldFile.Probe(path)}")
            : [];

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
                    ? new JsonObject { ["configOptions"] = ConfigOptions(script) }
                    : new JsonObject { ["sessionId"] = _sessionId, ["configOptions"] = ConfigOptions(script) });
                break;
            case "session/set_config_option":
                SetConfigOption(script, id);
                break;
            case "session/prompt":
                Prompt(script, id, PromptText(message));
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

    /// <summary>The <c>model</c> option the session announces: the script's <c>models</c> (absent: <c>m-1</c>, what the stand-in has always been asked for); an empty list announces no option at all.</summary>
    private static JsonArray ConfigOptions(JsonElement script)
    {
        var models = script.TryGetProperty("models", out var listed) && listed.ValueKind == JsonValueKind.Array
            ? listed.EnumerateArray().Select(model => model.GetString() ?? string.Empty).ToArray()
            : ["m-1"];

        return models.Length == 0
            ? new JsonArray()
            : new JsonArray(new JsonObject
            {
                ["id"] = "model",
                ["options"] = new JsonArray([.. models.Select(model => (JsonNode?)new JsonObject { ["value"] = model, ["name"] = model })]),
            });
    }

    /// <summary>The text of the first block of a <c>session/prompt</c>.</summary>
    private static string PromptText(JsonElement message) =>
        message.TryGetProperty("params", out var parameters) && parameters.TryGetProperty("prompt", out var blocks) && blocks.ValueKind == JsonValueKind.Array && blocks.GetArrayLength() > 0
            ? Text(blocks[0], "text")
            : string.Empty;

    private static void SetConfigOption(JsonElement script, JsonElement id)
    {
        if (Text(script, "refuseModel") is { Length: > 0 } refused)
        {
            Error(id, -32000, refused);

            return;
        }

        Reply(id, new JsonObject());
    }

    /// <summary>
    /// <c>/usage</c> is the agent's own command: answered at once, no model call, none of the steps (a real agent does not replay its
    /// tool calls for it). The script says what it answers with, or fails it, or never answers it.
    /// </summary>
    private static void Usage(JsonElement script, JsonElement id)
    {
        if (script.TryGetProperty("usageHang", out var hang) && hang.ValueKind == JsonValueKind.True)
        {
            Thread.Sleep(Timeout.Infinite);
        }

        if (Text(script, "promptErrorOnUsage") is { Length: > 0 } failure)
        {
            Error(id, -32000, failure);

            return;
        }

        if (Text(script, "usageReply") is { Length: > 0 } reply)
        {
            Update(new JsonObject { ["sessionUpdate"] = "agent_message_chunk", ["content"] = new JsonObject { ["type"] = "text", ["text"] = reply } });
        }

        Reply(id, new JsonObject { ["stopReason"] = "end_turn" });
    }

    private static void Prompt(JsonElement script, JsonElement id, string text)
    {
        if (text == "/usage")
        {
            Usage(script, id);

            return;
        }

        if (script.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
        {
            foreach (var step in steps.EnumerateArray())
            {
                Step(step);
            }
        }

        if (Text(script, "promptError") is { Length: > 0 } failure)
        {
            Error(id, -32000, failure, script.TryGetProperty("promptErrorData", out var data) ? JsonNode.Parse(data.GetRawText()) : null);

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
                var reply = Request("fs/read_text_file", ReadParams(step));
                if (step.TryGetProperty("echo", out var echo) && echo.ValueKind == JsonValueKind.True)
                {
                    Update(new JsonObject { ["sessionUpdate"] = "agent_message_chunk", ["content"] = new JsonObject { ["type"] = "text", ["text"] = reply } });
                }

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
            case "stderr":
                // Stderr only: the protocol stream stays clean, so the turn still answers.
                Console.Error.Write(new string('e', (int)Number(step, "chars")));
                Console.Error.Flush();
                break;
            case "stderrText":
                // Stderr only, the vendor's own sentence as given: the protocol stream stays clean.
                Console.Error.Write(Text(step, "text"));
                Console.Error.Flush();
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
            case "grandchildSetsid":
                SpawnedInto(Text(step, "pidFile"), "sleep", "--own-session", "600000");
                break;
            case "grandchildLateSetsid":
                // Not the pid file of the sleeper's own: the late child writes THAT file when it spawns.
                Spawned("late-spawn", Text(step, "trigger"), Text(step, "pidFile"));
                break;
            case "grandchildExits":
                SpawnedInto(Text(step, "pidFile"), "exit-after", "0", "0");
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
        Serving.WritePidFile(pidFile, [child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
    }

    /// <summary>A FakeCli child with every stream of its own, its pid written to the file.</summary>
    private static void SpawnedInto(string pidFile, params string[] arguments) =>
        File.WriteAllText(pidFile, Spawned(arguments).Id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static Process Spawned(params string[] arguments) => SupervisionVerbs.StartChild(arguments);

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
    private static string Request(string method, JsonObject parameters)
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
                return line;
            }
        }

        // The client went away mid-request: nothing left to answer to.
        Environment.Exit(0);

        return string.Empty;
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

    /// <summary>A JSON-RPC error; <paramref name="data"/> is the error's own optional <c>data</c> member, where a server may keep the detail its message leaves out.</summary>
    private static void Error(JsonElement id, int code, string message, JsonNode? data = null) =>
        Write(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonNode.Parse(id.GetRawText()),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message, ["data"] = data },
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
