using System.Text;

namespace CoaiMcp.Install;

/// <summary>
/// The line that tells Codex to call coai's <c>roles</c> tool at the start of a task (v2 E3.S7, spec decision 9): ONE block between the marker lines <c>&lt;!-- coai:roles:start --&gt;</c> and
/// <c>&lt;!-- coai:roles:end --&gt;</c> in Codex's global instruction file, <c>${CODEX_HOME:-$HOME/.codex}/AGENTS.md</c>.
/// </summary>
/// <remarks>
/// <para><b>Measured 2026-11-22 on codex-cli 0.171.0</b> (<c>codex debug prompt-input</c>, a scratch HOME and a scratch <c>CODEX_HOME</c>, no model call): a marker line put in <c>AGENTS.md</c> there is in the
/// model-visible prompt input, and <c>CODEX_HOME</c> moves the file. agy's global file could NOT be measured without a signed-in model turn, so agy has no instruction target (see <c>shared/install-clients.json</c>).</para>
/// <para>The file is the PERSON'S text, so only the marker block is touched: it is added at the END (whole new lines), replaced in place when the wording changed, and taken away with the blank line that
/// separated it. Anything outside the markers — and a file whose markers are not one matched pair — is left alone (the latter is refused).</para>
/// </remarks>
internal sealed class CodexInstructions : IInstallTarget
{
    internal const string Start = "<!-- coai:roles:start -->";

    internal const string End = "<!-- coai:roles:end -->";

    /// <summary>The instruction, in one paragraph.</summary>
    internal const string Line = "At the start of every task, call the coai MCP tool `roles` and follow its table: which model each role runs (the first of a role's list, the others in order if it fails), the complexity bands and the work method.";

    /// <summary>The name an entry of ours has in the output and the manifest; its one argument is the instruction text.</summary>
    internal const string Name = "coai:roles";

    public Client Cli => Client.Codex;

    public string Kind => "instructions";

    public bool FollowsBinary => false;

    public string FileOf(InstallHost host) => host.CodexAgents;

    public string Via(PlanAction action) => action == PlanAction.None ? string.Empty : "edit of AGENTS.md (the coai:roles block)";

    public McpEntry NewEntry(string binary) => new(Name, [Line], new Dictionary<string, string>());

    public McpEntry WithCommand(McpEntry before, string binary) => NewEntry(binary);

    public ConfigRead Read(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return ConfigRead.None;
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var starts = Indexes(lines, Start);
        var ends = Indexes(lines, End);
        if (starts.Count == 0 && ends.Count == 0)
        {
            return ConfigRead.None;
        }

        if (starts.Count != 1 || ends.Count != 1 || ends[0] < starts[0])
        {
            return new ConfigRead.Unreadable($"the marker lines {Start} and {End} are not one matched pair");
        }

        var inner = string.Join(' ', lines[(starts[0] + 1)..ends[0]].Select(line => line.Trim()).Where(line => line.Length > 0));

        return new ConfigRead.Found(new McpEntry(Name, [inner], new Dictionary<string, string>()), []);
    }

    private static List<int> Indexes(string[] lines, string marker) =>
        [.. lines.Select((line, index) => (line, index)).Where(one => one.line.Trim() == marker).Select(one => one.index)];

    public Task<TargetOutcome> ApplyAsync(PlannedTarget plan, ApplyContext context, CancellationToken ct) => FileEdit.ApplyAsync(this, plan, context, Transform, ct);

    /// <summary>The file's new bytes: the block added at the end, its text replaced, or the block taken away.</summary>
    private static byte[] Transform(byte[]? original, PlannedTarget plan)
    {
        var text = original is null ? string.Empty : Encoding.UTF8.GetString(original);
        var hasBom = text.Length > 0 && text[0] == '﻿';
        var body = hasBom ? text[1..] : text;
        var eol = body.Contains("\r\n") ? "\r\n" : "\n";
        var lines = new List<string>(body.Split('\n').Select(line => line.TrimEnd('\r')));
        var start = lines.FindIndex(line => line.Trim() == Start);
        var end = lines.FindIndex(line => line.Trim() == End);
        if (start >= 0 && (end < start || lines.Count(line => line.Trim() == Start) != 1 || lines.Count(line => line.Trim() == End) != 1))
        {
            throw new FormatException($"the marker lines {Start} and {End} are not one matched pair");
        }

        if (plan.After is null)
        {
            if (start >= 0)
            {
                var from = start > 0 && lines[start - 1].Trim().Length == 0 ? start - 1 : start;
                lines.RemoveRange(from, end - from + 1);
            }
        }
        else if (start >= 0)
        {
            lines.RemoveRange(start + 1, end - start - 1);
            lines.Insert(start + 1, plan.After.Args[0]);
        }
        else
        {
            if (lines.Count > 0 && lines[^1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            if (lines.Count > 0)
            {
                lines.Add(string.Empty);
            }

            lines.AddRange([Start, plan.After.Args[0], End, string.Empty]);
        }

        var result = string.Join(eol, lines);
        if (plan.After is null && result.Trim().Length == 0 && original is not null)
        {
            result = string.Empty;
        }

        return Encoding.UTF8.GetBytes((hasBom ? "﻿" : string.Empty) + result);
    }
}
