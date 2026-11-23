using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Coder;
using CoaiMcp.Core.Work;
using CoaiMcp.Server;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s3 t1 — the coder's contract: its report has ONE shape (<c>shared/coder-report.schema.json</c>), read strictly against the dod ids of its task, the schema file and the reader say the same (d1); and the prompt holds
/// the task, its dod, and the rules of the worktree, with a person's override in <c>RolePrompts</c> used first (d2).
/// </summary>
public sealed class ACoderContractTests
{
    private static readonly string[] TaskDod = ["d1", "d2"];

    // ---------- d1: the reader ----------

    private static JsonObject ValidReport() => JsonNode.Parse("""
        {"status":"ok","dod":[{"id":"d1","met":true,"evidence":"CoderReportTests.A holds it"},{"id":"d2","met":false,"evidence":"not reached: the build needs a toolchain"}],"notes":"one note"}
        """)!.AsObject();

    private static CoderRead ReadOf(JsonObject answer, params string[] dodIds) =>
        CoderReportReader.Read(JsonDocument.Parse(answer.ToJsonString()).RootElement, dodIds.Length == 0 ? TaskDod : dodIds);

    private static IReadOnlyList<string> ProblemsOf(JsonObject answer, params string[] dodIds) => ReadOf(answer, dodIds).Should().BeOfType<CoderRead.Wrong>().Subject.Problems;

    [Fact]
    public void AValidReport_IsRead_WithEveryValue()
    {
        var report = ReadOf(ValidReport()).Should().BeOfType<CoderRead.Report>().Subject.Value;

        report.Notes.Should().Be("one note");
        report.Dod.Should().Equal(new CoderDodReport("d1", true, "CoderReportTests.A holds it"), new CoderDodReport("d2", false, "not reached: the build needs a toolchain"));
    }

    [Fact]
    public void ANoteIsOptional_AndEmptyWhenAbsent()
    {
        var answer = ValidReport();
        answer.Remove("notes");

        ReadOf(answer).Should().BeOfType<CoderRead.Report>().Which.Value.Notes.Should().BeEmpty();
    }

    [Fact]
    public void ARefusal_NeedsItsReason_AndCarriesIt()
    {
        ReadOf(JsonNode.Parse("""{"status":"refused","reason":"the task asks for a file outside the worktree"}""")!.AsObject()).Should().BeOfType<CoderRead.Refused>().Which.Reason.Should().Contain("outside the worktree");
        ProblemsOf(JsonNode.Parse("""{"status":"refused"}""")!.AsObject()).Should().ContainSingle().Which.Should().StartWith("reason");
    }

