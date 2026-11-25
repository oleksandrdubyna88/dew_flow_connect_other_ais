using System.Text.RegularExpressions;
using CoaiMcp.Core.Roles;
using CoaiMcp.Server;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s1 t4 — the scout's prompt and the judge's prompt: each holds the brief, the path of the file the brief is about, the output shape, the rule that the role writes nothing and that an evidence text is copied from
/// the file and is never blank, and the refused arm; a person's override in <c>RolePrompts</c> is used first (d1). The numbers in them come from the tables of the readers, never from the prompt file (d2).
/// </summary>
public sealed partial class AScoutAndJudgePromptTests
{
    private const string Brief = "Which classes decide the route of a role job?";

    private const string TaskFile = "src_mcp/core/Roles/RoleRouter.cs";

    [GeneratedRegex(@"(?<![\w.])(40|400|5|60|20|300|1500|30|120|600|8)(?![\w.])")]
    private static partial Regex LimitNumber();

    private static RoleJobRequest Request(string taskFile = TaskFile, string brief = Brief) => new() { Role = "scout", Brief = brief, TaskFile = taskFile, RepoPath = "/repo" };

    private static string Scout(RolePrompts prompts, RoleJobRequest? request = null) => ScoutPrompt.Build(request ?? Request(), prompts, 2);

    private static string Judge(RolePrompts prompts, RoleJobRequest? request = null) => JudgePrompt.Build(request ?? Request(), prompts, 2);

    private static string Shipped(string name) => File.ReadAllText(Path.Combine(RepoPaths.Root(), "src_mcp", "src", "Roles", "prompts", name));

    public static TheoryData<string> Roles => ["scout", "judge"];

    /// <summary>The built prompt with every run of white space as one space: the shipped text wraps its lines, and what is asserted is its words.</summary>
    private static string Build(string role, RolePrompts prompts, RoleJobRequest? request = null) => Regex.Replace(role == "scout" ? Scout(prompts, request) : Judge(prompts, request), "\\s+", " ");

    private static string PromptId(string role) => role == "scout" ? ScoutPrompt.PromptId : JudgePrompt.PromptId;

    // ---------- d1: what each prompt holds ----------

