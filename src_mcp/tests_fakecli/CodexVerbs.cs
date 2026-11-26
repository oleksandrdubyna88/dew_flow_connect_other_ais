using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CoaiMcp.FakeCli;

/// <summary>
/// The fake CLI as the codex CODER's model-free verbs (epoch 3 E3 s5 t3): <c>sandbox -P coai -- &lt;command&gt;</c>, <c>debug prompt-input &lt;flags&gt; x</c> and <c>doctor --json</c>, as MEASURED on codex-cli 0.171.0 (<c>v2/spikes/RESULTS_codex_coder.md</c> sections 4 and 9). A real codex is never started by a test.
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
    public static bool Handles(string[] args) => args is ["sandbox", ..] or ["doctor", ..] or ["debug", "prompt-input", ..];

    /// <summary>Runs the verb and answers its exit code.</summary>
    public static int Run(string[] args) => args[0] switch
    {
        "sandbox" => Sandbox(args),
        "doctor" => Doctor(args),
        _ => PromptInput(args),
    };

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
        var agents = !args.Contains("agents.enabled=false") || mode == "agents";
        var nested = Nested.Where(name => mode != "fewer" || name != "write_stdin");
        var description = "Run JavaScript. " + string.Concat(nested.Select(name => $"### `{name}`\n"));
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
            Namespace(json, "functions", [("exec", description), ("wait", string.Empty), ("request_user_input", string.Empty), ("request_user_input_async", string.Empty)]);
            if (agents)
            {
                Namespace(json, "collaboration", [.. new[] { "followup_task", "interrupt_agent", "list_agents", "send_message", "spawn_agent", "wait_agent" }.Select(name => (name, string.Empty))]);
            }

            foreach (var name in ExtraTools(mode, Directory.GetCurrentDirectory()))
            {
                json.WriteStartObject();
                json.WriteString("name", name);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteEndArray();
        }

        Console.Out.Write(Encoding.UTF8.GetString(buffer.ToArray()));

        return 0;
    }

    /// <summary>The tools a mode adds at the top level: an account connector, one a project's own config brings (the project is NOT trusted by the real CLI; a mode makes the fake trust it), or a name that is hostile text.</summary>
    private static IEnumerable<string> ExtraTools(string mode, string cwd) => mode switch
    {
        "mcp" => ["mcp__ologs__get_profile"],
        "project-mcp" when File.Exists(Path.Combine(cwd, ".codex", "config.toml")) => ["mcp__standin__probe"],
        "hostile" => ["HOSTILE-TEXT-A-ROW-MUST-NOT-CARRY"],
        _ => [],
    };

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
