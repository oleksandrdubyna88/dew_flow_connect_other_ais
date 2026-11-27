using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// The check of a codex coder turn's OWN stream, after EVERY turn and before anything of the turn is committed (epoch 3 E3 s5 t4; ids CX-S1, CX-T4 and CX-T5, judgement sections 13.3 and 13.11, risk C6). It is closed by construction: what it cannot read or does not know ends the job, so a codex that
/// changed itself is stopped by it and not passed. The owner accepted on 2026-11-26 that such a check may stop a job after codex updated itself: it is protection, not a version gate.
/// </summary>
/// <remarks>
/// <para><b>CX-S1.</b> The thread of the turn is the one the stream names in <c>thread.started</c>; on a resumed turn it must be the handle's, else the attempt is a crash (the next model starts from the last coai commit). A stream with no thread at all is a crash too.</para>
/// <para><b>CX-T4, exactly as section 13.11 states it,</b> over every item of the stream, the started form and the completed form alike: <c>agent_message</c>, <c>command_execution</c>, <c>file_change</c> and <c>error</c> items pass. An <c>mcp_tool_call</c> passes only when its server is exactly <c>codex</c>, its tool is exactly
/// <c>list_mcp_resources</c> or <c>list_mcp_resource_templates</c>, its error is null and the result of a completed item is ONE text block that parses to an empty <c>resources</c> or an empty <c>resourceTemplates</c> list. A <c>web_search</c> and a <c>collab_tool_call</c> item always end the job. An item of ANY
/// other type ends the job, and the reason names the type.</para>
/// <para><b>What CX-T5 rests on.</b> The rollout is the CLI's own file, in a folder the CLI's process can write: the check rests on CX-V2 (a command of the model cannot write the job's home), as the guard's records rest on "no command" for agy (S37).</para>
/// <para><b>CX-T5.</b> The rollout files in <c>sessions</c> of the job's <c>CODEX_HOME</c>: at least one, each of the handle's thread (the name ends with the thread id), each readable, and no call named <c>collaboration.*</c>. The stream does not show an agent call; the rollout does.</para>
/// </remarks>
internal static class CodexTurnCheck
{
    private static readonly string[] Passing = ["agent_message", "command_execution", "file_change", "error"];

    private static readonly string[] ListTools = ["list_mcp_resources", "list_mcp_resource_templates"];

    private const string AgentCallPrefix = "collaboration.";

    /// <summary>
    /// What the stream of a finished turn and the rollout of its thread say about the boundary. <paramref name="job"/> is the job the turn ran for: without it the rollout cannot be read, and that ends a turn that ended clean.
    /// </summary>
    /// <remarks>
    /// <para><b>A turn that ended badly</b> (a non-zero exit, a deadline, a cancel) is a CUT stream (pass 6, M16; the same idea as <c>AgyTurnCheck.Cut</c>). The check is asked for every end, before the launch's own stop is read, so a model that keeps a turn going to its deadline cannot hide a violation behind a crash that fails over. For a cut stream
    /// the answer is a boundary ONLY for what the turn itself proves: a bad item, a rollout file of another thread, a call named <c>collaboration.*</c>. A missing thread, a missing rollout, a last line that was cut and a stream that reached its ceiling are what a cut explains, so they are NOT a boundary: the launch's own stop (a crash, a limit, a lost sign-in) is read. For a clean end every one of them ends the job.</para>
    /// <para>The items are read FIRST, so a bad item is a boundary even in a stream that names no thread or another thread: a violation is not hidden by a crash.</para>
    /// </remarks>
    public static CoderTurnVerdict Check(ProcessResult process, string expectedSession, BoundaryJob? job)
    {
        var lines = Lines(process.StdOut);
        var thread = ThreadOf(lines.Events);
        var clean = process.ExitCode == 0 && !process.TimedOut && !process.Cancelled;

        if (Items(lines.Events).Select(pair => ProblemOf(pair.Item, pair.Completed)).FirstOrDefault(problem => problem.Length > 0) is { } wrong)
        {
            return new CoderTurnVerdict.Boundary(wrong);
        }

        return clean ? Whole(process, lines, thread, expectedSession, job) : Cut(thread, expectedSession, job);
    }

