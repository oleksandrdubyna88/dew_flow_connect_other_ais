using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// What keeps an antigravity launch from WRITING inside the roots it is given (todo/PLAN_agy_cannot_write_its_roots.md):
/// <c>--mode plan</c> does not — agy 1.3.1 wrote a file inside its <c>--add-dir</c> root on both sides
/// (research/RESULTS_agy_searches_through_coai.md §3).
/// </summary>
/// <remarks>
/// <para><b>Two blocks, because each fails open alone</b> (research/RESULTS_agy_write_block.md, agy 1.3.2, both sides):
/// an agent whose <c>tools</c> list holds only <c>view_file</c>, selected with <c>--agent</c> — 0 of 3 written per side,
/// even with no prose against writing — and a <c>PreToolUse</c> hook that allows only <c>view_file</c> — 0 of 3. An agent
/// that is not found falls back to agy's default agent and writes; a hook handler that answers nothing let a write through
/// on Windows. Together a write needs both to fail at once. Reads still work (3 of 3 each).</para>
/// <para><b>Where they live.</b> In a folder coai owns, the launch's cwd — never the operator's agy configuration, never a
/// root (the plan round's finding 0): agy discovers <c>.agents/agents/</c> and <c>.agents/hooks.json</c> from its cwd, and
/// reaches the roots through <c>--add-dir</c>. The folder is named by the handler that serves it, so every launch of one
/// binary shares one folder with fixed content: there is nothing per launch to leave behind, and <see cref="Prepare"/>
/// rewrites whatever is not exactly what it should be.</para>
/// <para><b>The handler is this binary</b> (<see cref="HookArgument"/>), started through a two-line script in the folder:
/// a quoted absolute path in the hook's own command broke under <c>cmd /c</c> on Windows (the probe's first run). Inside
/// a script the quotes are ordinary.</para>
/// </remarks>
public static class AntigravityReadOnly
{
    /// <summary>The agent every agy launch is started as — its tool list is <see cref="AllowedTool"/> alone.</summary>
    public const string AgentName = "coai-reader";

    /// <summary>The one tool a launch may use: agy reads a file it names with it, and that is all coai asks of agy.</summary>
    public const string AllowedTool = "view_file";

    /// <summary>The argument that turns this binary into the hook's handler: a <c>PreToolUse</c> payload in, a decision out.</summary>
    public const string HookArgument = "--agy-hook";

    /// <summary>What a denied tool is told — and through it, the model.</summary>
    public const string DenyReason = "coai: this consultation is read-only; only view_file runs";

    /// <summary>The arguments every agy argv carries.</summary>
    public static IReadOnlyList<string> AgentArguments { get; } = ["--agent", AgentName];

    /// <summary>Where the folders live: the system's temporary folder, under a name of coai's own.</summary>
    public static string DefaultBase => Path.Combine(Path.GetTempPath(), "coai-agy");

    private const string AgentsDir = ".agents";

    /// <summary>
    /// The folder an agy launch runs from, prepared for THIS binary — or an exception: no launch without its block.
    /// </summary>
    /// <remarks>Checked on every launch rather than once: a temp-folder cleaner may have removed it since the last.</remarks>
    public static string Home() => Prepare(DefaultBase, HookHandler.OfThisProcess()) switch
    {
        Prepared.Ready ready => ready.Folder,
        Prepared.Failed failed => throw new InvalidOperationException(failed.Reason),
        _ => throw new InvalidOperationException("the union is closed"),
    };

    /// <summary>What preparing the folder came to: the folder, or why there is none.</summary>
    public abstract record Prepared
    {
        private Prepared()
        {
        }

        public sealed record Ready(string Folder) : Prepared;

        public sealed record Failed(string Reason) : Prepared;
    }

