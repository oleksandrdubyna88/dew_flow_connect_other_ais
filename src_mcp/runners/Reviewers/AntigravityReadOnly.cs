using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// What keeps an antigravity launch from WRITING inside the roots it is given (research/PLAN_agy_cannot_write_its_roots.md),
/// and from READING outside them (research/PLAN_the_agy_hook_checks_the_path.md): <c>--mode plan</c> does not — agy 1.3.1 wrote a file inside its <c>--add-dir</c> root on both sides
/// (research/RESULTS_agy_searches_through_coai.md §3).
/// </summary>
/// <remarks>
/// <para><b>Two blocks, because each fails open alone</b> (research/RESULTS_agy_write_block.md, agy 1.3.2, both sides):
/// an agent whose <c>tools</c> list holds only <c>view_file</c>, selected with <c>--agent</c> — 0 of 3 written per side,
/// even with no prose against writing — and a <c>PreToolUse</c> hook that allows only the file readers, <c>view_file</c> or
/// <c>read_file</c> (agy 1.5.9 renamed the tool the model is offered; research/RESULTS_agy_read_file.md) — 0 of 3. An agent
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
/// <para><b>The hook checks the place too.</b> A reader is allowed only on a file inside a folder the launch was given — the
/// <c>workspacePaths</c> of the payload, resolved through links — and never inside the coai folder (the payload's <c>cwd</c>);
/// see <see cref="Decide"/>.</para>
/// </remarks>
public static class AntigravityReadOnly
{
    /// <summary>The agent every agy launch is started as — its tool list is <see cref="AgentTool"/> alone.</summary>
    public const string AgentName = "coai-reader";

    /// <summary>The one tool the agent file lists: agy reads a file it names with it, and that is all coai asks of agy.</summary>
    /// <remarks>Stays <c>view_file</c> on every agy version: measured on 1.5.9, listing it still yields <c>read_file</c>
    /// (research/RESULTS_agy_read_file.md), so the agent file never names <c>read_file</c>.</remarks>
    public const string AgentTool = "view_file";

    /// <summary>The tool names the hook allows: <c>view_file</c> on agy up to 1.5.8, <c>read_file</c> on 1.5.9
    /// (research/RESULTS_agy_read_file.md) — both read-only file readers. Matched exactly, case-sensitively.</summary>
    public static IReadOnlyList<string> HookAllowedTools { get; } = ["view_file", "read_file"];

    /// <summary>The argument that turns this binary into the hook's handler: a <c>PreToolUse</c> payload in, a decision out.</summary>
    public const string HookArgument = "--agy-hook";

    /// <summary>What a denied tool is told — and through it, the model.</summary>
    public const string DenyReason = "coai: this consultation is read-only; only view_file or read_file runs";

    /// <summary>What a read outside the folders the launch was given is told.</summary>
    public const string DenyOutsideReason = "coai: this consultation reads only inside the folders it was given";

    /// <summary>The arguments every agy argv carries.</summary>
    public static IReadOnlyList<string> AgentArguments { get; } = ["--agent", AgentName];

