using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// The check of an agy coder turn's OWN stream, asked after EVERY turn, before anything of that turn is committed (epoch 3 E3 s6 t5; judgement 14.4 G15, ids AG-G5, AG-A2, AG-M2, AG-S1; SPEC C6, C8, V13, V18, V25). Pure: the process the turn left and the records the guard wrote for it.
/// </summary>
/// <remarks>
/// <para><b>G15 — the stream beside the guard's records</b> (a must-change the owner named on 2026-11-26). Every tool event of the stream has exactly ONE record with the same conversation id, step index and tool name; an event that ended <c>DONE</c> has the decision <c>allow</c>, and an event whose record says
/// <c>deny</c> ended <c>ERROR</c>; every record has its event, so the two counts are equal; every tool name is <c>read_file</c>, <c>write_file</c> or <c>edit_file</c>. An event with no record (the hook was not called, or the handler died — the open door of risk A4), a record with no event, two records for one step and
/// another tool name each end the job <c>coder-boundary</c>. It is the ONE check that notices, with no version gate, the day agy ships a release that does not call the hook for some tool; it notices after the tool ran, and the unit bounds what the tool could reach. The step index is agy's own counter and not an id
/// (risk A13): a release that numbers steps otherwise makes this check refuse jobs, which is closed, as intended.</para>
/// <para><b>The signs of a mode that did not hold</b> (AG-M2, V18): a stderr line that starts <c>warning: --mode</c> or <c>warning: unrecognized --mode</c>, a result with a <c>denied_actions</c> that is not empty, and the stderr line that a tool required the <c>command</c> permission — with the coder agent and the guard nothing can reach the
/// permission layer, so either sign means the agent file or the guard did not hold and only the mode stopped a command. The flag on the argv alone proves no mode (A9): the init line says <c>request-review</c> for every mode, so nothing compares it.</para>
/// <para><b>The session</b> (AG-S1, V13): a resumed turn whose conversation id is not the handle's — also with exit code 0 — or whose stderr has the not-found line ends the attempt as a crash: a conversation that agy starts in silence is never continued as the job's.</para>
/// <para>A sentence names a step and a tool, never an argument, a path or a line of stderr: stderr of a lost sign-in holds a login address.</para>
/// </remarks>
internal static partial class AgyTurnCheck
{
    /// <summary>The tools of the coder, by the names the stream and the hook share.</summary>
    private static readonly string[] Tools = ["read_file", "write_file", "edit_file"];

