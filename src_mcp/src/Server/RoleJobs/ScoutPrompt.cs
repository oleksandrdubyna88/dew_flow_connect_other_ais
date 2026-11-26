using CoaiMcp.Core.Roles;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The scout's prompt (<c>src_mcp/src/Roles/prompts/scout.md</c>, embedded) with its brief and its numbers filled in (epoch 3 E3 s1 t4). <b>No number is written in the prompt file</b>: the limits are placeholders filled from the tables of
/// <see cref="ScoutOutputReader"/>, so what the model is told is what the reader accepts and a changed limit shows in the built prompt. The schema is <c>shared/scout-output.schema.json</c>, embedded. The repair prompt is the BA's
/// (<see cref="BaPrompt.Repair"/>): it carries the errors of a wrong answer back to the same session.
/// </summary>
internal static class ScoutPrompt
{
    /// <summary>The id of the prompt in <see cref="RolePrompts"/>: the override file is <c>prompts/scout.md</c>.</summary>
    public const string PromptId = "scout";

    internal const string PromptResource = "CoaiMcp.scout-prompt.md";
    internal const string SchemaResource = "CoaiMcp.scout-output.schema.json";

    public static string Build(RoleJobRequest request, RolePrompts prompts, int repairTurns) =>
        ReadOnlyPrompt.Build("scout", PromptId, PromptResource, request, prompts, Values(repairTurns));

    private static Dictionary<string, string> Values(int repairTurns) => new(StringComparer.Ordinal)
    {
        ["maxFacts"] = ReadOnlyPrompt.Number(ScoutOutputReader.MaxFacts),
        ["maxClaim"] = ReadOnlyPrompt.Number(ScoutOutputReader.MaxClaim),
        ["minEvidence"] = ReadOnlyPrompt.Number(ScoutOutputReader.MinEvidence),
        ["maxEvidence"] = ReadOnlyPrompt.Number(ScoutOutputReader.MaxEvidence),
        ["maxText"] = ReadOnlyPrompt.Number(RoleEvidence.MaxTextLength),
        ["maxFiles"] = ReadOnlyPrompt.Number(ScoutOutputReader.MaxFiles),
        ["maxUnknown"] = ReadOnlyPrompt.Number(ScoutOutputReader.MaxUnknown),
        ["maxUnknownText"] = ReadOnlyPrompt.Number(ScoutOutputReader.MaxUnknownText),
        ["repairTurns"] = ReadOnlyPrompt.Number(repairTurns),
        ["schema"] = PromptTemplate.Embedded(SchemaResource).TrimEnd(),
    };
}
