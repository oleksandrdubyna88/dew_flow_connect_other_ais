using System.Text.Json;
using System.Text.RegularExpressions;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// The tool list of a codex session as <c>codex debug prompt-input</c> prints it (epoch 3 E3 s5 t3, id CX-T1; judgement 13.3): the names of the top-level tools of <c>additional_tools</c> (namespace and name) and the nested tools of the JavaScript tool <c>exec</c>, which its description names as
/// <c>### `name`</c>. With the argv of the judgement's section 13.4 it is exactly the 11 measured names, and no name that starts with <c>collaboration.</c>. The list says NOTHING about the web search (CX-T6): it is the same with and without the flag.
/// </summary>
internal static partial class CodexToolList
{
    /// <summary>The 11 names measured on codex-cli 0.171.0 with the argv of the judgement's section 13.4.</summary>
    internal static IReadOnlyList<string> Measured { get; } =
    [
        "functions.exec", "functions.wait", "functions.request_user_input", "functions.request_user_input_async",
        "apply_patch", "exec_command", "write_stdin", "list_mcp_resources", "list_mcp_resource_templates", "read_mcp_resource", "clock__curr_time",
    ];

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

    /// <summary>
    /// The sentence that says the list is not the measured one, or empty when it is. It names COUNTS and two known prefixes, never a name the program printed: a row never carries the output of a program it ran.
    /// </summary>
    internal static string Problem(IReadOnlyList<string>? names)
    {
        if (names is null)
        {
            return "the tool list of the CLI could not be read";
        }

        var extra = names.Except(Measured, StringComparer.Ordinal).ToList();
        var missing = Measured.Except(names, StringComparer.Ordinal).ToList();
        if (extra.Count == 0 && missing.Count == 0)
        {
            return string.Empty;
        }

        var agents = names.Any(name => name.StartsWith("collaboration.", StringComparison.Ordinal)) ? "; an agent tool (collaboration.*) is among them" : string.Empty;
        var mcp = names.Any(name => name.StartsWith("mcp__", StringComparison.Ordinal)) ? "; an MCP tool is among them" : string.Empty;

        return $"the tool list of the CLI is not the measured one: {extra.Count} name(s) are not on the measured list and {missing.Count} of it are missing{agents}{mcp}";
    }
}
