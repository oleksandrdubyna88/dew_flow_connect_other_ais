using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// The write guard of the agy CODER (epoch 3 E3 s6 t2; judgement G2, G3, G4 as G15 amends it, G5; SPEC C6, V15): the folder the coder's unit starts in — a coder agent with the measured write tools, and a <c>PreToolUse</c> hook whose handler is coai's own program (<c>--agy-hook</c>) — and the
/// decision that handler gives for every input.
/// </summary>
/// <remarks>
/// <para><b>Closed by construction</b> (G2). The handler writes an explicit decision for EVERY input: an empty input, text that is not JSON, a payload with no <c>Path</c>, a payload it cannot read, an exception, a deadline of its own (<see cref="Deadline"/>, under the hook's 10 s) and a record it cannot write are each a
/// <c>deny</c>. The one answer agy 1.5.17 and 1.5.18 take as <i>allow</i> — exit 0 and no output — is never given. A decision that cannot be WRITTEN is a non-zero exit (agy fails a tool closed on a non-zero exit).</para>
/// <para><b>Which case it is</b> is chosen by the guard's OWN file, <see cref="GuardFile"/>, in the handler's folder (the folder of <c>hooks.json</c>, the handler's working directory) — never by a field of the payload, which the model's turn can influence. With no such file the handler is the read-only
/// handler of the consultants, byte for byte (<see cref="AntigravityReadOnly.Decide"/>); with one it is the coder's guard, and a file that cannot be read is a deny for everything, never the read-only case.</para>
/// <para><b>What it allows</b> (V15): <c>read_file</c>, <c>write_file</c> and <c>edit_file</c> (the names the hook sees; the agent file lists the init names), and only when <c>Path</c>, resolved through links, is inside the worktree that coai wrote into the guard's file AND that the payload's <c>workspacePaths</c> hold,
/// outside the payload's <c>cwd</c> (this folder) and not under <c>.git</c>, <c>.agents</c> or <c>.codex</c> of the worktree. Everything else is denied.</para>
/// <para><b>The record</b> (G4 as G15 amends it): one line for each decision — the conversation id, the step index, the tool name and the decision; never the content of a write — appended to the file the guard's file names, which lies in the job's coder folder outside the worktree and outside this folder. The step
/// index is agy's own counter and the only thing the stream and the hook share (judgement 14.2, J23); <c>AgyTurnCheck</c> lays the stream beside these lines after every turn.</para>
/// </remarks>
public static class AntigravityCoderGuard
{
    /// <summary>The agent every coder launch is started as.</summary>
    public const string AgentName = "coai-coder";

    /// <summary>The guard's own file, beside <c>hooks.json</c>: the worktree and the records file coai wrote for this job.</summary>
    public const string GuardFile = "coai-guard.json";

    /// <summary>The tools the handler allows, by the names the hook sees.</summary>
    public static IReadOnlyList<string> AllowedTools { get; } = ["read_file", "write_file", "edit_file"];

    /// <summary>The handler's own time: under the hook's 10 s, so a handler that is slow is denied by itself and not killed.</summary>
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    /// <summary>What a denied tool is told.</summary>
    public const string DenyReason = "coai: this coder works only inside its worktree, with read_file, write_file and edit_file";

    /// <summary>What a path outside the worktree, or under <c>.git</c>, <c>.agents</c> or <c>.codex</c> of it, is told.</summary>
    public const string DenyPlaceReason = "coai: this coder reads and writes only inside its worktree, and not under .git, .agents or .codex";

    /// <summary>What a decision that could not be recorded is told.</summary>
    public const string DenyRecordReason = "coai: the guard could not record this decision";

    private const string AgentsDir = ".agents";

    /// <summary>The names under the worktree that no tool of the coder reaches.</summary>
    private static readonly string[] Reserved = [".git", ".agents", ".codex"];

    /// <summary>
    /// The agent file, the measured bytes (<c>RESULTS_agy_coder.md</c> section 2; the comparison of E2 ran this file): the init names of the four tools of the coder — <c>view_file</c>, <c>write_to_file</c>, <c>replace_file_content</c>, <c>multi_replace_file_content</c> — and
    /// <c>commandExecutionPolicy: "off"</c>. The model's own list is <c>read_file</c>, <c>write_file</c>, <c>edit_file</c> and <c>manage_task</c> (the hook names differ from the init names).
    /// </summary>
    public const string AgentFile =
        "---\n"
        + $"name: {AgentName}\n"
        + "description: coai coder\n"
        + "tools:\n"
        + "  - view_file\n"
        + "  - write_to_file\n"
        + "  - replace_file_content\n"
        + "  - multi_replace_file_content\n"
        + "mainAgent: true\n"
        + "subagent: false\n"
        + "commandExecutionPolicy: \"off\"\n"
        + "---\n"
        + "Do what the person asks with the tools you have.\n";

