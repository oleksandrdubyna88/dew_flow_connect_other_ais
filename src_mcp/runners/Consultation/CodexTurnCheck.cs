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
/// <para><b>CX-T5.</b> The rollout files in <c>sessions</c> of the job's <c>CODEX_HOME</c>: at least one, each of the handle's thread (the name ends with the thread id), each readable, and no call named <c>collaboration.*</c>. The stream does not show an agent call; the rollout does.</para>
/// </remarks>
internal static class CodexTurnCheck
{
    private static readonly string[] Passing = ["agent_message", "command_execution", "file_change", "error"];

    private static readonly string[] ListTools = ["list_mcp_resources", "list_mcp_resource_templates"];

    private const string AgentCallPrefix = "collaboration.";

    /// <summary>What the stream of a finished turn and the rollout of its thread say about the boundary. <paramref name="job"/> is the job the turn ran for: without it the rollout cannot be read, and that ends the job.</summary>
    public static CoderTurnVerdict Check(ProcessResult process, string expectedSession, BoundaryJob? job)
    {
        var lines = Lines(process.StdOut);
        if (lines.Unreadable > 0)
        {
            return new CoderTurnVerdict.Boundary($"the stream of the codex turn holds {lines.Unreadable} line(s) that look like events and cannot be read, so the turn cannot be checked (CX-T4)");
        }

        if (ThreadOf(lines.Events) is not { Length: > 0 } thread)
        {
            return new CoderTurnVerdict.Crash("the stream of the codex turn names no thread, so the turn has no session to continue (CX-S1)");
        }

        if (expectedSession.Length > 0 && thread != expectedSession)
        {
            return new CoderTurnVerdict.Crash("the codex turn answered in a thread that is not the one of the job's handle, so the session is lost (CX-S1)");
        }

        if (Items(lines.Events).Select(ProblemOf).FirstOrDefault(problem => problem.Length > 0) is { } wrong)
        {
            return new CoderTurnVerdict.Boundary(wrong);
        }

        return Rollout(job, thread) is { Length: > 0 } rollout ? new CoderTurnVerdict.Boundary(rollout) : new CoderTurnVerdict.Fine();
    }

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

    /// <summary>The item of every <c>item.*</c> event, started, updated and completed alike.</summary>
    private static IEnumerable<JsonElement> Items(IReadOnlyList<JsonElement> events) =>
        events.Where(item => Text(item, "type").StartsWith("item.", StringComparison.Ordinal) && item.TryGetProperty("item", out var found) && found.ValueKind == JsonValueKind.Object)
            .Select(item => item.GetProperty("item"));

    /// <summary>The sentence that says why this item ends the job, or empty when it passes (the rule of section 13.11).</summary>
    private static string ProblemOf(JsonElement item)
    {
        var type = Text(item, "type");
        if (Passing.Contains(type))
        {
            return string.Empty;
        }

        return type switch
        {
            "mcp_tool_call" => McpProblem(item),
            "web_search" => "the codex turn used the web search, which a coder never has (CX-T4: an item of type web_search)",
            "collab_tool_call" => "the codex turn made an agent call, which a coder never has (CX-T4: an item of type collab_tool_call)",
            _ => $"the stream of the codex turn holds an item of the type '{Safe(type)}', which nobody measured, so the turn cannot be told from one with a tool outside the list (CX-T4)",
        };
    }

    private static string McpProblem(JsonElement item)
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

        return item.TryGetProperty("result", out var result) && result.ValueKind != JsonValueKind.Null && !EmptyList(result) ? Start + "a list that is not empty, or an answer that is not one text block)" : string.Empty;
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

    /// <summary>The sentence that says the rollout of the turn's thread is missing, unreadable, not the handle's alone or holds an agent call, or empty.</summary>
    private static string Rollout(BoundaryJob? job, string thread)
    {
        if (job is null)
        {
            return "the rollout of the codex turn cannot be read: no job was given to find its CODEX_HOME (CX-T5)";
        }

        var sessions = Path.Combine(CodexCoderVendor.CodexHome(job), "sessions");
        string[] files;
        try
        {
            files = Directory.Exists(sessions) ? [.. Directory.EnumerateFiles(sessions, "rollout-*.jsonl", SearchOption.AllDirectories)] : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "the rollout files of the codex turn cannot be listed (CX-T5)";
        }

        if (!files.Any(file => file.EndsWith($"-{thread}.jsonl", StringComparison.Ordinal)))
        {
            return "no rollout file of the turn's thread is in the sessions of the job's CODEX_HOME, so the turn cannot be checked (CX-T5)";
        }

        if (files.Any(file => !file.EndsWith($"-{thread}.jsonl", StringComparison.Ordinal)))
        {
            return "a rollout file of another thread is in the sessions of the job's CODEX_HOME: a child of the session ran (CX-T5)";
        }

        return files.Select(AgentCallIn).FirstOrDefault(problem => problem.Length > 0) ?? string.Empty;
    }

    private static string AgentCallIn(string file)
    {
        try
        {
            foreach (var line in File.ReadLines(file).Where(line => line.Length > 0))
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object && Text(payload, "name").StartsWith(AgentCallPrefix, StringComparison.Ordinal))
                {
                    return "the rollout of the codex turn holds a call named collaboration.*, an agent call (CX-T5)";
                }
            }

            return string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return "a rollout file of the codex turn cannot be read, so the turn cannot be checked (CX-T5)";
        }
    }

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
