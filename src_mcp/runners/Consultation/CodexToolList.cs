using System.Text.Json;
using System.Text.RegularExpressions;
using CoaiMcp.Core.Notices;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// The tool list of a codex session as <c>codex debug prompt-input</c> prints it (epoch 3 E3 s5 t3, id CX-T1; judgement 13.3; changed by s2 t5, <c>SPEC_CHANGES.md</c> section Q3): the names of the top-level tools of <c>additional_tools</c> (namespace and name) and the nested tools of the JavaScript tool <c>exec</c>, which its description names as
/// <c>### `name`</c>. The list says NOTHING about the web search (CX-T6): it is the same with and without the flag.
/// </summary>
/// <remarks>
/// <para><b>The rule (Q3; S4 items 8 to 10).</b> The row holds when the inventory was read WHOLE (<see cref="Read"/>) and NO name of it is outside the known set of 11 names measured with the argv of the judgement's section 13.4. Fewer names pass, and the envelope names the known tools the model does not have.
/// A name outside the set — an agent tool (<c>collaboration.*</c>), an MCP tool (<c>mcp__*</c>), a web or image tool, a new name — refuses the launch of THIS model: the row is red, and at the first launch of an entry, with every other row holding, the entry is <c>model-unavailable</c> and the job goes to the next entry (the owner,
/// 2026-11-27, ledger entry 2.2: <see cref="UnknownToolFailsOver"/>); the stop word of a red tool row is decided in ONE place, <see cref="CoaiMcp.Server.RoleJobs.RoleStops.OfPreflightRefusal"/>, which takes the number of the launch. The two reads, in the worktree and in the stand-in project with its own MCP config, must hold the same names, or the job ends
/// <c>coder-boundary</c>. A read that is not whole, a report that cannot be read and a list with no name pass nothing: in both reads it is <c>model-unavailable</c>, in one read only it is the boundary.</para>
/// <para><b>The one exception to "a row never carries the output of a program it ran" (s5 t3 d4):</b> the names outside the known set are shown, but only a name of 1 to 64 characters of letters, digits, <c>_</c>, <c>.</c> and <c>-</c> that the redactor leaves as it is; any other name is said as a name that cannot be shown, with its length;
/// at most 5 names are shown, then the count (<see cref="Shown"/>).</para>
/// </remarks>
internal static partial class CodexToolList
{
    /// <summary>The 11 names measured on codex-cli 0.171.0 with the argv of the judgement's section 13.4: the known set. A model may have fewer of them and none outside.</summary>
    internal static IReadOnlyList<string> Known { get; } =
    [
        "functions.exec", "functions.wait", "functions.request_user_input", "functions.request_user_input_async",
        "apply_patch", "exec_command", "write_stdin", "list_mcp_resources", "list_mcp_resource_templates", "read_mcp_resource", "clock__curr_time",
    ];

    /// <summary>
    /// What a tool outside the known set, the same in both reads with every other row holding, does at the FIRST launch of an entry (Q3 item 1; ledger entry 2.2, the owner, 2026-11-27): true — the entry ends <c>model-unavailable</c> and the job goes to the next entry. False would make that case a
    /// boundary stop that ends the job (entry 2.1). The notice <c>coder-tool-unknown</c> is raised either way; after the first launch of an entry every red row ends the job whatever this says.
    /// </summary>
    internal const bool UnknownToolFailsOver = true;

    /// <summary>The most names of a list that are shown (Q3 item 5), then the count.</summary>
    internal const int MostShown = 5;

    /// <summary>The longest name that is shown, in characters.</summary>
    internal const int MostNameLength = 64;

