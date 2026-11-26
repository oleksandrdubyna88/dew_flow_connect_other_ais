using System.Text;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Toolchain;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// The rows of a finished codex preflight unit (epoch 3 E3 s5 t3): the rows the shell program decided, and the three it hands over as <c>data</c> lines — CX-T1, CX-C3 and CX-C4, whose evidence is a program's whole answer that only code can read (JSON). Every sentence here is written by coai: a row never carries
/// a word a program printed, so a hostile program cannot write into one, and none holds a token.
/// </summary>
internal static class CodexPreflightRows
{
    /// <summary>The rows whose verdict is made here, from data lines.</summary>
    private static readonly string[] Computed = ["tool-list", "no-project-tools", "permission-profile"];

    /// <summary>The rows of the unit that ran: <c>user-manager</c>, then <see cref="CodexPreflight.Items"/> in order — or, for a unit that did not run to its end, the one row that says why.</summary>
    internal static IReadOnlyList<PreflightRow> Build(UnitStart started, bool gone, BoundaryJob job)
    {
        var items = CodexPreflight.Items(job);
        var scripted = items.Where(item => !Computed.Contains(item)).ToList();
        var basic = BoundaryPreflight.Rows(started, gone, scripted);
        if (basic.Count != scripted.Count + 1)
        {
            return basic;
        }

        var data = DataOf(started.Result.StdOut);
        var byItem = basic.Skip(1).ToDictionary(row => row.Item, StringComparer.Ordinal);
        var worktree = CodexToolList.Problem(Names(data, "tools-worktree"));
        var standIn = CodexToolList.Problem(Names(data, "tools-standin"));
        var doctor = CodexDoctorReport.Read(Text(data, "doctor"));
        var profile = CodexDoctorReport.ProfileProblem(doctor);
        var mcp = CodexDoctorReport.McpProblem(doctor);
        var computed = new Dictionary<string, PreflightRow>(StringComparer.Ordinal)
        {
            ["tool-list"] = worktree.Length + standIn.Length == 0
                ? new PreflightRow("tool-list", true, "the tool list of the CLI, asked with the job's own model and flags, is the 11 measured names, in the worktree and in a stand-in project with an MCP server of its own config (it says nothing about the web search)")
                : new PreflightRow("tool-list", false, string.Join("; ", new[] { worktree.Length > 0 ? "in the worktree: " + worktree : string.Empty, standIn.Length > 0 ? "in a stand-in project: " + standIn : string.Empty }.Where(text => text.Length > 0))),
            ["no-project-tools"] = standIn.Length + mcp.Length == 0
                ? new PreflightRow("no-project-tools", true, "a project's own .codex/config.toml with an MCP server does not change the tool list, and the doctor report says no MCP server is configured")
                : new PreflightRow("no-project-tools", false, string.Join("; ", new[] { standIn.Length > 0 ? "in a stand-in project: " + standIn : string.Empty, mcp }.Where(text => text.Length > 0))),
            ["permission-profile"] = profile.Length == 0
                ? new PreflightRow("permission-profile", true, "the doctor report of the CLI shows the profile coai, a restricted filesystem and network sandbox and the approval policy Never")
                : new PreflightRow("permission-profile", false, profile),
        };

        return [basic[0], .. items.Select(item => computed.TryGetValue(item, out var made) ? made : byItem[item])];
    }

    private static IReadOnlyList<string>? Names(Dictionary<string, string> data, string name) => CodexToolList.NamesOf(Text(data, name));

    private static string Text(Dictionary<string, string> data, string name) => data.GetValueOrDefault(name) ?? string.Empty;

    /// <summary>The <c>data</c> lines of the unit's output: name and the decoded text. A line that is not valid base64 is an empty answer, which a row then reads as unreadable.</summary>
    internal static Dictionary<string, string> DataOf(string output)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parts in output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r').Split('\t')).Where(parts => parts.Length == 3 && parts[0] == "data"))
        {
            data[parts[1]] = Decoded(parts[2]);
        }

        return data;
    }

    private static string Decoded(string base64)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }
}
