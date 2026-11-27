using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace CoaiMcp.FakeCli;

/// <summary>
/// The fake CLI as the codex CODER's model-free verbs (epoch 3 E3 s5 t3): <c>sandbox -P coai -- &lt;command&gt;</c>, <c>debug prompt-input &lt;flags&gt; x</c>, <c>doctor --json</c> and — since epoch 3 E3 s2 t5 — <c>debug models</c>, as MEASURED on codex-cli 0.171.0 (<c>v2/spikes/RESULTS_codex_coder.md</c> sections 4 and 9). A real codex is never started by a test.
/// </summary>
/// <remarks>
/// <para><b>The sandbox is a REAL one.</b> <c>sandbox</c> runs the command under <c>bwrap</c> with what the profile <c>coai</c> shows a command: the system folders and the fake's own folder read-only, the working folder (the worktree) writable with <c>.git</c>, <c>.agents</c> and
/// <c>.codex</c> of it read-only, an empty <c>/tmp</c>, its own process and network namespaces, and nothing else — so a preflight row that asks "can the sandbox read the job folder" is answered by a kernel, not by a variable. A test makes the fake LEAK one thing through the steering
/// (<c>FAKECLI_CODEX_SANDBOX</c>: <c>fail</c>, <c>none</c>, or a list of <c>net</c>, <c>pid</c>, <c>job</c>, <c>reads</c>, <c>names</c>) and sees the row of that thing go red.</para>
/// <para><b>The other two verbs answer what the real CLI answers for the argv they are given</b>: a tool list WITHOUT the six <c>collaboration.*</c> names only when <c>-c agents.enabled=false</c> is on the argv, a doctor report with the approval policy <c>Never</c> only when <c>-c approval_policy="never"</c> is. A flag coai forgot shows
/// in the answer, as it does with the real CLI. The steering is the environment or the minimal-environment file beside the fake's binary (a unit's <c>env -i</c> carries no <c>FAKECLI_*</c> name).</para>
/// </remarks>
internal static class CodexVerbs
{
    /// <summary>Whether <paramref name="args"/> are one of the three verbs.</summary>
    public static bool Handles(string[] args) => args is ["sandbox", ..] or ["doctor", ..] or ["debug", "prompt-input", ..] or ["debug", "models", ..];

    /// <summary>Runs the verb and answers its exit code. Every call is one line of <c>fake-verbs.log</c> in the <c>CODEX_HOME</c> it ran with (when it has one): a test reads which of the vendor's programs a preflight started.</summary>
    public static int Run(string[] args)
    {
        Log(args is ["debug", "models", ..] ? "debug-models" : args[0]);

        return args switch
        {
            ["sandbox", ..] => Sandbox(args),
            ["doctor", ..] => Doctor(args),
            ["debug", "models", ..] => Models(args),
            _ => PromptInput(args),
        };
    }

    private static void Log(string verb)
    {
        if (CodexHome() is { Length: > 0 } home && Directory.Exists(home))
        {
            File.AppendAllText(Path.Combine(home, "fake-verbs.log"), verb + "\n");
        }
    }