    /// <summary>Where the folders live: the person's OWN application-data folder, never the shared temporary one.</summary>
    /// <remarks>Security review of 0a32211c: on Linux <c>/tmp</c> is shared by every account, so another one could plant
    /// the folder — and the hook script agy runs as this user — before coai wrote it. Created owner-only on Unix.</remarks>
    public static string DefaultBase => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "coai-agy");

    private const string AgentsDir = ".agents";

    /// <summary>
    /// The folder an agy launch runs from, prepared for THIS binary — or an exception: no launch without its block.
    /// </summary>
    /// <remarks>Checked on every launch rather than once: something may have removed or changed it since the last.</remarks>
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

        if (!Path.IsPathRooted(baseDir))
        {
            // No per-user application-data folder (no HOME): a relative base would land beside whatever the process runs in.
            return new Prepared.Failed($"agy was not launched: there is no per-user folder to hold its read-only agent and hook ('{baseDir}')");
        }

        var folder = Path.Combine(baseDir, handler.Key());
        try
        {
            // The folders first, owner-only, and only then the files: written into a folder another account could still
            // enter, the hook script could be swapped before agy runs it (code round, codex — a permissive umask).
            string[] folders = [baseDir, folder, Path.Combine(folder, AgentsDir), Path.Combine(folder, AgentsDir, "agents")];
            foreach (var directory in folders)
            {
                Directory.CreateDirectory(directory);
            }

            OwnerOnly(folders);
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

        // The folder exists, owner-only, before any file is written (Prepare).
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, content);
        File.Move(temporary, path, overwrite: true);
    }

    private const UnixFileMode Owner = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>
    /// Every folder of ours entered by its owner only, on Unix — set explicitly and checked on every launch, because the
    /// mode handed to <c>Directory.CreateDirectory</c> was not what the folders got — live in WSL, 2026-10-09: the one
    /// prepared tree (its base and handler folders) both 0755 after one product run, and the owner-only test red in its
    /// one WSL run (493). Windows' per-user folder is its owner's already.
    /// </summary>
    private static void OwnerOnly(IEnumerable<string> directories)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (var directory in directories)
        {
            if (File.GetUnixFileMode(directory) != Owner)
            {
                File.SetUnixFileMode(directory, Owner);
            }
        }
    }

    /// <summary>The agent: its tool list is the block; its prose tells the model why, which the probe showed it then cites.</summary>
    public const string AgentFile =
        "---\n"
        + $"name: {AgentName}\n"
        + "description: A read-only consultant for ConnectOtherAIs. It reads files with view_file and never writes, edits or deletes.\n"
        + "tools:\n"
        + $"  - {AgentTool}\n"
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
                        // The handler's cwd is this file's folder; the script is named RELATIVE to it, explicitly. A bare
                        // `coai-hook.cmd` is not found where NoDefaultCurrentDirectoryInExePath is set — Claude Code sets
                        // it, agy inherits it — and on Windows the hook then silently did nothing (live, 2026-10-09).
                        ["command"] = windows ? @".\coai-hook.cmd" : "sh ./coai-hook.sh",
                        ["timeout"] = 10,
                    }),
                }),
            },
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    /// <summary>
    /// The handler's decision for one <c>PreToolUse</c> payload: allow one of <see cref="HookAllowedTools"/> on a file inside a folder
    /// the launch was given and outside the coai folder, deny every other tool or place — and deny what cannot be read, so a payload
    /// this code does not understand never lets a write or a read outside through. Never throws.
    /// </summary>
    /// <param name="payload">The text on the hook's standard input.</param>
    /// <param name="read">How one entry is looked at; the real file system when null (the seam <see cref="RealPath.Resolve"/> has).</param>
    /// <remarks>
    /// <para>Read from the payload's own <c>toolCall.name</c>, never by searching its text: a tool's ARGUMENTS can
    /// contain any string, the name of an allowed tool included.</para>
    /// <para><b>The place</b> (research/PLAN_the_agy_hook_checks_the_path.md, S1): <c>toolCall.args.Path</c>, <c>cwd</c> and every entry of
    /// <c>workspacePaths</c> must be fully qualified — so the hook process's own directory, which is the coai folder, is no base —
    /// and are resolved with <see cref="RealPath"/>, root-down, links followed, the last name included. The target must be inside one
    /// resolved entry and NOT inside the resolved <c>cwd</c> (the coai folder: the hook script, the agent file). A payload with two
    /// spellings of <c>Path</c>, a duplicate property, or any entry that does not resolve is denied whole. Both denials are constants:
    /// <see cref="DenyReason"/> for the tool, <see cref="DenyOutsideReason"/> for the place; neither quotes the model's path.</para>
    /// </remarks>
    public static string Decide(string payload, EntryReader? read = null) =>
        Parse(payload) is not { } call || !HookAllowedTools.Contains(ToolName(call), StringComparer.Ordinal)
            ? Deny(DenyReason)
            : ReadsInside(call, read) ? """{"decision":"allow"}""" : Deny(DenyOutsideReason);

    private static string Deny(string reason) => new JsonObject { ["decision"] = "deny", ["reason"] = reason }.ToJsonString();

    private static JsonObject? Parse(string payload)
    {
        try
        {
            return JsonNode.Parse(payload, nodeOptions: null, new JsonDocumentOptions { AllowDuplicateProperties = false }) as JsonObject;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static string ToolName(JsonObject call) =>
        call["toolCall"] is JsonObject tool && Text(tool["name"]) is { } name ? name : string.Empty;

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>The one place the process edge catches everything: a handler that answers nothing let a write through on Windows (research/RESULTS_agy_write_block.md).</summary>
    private static bool ReadsInside(JsonObject call, EntryReader? read)
    {
        try
        {
            return Placed(call, read) is { } places && places.Allows();
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>What the place check compares, every one of them resolved.</summary>
    private sealed record Places(string Target, string Cwd, IReadOnlyList<string> Roots)
    {
        public bool Allows() =>
            !RealPath.IsInside(Cwd, Target, DocumentId.Comparison)
            && Roots.Any(root => RealPath.IsInside(root, Target, DocumentId.Comparison));
    }

    private static Places? Placed(JsonObject call, EntryReader? read) =>
        Real(PathText(call), read) is { } target
        && Real(Qualified(call["cwd"]), read) is { } cwd
        && Roots(call["workspacePaths"], read) is { } roots
            ? new Places(target, cwd, roots)
            : null;

    /// <summary>The <c>Path</c> argument: the ONE property of <c>args</c> whose name is <c>Path</c> without regard to case, and spelled <c>Path</c>.</summary>
    private static string? PathText(JsonObject call) =>
        call["toolCall"]?["args"] is JsonObject args
        && args.Where(pair => string.Equals(pair.Key, "Path", StringComparison.OrdinalIgnoreCase)).ToList() is [{ Key: "Path", Value: var value }]
            ? Qualified(value)
            : null;

    private static string? Qualified(JsonNode? node) =>
        Text(node) is { } text && Path.IsPathFullyQualified(text) ? text : null;

    private static string? Real(string? text, EntryReader? read) =>
        text is not null && RealPath.Resolve(text, read: read) is { Ok: true } real ? real.Path : null;

    /// <summary>Every entry of <c>workspacePaths</c>, resolved; null when it is not an array or ONE element is not a resolvable full path.</summary>
    private static IReadOnlyList<string>? Roots(JsonNode? node, EntryReader? read)
    {
        if (node is not JsonArray entries)
        {
            return null;
        }

        var roots = entries.Select(entry => Real(Qualified(entry), read)).ToList();

        return roots.All(root => root is not null) ? roots.OfType<string>().ToList() : null;
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