    /// <summary>What one read of the tool report came to: the names it holds, and whether the inventory was read WHOLE.</summary>
    /// <param name="Names">The names found: the top-level tools (namespace and name) and the nested tools of <c>functions.exec</c>.</param>
    /// <param name="Whole">True when the report is the expected JSON, every tool entry has a name, a tool that holds tools was walked, and — when <c>functions.exec</c> is in the list — every line of its description that starts with <c>###</c> matches <see cref="NestedLine"/> and at least one does.</param>
    internal sealed record ToolRead(IReadOnlyList<string> Names, bool Whole)
    {
        /// <summary>A read that is a list: whole, and holding at least one name. An empty list holds no outside name, so it would pass the rule — and it is not a list.</summary>
        public bool IsAList => Whole && Names.Count > 0;

        /// <summary>A report that could not be read at all.</summary>
        public static ToolRead Unreadable { get; } = new([], false);
    }

    /// <summary>
    /// The inventory of <paramref name="json"/>, as <c>codex debug prompt-input</c> prints it, by the CLOSED rule of addendum 4 (A4-M1; S4 item 8). A read is whole only when ALL of this is true, and any other read is a list that cannot be read:
    /// (1) the report is a JSON array with exactly ONE item of type <c>additional_tools</c> whose <c>tools</c> is an array, and no other item has a member named <c>tools</c>; (2) every element of a <c>tools</c> array, at every depth, is an object with a name of at least one character, and an element that has a member <c>tools</c> has it as an array
    /// and is walked by the same rule — nothing is skipped; (3) <c>functions.exec</c> is there at most once, and when it is there its <c>description</c> is a string; (4) the grammar <c>^### `[A-Za-z0-9_.-]+`$</c> is asked line by line on WHOLE lines (a carriage return or a space after the name is a character; a line that starts with <c>###</c> after spaces or tabs is a <c>###</c> line and does not match;
    /// a heading of another level whose text starts with a backtick makes the read not whole); (5) at least one line matches. The nested tools of <c>functions.exec</c> are in free text written for the model, so a shorter list read from text that changed its form must not pass as fewer tools.
    /// </summary>
    internal static ToolRead Read(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return ToolRead.Unreadable;
            }

            JsonElement? tools = null;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var isTools = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "additional_tools";
                if (isTools && tools is not null)
                {
                    return ToolRead.Unreadable;
                }