    [GeneratedRegex("""^warning: (?:unrecognized )?--mode\b""", RegexOptions.CultureInvariant | RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ModeWarning();

    [GeneratedRegex("""conversation "[^"\r\n]*" not found""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NotFound();

    [GeneratedRegex("""a tool required the "command" permission""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CommandPermission();

    /// <summary>One tool event of the stream: its conversation, step, tool name and the last state seen.</summary>
    private sealed record ToolEvent(string Conversation, long Step, string Tool, string State);

    /// <summary>One line of the guard's records.</summary>
    private sealed record Record(string Conversation, long Step, string Tool, string Decision);

    /// <summary>The verdict for one finished turn. <paramref name="records"/> is the text of the guard's records for THIS turn, or null when the file could not be read.</summary>
    public static CoderTurnVerdict Check(ProcessResult process, string expectedSession, string? records) => Check(process, expectedSession, () => records);

    /// <summary>
    /// The same, with the records given as a READ that is made only after what the stream alone proves has been judged (pass 7, M18): a mode sign, or an event of a tool outside the three, ends the job as a boundary without a file of the job's own folder being touched.
    /// </summary>
    public static CoderTurnVerdict Check(ProcessResult process, string expectedSession, Func<string?> readRecords)
    {
        var lines = Lines(process.StdOut);
        if (ModeSign(process, lines) is { Length: > 0 } sign)
        {
            return new CoderTurnVerdict.Boundary(sign);
        }

        var cutEnd = process.ExitCode != 0 || process.TimedOut || process.Cancelled;
        if (ForeignTool(Events(lines), cutEnd) is { Length: > 0 } foreignTool)
        {
            return new CoderTurnVerdict.Boundary(foreignTool);
        }

        var records = readRecords();

        // A launch that did not end cleanly (an exit that is not 0, a deadline, a cancel) has a CUT stream: nothing says the counts are equal or that every event ended. What ONE side alone proves still ends the job (pass 5, M11) — a boundary wins over the crash that follows.
        if (process.ExitCode != 0 || process.TimedOut || process.Cancelled)
        {
            return Cut(Events(lines), records) is { Length: > 0 } alone ? new CoderTurnVerdict.Boundary(alone) : new CoderTurnVerdict.Fine();
        }

        if (Session(process, lines, expectedSession) is { Length: > 0 } lost)
        {
            return new CoderTurnVerdict.Crash(lost);
        }

        return Tie(Events(lines), records) is { Length: > 0 } broken ? new CoderTurnVerdict.Boundary(broken) : new CoderTurnVerdict.Fine();
    }

    // ---------- the signs of AG-M2 ----------

    private static string ModeSign(ProcessResult process, IReadOnlyList<JsonElement> lines)
    {
        if (ModeWarning().IsMatch(process.StdErr.ReplaceLineEndings("\n")))
        {
            return "the agy turn printed a warning about its --mode, so the mode the turn ran in is not the one asked for (the signs of AG-M2); nothing of this turn was committed";
        }

        if (CommandPermission().IsMatch(process.StdErr))
        {
            return "a tool of the agy turn required the command permission, which means the agent file or the guard did not hold and only the mode stopped a command (AG-M2); nothing of this turn was committed";
        }

        return lines.Any(line => Result(line) is { } result && result.TryGetProperty("denied_actions", out var denied) && denied.ValueKind == JsonValueKind.Array && denied.GetArrayLength() > 0)
            ? "the result of the agy turn names denied actions, which means the agent file or the guard did not hold and only the mode stopped a command (AG-M2); nothing of this turn was committed"
            : string.Empty;
    }

    // ---------- a cut stream ----------

    /// <summary>The sentence for an event of a tool outside the three, in any state — what the stream alone proves, on a clean end and on a cut one; empty when there is none.</summary>
    private static string ForeignTool(IReadOnlyList<ToolEvent> events, bool cut) =>
        events.FirstOrDefault(item => !Tools.Contains(item.Tool, StringComparer.Ordinal)) is { } foreign
            ? $"the {(cut ? "cut " : string.Empty)}agy turn has a tool event (step {foreign.Step.ToString(CultureInfo.InvariantCulture)}) for a tool outside read_file, write_file and edit_file, so the coder's boundary did not hold (G15, AG-A2); nothing of this turn was committed"
            : string.Empty;

    /// <summary>What a cut stream shows by one side alone: an event of a tool outside the three in any state, a <c>DONE</c> event with no record or with a record that is not <c>allow</c>, and records that cannot be read beside a <c>DONE</c> event. An event that did not end, and a record with no event, are what a cut explains.</summary>
    private static string Cut(List<ToolEvent> events, string? records)
    {
        // A tool outside the three is a break in EVERY state (re-check leg b): a cut explains a missing END of an event, never that such a tool was started at all.
        if (ForeignTool(events, cut: true) is { Length: > 0 } foreignTool)
        {
            return foreignTool;
        }

        var done = events.Where(item => item.State == "DONE").ToList();
        if (done.Count == 0)
        {
            return string.Empty;
        }

        var written = records is null ? null : Parse(records);
        if (written is null)
        {
            return "the guard's records of the cut agy turn could not be read beside a DONE tool event, so nothing shows the hook decided it (G15); nothing of this turn was committed";
        }

        foreach (var item in done)
        {
            var same = written.Where(record => record.Step == item.Step && record.Conversation == item.Conversation && record.Tool == item.Tool).ToList();
            if (same.Count == 0)
            {
                return $"the cut agy turn has a DONE tool event (step {item.Step.ToString(CultureInfo.InvariantCulture)}, {item.Tool}) with no record of the guard: the hook was not called for it, or the handler did not answer (G15); nothing of this turn was committed";
            }

            if (same.Any(record => record.Decision != "allow"))
            {
                return $"the agy tool event of step {item.Step.ToString(CultureInfo.InvariantCulture)} ended DONE although the guard's decision was not allow (G15); nothing of this turn was committed";
            }
        }

        return string.Empty;
    }

    // ---------- the session ----------

    private static string Session(ProcessResult process, IReadOnlyList<JsonElement> lines, string expectedSession)
    {
        if (expectedSession.Length == 0)
        {
            return string.Empty;
        }

        if (NotFound().IsMatch(process.StdErr))
        {
            return "agy said the conversation of the job was not found, so the turn is not a continuation of it; a fix turn never goes on in a new conversation";
        }

        var said = ConversationOf(lines);

        return said.Length == 0
            ? "the stream of the resumed agy turn names no conversation, so it cannot be told to be the job's"
            : string.Equals(said, expectedSession, StringComparison.Ordinal) ? string.Empty : "the conversation of the resumed agy turn is not the handle's: a conversation that agy starts in silence is never continued as the job's";
    }

    /// <summary>The conversation id of the stream: the one the last event that names one gives.</summary>
    internal static string ConversationOf(IReadOnlyList<JsonElement> lines) =>
        lines.Reverse().Select(line =>
            line.TryGetProperty("conversation_id", out var top) && top.ValueKind == JsonValueKind.String ? top.GetString()!
            : Inner(line) is { } inner && inner.TryGetProperty("conversation_id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString()! : string.Empty)
            .FirstOrDefault(id => id.Length > 0) ?? string.Empty;

    // ---------- G15 ----------

    private static string Tie(IReadOnlyList<ToolEvent> events, string? records)
    {
        if (events.Count == 0 && string.IsNullOrWhiteSpace(records))
        {
            return string.Empty;
        }

        if (records is null)
        {
            return "the guard's records of the agy turn could not be read, so no tool event of it can be tied to a decision of the guard (G15)";
        }

        var written = Parse(records);
        if (written is null)
        {
            return "a line of the guard's records of the agy turn is not a record, so the turn cannot be tied to the guard (G15)";
        }

        var foreign = events.FirstOrDefault(item => !Tools.Contains(item.Tool, StringComparer.Ordinal));
        if (foreign is not null)
        {
            return $"the agy turn has a tool event (step {foreign.Step.ToString(CultureInfo.InvariantCulture)}) for a tool outside read_file, write_file and edit_file, so the coder's boundary did not hold (G15, AG-A2); nothing of this turn was committed";
        }

        return Pairs(events, written);
    }

    private static string Pairs(IReadOnlyList<ToolEvent> events, IReadOnlyList<Record> written)
    {
        foreach (var item in events)
        {
            var same = written.Where(record => record.Step == item.Step && record.Conversation == item.Conversation && record.Tool == item.Tool).ToList();
            if (same.Count == 0)
            {
                return $"the agy turn has a tool event (step {item.Step.ToString(CultureInfo.InvariantCulture)}, {item.Tool}) with no record of the guard: the hook was not called for it, or the handler did not answer (G15); nothing of this turn was committed";
            }

            if (same.Count > 1 || written.Count(record => record.Step == item.Step && record.Conversation == item.Conversation) > 1)
            {
                return $"the guard holds more than one record for step {item.Step.ToString(CultureInfo.InvariantCulture)} of the agy turn (G15); nothing of this turn was committed";
            }

            var decision = same[0].Decision;
            if (item.State == "DONE" && decision != "allow")
            {
                return $"the agy tool event of step {item.Step.ToString(CultureInfo.InvariantCulture)} ended DONE although the guard's decision was not allow (G15); nothing of this turn was committed";
            }

            if (decision == "deny" && item.State != "ERROR")
            {
                return $"the guard denied step {item.Step.ToString(CultureInfo.InvariantCulture)} of the agy turn and the tool event did not end ERROR (G15); nothing of this turn was committed";
            }

            if (item.State is not ("DONE" or "ERROR"))
            {
                return $"the agy tool event of step {item.Step.ToString(CultureInfo.InvariantCulture)} did not end (G15); nothing of this turn was committed";
            }
        }

        var unmatched = written.FirstOrDefault(record => !events.Any(item => item.Step == record.Step && item.Conversation == record.Conversation && item.Tool == record.Tool));

        return unmatched is not null
            ? $"the guard holds a record (step {unmatched.Step.ToString(CultureInfo.InvariantCulture)}, {unmatched.Tool}) that no tool event of the agy turn has (G15); the two counts must be equal; nothing of this turn was committed"
            : events.Count == written.Count ? string.Empty : "the tool events of the agy turn and the guard's records are not equal in number (G15); nothing of this turn was committed";
    }

    /// <summary>The records, or null when a line is not one.</summary>
    private static List<Record>? Parse(string text)
    {
        List<Record> records = [];
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("conversationId", out var conversation) || conversation.ValueKind != JsonValueKind.String
                    || !root.TryGetProperty("stepIdx", out var step) || !step.TryGetInt64(out var index)
                    || !root.TryGetProperty("tool", out var tool) || tool.ValueKind != JsonValueKind.String
                    || !root.TryGetProperty("decision", out var decision) || decision.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                records.Add(new Record(conversation.GetString()!, index, tool.GetString()!, decision.GetString()!));
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return records;
    }

    // ---------- the stream ----------

    /// <summary>The tool events of the stream, one for each (conversation, step), with the last state seen for it.</summary>
    private static List<ToolEvent> Events(IReadOnlyList<JsonElement> lines)
    {
        Dictionary<(string, long), ToolEvent> events = [];
        foreach (var line in lines)
        {
            if (!line.TryGetProperty("event", out var kind) || kind.GetString() != "step_update"
                || !line.TryGetProperty("step_update", out var update) || update.ValueKind != JsonValueKind.Object
                || !update.TryGetProperty("step_type", out var type) || type.GetString() != "tool"
                || !update.TryGetProperty("step_index", out var step) || !step.TryGetInt64(out var index))
            {
                continue;
            }

            var conversation = update.TryGetProperty("conversation_id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString()! : string.Empty;
            var tool = update.TryGetProperty("tool_name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString()! : string.Empty;
            var state = update.TryGetProperty("state", out var said) && said.ValueKind == JsonValueKind.String ? said.GetString()! : string.Empty;
            events[(conversation, index)] = new ToolEvent(conversation, index, tool, state);
        }

        return [.. events.Values];
    }

    private static List<JsonElement> Lines(string stdout)
    {
        List<JsonElement> lines = [];
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(text => text.StartsWith('{')))
        {
            try
            {
                lines.Add(JsonDocument.Parse(line).RootElement.Clone());
            }
            catch (JsonException)
            {
                // A half-written last line, or prose beside the stream: not an event.
            }
        }

        return lines;
    }

    private static JsonElement? Result(JsonElement line) =>
        line.TryGetProperty("event", out var kind) && kind.GetString() == "result" && line.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object ? result : null;

    private static JsonElement? Inner(JsonElement line) =>
        Result(line) ?? (line.TryGetProperty("step_update", out var update) && update.ValueKind == JsonValueKind.Object ? update : null);
}