    private static CoderTurnVerdict Whole(ProcessResult process, Parsed lines, string thread, string expectedSession, BoundaryJob? job)
    {
        if (lines.Unreadable > 0 || process.Truncated)
        {
            return new CoderTurnVerdict.Boundary(lines.Unreadable > 0
                ? $"the stream of the codex turn holds {lines.Unreadable} line(s) that look like events and cannot be read, so the turn cannot be checked (CX-T4)"
                : "the stream of the codex turn reached its ceiling and was cut, so the turn cannot be checked (CX-T4)");
        }

        if (thread.Length == 0)
        {
            return new CoderTurnVerdict.Crash("the stream of the codex turn names no thread, so the turn has no session to continue (CX-S1)");
        }

        if (expectedSession.Length > 0 && thread != expectedSession)
        {
            return new CoderTurnVerdict.Crash("the codex turn answered in a thread that is not the one of the job's handle, so the session is lost (CX-S1)");
        }

        return Rollout(job, thread, whole: true) is { Length: > 0 } rollout ? new CoderTurnVerdict.Boundary(rollout) : new CoderTurnVerdict.Fine();
    }

    /// <summary>A turn that ended badly: the rollout is read for what it proves alone; everything else is the launch's own stop.</summary>
    private static CoderTurnVerdict Cut(string thread, string expectedSession, BoundaryJob? job) =>
        Rollout(job, thread.Length > 0 ? thread : expectedSession, whole: false) is { Length: > 0 } proved ? new CoderTurnVerdict.Boundary(proved) : new CoderTurnVerdict.Fine();

    private sealed record Parsed(IReadOnlyList<JsonElement> Events, int Unreadable);

