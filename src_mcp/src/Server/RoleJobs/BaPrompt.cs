using System.Text;
using CoaiMcp.Core.Work;
using CoaiMcp.Roles;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>What the BA's prompt says about the epic and its surroundings — text, ready to be placed; <see cref="BaEpic.Inputs"/> makes it from the repository.</summary>
internal sealed record BaPromptInputs(string EpicPath, string EpicFolder, string EpicId, string EpicYaml, string PlanText, string OtherEpics, string ArchitecturePaths, string FinishedEpic);

/// <summary>
/// The BA's prompt (<c>src_mcp/src/Roles/prompts/ba.md</c>, embedded) with its numbers and texts filled in — and the repair prompt that carries the errors of a wrong answer back to the same session.
/// </summary>
/// <remarks>
/// <para><b>No number of the work method is written in the prompt file.</b> The limits (<c>maxStoriesPerEpic</c>, <c>maxTasksPerStory</c>), the kinds and the complexity range are placeholders filled from the device's work method and
/// from the validator's own tables, so a plan the prompt asks for is a plan this device accepts, and a changed limit shows in the built prompt (<c>TheBaPromptShowsTheDevicesLimitsTests</c>). The rule text is
/// <c>shared/work-method.md</c> and the output schema is <c>shared/ba-output.schema.json</c>, both embedded: the prompt cannot say something else than the checker does.</para>
/// <para>One pass over the template: a value that holds <c>{{…}}</c> is not read again, so an epic whose text names a placeholder cannot change the prompt. An unknown placeholder in the template is a build fault, so it throws.</para>
/// </remarks>
internal static class BaPrompt
{
    internal const string PromptResource = "CoaiMcp.ba-prompt.md";
    internal const string RulesResource = "CoaiMcp.work-method.md";
    internal const string SchemaResource = "CoaiMcp.ba-output.schema.json";

    /// <summary>The most errors one repair turn carries; the rest are counted, not listed.</summary>
    public const int MostErrors = 40;

    /// <summary>The first prompt of a BA job.</summary>
    public static string Build(BaPromptInputs inputs, WorkMethod method, int repairTurns) => Fill(Template(PromptResource), Values(inputs, method, repairTurns));

    /// <summary>
    /// The next turn: the errors the plan check found (at most <see cref="MostErrors"/>, the rest counted) and the instruction to answer again with the WHOLE object — the session still has the conversation, so nothing else is repeated.
    /// </summary>
    public static string Repair(IReadOnlyList<string> errors)
    {
        var shown = errors.Take(MostErrors).ToList();
        var text = new StringBuilder("Your answer was checked against the rules and has errors. Fix every one of them and answer again with the WHOLE JSON object — the same shape, the same rules, nothing before it and nothing after it.\n\n");
        foreach (var error in shown)
        {
            text.Append("- ").Append(error).Append('\n');
        }

        if (errors.Count > shown.Count)
        {
            text.Append($"- … and {errors.Count - shown.Count} more errors that are not listed; fix these first and the rest may show.\n");
        }

        return text.ToString();
    }

    private static Dictionary<string, string> Values(BaPromptInputs inputs, WorkMethod method, int repairTurns) => new(StringComparer.Ordinal)
    {
        ["epicPath"] = inputs.EpicPath,
        ["epicFolder"] = inputs.EpicFolder,
        ["epicId"] = inputs.EpicId,
        ["epicYaml"] = inputs.EpicYaml,
        ["planText"] = inputs.PlanText,
        ["otherEpics"] = inputs.OtherEpics,
        ["architecturePaths"] = inputs.ArchitecturePaths,
        ["finishedEpic"] = inputs.FinishedEpic,
        ["maxStoriesPerEpic"] = method.MaxStoriesPerEpic.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["maxTasksPerStory"] = method.MaxTasksPerStory.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["complexityMin"] = WorkTables.ComplexityMin.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["complexityMax"] = WorkTables.ComplexityMax.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["kinds"] = string.Join(", ", WorkTables.Kinds),
        ["repairTurns"] = repairTurns.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["rules"] = Template(RulesResource).TrimEnd(),
        ["schema"] = Template(SchemaResource).TrimEnd(),
    };

    private static string Fill(string template, Dictionary<string, string> values) => PromptTemplate.Fill("BA", template, values);

    /// <summary>An embedded text of the prompt: a missing one is a build fault, so it throws (a default that can go missing is not a default).</summary>
    internal static string Template(string resource) => PromptTemplate.Embedded(resource);
}