                if (isTools)
                {
                    tools = item;
                }
                else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("tools", out _))
                {
                    return ToolRead.Unreadable;
                }
            }

            if (tools is not { } found || !found.TryGetProperty("tools", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return ToolRead.Unreadable;
            }

            var names = new List<string>();
            var execs = 0;
            var whole = Walk(list, string.Empty, names, ref execs);

            return new ToolRead(names, whole);
        }
        catch (JsonException)
        {
            return ToolRead.Unreadable;
        }
    }

    /// <summary>Walks one list of tools; false when any element is not an object with a name, a member <c>tools</c> is not an array, or the inventory of <c>functions.exec</c> is not in the one form. Nothing is skipped: the walk goes on to find every name it can, and the read is not whole.</summary>
    private static bool Walk(JsonElement tools, string prefix, List<string> names, ref int execs)
    {
        var whole = true;
        foreach (var tool in tools.EnumerateArray())
        {
            if (tool.ValueKind != JsonValueKind.Object || !tool.TryGetProperty("name", out var named) || named.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(named.GetString()))
            {
                whole = false;

                continue;
            }

            var name = named.GetString()!;
            if (tool.TryGetProperty("tools", out var inner))
            {
                if (inner.ValueKind != JsonValueKind.Array)
                {
                    whole = false;

                    continue;
                }

                whole &= Walk(inner, name + ".", names, ref execs);

                continue;
            }

            names.Add(prefix + name);
            if (prefix + name == "functions.exec")
            {
                execs++;
                whole &= execs == 1 && Nested(tool, names);
            }
        }

        return whole;
    }

    private static bool Nested(JsonElement exec, List<string> names)
    {
        if (!exec.TryGetProperty("description", out var description) || description.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var found = new List<string>();
        var whole = true;
        foreach (var line in (description.GetString() ?? string.Empty).Split('\n'))
        {
            var match = NestedLine().Match(line);
            if (match.Success)
            {
                found.Add(match.Groups[1].Value);

                continue;
            }

            // A line that starts with ### after spaces or tabs is a ### line and does not match; a heading of another level whose text starts with a backtick makes the read not whole.
            var trimmed = line.TrimStart(' ', '\t');
            whole &= !trimmed.StartsWith("###", StringComparison.Ordinal) && !AnotherHeading().IsMatch(trimmed);
        }

        names.AddRange(found.Distinct(StringComparer.Ordinal));

        return whole && found.Count > 0;
    }

    /// <summary>A heading of one to six <c>#</c> whose text starts with a backtick: a tool named in another form of heading.</summary>
    [GeneratedRegex("^#{1,6}\\s*`", RegexOptions.CultureInvariant)]
    private static partial Regex AnotherHeading();

    /// <summary>The one form of a nested tool's line: <c>### `name`</c> and nothing else on the line.</summary>
    [GeneratedRegex("^### `([A-Za-z0-9_.-]+)`$", RegexOptions.CultureInvariant)]
    private static partial Regex NestedLine();

    /// <summary>The names of <paramref name="names"/> outside the known set, once each, in the order they were read.</summary>
    internal static IReadOnlyList<string> Outside(IEnumerable<string> names) => [.. names.Except(Known, StringComparer.Ordinal).Distinct(StringComparer.Ordinal)];

    /// <summary>The known tools that a read does not have, in the order of <see cref="Known"/>.</summary>
    internal static IReadOnlyList<string> Absent(IReadOnlyList<string> names) => [.. Known.Except(names, StringComparer.Ordinal)];

    /// <summary>Whether a name may be shown: 1 to 64 characters of letters, digits, <c>_</c>, <c>.</c> and <c>-</c>, which the redactor leaves as it is.</summary>
    internal static bool CanBeShown(string name) =>
        name.Length is >= 1 and <= MostNameLength
        && name.All(character => character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '.' or '-')
        && string.Equals(Redaction.SafeText(name, MostNameLength), name, StringComparison.Ordinal);

    /// <summary>
    /// The names outside the known set as a person reads them: each name that <see cref="CanBeShown"/>, else "a name that cannot be shown" with its length; at most <see cref="MostShown"/> of them, then the count. Empty for no name.
    /// </summary>
    internal static string Shown(IReadOnlyList<string> outside)
    {
        var shown = string.Join(", ", outside.Take(MostShown).Select(name => CanBeShown(name) ? name : $"a name that cannot be shown ({name.Length} characters)"));

        return outside.Count > MostShown ? $"{shown}; and {outside.Count - MostShown} more ({outside.Count} in all)" : shown;
    }

    /// <summary>The reason a notice <c>coder-tool-unknown</c> gives: a tool outside the known set, or an inventory that was not read whole (then no name is shown).</summary>
    internal const string ReasonUnknownTool = "unknown-tool";

    /// <summary>See <see cref="ReasonUnknownTool"/>.</summary>
    internal const string ReasonNotWhole = "inventory-not-whole";

    /// <summary>What the two reads of the tool list came to under the rule of Q3: the verdict of the row <c>tool-list</c> and the one of <c>no-project-tools</c>. An empty sentence means the row holds.</summary>
    /// <param name="Outside">The names outside the known set, in either read.</param>
    /// <param name="Absent">The known tools the worktree read does not have (empty when that read is not a list).</param>
    /// <param name="ToolList">The sentence of the row <c>tool-list</c>, or empty.</param>
    /// <param name="ToolListUnavailable">True when the red row <c>tool-list</c> says "this model cannot be used" (the inventory could not be read whole in both reads, or an unknown tool when <see cref="UnknownToolFailsOver"/>) and so is <c>model-unavailable</c> at the first launch of an entry; false when it is a boundary row.</param>
    /// <param name="NoProjectTools">The tool part of the sentence of the row <c>no-project-tools</c>, or empty.</param>
    /// <param name="Reason">Why the person is told (<see cref="ReasonUnknownTool"/>, <see cref="ReasonNotWhole"/>), or empty when the rows hold.</param>
    /// <param name="ShownNames">The names outside the known set as <see cref="Shown"/> says them — only when BOTH reads hold the same names (A4-M2); empty when the reads differ or one was not read: a name that one read only holds can be text of the repository, so it is counted and never shown.</param>
    internal sealed record Judgement(IReadOnlyList<string> Outside, IReadOnlyList<string> Absent, string ToolList, bool ToolListUnavailable, string NoProjectTools, string Reason, string ShownNames);

    /// <summary>
    /// The two reads — in the worktree and in the stand-in project with its own MCP config — under the rule of Q3. Sentences are coai's own: they carry counts, two known prefixes and, for names outside the set, only what <see cref="Shown"/> lets through.
    /// </summary>
    internal static Judgement Judge(ToolRead worktree, ToolRead standIn)
    {
        var both = worktree.Names.Concat(standIn.Names).ToList();
        var outside = Outside(both);
        var worktreeRead = worktree.IsAList;
        var standInRead = standIn.IsAList;
        // A name is shown only when BOTH reads hold it and the reads are the same (A4-M2): the stand-in project is coai's own, the worktree is the repository's.
        var sameReads = worktreeRead && standInRead && worktree.Names.ToHashSet(StringComparer.Ordinal).SetEquals(standIn.Names);
        var shown = sameReads ? Shown(outside) : string.Empty;
        var absent = worktreeRead ? Absent(worktree.Names) : [];
        var unavailable = false;
        var reason = string.Empty;
        string toolList;
        if (outside.Count > 0)
        {
            var agents = sameReads && both.Any(name => name.StartsWith("collaboration.", StringComparison.Ordinal)) ? "; an agent tool (collaboration.*) is among them" : string.Empty;
            var mcp = sameReads && both.Any(name => name.StartsWith("mcp__", StringComparison.Ordinal)) ? "; an MCP tool is among them" : string.Empty;
            toolList = sameReads
                ? $"the tool list of the CLI holds {outside.Count} name(s) outside the known set of {Known.Count}: {shown}{agents}{mcp}"
                : $"the tool lists of the CLI hold {outside.Count} name(s) outside the known set of {Known.Count} in all, and the two reads are not the same, so no name is shown";
            unavailable = UnknownToolFailsOver;
            reason = ReasonUnknownTool;
        }
        else if (!worktreeRead && !standInRead)
        {
            toolList = "the tool inventory of the CLI could not be read whole, or holds no name, in the worktree and in the stand-in project, so nothing is known of the tools of this model";
            unavailable = true;
            reason = ReasonNotWhole;
        }
        else if (!worktreeRead)
        {
            toolList = "in the worktree: the tool inventory of the CLI could not be read whole, or holds no name";
            reason = ReasonNotWhole;
        }
        else
        {
            toolList = string.Empty;
        }

        string noProject;
        if (!worktreeRead && !standInRead)
        {
            noProject = string.Empty;
        }
        else if (!worktreeRead || !standInRead)
        {
            noProject = "the tool inventory of the CLI could not be read whole, or holds no name, in " + (worktreeRead ? "the stand-in project" : "the worktree") + " only, so the two reads are not the same";
            reason = reason.Length > 0 ? reason : ReasonNotWhole;
        }
        else if (!worktree.Names.ToHashSet(StringComparer.Ordinal).SetEquals(standIn.Names))
        {
            noProject = $"the tool list of the stand-in project, with an MCP server of its own config, is not the one of the worktree: the project adds {standIn.Names.Except(worktree.Names, StringComparer.Ordinal).Distinct(StringComparer.Ordinal).Count()} name(s) and removes {worktree.Names.Except(standIn.Names, StringComparer.Ordinal).Distinct(StringComparer.Ordinal).Count()}";
        }
        else
        {
            noProject = string.Empty;
        }

        return new Judgement(outside, absent, toolList, unavailable, noProject, reason, shown);
    }
}
