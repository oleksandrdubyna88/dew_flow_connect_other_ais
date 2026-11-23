using System.Globalization;
using System.Text;
using CoaiMcp.Core.Work;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>What the coder's prompt says about its task — text and lists, ready to be placed; <see cref="CoderTask"/> makes it from the task file.</summary>
/// <param name="TaskPath">The task file, from the repository's top, written with <c>/</c>.</param>
internal sealed record CoderPromptInputs(string TaskPath, string TaskId, string Title, string Kind, IReadOnlyList<string> Files, IReadOnlyList<WorkDod> Dod);

/// <summary>
/// The coder's prompt (<c>src_mcp/src/Roles/prompts/coder.md</c>, embedded) with its task filled in — and the repair prompt that carries the errors of a wrong report back to the same session.
/// </summary>
/// <remarks>
/// <para><b>A person's text first.</b> An override of the prompt <see cref="PromptId"/> in <see cref="RolePrompts"/> (the file <c>prompts/coder.md</c> of the data directory) is used when it has text, with the same placeholders
/// filled; an empty one is no override. Its unknown placeholders stay as written — a person's typo does not stop a job — while one in the shipped template is a build fault and throws.</para>
/// <para>The rules of the worktree (coai commits, builds and tests; no path outside the worktree) and the report's schema (<c>shared/coder-report.schema.json</c>, embedded) are in the shipped text, so the prompt cannot say
/// something else than the reader of the report checks.</para>
/// </remarks>
internal static class CoderPrompt
{
    /// <summary>The id of the prompt in <see cref="RolePrompts"/>: the override file is <c>prompts/coder.md</c>.</summary>
    public const string PromptId = "coder";

    internal const string PromptResource = "CoaiMcp.coder-prompt.md";
    internal const string SchemaResource = "CoaiMcp.coder-report.schema.json";

    /// <summary>The first prompt of a coder job: the person's override when it has text, else the shipped text.</summary>
    public static string Build(CoderPromptInputs inputs, RolePrompts prompts)
    {
        var written = prompts.Written(PromptId);
        var custom = !string.IsNullOrWhiteSpace(written);

        return PromptTemplate.Fill("coder", custom ? written : PromptTemplate.Embedded(PromptResource), Values(inputs), strict: !custom);
    }

    /// <summary>
    /// The next turn: the errors the report check found (at most <see cref="BaPrompt.MostErrors"/>, the rest counted) and the instruction to answer again with the WHOLE report. The session still has the work, so nothing is repeated
    /// and no file is to be changed for it.
    /// </summary>
    public static string Repair(IReadOnlyList<string> errors)
    {
        var shown = errors.Take(BaPrompt.MostErrors).ToList();
        var text = new StringBuilder("Your report was checked and has errors. Do not change any file for this. Fix every one of them and answer again with the WHOLE JSON report — the same shape, the same rules, nothing before it and nothing after it.\n\n");
        foreach (var error in shown)
        {
            text.Append("- ").Append(error).Append('\n');
        }

        if (errors.Count > shown.Count)
        {
            text.Append(CultureInfo.InvariantCulture, $"- … and {errors.Count - shown.Count} more errors that are not listed; fix these first and the rest may show.\n");
        }

        return text.ToString();
    }

    private static Dictionary<string, string> Values(CoderPromptInputs inputs) => new(StringComparer.Ordinal)
    {
        ["taskPath"] = inputs.TaskPath,
        ["taskId"] = inputs.TaskId,
        ["title"] = inputs.Title,
        ["kind"] = inputs.Kind,
        ["files"] = inputs.Files.Count == 0 ? "- (the task names no file: find the right ones in the repository)" : string.Join('\n', inputs.Files.Select(file => $"- `{file}`")),
        ["dod"] = string.Join('\n', inputs.Dod.Select(item => $"- {item.Id}: {item.Item}")),
        ["schema"] = PromptTemplate.Embedded(SchemaResource).TrimEnd(),
    };
}