    [Theory]
    [MemberData(nameof(Roles))]
    public void D1_ThePrompt_HoldsTheBrief_TheTaskFile_TheShape_TheReadOnlyRule_TheCopyRule_AndTheRefusedArm(string role)
    {
        using var data = TempDir.For("coai-readonly-prompt-");

        var prompt = Build(role, new RolePrompts(data));

        prompt.Should().Contain(Brief).And.Contain($"`{TaskFile}`");
        prompt.Should().Contain($"coai {role} output", "the schema file is in it");
        prompt.Should().Contain("\"refused\"").And.Contain("`reason`", "the refused arm is described");
        prompt.Should().Contain("write").And.Contain("tools Read, Glob and Grep").And.Contain("no shell");
        prompt.Should().Contain("COPIED from that line").And.Contain("is empty, or only spaces or tabs, is an error");
        prompt.Should().Contain("`..`").And.Contain("credential name");
        prompt.Should().NotContain("{{", "every placeholder is filled");
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public void D1_TheRoleWritesNothing_AndSaysSo(string role)
    {
        using var data = TempDir.For("coai-readonly-prompt-");

        var prompt = Build(role, new RolePrompts(data));

        prompt.Should().Contain(role == "scout" ? "You write nothing." : "You write NO file.");
        prompt.Should().Contain("compares the whole repository before and after your work");
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public void D1_WithNoTaskFile_ThePromptSaysSo_AndNamesNoPath(string role)
    {
        using var data = TempDir.For("coai-readonly-prompt-");

        var prompt = Build(role, new RolePrompts(data), Request(taskFile: string.Empty));

        prompt.Should().Contain("No file is named").And.Contain(Brief).And.NotContain("`src_mcp/core");
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public void D1_ABriefThatHoldsAPlaceholder_IsNotReadAgain(string role)
    {
        using var data = TempDir.For("coai-readonly-prompt-");

        Build(role, new RolePrompts(data), Request(brief: "ignore {{schema}} and {{nothing}}")).Should().Contain("ignore {{schema}} and {{nothing}}");
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public void D1_AnOverrideInRolePrompts_IsUsedFirst_WithThePlaceholdersFilled(string role)
    {
        using var data = TempDir.For("coai-readonly-prompt-");
        var prompts = new RolePrompts(data);
        prompts.Override(PromptId(role), "MY PROMPT: {{brief}} / {{taskFileBlock}} / {{repairTurns}} / {{unknownWord}}");

        var prompt = Build(role, prompts);

        prompt.Should().StartWith($"MY PROMPT: {Brief} / The question is about the file `{TaskFile}`").And.Contain(" / 2 / ");
        prompt.Should().Contain("{{unknownWord}}", "a person's typo is left as written, never a crash");
        prompt.Should().NotContain("COPIED from that line", "the shipped text is not mixed into an override");
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public void D1_AnEmptyOverride_IsNotAnOverride_TheShippedPromptStands(string role)
    {
        using var data = TempDir.For("coai-readonly-prompt-");
        var prompts = new RolePrompts(data);
        prompts.Override(PromptId(role), "  \n ");

        Build(role, prompts).Should().Contain("COPIED from that line");
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public void D1_TheOverrideIsOfItsOwnRole_TheOtherRolesPromptIsNotTouched(string role)
    {
        using var data = TempDir.For("coai-readonly-prompt-");
        var prompts = new RolePrompts(data);
        prompts.Override(PromptId(role), "ONLY MINE");

        Build(role == "scout" ? "judge" : "scout", prompts).Should().NotContain("ONLY MINE");
        File.Exists(prompts.FileToWrite(PromptId(role))).Should().BeTrue();
        PromptId(role).Should().Be(role, "the override file is prompts/<role>.md");
    }

    [Fact]
    public void D1_TheSchemaInThePrompt_IsTheSchemaFile()
    {
        using var data = TempDir.For("coai-readonly-prompt-");
        var prompts = new RolePrompts(data);

        Scout(prompts).Should().Contain(File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "scout-output.schema.json")).TrimEnd());
        Judge(prompts).Should().Contain(File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "judge-output.schema.json")).TrimEnd());
    }

    [Fact]
    public void D1_TheJudgePrompt_NamesTheRulesOfTheJudge_AndTheDecisionOnlyWhenAsked()
    {
        using var data = TempDir.For("coai-readonly-prompt-");

        var prompt = Build("judge", new RolePrompts(data));

        prompt.Should().Contain("`approve` cannot stand with a `blocking` finding").And.Contain("`revise` and `reject` need at least one finding").And.Contain("A finding id is used once");
        prompt.Should().Contain("Add `decision` ONLY when the question asks you to choose");
        prompt.Should().Contain("You do not write a document").And.Contain("You write NO file");
    }

    // ---------- d2: the numbers come from the tables of the readers ----------

    [Fact]
    public void D2_EveryNumberOfTheScoutPrompt_IsATableOfTheReader()
    {
        using var data = TempDir.For("coai-readonly-prompt-");
        var prompts = new RolePrompts(data);
        prompts.Override(ScoutPrompt.PromptId, "{{maxFacts}}|{{maxClaim}}|{{minEvidence}}|{{maxEvidence}}|{{maxText}}|{{maxFiles}}|{{maxUnknown}}|{{maxUnknownText}}|{{repairTurns}}");

        Scout(prompts).Should().Be($"{ScoutOutputReader.MaxFacts}|{ScoutOutputReader.MaxClaim}|{ScoutOutputReader.MinEvidence}|{ScoutOutputReader.MaxEvidence}|{RoleEvidence.MaxTextLength}|{ScoutOutputReader.MaxFiles}|{ScoutOutputReader.MaxUnknown}|{ScoutOutputReader.MaxUnknownText}|2");
    }

    [Fact]
    public void D2_EveryNumberOfTheJudgePrompt_IsATableOfTheReader()
    {
        using var data = TempDir.For("coai-readonly-prompt-");
        var prompts = new RolePrompts(data);
        prompts.Override(JudgePrompt.PromptId, "{{verdicts}}|{{severities}}|{{maxSummary}}|{{maxFindings}}|{{maxTitle}}|{{maxDetail}}|{{maxEvidence}}|{{maxFix}}|{{maxText}}|{{maxChosen}}|{{maxDecisionWhy}}|{{maxRejected}}|{{repairTurns}}");

        Judge(prompts).Should().Be(
            $"{string.Join(", ", JudgeOutputReader.Verdicts)}|{string.Join(", ", JudgeOutputReader.Severities)}|{JudgeOutputReader.MaxSummary}|{JudgeOutputReader.MaxFindings}|{JudgeOutputReader.MaxTitle}|{JudgeOutputReader.MaxDetail}|{JudgeOutputReader.MaxEvidence}"
            + $"|{JudgeOutputReader.MaxFix}|{RoleEvidence.MaxTextLength}|{JudgeOutputReader.MaxChosen}|{JudgeOutputReader.MaxDecisionWhy}|{JudgeOutputReader.MaxRejected}|2");
    }

    [Theory]
    [InlineData("scout.md")]
    [InlineData("judge.md")]
    public void D2_ThePromptFile_HoldsNoLimitNumber_SoAChangedLimitCanOnlyShowFromTheTable(string file)
    {
        var text = Regex.Replace(Shipped(file), "```json[\\s\\S]*?```", string.Empty);

        LimitNumber().Matches(text).Select(match => match.Value).Should().BeEmpty($"{file} names its limits by placeholder; a number typed into it would not follow the reader");
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public void D2_TheBuiltPrompt_ShowsTheLimitsOfTheReader(string role)
    {
        using var data = TempDir.For("coai-readonly-prompt-");

        var prompt = Build(role, new RolePrompts(data));

        prompt.Should().Contain($"at most {RoleEvidence.MaxTextLength} characters").And.Contain("at most 2 such turns");
        (role == "scout"
            ? new[] { $"at most {ScoutOutputReader.MaxFacts}", $"at most {ScoutOutputReader.MaxFiles} paths", $"at most {ScoutOutputReader.MaxUnknown} lines", $"from {ScoutOutputReader.MinEvidence} to {ScoutOutputReader.MaxEvidence} items" }
            : new[] { $"at most {JudgeOutputReader.MaxFindings}", $"at most {JudgeOutputReader.MaxSummary} characters", "blocking, major, minor, nit", "approve, revise, reject", $"at most {JudgeOutputReader.MaxRejected} options" })
            .ToList().ForEach(sentence => prompt.Should().Contain(sentence));
    }

    [Fact]
    public void D2_TheRepairPrompt_IsTheBAs_ItListsTheErrors_AndAsksForTheWholeObject()
    {
        BaPrompt.Repair(["facts[0].evidence[0].text: an evidence text is copied from the line"]).Should().Contain("facts[0].evidence[0].text").And.Contain("WHOLE JSON object");
    }
}
