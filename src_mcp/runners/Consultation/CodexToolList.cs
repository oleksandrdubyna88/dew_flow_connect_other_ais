using System.Text.Json;
using System.Text.RegularExpressions;
using CoaiMcp.Core.Notices;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// The tool list of a codex session as <c>codex debug prompt-input</c> prints it (epoch 3 E3 s5 t3, id CX-T1; judgement 13.3; changed by s2 t5, <c>SPEC_CHANGES.md</c> section Q3): the names of the top-level tools of <c>additional_tools</c> (namespace and name) and the nested tools of the JavaScript tool <c>exec</c>, which its description names as
/// <c>### `name`</c>. The list says NOTHING about the web search (CX-T6): it is the same with and without the flag.
/// </summary>
/// <remarks>
/// <para><b>The rule (Q3).</b> The row holds when NO name of the list is outside the known set of 11 names measured with the argv of the judgement's section 13.4. Fewer names pass, and the envelope names the known tools the model does not have. A name outside the set — an agent tool
/// (<c>collaboration.*</c>), an MCP tool (<c>mcp__*</c>), a web or image tool, a new name — refuses the launch (<c>coder-boundary</c>). The two reads, in the worktree and in the stand-in project with its own MCP config, must hold the same names. A list that cannot be read, or that holds no name,
/// passes nothing: in both reads it is <c>model-unavailable</c>, in one read only it is the boundary.</para>
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
    /// THE one place that says what a tool outside the known set does at the preflight (Q3 item 1, and the open question of the owner): false — the row is red as a BOUNDARY row, the job ends <c>coder-boundary</c> and the next entry is not tried. True would make that one case
    /// <c>model-unavailable</c>, which fails over to the next entry; the notice <c>coder-tool-unknown</c> is raised either way. A one-line change.
    /// </summary>
    internal const bool UnknownToolFailsOver = false;

    /// <summary>The most names of a list that are shown (Q3 item 5), then the count.</summary>
    internal const int MostShown = 5;

    /// <summary>The longest name that is shown, in characters.</summary>
    internal const int MostNameLength = 64;

    /// <summary>The names the output names, or null when it is not a prompt-input report at all (no JSON, no <c>additional_tools</c>).</summary>
    internal static IReadOnlyList<string>? NamesOf(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var tools = document.RootElement.EnumerateArray().FirstOrDefault(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("type", out var type) && type.GetString() == "additional_tools");
            if (tools.ValueKind != JsonValueKind.Object || !tools.TryGetProperty("tools", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var names = new List<string>();
            Walk(list, string.Empty, names);

            return names;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Walk(JsonElement tools, string prefix, List<string> names)
    {
        foreach (var tool in tools.EnumerateArray().Where(tool => tool.ValueKind == JsonValueKind.Object))
        {
            var name = tool.TryGetProperty("name", out var named) && named.ValueKind == JsonValueKind.String ? named.GetString() ?? string.Empty : string.Empty;
            if (tool.TryGetProperty("tools", out var inner) && inner.ValueKind == JsonValueKind.Array)
            {
                Walk(inner, name + ".", names);

                continue;
            }

            names.Add(prefix + name);
            if (prefix + name == "functions.exec" && tool.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String)
            {
                names.AddRange(Nested().Matches(description.GetString() ?? string.Empty).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal));
            }
        }
    }

    [GeneratedRegex("### `(\\w+)`", RegexOptions.CultureInvariant)]
    private static partial Regex Nested();

    /// <summary>The names of <paramref name="names"/> outside the known set, once each, in the order they were read.</summary>
    internal static IReadOnlyList<string> Outside(IEnumerable<string> names) => [.. names.Except(Known, StringComparer.Ordinal).Distinct(StringComparer.Ordinal)];

    /// <summary>The known tools that a read does not have, in the order of <see cref="Known"/>.</summary>
    internal static IReadOnlyList<string> Absent(IReadOnlyList<string> names) => [.. Known.Except(names, StringComparer.Ordinal)];

    /// <summary>Whether a read is a list at all: it was read, and it holds at least one name. An empty list holds no outside name, so it would pass the rule — and it is not a list.</summary>
    internal static bool IsAList(IReadOnlyList<string>? names) => names is { Count: > 0 };

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

    /// <summary>What the two reads of the tool list came to under the rule of Q3: the verdict of the row <c>tool-list</c> and the one of <c>no-project-tools</c>. An empty sentence means the row holds.</summary>
    /// <param name="Outside">The names outside the known set, in either read.</param>
    /// <param name="Absent">The known tools the worktree read does not have (empty when that read is not a list).</param>
    /// <param name="ToolList">The sentence of the row <c>tool-list</c>, or empty.</param>
    /// <param name="ToolListUnavailable">True when the red row <c>tool-list</c> is a case of <c>model-unavailable</c> (the list could not be read in both reads, or an unknown tool when <see cref="UnknownToolFailsOver"/>), false when it is a boundary row.</param>
    /// <param name="NoProjectTools">The tool part of the sentence of the row <c>no-project-tools</c>, or empty.</param>
    internal sealed record Judgement(IReadOnlyList<string> Outside, IReadOnlyList<string> Absent, string ToolList, bool ToolListUnavailable, string NoProjectTools)
    {
        /// <summary>The names outside the known set, as <see cref="Shown"/> says them.</summary>
        public string ShownNames => Shown(Outside);
    }

    /// <summary>
    /// The two reads — in the worktree and in the stand-in project with its own MCP config; null when a read gave no JSON report — under the rule of Q3. Sentences are coai's own: they carry counts, two known prefixes and, for names outside the set, only what <see cref="Shown"/> lets through.
    /// </summary>
    internal static Judgement Judge(IReadOnlyList<string>? worktree, IReadOnlyList<string>? standIn)
    {
        var both = (worktree ?? []).Concat(standIn ?? []).ToList();
        var outside = Outside(both);
        var worktreeRead = IsAList(worktree);
        var standInRead = IsAList(standIn);
        var absent = worktreeRead ? Absent(worktree!) : [];
        var unavailable = false;
        string toolList;
        if (outside.Count > 0)
        {
            var agents = both.Any(name => name.StartsWith("collaboration.", StringComparison.Ordinal)) ? "; an agent tool (collaboration.*) is among them" : string.Empty;
            var mcp = both.Any(name => name.StartsWith("mcp__", StringComparison.Ordinal)) ? "; an MCP tool is among them" : string.Empty;
            toolList = $"the tool list of the CLI holds {outside.Count} name(s) outside the known set of {Known.Count}: {Shown(outside)}{agents}{mcp}";
            unavailable = UnknownToolFailsOver;
        }
        else if (!worktreeRead && !standInRead)
        {
            toolList = "the tool list of the CLI could not be read, or holds no name, in the worktree and in the stand-in project, so nothing is known of the tools of this model";
            unavailable = true;
        }
        else if (!worktreeRead)
        {
            toolList = "in the worktree: the tool list of the CLI could not be read, or holds no name";
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
            noProject = "the tool list of the CLI could not be read, or holds no name, in " + (worktreeRead ? "the stand-in project" : "the worktree") + " only, so the two reads are not the same";
        }
        else if (!worktree!.ToHashSet(StringComparer.Ordinal).SetEquals(standIn!))
        {
            noProject = $"the tool list of the stand-in project, with an MCP server of its own config, is not the one of the worktree: the project adds {standIn!.Except(worktree!, StringComparer.Ordinal).Distinct(StringComparer.Ordinal).Count()} name(s) and removes {worktree!.Except(standIn!, StringComparer.Ordinal).Distinct(StringComparer.Ordinal).Count()}";
        }
        else
        {
            noProject = string.Empty;
        }

        return new Judgement(outside, absent, toolList, unavailable, noProject);
    }
}