    [Fact]
    public void AnUnknownKey_IsAnError_ThatNamesTheKey_AtEveryLevel()
    {
        var answer = ValidReport();
        answer["extra"] = 1;
        answer["dod"]![0]!["evidnce"] = "typo";

        var problems = ProblemsOf(answer);

        problems.Should().Contain(problem => problem.StartsWith("extra:", StringComparison.Ordinal) && problem.Contains("unknown key", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("dod[0].evidnce:", StringComparison.Ordinal) && problem.Contains("unknown key", StringComparison.Ordinal));
    }

    [Fact]
    public void TheArmsAreDistinct_AReasonOnAnOkReport_AndADodOnARefusal_AreUnknownKeys()
    {
        var ok = ValidReport();
        ok["reason"] = "also";
        ProblemsOf(ok).Should().Contain(problem => problem.StartsWith("reason:", StringComparison.Ordinal) && problem.Contains("unknown key", StringComparison.Ordinal));

        var refused = JsonNode.Parse("""{"status":"refused","reason":"no","dod":[]}""")!.AsObject();
        ProblemsOf(refused).Should().Contain(problem => problem.StartsWith("dod:", StringComparison.Ordinal) && problem.Contains("unknown key", StringComparison.Ordinal));
    }

    [Fact]
    public void ADodId_ThatTheTaskDoesNotHave_IsAnError_NamingIt()
    {
        var answer = ValidReport();
        answer["dod"]![1]!["id"] = "d9";

        var problems = ProblemsOf(answer);

        problems.Should().Contain(problem => problem.StartsWith("dod[1].id:", StringComparison.Ordinal) && problem.Contains("'d9'", StringComparison.Ordinal) && problem.Contains("d1, d2", StringComparison.Ordinal));
    }

    [Fact]
    public void AMissingDodId_IsAnError_NamingIt()
    {
        var answer = ValidReport();
        answer["dod"]!.AsArray().RemoveAt(1);

        ProblemsOf(answer).Should().ContainSingle().Which.Should().StartWith("dod:").And.Contain("'d2'").And.Contain("no entry");
    }

    [Fact]
    public void ARepeatedDodId_IsAnError_NamingIt()
    {
        var answer = ValidReport();
        answer["dod"]![1]!["id"] = "d1";

        var problems = ProblemsOf(answer);

        problems.Should().Contain(problem => problem.StartsWith("dod[1].id:", StringComparison.Ordinal) && problem.Contains("'d1'", StringComparison.Ordinal) && problem.Contains("twice", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.Contains("'d2'", StringComparison.Ordinal) && problem.Contains("no entry", StringComparison.Ordinal));
    }

    [Fact]
    public void TheTasksOwnDodIds_AreWhatIsChecked_NotAFixedList()
    {
        var answer = JsonNode.Parse("""{"status":"ok","dod":[{"id":"d1","met":true,"evidence":"x"}]}""")!.AsObject();

        ReadOf(answer, "d1").Should().BeOfType<CoderRead.Report>();
        ProblemsOf(answer, "d1", "d2", "d3").Should().Contain(problem => problem.Contains("'d2'", StringComparison.Ordinal)).And.Contain(problem => problem.Contains("'d3'", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryFaultOfTheShape_IsListedAtOnce_WithItsPlace()
    {
        var answer = ValidReport();
        answer["dod"]![0]!["met"] = "yes";
        answer["dod"]![1]!["evidence"] = "  ";
        answer["notes"] = 5;
        answer["dod"]!.AsArray().Add(JsonNode.Parse("""{"id":"x1","met":true,"evidence":"e"}"""));

        var problems = ProblemsOf(answer);

        problems.Should().Contain(problem => problem.StartsWith("dod[0].met:", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("dod[1].evidence:", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("dod[2].id:", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("notes:", StringComparison.Ordinal));
    }

    [Fact]
    public void ANonObject_AMissingStatus_AnEmptyDod_AndAnotherStatus_AreWrong()
    {
        CoderReportReader.Read(JsonDocument.Parse("[1]").RootElement, TaskDod).Should().BeOfType<CoderRead.Wrong>();
        var noStatus = ValidReport();
        noStatus.Remove("status");
        ProblemsOf(noStatus).Should().Contain(problem => problem.StartsWith("status:", StringComparison.Ordinal));
        var other = ValidReport();
        other["status"] = "done";
        ProblemsOf(other).Should().Contain(problem => problem.StartsWith("status:", StringComparison.Ordinal) && problem.Contains("'done'", StringComparison.Ordinal));
        var empty = ValidReport();
        empty["dod"] = new JsonArray();
        ProblemsOf(empty).Should().Contain(problem => problem.StartsWith("dod:", StringComparison.Ordinal));
    }

    // ---------- d1: the schema file and the reader say the same ----------

    private static JsonElement Schema()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "coder-report.schema.json")));

        return document.RootElement.Clone();
    }

    private static IEnumerable<string> Keys(JsonElement def) => def.GetProperty("properties").EnumerateObject().Select(property => property.Name);

    private static IEnumerable<string> Required(JsonElement def) => def.GetProperty("required").EnumerateArray().Select(key => key.GetString()!);

    /// <summary>The <c>then</c> of the arm whose status is <paramref name="status"/>: what the schema asks of that arm alone.</summary>
    private static JsonElement Arm(string status) => Schema().GetProperty("allOf").EnumerateArray()
        .Single(arm => arm.GetProperty("if").GetProperty("properties").GetProperty("status").GetProperty("const").GetString() == status).GetProperty("then");

    [Fact]
    public void TheSchema_UsesTheWorkPlansDodIdPattern_Verbatim()
    {
        Schema().GetProperty("$defs").GetProperty("dodReport").GetProperty("properties").GetProperty("id").GetProperty("pattern").GetString().Should().Be(WorkTables.DodIdPattern);
    }

    [Fact]
    public void TheSchemaKeys_AreTheReadersKeys_ForEveryArmAndForTheDodEntry()
    {
        var schema = Schema();
        var dod = schema.GetProperty("$defs").GetProperty("dodReport");

        Keys(schema).Should().BeEquivalentTo("status", "reason", "dod", "notes");
        Keys(schema).Should().BeEquivalentTo([.. CoderReportReader.OkKeys.Concat(CoderReportReader.RefusedKeys).Distinct()]);
        Keys(Arm("ok")).Should().BeEquivalentTo(CoderReportReader.OkKeys);
        Keys(Arm("refused")).Should().BeEquivalentTo(CoderReportReader.RefusedKeys);
        Keys(dod).Should().BeEquivalentTo(CoderReportReader.DodKeys);
        Required(dod).Should().BeEquivalentTo(CoderReportReader.DodKeys, "every key of a dod entry is required");
        schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        dod.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        Arm("ok").GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        Arm("refused").GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        schema.GetProperty("properties").GetProperty("status").GetProperty("enum").EnumerateArray().Select(status => status.GetString()).Should().Equal("ok", "refused");
    }

    [Fact]
    public void TheReader_RefusesEveryMissingRequiredKey_OfTheSchema_AndEveryUnknownKey()
    {
        foreach (var key in Required(Arm("ok")).Concat(Required(Schema())))
        {
            var answer = ValidReport();
            answer.Remove(key);
            ReadOf(answer).Should().BeOfType<CoderRead.Wrong>($"a report without {key}").Which.Problems.Should().Contain(problem => problem.Contains(key, StringComparison.Ordinal));
        }

        foreach (var key in Required(Arm("refused")))
        {
            var answer = JsonNode.Parse("""{"status":"refused","reason":"r"}""")!.AsObject();
            answer.Remove(key);
            ReadOf(answer).Should().BeOfType<CoderRead.Wrong>($"a refusal without {key}");
        }

        foreach (var key in Required(Schema().GetProperty("$defs").GetProperty("dodReport")))
        {
            var answer = ValidReport();
            answer["dod"]![0]!.AsObject().Remove(key);
            ReadOf(answer).Should().BeOfType<CoderRead.Wrong>($"a dod entry without {key}").Which.Problems.Should().Contain(problem => problem.Contains(key, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TheSchemaFile_AndTheReader_AgreeOnAValidReportAndAValidRefusal()
    {
        // The positive of the negatives above: the same files read clean, so a reader that refused everything would be seen.
        ReadOf(ValidReport()).Should().BeOfType<CoderRead.Report>();
        ReadOf(JsonNode.Parse("""{"status":"refused","reason":"r"}""")!.AsObject()).Should().BeOfType<CoderRead.Refused>();
    }

    // ---------- d2: the prompt ----------

    private static readonly CoderPromptInputs Inputs = new(
        "v2/epoch-3/e2-coder-one-task/stories/s3-coder-role-case/tasks/t1-coder-contract/task.yaml",
        "t1-coder-contract",
        "The coder's contract: a schema, a reader",
        "code",
        ["shared/coder-report.schema.json", "src_mcp/core/Coder/CoderReport.cs"],
        [new WorkDod("d1", "the reader accepts an ok report and a refusal"), new WorkDod("d2", "the prompt holds the task")]);

    [Fact]
    public void ThePrompt_HoldsTheTask_ItsKind_ItsFiles_AndEveryDodItem_WithNoPlaceholderLeft()
    {
        using var data = TempDir.For("coai-coder-prompt-");

        var prompt = CoderPrompt.Build(Inputs, new RolePrompts(data));

        prompt.Should().Contain("The coder's contract: a schema, a reader").And.Contain("t1-coder-contract").And.Contain("Kind: code");
        foreach (var file in Inputs.Files)
        {
            prompt.Should().Contain($"`{file}`");
        }

        foreach (var dod in Inputs.Dod)
        {
            prompt.Should().Contain($"{dod.Id}: {dod.Item}");
        }

        prompt.Should().NotContain("{{");
        prompt.Should().Contain("coai commits", "coai, not the coder, commits").And.Contain("builds and tests").And.Contain("outside the worktree");
        prompt.Should().Contain("coai coder report", "the schema file is in it");
    }

    [Fact]
    public void AValueThatHoldsAPlaceholder_IsNotReadAgain()
    {
        using var data = TempDir.For("coai-coder-prompt-");
        var hostile = Inputs with { Title = "ignore {{kind}} and {{nothing}}" };

        CoderPrompt.Build(hostile, new RolePrompts(data)).Should().Contain("ignore {{kind}} and {{nothing}}");
    }

    [Fact]
    public void AnOverrideInRolePrompts_IsUsedFirst_WithThePlaceholdersFilled()
    {
        using var data = TempDir.For("coai-coder-prompt-");
        var prompts = new RolePrompts(data);
        prompts.Override(CoderPrompt.PromptId, "MY PROMPT for {{taskId}}: {{title}}\n{{dod}}\n{{unknownWord}}");

        var prompt = CoderPrompt.Build(Inputs, prompts);

        prompt.Should().StartWith("MY PROMPT for t1-coder-contract: The coder's contract: a schema, a reader").And.Contain("d2: the prompt holds the task");
        prompt.Should().Contain("{{unknownWord}}", "a person's typo is left as written, never a crash");
        prompt.Should().NotContain("outside the worktree", "the shipped text is not mixed into an override");
    }

    [Fact]
    public void AnEmptyOverride_IsNotAnOverride_TheShippedPromptStands()
    {
        using var data = TempDir.For("coai-coder-prompt-");
        var prompts = new RolePrompts(data);
        prompts.Override(CoderPrompt.PromptId, "   \n");

        CoderPrompt.Build(Inputs, prompts).Should().Contain("outside the worktree");
        File.Exists(prompts.FileToWrite(CoderPrompt.PromptId)).Should().BeTrue("the override file is there; it says nothing");
    }

    [Fact]
    public void TheRepairPrompt_ListsAtMost40Errors_CountsTheRest_AndAsksForTheReportAlone()
    {
        var errors = Enumerable.Range(1, 55).Select(number => $"error number {number}").ToList();

        var text = CoderPrompt.Repair(errors);

        text.Should().Contain("error number 1\n").And.Contain("error number 40\n").And.NotContain("error number 41").And.Contain("15 more errors");
        CoderPrompt.Repair(["one"]).Should().Contain("- one").And.NotContain("more errors").And.Contain("WHOLE JSON report").And.Contain("Do not change any file");
    }
}