    /// <summary>The steering value <paramref name="name"/> (the environment first, then the minimal-environment file), or empty.</summary>
    private static string Steering(string name)
    {
        if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } fromEnvironment)
        {
            return fromEnvironment;
        }

        var file = MinimalSteeringFile.Location;
        if (!File.Exists(file))
        {
            return string.Empty;
        }

        var parsed = JsonSerializer.Deserialize(File.ReadAllText(file), FakeCliJsonContext.Default.DictionaryStringString);

        return parsed is not null && parsed.TryGetValue(name, out var value) ? value : string.Empty;
    }

    /// <summary>
    /// Where a codex-shaped launch ran: for a launch that has a <c>CODEX_HOME</c>, one file <c>sidecars/&lt;pid&gt;.where</c> in the record folder holding its working folder and that home, one to a line — what the argv record cannot say, and what a test of "the same folder, the same home" reads. Nothing for a launch with no such variable.
    /// </summary>
    public static void RecordWhere(string recordFolder)
    {
        if (Environment.GetEnvironmentVariable("CODEX_HOME") is not { Length: > 0 } home)
        {
            return;
        }

        var folder = Path.Combine(recordFolder, "sidecars");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".where"), [Directory.GetCurrentDirectory(), home]);
    }

    // ---------- sandbox ----------

    private static int Sandbox(string[] args)
    {
        var dash = Array.IndexOf(args, "--");
        if (dash < 0 || dash + 1 >= args.Length)
        {
            Console.Error.WriteLine("fake codex sandbox: no command after --");

            return 2;
        }

        var mode = Steering("FAKECLI_CODEX_SANDBOX");
        if (mode == "fail")
        {
            Console.Error.WriteLine("fake codex sandbox: bwrap: No permissions to create a new namespace");

            return 1;
        }

        var command = args[(dash + 1)..];
        if (mode == "blind")
        {
            // A sandbox in which the tools of a probe are not there: the probe prints nothing but its start and its end, which no row may read as "nothing was found".
            return Run("bwrap", [.. BwrapArguments([]).Select(argument => argument == "/usr/bin:/bin" ? "/nonexistent" : argument), .. command]);
        }

        var leaks = mode.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);

        return Run(mode == "none" ? command[0] : "bwrap", mode == "none" ? command[1..] : [.. BwrapArguments(leaks), .. command]);
    }

    private static int Run(string program, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(program) { UseShellExecute = false };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using var child = Process.Start(info)!;
            child.WaitForExit();

            return child.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Console.Error.WriteLine($"fake codex sandbox: {e.Message}");

            return 1;
        }
    }

    /// <summary>The <c>bwrap</c> options of the profile <c>coai</c>, less what <paramref name="leaks"/> names.</summary>
    private static IEnumerable<string> BwrapArguments(HashSet<string> leaks)
    {
        var cwd = Directory.GetCurrentDirectory();
        var own = AppContext.BaseDirectory.TrimEnd('/');
        // testsToDelete-exempt: raw-temp — the sandbox's own mount points, spelled in its argument list, never a path of the host
        List<string> arguments = ["--die-with-parent", "--new-session", "--clearenv", "--setenv", "PATH", "/usr/bin:/bin", "--setenv", "HOME", "/tmp"];
        if (!leaks.Contains("pid"))
        {
            arguments.Add("--unshare-pid");
        }

        if (!leaks.Contains("net"))
        {
            arguments.Add("--unshare-net");
        }

        arguments.AddRange(["--ro-bind", "/usr", "/usr"]);
        foreach (var system in new[] { "bin", "sbin", "lib", "lib64" })
        {
            arguments.AddRange(Directory.Exists("/" + system) || File.Exists("/" + system) ? SystemFolder(system) : []);
        }

        // testsToDelete-exempt: raw-temp — the sandbox's own empty tmpfs mount point, never a path of the host
        arguments.AddRange(["--proc", "/proc", "--dev", "/dev", "--tmpfs", "/tmp", "--ro-bind", own, own, "--bind", cwd, cwd]);
        if (CodexHome() is { Length: > 0 } home && Path.GetDirectoryName(Path.GetDirectoryName(home)) is { Length: > 0 } vendor)
        {
            arguments.AddRange(leaks.Contains("job") ? ["--bind", vendor, vendor] : leaks.Contains("reads") ? ["--ro-bind", vendor, vendor] : []);
        }

        arguments.AddRange(leaks.Contains("names") ? [] : Masks(cwd));
        arguments.AddRange(["--chdir", cwd]);

        return [.. arguments, "--"];
    }

    private static string CodexHome() => Environment.GetEnvironmentVariable("CODEX_HOME") ?? string.Empty;

    /// <summary>A system folder as the host has it: a link to <c>usr/…</c> on a merged-usr machine, else a read-only bind.</summary>
    private static string[] SystemFolder(string name)
    {
        var info = new FileInfo("/" + name);

        return info.LinkTarget is { Length: > 0 } target ? ["--symlink", target, "/" + name] : ["--ro-bind", "/" + name, "/" + name];
    }

    /// <summary>What the real sandbox masks read-only in the writable root: the <c>.git</c> entry and the <c>.agents</c> and <c>.codex</c> folders (made empty when they are not there, as the real one makes them).</summary>
    private static IEnumerable<string> Masks(string cwd)
    {
        var masks = new List<string>();
        foreach (var name in new[] { ".git", ".agents", ".codex" })
        {
            var path = Path.Combine(cwd, name);
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            masks.AddRange(["--ro-bind", path, path]);
        }

        return masks;
    }

    // ---------- debug models ----------

    /// <summary>
    /// <c>codex debug models</c> (epoch 3 E3 s2 t5): a network call and no model call, which prints the live list of the account as ONE JSON object <c>{"models":[…]}</c>. It answers from the RECORDING of the real command (<c>fixtures/role-stops/codex-debug-models.json</c>, scrubbed to the fields coai reads: stdout, stderr and the exit code as the real CLI
    /// gave them), or from the file <c>FAKECLI_CODEX_MODELS</c> names (a recording of the same shape), or — for a word — the one failure a test needs, or <c>slugs:a=list,b=hide</c> for a list of exactly these models: <c>exit1</c> (a CLI that fails), <c>garbage</c> (an exit 0 that is no list), <c>huge</c> (a list over the limit), <c>sign-in-lost</c> (the refresh failure of a copy that is no use) and <c>slow</c> (a list that comes after the deadline), <c>write-stopped</c> (a refused write, as the OS layer says it) and <c>ignores-term</c> (a process that ignores the stop signal). Each sidecar also names what the working folder held when the call was made.
    /// Each call is a sidecar <c>sidecars/&lt;pid&gt;.models</c> of the record folder (working folder, <c>HOME</c>, <c>CODEX_HOME</c>, one to a line), which a test reads to see where and with which home the list was asked; it is NO launch of a model, so the record folder holds no launch record for it.
    /// </summary>
    private static int Models(string[] args)
    {
        if (Steering("FAKECLI_RECORD_DIR") is { Length: > 0 } record)
        {
            var folder = Path.Combine(record, "sidecars");
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".models"), [Directory.GetCurrentDirectory(), Environment.GetEnvironmentVariable("HOME") ?? string.Empty, CodexHome(), string.Join(" ", args), string.Join(",", Directory.EnumerateFileSystemEntries(Directory.GetCurrentDirectory()).Select(Path.GetFileName))]);
        }

        var mode = Steering("FAKECLI_CODEX_MODELS");
        switch (mode)
        {
            case "exit1":
                Console.Error.WriteLine("fake codex: debug models failed");

                return 1;
            case "garbage":
                Console.Out.Write("this is not a list\n");

                return 0;
            case "huge":
                Console.Out.Write("{\"models\":[" + new string(' ', 4_500_000) + "]}");

                return 0;
            case "sign-in-lost":
                Console.Error.WriteLine("Failed to refresh token: 401 Unauthorized");

                return 1;
            case "write-stopped":
                Console.Error.WriteLine("fake codex: cannot write /etc/hosts: Read-only file system");

                return 1;
            case "ignores-term":
                // A list that is never finished and a process that ignores the stop signal (an unit that cannot be stopped, once the stop timeout is taken away).
                using (PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => context.Cancel = true))
                {
                    Thread.Sleep(TimeSpan.FromMinutes(5));
                }

                return 0;
            case "slow":
                Thread.Sleep(TimeSpan.FromMinutes(2));

                return 0;
        }

        if (mode.StartsWith("slugs:", StringComparison.Ordinal))
        {
            // slugs:<slug>=<visibility>,<slug>=<visibility>… — a list of exactly these models, in this order, in the shape of the real one.
            var models = mode["slugs:".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(pair => pair.Split('=')).Select(pair => $"{{\"slug\":\"{pair[0]}\",\"visibility\":\"{(pair.Length > 1 ? pair[1] : "list")}\"}}");
            Console.Out.Write("{\"models\":[" + string.Join(',', models) + "]}");

            return 0;
        }

        var file = mode.Length > 0 ? mode : Path.Combine(AppContext.BaseDirectory, "fixtures", "role-stops", "codex-debug-models.json");
        if (!File.Exists(file))
        {
            Console.Error.WriteLine("fake codex: no recording of debug models at " + file);

            return 1;
        }

        using var recording = JsonDocument.Parse(File.ReadAllText(file));
        var root = recording.RootElement;
        Console.Out.Write(root.TryGetProperty("stdout", out var stdout) ? stdout.GetString() : string.Empty);
        Console.Error.Write(root.TryGetProperty("stderr", out var stderr) ? stderr.GetString() : string.Empty);

        return root.TryGetProperty("exitCode", out var code) && code.TryGetInt32(out var exit) ? exit : 0;
    }

    // ---------- debug prompt-input ----------

    /// <summary>The nested tools of the JavaScript tool <c>exec</c>, named in its description as the real CLI names them.</summary>
    private static readonly string[] Nested = ["apply_patch", "exec_command", "write_stdin", "list_mcp_resources", "list_mcp_resource_templates", "read_mcp_resource", "clock__curr_time"];

    private static int PromptInput(string[] args)
    {
        if (ExpectedModelProblem(args) is { Length: > 0 } problem)
        {
            Console.Error.WriteLine(problem);

            return 2;
        }

        var mode = Steering("FAKECLI_CODEX_TOOLS");
        // A project that has its own config (the stand-in project of the preflight) changes the list in these modes: it takes a tool AWAY, or leaves none (epoch 3 E3 s2 t5).
        var project = File.Exists(Path.Combine(Directory.GetCurrentDirectory(), ".codex", "config.toml"));
        if (mode == "unreadable")
        {
            Console.Out.Write("this is not a report\n");

            return 0;
        }

        var agents = !args.Contains("agents.enabled=false") || mode == "agents";
        string[] without = mode switch
        {
            "fewer" => ["write_stdin"],
            "nine" => ["write_stdin", "read_mcp_resource"],
            "project-removes" when project => ["write_stdin"],
            _ => [],
        };
        var nested = Nested.Where(name => !without.Contains(name));
        // The nested inventory in ANOTHER form (a list in a sentence): the text a shorter list could be read from, if the grammar did not refuse it (epoch 3 E3 s2 t5, V36).
        var otherForm = mode == "other-form" || (mode == "project-other-form" && project);
        var description = otherForm
            ? "Run JavaScript. The tools you may call from it are: " + string.Join(", ", nested) + "."
            : "Run JavaScript.\n" + string.Concat(nested.Select(name => $"### `{name}`\n"));
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();
            json.WriteStartObject();
            json.WriteString("type", "message");
            json.WriteString("role", "developer");
            json.WriteString("content", "a made-up prompt");
            json.WriteEndObject();
            json.WriteStartObject();
            json.WriteString("type", "additional_tools");
            json.WriteStartArray("tools");
            if (mode == "empty" || (mode == "project-empty" && project))
            {
                json.WriteEndArray();
                json.WriteEndObject();
                json.WriteEndArray();
                json.Flush();
                Console.Out.Write(Encoding.UTF8.GetString(buffer.ToArray()));

                return 0;
            }

            if (mode == "one")
            {
                // ONE name: a single top-level tool and no JavaScript tool, so there is no nested inventory to read.
                json.WriteStartObject();
                json.WriteString("name", "apply_patch");
                json.WriteEndObject();
            }
            else
            {
                Namespace(json, "functions", [("exec", description), ("wait", string.Empty), ("request_user_input", string.Empty), ("request_user_input_async", string.Empty)]);
            }

            if (agents)
            {
                Namespace(json, "collaboration", [.. new[] { "followup_task", "interrupt_agent", "list_agents", "send_message", "spawn_agent", "wait_agent" }.Select(name => (name, string.Empty))]);
            }

            foreach (var name in ExtraTools(mode, Directory.GetCurrentDirectory(), args))
            {
                json.WriteStartObject();
                json.WriteString("name", name);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
            if (mode == "second-item")
            {
                // A second item of the same type (A4-M1 item 1): the read must not take the first and go on.
                json.WriteStartObject();
                json.WriteString("type", "additional_tools");
                json.WriteStartArray("tools");
                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        Console.Out.Write(Encoding.UTF8.GetString(buffer.ToArray()));

        return 0;
    }

    /// <summary>The tools a mode adds at the top level: an account connector, one a project's own config brings (the project is NOT trusted by the real CLI; a mode makes the fake trust it), or a name that is hostile text.</summary>
    private static IEnumerable<string> ExtraTools(string mode, string cwd, string[] args) => mode switch
    {
        // A new tool for ONE model only (the model of the probe is asked as -c model="…"): the entry of that model is refused, the next one is not (epoch 3 E3 s2 t5, V33).
        _ when mode.StartsWith("new-tool-in:", StringComparison.Ordinal) && args.Contains($"model=\"{mode["new-tool-in:".Length..]}\"") => ["brand_new_tool"],
        // A new tool that shows from the THIRD probe of the job on (the preflight of the first launch makes two: the worktree and the stand-in project), so it shows on a repair turn or a fix turn that was not there at turn 0.
        "late-tool" when ProbeCalls() >= 3 => ["brand_new_tool"],
        // A tool that only the read in the worktree shows: what a file of the repository could add (A4-M2); the stand-in project is coai's own folder and never shows it.
        "worktree-only" when !File.Exists(Path.Combine(cwd, ".codex", "config.toml")) => ["worktree_only_tool"],
        "mcp" => ["mcp__ologs__get_profile"],
        "project-mcp" when File.Exists(Path.Combine(cwd, ".codex", "config.toml")) => ["mcp__standin__probe"],
        "hostile" => ["HOSTILE-TEXT-A-ROW-MUST-NOT-CARRY"],
        "new-tool" => ["brand_new_tool"],
        "many-new" => [.. Enumerable.Range(1, 8).Select(index => $"new_tool_{index}")],
        "hostile-names" => ["bad\nname", "control\u0001char", "sk-ant-oat01-abcdefghij/klmnop+qrstuv==", "sk-ant-oat01-abcdefghijklmnopqrstuvwxyz0123456789ABCDEF", new string('a', 300)],
        _ => [],
    };

    /// <summary>How many calls of <c>debug</c> (prompt-input or models) this CODEX_HOME has seen, this one included (<c>fake-verbs.log</c>).</summary>
    private static int ProbeCalls() => CodexHome() is { Length: > 0 } home && File.Exists(Path.Combine(home, "fake-verbs.log")) ? File.ReadAllLines(Path.Combine(home, "fake-verbs.log")).Count(line => line == "debug") : 0;

    private static void Namespace(Utf8JsonWriter json, string name, IEnumerable<(string Name, string Description)> tools)
    {
        json.WriteStartObject();
        json.WriteString("name", name);
        json.WriteStartArray("tools");
        foreach (var (tool, description) in tools)
        {
            json.WriteStartObject();
            json.WriteString("name", tool);
            json.WriteString("description", description);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    /// <summary>With <c>FAKECLI_CODEX_EXPECT_MODEL</c> the call must name that model as <c>-c model="…"</c> or <c>-m …</c>: a probe that ran for another model is a fault the test reads.</summary>
    private static string ExpectedModelProblem(string[] args)
    {
        var expect = Steering("FAKECLI_CODEX_EXPECT_MODEL");

        return expect.Length == 0 || args.Contains($"model=\"{expect}\"") || Array.IndexOf(args, "-m") is var at && at >= 0 && at + 1 < args.Length && args[at + 1] == expect
            ? string.Empty
            : $"fake codex: the probe was not asked for the model {expect}";
    }

    // ---------- doctor ----------

    private static int Doctor(string[] args)
    {
        var mode = Steering("FAKECLI_CODEX_DOCTOR");
        if (mode == "garbage")
        {
            Console.Out.Write("this is not a report\n");

            return 0;
        }

        var approval = args.Contains("approval_policy=\"never\"") ? "Never" : "OnRequest";
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("schemaVersion", 1);
            json.WriteString("overallStatus", "warning");
            json.WriteStartObject("checks");
            Check(json, "config.load", mode == "config-error" ? "fail" : "ok", mode == "config-error" ? "config could not be loaded" : "config loaded", [("mcp servers", mode == "mcp-servers" ? "1" : "0"), ("configuration scope", "invocation config, including cloud-managed policy")]);
            Check(json, "sandbox.filesystem_paths", "ok", "configured filesystem paths listed", mode == "no-profile" ? [("operation", "resolve paths only")] : [("permission profile", mode == "other-profile" ? "danger" : "coai")]);
            Check(json, "sandbox.helpers", "ok", "sandbox configuration is readable", [("approval policy", mode == "approval-open" ? "OnRequest" : approval), ("filesystem sandbox", mode == "filesystem-open" ? "unrestricted" : "restricted"), ("network sandbox", mode == "network-open" ? "enabled" : "restricted")]);
            if (mode == "hostile")
            {
                Check(json, "hostile", "ok", "HOSTILE-TEXT-A-ROW-MUST-NOT-CARRY", [("hostile", "HOSTILE-TEXT-A-ROW-MUST-NOT-CARRY")]);
            }

            json.WriteEndObject();
            json.WriteEndObject();
        }

        Console.Out.Write(Encoding.UTF8.GetString(buffer.ToArray()));

        return mode == "exit1" ? 1 : 0;
    }

    private static void Check(Utf8JsonWriter json, string id, string status, string summary, IEnumerable<(string Key, string Value)> details)
    {
        json.WriteStartObject(id);
        json.WriteString("id", id);
        json.WriteString("status", status);
        json.WriteString("summary", summary);
        json.WriteStartObject("details");
        foreach (var (key, value) in details)
        {
            json.WriteString(key, value);
        }

        json.WriteEndObject();
        json.WriteEndObject();
    }
}