    /// <summary>The JSON events of the stream, one per line. A line that is not an object and does not start with <c>{</c> is ignored (a log line); one that starts with <c>{</c> and cannot be read is counted: it could hold an item.</summary>
    private static Parsed Lines(string stdout)
    {
        var events = new List<JsonElement>();
        var unreadable = 0;
        foreach (var line in stdout.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Where(line => line.StartsWith('{')))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    events.Add(document.RootElement.Clone());
                }
            }
            catch (JsonException)
            {
                unreadable++;
            }
        }

        return new Parsed(events, unreadable);
    }

    private static string ThreadOf(IReadOnlyList<JsonElement> events) =>
        events.Where(item => Text(item, "type") == "thread.started").Select(item => Text(item, "thread_id")).FirstOrDefault(id => id.Length > 0) ?? string.Empty;

    /// <summary>
    /// The item of every <c>item.*</c> event, started, updated and completed alike, and whether the event is the completed form. An <c>item.*</c> event whose item cannot be read is an item with no type: it cannot be told from a tool outside the list, so it ends the job like a type nobody measured (S43).
    /// </summary>
    private static IEnumerable<(JsonElement Item, bool Completed)> Items(IReadOnlyList<JsonElement> events) =>
        events.Where(item => Text(item, "type").StartsWith("item.", StringComparison.Ordinal))
            .Select(item => (item.TryGetProperty("item", out var found) && found.ValueKind == JsonValueKind.Object ? found : default, Text(item, "type") == "item.completed"));

    /// <summary>The sentence that says why this item ends the job, or empty when it passes (the rule of section 13.11).</summary>
    private static string ProblemOf(JsonElement item, bool completed)
    {
        var type = Text(item, "type");
        if (Passing.Contains(type))
        {
            return string.Empty;
        }

        return type switch
        {
            "mcp_tool_call" => McpProblem(item, completed),
            "web_search" => "the codex turn used the web search, which a coder never has (CX-T4: an item of type web_search)",
            "collab_tool_call" => "the codex turn made an agent call, which a coder never has (CX-T4: an item of type collab_tool_call)",
            _ => $"the stream of the codex turn holds an item of the type '{Safe(type)}', which nobody measured, so the turn cannot be told from one with a tool outside the list (CX-T4)",
        };
    }

    private static string McpProblem(JsonElement item, bool completed)
    {
        const string Start = "the codex turn called an MCP tool that is not one of the two list tools of the server codex with an empty answer (CX-T4: ";
        if (Text(item, "server") != "codex")
        {
            return Start + "another server)";
        }

        if (!ListTools.Contains(Text(item, "tool")))
        {
            return Start + "another tool)";
        }

        if (item.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
        {
            return Start + "an error)";
        }

        // A started item has no answer yet; a completed one must have exactly one text block that parses to an empty list (S43: a completed call with no result is not that).
        var answered = item.TryGetProperty("result", out var result) && result.ValueKind != JsonValueKind.Null;

        return (answered ? !EmptyList(result) : completed) ? Start + "a list that is not empty, or an answer that is not one text block)" : string.Empty;
    }

    /// <summary>True when the result is ONE text block that parses to exactly <c>{"resources":[]}</c> or <c>{"resourceTemplates":[]}</c>.</summary>
    private static bool EmptyList(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array || content.GetArrayLength() != 1)
        {
            return false;
        }

        var block = content[0];
        if (block.ValueKind != JsonValueKind.Object || Text(block, "type") != "text")
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(Text(block, "text"));
            var properties = document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.EnumerateObject().ToList() : [];

            return properties is [{ Name: "resources" or "resourceTemplates", Value: { ValueKind: JsonValueKind.Array } list }] && list.GetArrayLength() == 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>The type of an item, as a word that cannot carry a line or a long text into a sentence.</summary>
    private static string Safe(string type) => type.Length <= 40 && type.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.') ? type : "(unreadable)";

    // ---------- CX-T5 ----------

    private static readonly string[] AgentCallNames = ["spawn_agent", "send_message", "followup_task", "interrupt_agent", "list_agents", "wait_agent"];

    /// <summary>
    /// The sentence that says the rollout of the turn's thread proves a boundary fault, or empty. <paramref name="whole"/> is a turn that ended clean: then a missing job, a sessions entry that is not a real folder, a file of another name, a missing or unreadable file and a missing file of the thread are faults too. For a cut turn
    /// only what the files PROVE alone is: a file of another thread (when the thread is known) and an agent call.
    /// </summary>
    private static string Rollout(BoundaryJob? job, string thread, bool whole)
    {
        if (job is null)
        {
            return whole ? "the rollout of the codex turn cannot be read: no job was given to find its CODEX_HOME (CX-T5)" : string.Empty;
        }

        var listed = Listed(Path.Combine(CodexCoderVendor.CodexHome(job), "sessions"));
        if (listed.Problem.Length > 0)
        {
            // An entry that is not what the CLI writes (a link, a node of another kind) proves a command of the model ran, on a cut turn too (pass 8, M19).
            return whole || listed.Odd ? listed.Problem : string.Empty;
        }

        var rollouts = listed.Files.Where(file => Path.GetFileName(file).StartsWith("rollout-", StringComparison.Ordinal) && file.EndsWith(".jsonl", StringComparison.Ordinal)).ToList();
        if (whole && listed.Files.Count != rollouts.Count)
        {
            return "a file that is not a rollout is in the sessions of the job's CODEX_HOME, so the turn cannot be checked (CX-T5)";
        }

        if (thread.Length > 0 && rollouts.Any(file => !file.EndsWith($"-{thread}.jsonl", StringComparison.Ordinal)))
        {
            return "a rollout file of another thread is in the sessions of the job's CODEX_HOME: a child of the session ran (CX-T5)";
        }

        if (whole && !rollouts.Any(file => file.EndsWith($"-{thread}.jsonl", StringComparison.Ordinal)))
        {
            return "no rollout file of the turn's thread is in the sessions of the job's CODEX_HOME, so the turn cannot be checked (CX-T5)";
        }

        var read = rollouts.Select(AgentCallIn).ToList();

        return read.FirstOrDefault(result => result == Agent) is { } agent
            ? "the rollout of the codex turn holds a call named collaboration.*, an agent call (CX-T5)"
            : read.Any(result => result == NotRegular) ? "a rollout file of the codex turn is not a regular file within its limit (a FIFO, a link or a file that is too big), so a command ran in the unit (CX-T5)"
            : whole && read.Any(result => result == Unreadable) ? "a rollout file of the codex turn cannot be read, so the turn cannot be checked (CX-T5)" : string.Empty;
    }

    private const string Agent = "agent";

    private const string Unreadable = "unreadable";

    private const string NotRegular = "not-regular";

    private const string Clean = "clean";

    /// <summary>The files below a sessions folder, or the sentence that says they cannot be listed. The entry is looked at WITHOUT following it: a link, or any entry that is not a folder, is not a real sessions folder; a folder that is not there is no file.</summary>
    private static (IReadOnlyList<string> Files, string Problem, bool Odd) Listed(string sessions)
    {
        switch (Runners.Files.RealPath.Lstat(sessions).Kind)
        {
            case Runners.Files.EntryKind.Missing:
                return ([], string.Empty, false);
            case Runners.Files.EntryKind.Directory:
                // One walk for every folder a unit can write: no link followed, nothing opened, a limit on entries and depth, a time bound (pass 8, S55).
                var walked = Runners.Files.UnitWrittenFile.Walk(sessions);

                return walked.Problem.Length == 0 ? (walked.Files, string.Empty, false) : ([], $"the rollout files of the codex turn cannot be listed: {walked.Problem} (CX-T5)", walked.Odd);

            default:
                return ([], "the sessions entry of the job's CODEX_HOME is not a real folder, so the turn cannot be checked (CX-T5)", true);
        }
    }

    /// <summary>Whether a rollout file holds a call of an agent tool: <see cref="Agent"/>, or <see cref="Unreadable"/> when a line or the file cannot be read (a line or the file cannot be read; every other line is still searched), else <see cref="Clean"/>. A call is a payload whose <c>name</c> starts with <c>collaboration.</c> or is one of the six bare names, or whose <c>namespace</c> is <c>collaboration</c>.</summary>
    private static string AgentCallIn(string file)
    {
        var unreadable = false;
        var (stream, failure) = Runners.Files.RegularFiles.OpenNoFollow(file);
        if (stream is null)
        {
            // A FIFO, a link or a node of another kind never waits and is never followed: it is a file a command made (pass 8, M19, S55).
            return failure is Runners.Files.FileReadResult.Unreadable { Reason: var reason } && (reason.Contains(Runners.Files.RegularFiles.NotRegular, StringComparison.Ordinal) || reason.Contains("symbolic link", StringComparison.Ordinal)) ? NotRegular : Unreadable;
        }

        try
        {
            using (stream)
            {
                if (stream.Length > Runners.Files.UnitWrittenFile.MostRolloutBytes)
                {
                    return NotRegular;
                }

                using var reader = new StreamReader(stream);
                string? line;
                while ((line = Runners.Files.UnitWrittenFile.ReadLine(reader, Runners.Files.UnitWrittenFile.MostLineChars)) is not null)
                {
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    try
                    {
                        using var document = JsonDocument.Parse(line);
                        if (document.RootElement.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object && IsAgentCall(payload))
                        {
                            return Agent;
                        }
                    }
                    catch (JsonException)
                    {
                        // One line that cannot be read does not end the search of the file: a call after it is still found.
                        unreadable = true;
                    }
                }
            }
        }
        catch (InvalidDataException)
        {
            // A line past the limit: no CLI wrote this file, and it is not read to its end (pass 9, S68).
            return NotRegular;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Unreadable;
        }

        return unreadable ? Unreadable : Clean;
    }

    private static bool IsAgentCall(JsonElement payload)
    {
        var name = Text(payload, "name");

        return name.StartsWith(AgentCallPrefix, StringComparison.Ordinal) || Text(payload, "namespace") == "collaboration" || (AgentCallNames.Contains(name) && Text(payload, "type") is "function_call" or "custom_tool_call");
    }

    /// <summary>
    /// Whether any rollout file below <paramref name="sessions"/> holds an agent call, read leniently (a line that cannot be read is skipped, a missing folder has none): what a FRESH launch asks before it removes the folder, so the one record of a child is never deleted unread (pass 6, M16). It looks at the entry
    /// without following a link.
    /// </summary>
    internal static bool HoldsAnAgentCall(string sessions) => InspectSessions(sessions).Holds;

    /// <summary>
    /// What a FRESH launch must know before it removes the <c>sessions</c> folder: whether a rollout holds an agent call, and the sentence that says an entry in it is not what the CLI writes (a link, a FIFO, a file past its limit, a walk too wide) or that the look did not finish — empty when all is as it should be.
    /// The walk and every read go through the one road for folders a unit can write, and the whole look is bounded in time (pass 8, M19, S55).
    /// </summary>
    internal static (bool Holds, string Problem) InspectSessions(string sessions) =>
        Runners.Files.UnitWrittenFile.Bounded(() =>
        {
            var listed = Listed(sessions);
            if (listed.Problem.Length > 0)
            {
                return (false, listed.Odd ? listed.Problem : string.Empty);
            }

            var read = listed.Files.Where(file => Path.GetFileName(file).StartsWith("rollout-", StringComparison.Ordinal)).Select(AgentCallIn).ToList();

            return (read.Contains(Agent), read.Contains(NotRegular) ? "a rollout file in the sessions of the job's CODEX_HOME is not a regular file within its limit (a FIFO, a link or a file that is too big)" : string.Empty);
        }, (false, "the look at the sessions of the job's CODEX_HOME did not finish in time"));

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