    /// <summary>The hook: every tool call goes to the handler (matcher <c>*</c>, timeout 10), which answers for each one.</summary>
    public static string HooksFile(bool windows) =>
        new JsonObject
        {
            ["coai-coder-guard"] = new JsonObject
            {
                ["PreToolUse"] = new JsonArray(new JsonObject
                {
                    ["matcher"] = "*",
                    ["hooks"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = windows ? @".\coai-hook.cmd" : "sh ./coai-hook.sh",
                        ["timeout"] = 10,
                    }),
                }),
            },
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    /// <summary>The guard's own file for a job: the worktree and the records file, as the handler reads them.</summary>
    public static string GuardFileText(string worktree, string recordsFile) =>
        new JsonObject { ["version"] = 1, ["worktree"] = worktree, ["records"] = recordsFile }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Every file of the guard folder, by path relative to it, with the bytes it must hold.</summary>
    public static IReadOnlyList<(string Path, string Content)> Files(HookHandler handler, string worktree, string recordsFile) =>
    [
        (Path.Combine(AgentsDir, "agents", AgentName + ".md"), AgentFile),
        (Path.Combine(AgentsDir, "hooks.json"), HooksFile(OperatingSystem.IsWindows())),
        (Path.Combine(AgentsDir, "coai-hook.cmd"), handler.CmdScript()),
        (Path.Combine(AgentsDir, "coai-hook.sh"), handler.ShScript()),
        (Path.Combine(AgentsDir, GuardFile), GuardFileText(worktree, recordsFile)),
    ];

