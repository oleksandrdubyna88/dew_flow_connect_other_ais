using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CoaiMcp.FakeCli;

/// <summary>
/// The fake CLI as an agy CODER (epoch 3 E3 s6 t3, t5): it speaks agy's stream-json the way the measured 1.5.18 did (<c>RESULTS_agy_coder.md</c> section 10.4) and, for every scripted tool call, does what agy does — it runs the hook command of the <c>hooks.json</c> in <c>.agents</c> of its
/// working folder with the payload agy sends (conversation id, step index, tool call, the folders it was given), takes the answer as agy does (<c>deny</c>, a non-zero exit, and — the open door — exit 0 with no output as ALLOW), performs the tool when it is allowed, and prints the events of the step.
/// No real agy is ever started: this is the stand-in, and the hook handler it calls is the product's own code.
/// </summary>
/// <remarks>
/// The script is the model-map field <c>agyTools</c>, an array of objects with <c>tool</c> (the name the hook sees), <c>path</c> (absolute, or relative to the <c>--add-dir</c> folder), <c>content</c>, <c>target</c> and <c>replacement</c>; and the flags <c>skipHook</c> (an event with no hook call), <c>noEvent</c>
/// (a hook call with no event) <c>hookTwice</c> (two hook calls for one step) and <c>stayActive</c> (an event that never ends), which are how a test makes the stream and the records disagree. The usage a turn reports is the running sum of the conversation, as agy reports it.
/// </remarks>
internal static class AgyCoder
{
    /// <summary>Runs the scripted turn: the stream on stdout, <c>stderr</c> on standard error, the exit code the script says. <paramref name="steer"/> reads the model-map values.</summary>
    internal static int Run(string[] args, string toolsJson, Func<string, string?> steer, string recordDir = "")
    {
        var addDir = After(args, "--add-dir");
        var requested = After(args, "--conversation");
        var conversation = steer("AGY_CONVERSATION") is { Length: > 0 } forced ? forced : requested.Length > 0 ? requested : Guid.NewGuid().ToString();
        var cwd = Directory.GetCurrentDirectory();
        var home = Environment.GetEnvironmentVariable("HOME") ?? cwd;
        var state = Path.Combine(home, ".gemini", $"fake-agy-{conversation}.txt");
        var (step, tokensIn, tokensOut) = ReadState(state);
        if (recordDir.Length > 0)
        {
            // What a test reads of this launch: where it stood and which home it had (a sidecar of the record folder, never stdout).
            var sidecars = Path.Combine(recordDir, "sidecars");
            Directory.CreateDirectory(sidecars);
            File.WriteAllText(Path.Combine(sidecars, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".agy"), $"cwd={cwd}\nhome={home}\n");
        }

        var output = new StringBuilder();
        output.AppendLine(new JsonObject
        {
            ["event"] = "init",
            ["conversation_id"] = conversation,
            ["init"] = new JsonObject { ["model"] = After(args, "--model"), ["cwd"] = cwd, ["agent"] = After(args, "--agent"), ["tools"] = new JsonArray("run_command", "view_file", "write_to_file"), ["permission_mode"] = "request-review" },
        }.ToJsonString());

        if (steer("AGY_FIFO_RECORDS") == "1")
        {
            // A command of the model in the unit: the guard's records (beside the guard folder, in the vendor's writable folder) are replaced by a FIFO.
            var records = Path.Combine(Path.GetDirectoryName(cwd)!, "guard-records.jsonl");
            File.Delete(records);
            using var fifo = Process.Start(new ProcessStartInfo("/usr/bin/mkfifo", [records]) { UseShellExecute = false })!;
            fifo.WaitForExit();
        }

        foreach (var call in JsonNode.Parse(toolsJson)!.AsArray().OfType<JsonObject>())
        {
            step += 2;
            Tool(call, conversation, step, cwd, addDir, output);
        }

        if (int.TryParse(steer("AGY_HOLD_MS"), out var hold) && hold > 0)
        {
            // A turn that goes on after its events: what it printed so far is on stdout when the deadline kills it.
            Console.Out.Write(output.ToString());
            Console.Out.Flush();
            output.Clear();
            Thread.Sleep(hold);
        }

        step += 1;
        tokensIn += int.TryParse(steer("AGY_TOKENS_IN"), out var asked) ? asked : 100;
        tokensOut += int.TryParse(steer("AGY_TOKENS_OUT"), out var made) ? made : 10;
        Directory.CreateDirectory(Path.GetDirectoryName(state)!);
        File.WriteAllText(state, $"{step} {tokensIn} {tokensOut}");
        var result = new JsonObject
        {
            ["conversation_id"] = conversation,
            ["status"] = "SUCCESS",
            ["response"] = steer("AGY_ANSWER") ?? string.Empty,
            ["duration_seconds"] = 0.1,
            ["num_turns"] = 1,
            ["usage"] = new JsonObject { ["input_tokens"] = tokensIn, ["output_tokens"] = tokensOut, ["thinking_tokens"] = 0, ["cache_read_tokens"] = 0, ["total_tokens"] = tokensIn + tokensOut },
        };
        if (steer("AGY_DENIED_ACTIONS") is { Length: > 0 } denied)
        {
            result["denied_actions"] = JsonNode.Parse(denied);
        }

        output.AppendLine(new JsonObject { ["event"] = "result", ["result"] = result }.ToJsonString());
        Console.Out.Write(output.ToString());
        Console.Out.Flush();
        if (steer("STDERR") is { Length: > 0 } errors)
        {
            Console.Error.WriteLine(errors);
        }

        return int.TryParse(steer("EXIT"), out var exit) ? exit : 0;
    }

