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
        var tools = CodexToolList.Judge(Names(data, "tools-worktree"), Names(data, "tools-standin"));
        var doctor = CodexDoctorReport.Read(Text(data, "doctor"));
        var profile = CodexDoctorReport.ProfileProblem(doctor);
        var mcp = CodexDoctorReport.McpProblem(doctor);
        // The host looks for what the unit wrote into the main checkout and beside the worktree: a marker on the disk means the folder was not covered, whatever the unit's own listing said (S40).
        foreach (var (item, folder) in new[] { ("main-hidden", job.Tree.MainCheckout), ("coder-root-hidden", job.CoderRoot) })
        {
            if (Removed(Path.Combine(folder, CodexPreflightScript.CoverMarker)))
            {
                byItem[item] = new PreflightRow(item, false, "a file the unit wrote into this folder reached the disk of the host, so the folder is not covered by the unit's own tmpfs");
            }
        }

        var computed = new Dictionary<string, PreflightRow>(StringComparer.Ordinal)
        {
            ["tool-list"] = tools.ToolList.Length == 0
                ? new PreflightRow("tool-list", true, HeldSentence(tools)) { Absent = tools.Absent }
                : new PreflightRow("tool-list", false, tools.ToolList) { Unavailable = tools.ToolListUnavailable, Tools = tools.Outside.Count > 0 ? tools.ShownNames : string.Empty },
            ["no-project-tools"] = tools.NoProjectTools.Length + mcp.Length == 0
                ? new PreflightRow("no-project-tools", true, "a project's own .codex/config.toml with an MCP server does not change the tool list, and the doctor report says no MCP server is configured")
                : new PreflightRow("no-project-tools", false, string.Join("; ", new[] { tools.NoProjectTools, mcp }.Where(text => text.Length > 0))),
            ["permission-profile"] = profile.Length == 0
                ? new PreflightRow("permission-profile", true, "the doctor report of the CLI shows the profile coai, a restricted filesystem and network sandbox and the approval policy Never")
                : new PreflightRow("permission-profile", false, profile),
        };

        return [basic[0], .. items.Select(item => computed.TryGetValue(item, out var made) ? made : byItem[item])];
    }

    /// <summary>The sentence of a tool-list row that holds (Q3): no name outside the known set, in the worktree and in a stand-in project with an MCP server of its own config; the known tools the model does not have are named — coai's own strings.</summary>
    private static string HeldSentence(CodexToolList.Judgement tools) =>
        $"the tool list of the CLI, asked with the job's own model and flags, holds {CodexToolList.Known.Count - tools.Absent.Count} of the {CodexToolList.Known.Count} known names and no name outside the known set, the same in the worktree and in a stand-in project with an MCP server of its own config (it says nothing about the web search)"
        + (tools.Absent.Count == 0 ? string.Empty : $"; this model does not have: {string.Join(", ", tools.Absent)}");

    /// <summary>Whether a marker file is there; it is removed by its own name (a link is unlinked, never followed).</summary>
    private static bool Removed(string marker)
    {
        if (Files.RealPath.Lstat(marker).Kind is not (Files.EntryKind.File or Files.EntryKind.Link))
        {
            return false;
        }

        try
        {
            File.Delete(marker);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Still there: it is a bad row all the same, and the job's folders are removed with the job.
        }

        return true;
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