    private const UnixFileMode Owner = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>
    /// The guard folder of one job, made for THIS launch: every file exactly as <see cref="Files"/> says, nothing else in it, the folders owner-only, then every file READ BACK and compared (G5, AG-G3). Run before EVERY launch, from outside the unit. A refusal says why; never a folder missing part of its block.
    /// </summary>
    public static AntigravityReadOnly.Prepared Prepare(string folder, HookHandler handler, string worktree, string recordsFile)
    {
        if (handler.Unsafe() is { Length: > 0 } why)
        {
            return new AntigravityReadOnly.Prepared.Failed($"agy was not launched: its hook handler {why}");
        }

        if (!Path.IsPathRooted(folder) || !Path.IsPathFullyQualified(worktree) || !Path.IsPathFullyQualified(recordsFile))
        {
            return new AntigravityReadOnly.Prepared.Failed("agy was not launched: the guard folder, the worktree and the records file must be full paths");
        }

        // The records are outside the worktree (a model could write there) and outside this folder (which is read-only in the unit, so the handler could not append).
        if (RealPath.IsInside(worktree, recordsFile, StringComparison.Ordinal) || RealPath.IsInside(folder, recordsFile, StringComparison.Ordinal))
        {
            return new AntigravityReadOnly.Prepared.Failed("agy was not launched: the guard's records file must lie outside the worktree and outside the guard folder");
        }

        try
        {
            var files = Files(handler, worktree, recordsFile);
            string[] folders = [folder, Path.Combine(folder, AgentsDir), Path.Combine(folder, AgentsDir, "agents")];
            foreach (var directory in folders)
            {
                if (RealPath.Lstat(directory).Kind is EntryKind.File or EntryKind.Link)
                {
                    File.Delete(directory);
                }

                Directory.CreateDirectory(directory);
            }

            if (!OperatingSystem.IsWindows())
            {
                foreach (var directory in folders)
                {
                    File.SetUnixFileMode(directory, Owner);
                }
            }

            Sweep(folder, files.Select(file => Path.Combine(folder, file.Path)).ToHashSet(StringComparer.Ordinal), folders.ToHashSet(StringComparer.Ordinal));
            foreach (var (relative, content) in files)
            {
                Write(Path.Combine(folder, relative), content);
            }

            return ReadBack(folder, files) is { Length: > 0 } wrong
                ? new AntigravityReadOnly.Prepared.Failed($"agy was not launched: the coder's guard folder does not hold what coai wrote ({wrong})")
                : new AntigravityReadOnly.Prepared.Ready(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return new AntigravityReadOnly.Prepared.Failed($"agy was not launched: coai could not prepare the coder's guard folder ({e.GetType().Name})");
        }
    }

    /// <summary>Whether every file of <paramref name="files"/> is under <paramref name="folder"/> with exactly its bytes, and nothing else is — the sentence of the first difference, or empty.</summary>
    public static string ReadBack(string folder, IReadOnlyList<(string Path, string Content)> files)
    {
        foreach (var (relative, content) in files)
        {
            var path = Path.Combine(folder, relative);
            if (RealPath.Lstat(path).Kind != EntryKind.File || File.ReadAllText(path) != content)
            {
                return $"{relative.Replace(Path.DirectorySeparatorChar, '/')} is not the expected file";
            }
        }

        return string.Empty;
    }

    /// <summary>Removes everything under <paramref name="folder"/> that is not an expected file or folder; a link is removed as a link and never followed.</summary>
    private static void Sweep(string folder, HashSet<string> files, HashSet<string> folders)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.TopDirectoryOnly))
        {
            var kind = RealPath.Lstat(entry).Kind;
            if (kind == EntryKind.Directory && folders.Contains(entry))
            {
                Sweep(entry, files, folders);
            }
            else if (!files.Contains(entry) || kind != EntryKind.File)
            {
                if (kind == EntryKind.Directory)
                {
                    Directory.Delete(entry, recursive: true);
                }
                else
                {
                    File.Delete(entry);
                }
            }
        }
    }

    private static void Write(string path, string content)
    {
        if (RealPath.Lstat(path).Kind is EntryKind.File or EntryKind.Link)
        {
            File.Delete(path);
        }

        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        }

        using var stream = new FileStream(path, options);
        stream.Write(new UTF8Encoding(false).GetBytes(content));
    }

    // ---------- the handler ----------

    /// <summary>
    /// The hook mode (<c>--agy-hook</c>): the payload on <paramref name="input"/>, the decision on <paramref name="output"/>. Exit 0 when the decision was written; 1 when it could not be (a closed output). With no guard file in <paramref name="handlerFolder"/> it is the read-only
    /// handler (<see cref="AntigravityReadOnly.RunHook"/>); with one it is the coder's guard, under its own deadline.
    /// </summary>
    public static int RunHook(TextReader input, TextWriter output, string handlerFolder, TextWriter? log = null)
    {
        if (RealPath.Lstat(Path.Combine(handlerFolder, GuardFile)).Kind == EntryKind.Missing)
        {
            return AntigravityReadOnly.RunHook(input, output);
        }

        var decision = DecideWithin(input, handlerFolder, Deadline, log);
        try
        {
            output.Write(decision);
            output.Flush();

            return 0;
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            return 1;
        }
    }

    /// <summary>The decision for the payload on <paramref name="input"/> inside <paramref name="deadline"/>: whatever is not an answer in time — a read that blocks, an exception — is a deny.</summary>
    public static string DecideWithin(TextReader input, string handlerFolder, TimeSpan deadline, TextWriter? log = null, EntryReader? read = null)
    {
        try
        {
            var work = Task.Run(() => Decide(input.ReadToEnd(), handlerFolder, read, log));

            return work.Wait(deadline) ? work.Result : Deny(DenyReason);
        }
        catch (Exception e)
        {
            Report(e, log);

            return Deny(DenyReason);
        }
    }

    /// <summary>
    /// The decision for one payload by the guard's file in <paramref name="handlerFolder"/>; the one record of it is appended before it is returned. Never throws.
    /// </summary>
    public static string Decide(string payload, string handlerFolder, EntryReader? read = null, TextWriter? log = null)
    {
        try
        {
            return Decision(payload, handlerFolder, read, log);
        }
        catch (Exception e)
        {
            Report(e, log);

            return Deny(DenyReason);
        }
    }

    private static string Decision(string payload, string handlerFolder, EntryReader? read, TextWriter? log)
    {
        if (ReadGuard(handlerFolder) is not { } guard)
        {
            // A guard file that is there and cannot be read is a coder folder in a state nobody wrote: nothing is allowed, and nothing can be recorded.
            return Deny(DenyReason);
        }

        var call = Parse(payload);
        var seen = SeenOf(call);
        var verdict = call is null ? Verdict.No(DenyReason) : Judge(call, guard, read, log);

        // The record comes BEFORE the decision leaves: a decision that was not recorded is not given as allow, and a deny whose record cannot be written is still a deny.
        return Append(guard.Records, seen, verdict.Allowed) || !verdict.Allowed ? verdict.Text : Deny(DenyRecordReason);
    }

    private sealed record Guard(string Worktree, string Records);

    private sealed record Verdict(bool Allowed, string Text)
    {
        public static Verdict Yes() => new(true, """{"decision":"allow"}""");

        public static Verdict No(string reason) => new(false, Deny(reason));
    }

    /// <summary>What the record keeps of a call: the conversation id, the step index (-1 when it is not one) and the tool name — never an argument.</summary>
    private sealed record Seen(string Conversation, long Step, string Tool);

    private static Guard? ReadGuard(string handlerFolder)
    {
        try
        {
            var text = File.ReadAllText(Path.Combine(handlerFolder, GuardFile));
            if (JsonNode.Parse(text, nodeOptions: null, new JsonDocumentOptions { AllowDuplicateProperties = false }) is not JsonObject root
                || Text(root["worktree"]) is not { } worktree || Text(root["records"]) is not { } records
                || !Path.IsPathFullyQualified(worktree) || !Path.IsPathFullyQualified(records))
            {
                return null;
            }

            return new Guard(worktree, records);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static Seen SeenOf(JsonObject? call) =>
        new(
            call is not null && Text(call["conversationId"]) is { } id ? id : string.Empty,
            call?["stepIdx"] is JsonValue step && step.TryGetValue<long>(out var index) ? index : -1,
            call is not null && call["toolCall"] is JsonObject tool && Text(tool["name"]) is { } name ? name : string.Empty);

    private static Verdict Judge(JsonObject call, Guard guard, EntryReader? read, TextWriter? log)
    {
        var tool = call["toolCall"] is JsonObject toolCall && Text(toolCall["name"]) is { } name ? name : string.Empty;
        if (!AllowedTools.Contains(tool, StringComparer.Ordinal))
        {
            return Verdict.No(DenyReason);
        }

        try
        {
            return Inside(call, guard, read) ? Verdict.Yes() : Verdict.No(DenyPlaceReason);
        }
        catch (Exception e)
        {
            Report(e, log);

            return Verdict.No(DenyPlaceReason);
        }
    }

    /// <summary>The place rule of V15: the target, resolved through links, is inside the worktree of the guard's file, which the payload's <c>workspacePaths</c> hold; outside the payload's <c>cwd</c>; and not under a reserved name of the worktree.</summary>
    private static bool Inside(JsonObject call, Guard guard, EntryReader? read)
    {
        if (Real(PathText(call), read) is not { } target
            || Real(Qualified(call["cwd"]), read) is not { } cwd
            || Real(guard.Worktree, read) is not { } worktree
            || Roots(call["workspacePaths"], read) is not { } roots)
        {
            return false;
        }

        var same = DocumentId.Comparison;

        return roots.Any(root => string.Equals(Path.TrimEndingDirectorySeparator(root), Path.TrimEndingDirectorySeparator(worktree), same))
            && RealPath.IsInside(worktree, target, same)
            && !RealPath.IsInside(cwd, target, same)
            && !Reserved.Any(reserved => RealPath.IsInside(Path.Combine(worktree, reserved), target, same));
    }

    private static bool Append(string records, Seen seen, bool allowed)
    {
        try
        {
            var line = new JsonObject
            {
                ["conversationId"] = seen.Conversation,
                ["stepIdx"] = seen.Step,
                ["tool"] = seen.Tool,
                ["decision"] = allowed ? "allow" : "deny",
            }.ToJsonString() + "\n";

            using var stream = new FileStream(records, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            stream.Write(Encoding.UTF8.GetBytes(line));
            stream.Flush(flushToDisk: true);

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static string Deny(string reason) => new JsonObject { ["decision"] = "deny", ["reason"] = reason }.ToJsonString();

    private static void Report(Exception e, TextWriter? log)
    {
        try
        {
            (log ?? Console.Error).WriteLine($"coai {AntigravityReadOnly.HookArgument}: denied after {e.GetType().Name}");
        }
        catch (Exception logging) when (logging is IOException or ObjectDisposedException)
        {
            // A closed standard error must not turn a denial into silence.
        }
    }

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

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>The <c>Path</c> argument: the ONE property of <c>args</c> whose name is <c>Path</c> without regard to case, spelled <c>Path</c>, a full path.</summary>
    private static string? PathText(JsonObject call) =>
        call["toolCall"]?["args"] is JsonObject args
        && args.Where(pair => string.Equals(pair.Key, "Path", StringComparison.OrdinalIgnoreCase)).ToList() is [{ Key: "Path", Value: var value }]
            ? Qualified(value)
            : null;

    private static string? Qualified(JsonNode? node) =>
        Text(node) is { } text && Path.IsPathFullyQualified(text) ? text : null;

    private static string? Real(string? text, EntryReader? read) =>
        text is not null && RealPath.Resolve(text, read: read) is { Ok: true } real ? real.Path : null;

    private static IReadOnlyList<string>? Roots(JsonNode? node, EntryReader? read)
    {
        if (node is not JsonArray entries)
        {
            return null;
        }

        var roots = entries.Select(entry => Real(Qualified(entry), read)).ToList();

        return roots.All(root => root is not null) ? roots.OfType<string>().ToList() : null;
    }
}