    /// <summary>
    /// The folder for <paramref name="handler"/> under <paramref name="baseDir"/>, every file in it exactly as it should be
    /// — or <see cref="Prepared.Failed"/>, never a folder missing part of its block.
    /// </summary>
    public static Prepared Prepare(string baseDir, HookHandler handler)
    {
        if (handler.Unsafe() is { Length: > 0 } why)
        {
            return new Prepared.Failed($"agy was not launched: its hook handler {why}");
        }

        var folder = Path.Combine(baseDir, handler.Key());
        try
        {
            foreach (var (relative, content) in Files(handler))
            {
                Ensure(Path.Combine(folder, relative), content);
            }

            return new Prepared.Ready(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return new Prepared.Failed($"agy was not launched: coai could not prepare its read-only agent and hook in '{folder}': {e.Message}");
        }
    }

    /// <summary>The four files, by path relative to the folder.</summary>
    private static IEnumerable<(string Path, string Content)> Files(HookHandler handler) =>
    [
        (Path.Combine(AgentsDir, "agents", AgentName + ".md"), AgentFile),
        (Path.Combine(AgentsDir, "hooks.json"), HooksFile(OperatingSystem.IsWindows())),
        (Path.Combine(AgentsDir, "coai-hook.cmd"), handler.CmdScript()),
        (Path.Combine(AgentsDir, "coai-hook.sh"), handler.ShScript()),
    ];

    /// <summary>Writes <paramref name="content"/> unless the file already holds exactly it — atomically, so two launches preparing at once both see a whole file.</summary>
    private static void Ensure(string path, string content)
    {
        if (File.Exists(path) && File.ReadAllText(path) == content)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, content);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>The agent: its tool list is the block; its prose tells the model why, which the probe showed it then cites.</summary>
    public const string AgentFile =
        "---\n"
        + $"name: {AgentName}\n"
        + "description: A read-only consultant for ConnectOtherAIs. It reads files with view_file and never writes, edits or deletes.\n"
        + "tools:\n"
        + $"  - {AllowedTool}\n"
        + "mainAgent: true\n"
        + "subagent: false\n"
        + "commandExecutionPolicy: \"off\"\n"
        + "---\n"
        + "You answer from what you read. You never create, edit or delete files.\n";

    /// <summary>The hook: every tool call goes to the handler, which answers for each one.</summary>
    public static string HooksFile(bool windows) =>
        new JsonObject
        {
            ["coai-read-only"] = new JsonObject
            {
                ["PreToolUse"] = new JsonArray(new JsonObject
                {
                    ["matcher"] = "*",
                    ["hooks"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "command",
                        // The handler's cwd is this file's folder, so the script is named, never a path.
                        ["command"] = windows ? "coai-hook.cmd" : "sh coai-hook.sh",
                        ["timeout"] = 10,
                    }),
                }),
            },
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    /// <summary>
    /// The handler's decision for one <c>PreToolUse</c> payload: allow <see cref="AllowedTool"/>, deny every other tool —
    /// and deny what cannot be read, so a payload this code does not understand never lets a write through.
    /// </summary>
    /// <remarks>Read from the payload's own <c>toolCall.name</c>, never by searching its text: a tool's ARGUMENTS can
    /// contain any string, the name of the allowed tool included.</remarks>
    public static string Decide(string payload) =>
        ToolName(payload) == AllowedTool
            ? """{"decision":"allow"}"""
            : new JsonObject { ["decision"] = "deny", ["reason"] = DenyReason }.ToJsonString();

    private static string ToolName(string payload)
    {
        try
        {
            return JsonNode.Parse(payload)?["toolCall"]?["name"]?.GetValue<string>() ?? string.Empty;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return string.Empty;
        }
    }

    /// <summary>The hook mode: the payload on <paramref name="input"/>, the decision on <paramref name="output"/>, exit 0.</summary>
    public static int RunHook(TextReader input, TextWriter output)
    {
        output.Write(Decide(input.ReadToEnd()));
        output.Flush();

        return 0;
    }
}

/// <summary>The program the hook starts: an executable and the arguments that come before <see cref="AntigravityReadOnly.HookArgument"/>.</summary>
public sealed record HookHandler(string Executable, IReadOnlyList<string> Prefix)
{
    /// <summary>This binary, however it was started — the one <see cref="LocalRuntime"/> starts for a local engine too.</summary>
    public static HookHandler OfThisProcess()
    {
        var (executable, prefix) = LocalRuntime.SelfInvocation();

        return new HookHandler(executable, prefix);
    }

    private IEnumerable<string> Parts => [Executable, .. Prefix];

    /// <summary>Why this handler cannot be written into a script safely — a quote or a line break in a path — or empty.</summary>
    internal string Unsafe() =>
        Parts.FirstOrDefault(part => part.Length == 0 || part.IndexOfAny(['"', '\r', '\n', '%', '`', '$']) >= 0) is { } bad
            ? $"path '{bad.Replace("\n", "\\n", StringComparison.Ordinal)}' cannot be written into a script safely"
            : string.Empty;

    /// <summary>A short, stable name for this handler's folder.</summary>
    internal string Key() =>
        "h" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', Parts))))[..12].ToLowerInvariant();

    internal string CmdScript() =>
        "@" + string.Join(' ', Parts.Select(part => $"\"{part}\"")) + " " + AntigravityReadOnly.HookArgument + "\r\n";

    internal string ShScript() =>
        "#!/bin/sh\nexec " + string.Join(' ', Parts.Select(part => $"\"{part}\"")) + " " + AntigravityReadOnly.HookArgument + "\n";
}
