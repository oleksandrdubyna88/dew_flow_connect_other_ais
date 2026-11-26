using CoaiMcp.Core.Roles;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The judge's prompt (<c>src_mcp/src/Roles/prompts/judge.md</c>, embedded) with its brief and its numbers filled in (epoch 3 E3 s1 t4). As for the scout, no number is written in the prompt file: the limits, the verdict words and
/// the severity words come from the tables of <see cref="JudgeOutputReader"/>. The schema is <c>shared/judge-output.schema.json</c>, embedded. The repair prompt is the BA's (<see cref="BaPrompt.Repair"/>).
/// </summary>
internal static class JudgePrompt
{
    /// <summary>The id of the prompt in <see cref="RolePrompts"/>: the override file is <c>prompts/judge.md</c>.</summary>
    public const string PromptId = "judge";

    internal const string PromptResource = "CoaiMcp.judge-prompt.md";
    internal const string SchemaResource = "CoaiMcp.judge-output.schema.json";

    public static string Build(RoleJobRequest request, RolePrompts prompts, int repairTurns) =>
        ReadOnlyPrompt.Build("judge", PromptId, PromptResource, request, prompts, Values(repairTurns));

    private static Dictionary<string, string> Values(int repairTurns) => new(StringComparer.Ordinal)
    {
        ["verdicts"] = string.Join(", ", JudgeOutputReader.Verdicts),
        ["severities"] = string.Join(", ", JudgeOutputReader.Severities),
        ["maxSummary"] = ReadOnlyPrompt.Number(JudgeOutputReader.MaxSummary),
        ["maxFindings"] = ReadOnlyPrompt.Number(JudgeOutputReader.MaxFindings),
        ["maxTitle"] = ReadOnlyPrompt.Number(JudgeOutputReader.MaxTitle),
        ["maxDetail"] = ReadOnlyPrompt.Number(JudgeOutputReader.MaxDetail),
        ["maxEvidence"] = ReadOnlyPrompt.Number(JudgeOutputReader.MaxEvidence),
        ["maxFix"] = ReadOnlyPrompt.Number(JudgeOutputReader.MaxFix),
        ["maxText"] = ReadOnlyPrompt.Number(RoleEvidence.MaxTextLength),
        ["maxChosen"] = ReadOnlyPrompt.Number(JudgeOutputReader.MaxChosen),
        ["maxDecisionWhy"] = ReadOnlyPrompt.Number(JudgeOutputReader.MaxDecisionWhy),
        ["maxRejected"] = ReadOnlyPrompt.Number(JudgeOutputReader.MaxRejected),
        ["repairTurns"] = ReadOnlyPrompt.Number(repairTurns),
        ["schema"] = PromptTemplate.Embedded(SchemaResource).TrimEnd(),
    };
}