    /// <summary>
    /// <c>models</c> as agy 1.5.18 does it (<c>RESULTS_agy_coder.md</c> 10.5): with a token that is EXPIRED it refreshes — a new access token and a new expiry in the file, the refresh token kept — and with a token that is not expired it changes nothing. Exit 0.
    /// </summary>
    internal static int Models()
    {
        var file = Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? ".", ".gemini", "antigravity-cli", "antigravity-oauth-token");
        if (JsonNode.Parse(File.ReadAllText(file)) is not JsonObject root || root["token"] is not JsonObject token)
        {
            Console.Error.WriteLine("Error: Please sign in to view available models.");

            return 1;
        }

        if (DateTime.TryParse(token["expiry"]?.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var expiry) && expiry <= DateTime.UtcNow)
        {
            token["access_token"] = "made-up-agy-access-token-refreshed";
            token["expiry"] = DateTime.UtcNow.AddMinutes(59).ToString("O");
            File.WriteAllText(file, root.ToJsonString());
        }

        return 0;
    }

    private static void Tool(JsonObject call, string conversation, int step, string cwd, string addDir, StringBuilder output)
    {
        var tool = call["tool"]?.GetValue<string>() ?? "write_file";
        var raw = call["path"]?.GetValue<string>() ?? string.Empty;
        var path = raw.Length == 0 || Path.IsPathRooted(raw) ? raw : Path.Combine(addDir, raw);
        var skipHook = call["skipHook"]?.GetValue<bool>() ?? false;
        var noEvent = call["noEvent"]?.GetValue<bool>() ?? false;
        var hookTwice = call["hookTwice"]?.GetValue<bool>() ?? false;
        var stayActive = call["stayActive"]?.GetValue<bool>() ?? false;
        var decision = skipHook ? Decision.Allowed : AskHook(cwd, addDir, conversation, step, tool, path);
        if (hookTwice)
        {
            AskHook(cwd, addDir, conversation, step, tool, path);
        }

        if (noEvent)
        {
            return;
        }

        output.AppendLine(Event(conversation, step, "ACTIVE", tool, path, null));
        if (!decision.Allow)
        {
            output.AppendLine(Event(conversation, step, "ERROR", tool, path, $"tool call denied by pre-tool hook: {decision.Reason}"));

            return;
        }

        if (stayActive)
        {
            // A command that does not return (a listener, a sleep): the event never ends.
            return;
        }

        Perform(tool, path, call);
        output.AppendLine(Event(conversation, step, "DONE", tool, path, null));
    }

    private static void Perform(string tool, string path, JsonObject call)
    {
        switch (tool)
        {
            case "write_file":
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, call["content"]?.GetValue<string>() ?? string.Empty);
                break;
            case "edit_file" when File.Exists(path):
                File.WriteAllText(path, File.ReadAllText(path).Replace(call["target"]?.GetValue<string>() ?? "\0", call["replacement"]?.GetValue<string>() ?? string.Empty, StringComparison.Ordinal));
                break;
            default:
                break;
        }
    }

    private static string Event(string conversation, int step, string state, string tool, string path, string? error)
    {
        var info = new JsonObject { ["name"] = tool, ["parameters"] = new JsonObject { ["Path"] = path } };
        if (error is not null)
        {
            info["error"] = new JsonObject { ["type"] = "TOOL_ERROR", ["message"] = error };
        }

        return new JsonObject
        {
            ["event"] = "step_update",
            ["step_update"] = new JsonObject { ["conversation_id"] = conversation, ["step_index"] = step, ["state"] = state, ["step_type"] = "tool", ["tool_name"] = tool, ["tool_info"] = info },
        }.ToJsonString();
    }

    private sealed record Decision(bool Allow, string Reason)
    {
        public static Decision Allowed { get; } = new(true, string.Empty);
    }

    /// <summary>What agy does with a hook: runs the command of <c>hooks.json</c> from its folder, the payload on stdin; a non-zero exit denies; <c>deny</c> denies; anything else — exit 0 with NO output too — allows.</summary>
    private static Decision AskHook(string cwd, string addDir, string conversation, int step, string tool, string path)
    {
        var agents = Path.Combine(cwd, ".agents");
        var hooks = JsonNode.Parse(File.ReadAllText(Path.Combine(agents, "hooks.json")))!.AsObject().First().Value!;
        var command = hooks["PreToolUse"]![0]!["hooks"]![0]!["command"]!.GetValue<string>();
        var payload = new JsonObject
        {
            ["artifactDirectoryPath"] = Path.Combine(cwd, "artifacts"),
            ["conversationId"] = conversation,
            ["cwd"] = cwd,
            ["hookEventName"] = "PreToolUse",
            ["lastUserInput"] = "the task",
            ["modelName"] = "fake",
            ["stepIdx"] = step,
            ["toolCall"] = new JsonObject { ["args"] = new JsonObject { ["Path"] = path }, ["id"] = $"call_{step}", ["name"] = tool },
            ["transcriptPath"] = Path.Combine(cwd, "transcript"),
            ["workspacePaths"] = new JsonArray(cwd, addDir),
        }.ToJsonString();
        var start = new ProcessStartInfo("sh", ["-c", command]) { WorkingDirectory = agents, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(start)!;
        process.StandardInput.Write(payload);
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode != 0 ? new Decision(false, "hook command failed")
            : stdout.Contains("\"decision\":\"deny\"", StringComparison.Ordinal) ? new Decision(false, Reason(stdout))
            : Decision.Allowed;
    }

    private static string Reason(string stdout)
    {
        try
        {
            return JsonNode.Parse(stdout)?["reason"]?.GetValue<string>() ?? "denied";
        }
        catch (JsonException)
        {
            return "denied";
        }
    }

    private static string After(string[] args, string flag)
    {
        var at = Array.IndexOf(args, flag);

        return at >= 0 && at + 1 < args.Length ? args[at + 1] : string.Empty;
    }

    private static (int Step, int In, int Out) ReadState(string file)
    {
        if (File.Exists(file) && File.ReadAllText(file).Split(' ') is [var a, var b, var c] && int.TryParse(a, out var step) && int.TryParse(b, out var tokensIn) && int.TryParse(c, out var tokensOut))
        {
            return (step, tokensIn, tokensOut);
        }

        return (1, 0, 0);
    }
}
