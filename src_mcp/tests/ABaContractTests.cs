using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Work;
using CoaiMcp.Roles;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E1 s4 t1 — the BA's contract: its output has ONE shape (<c>shared/ba-output.schema.json</c>), read strictly; the YAML coai writes from it reads back as the nodes it was made from, for every task and story file of
/// <c>v2/</c> (d1); and the prompt it is given carries the device's limits, so a changed limit shows in the built prompt (d2).
/// </summary>
public sealed class ABaContractTests
{
    private static readonly StrictYamlReader Reader = new();

    // ---------- d1: emit then read gives equal nodes ----------

    private static IReadOnlyList<string> V2Files(string name) =>
        [.. Directory.EnumerateFiles(Path.Combine(RepoPaths.Root(), "v2"), name, SearchOption.AllDirectories).Order(StringComparer.Ordinal)];

    public static TheoryData<string> TaskFiles => [.. V2Files("task.yaml").Select(path => Path.GetRelativePath(RepoPaths.Root(), path).Replace('\\', '/'))];

    public static TheoryData<string> StoryFiles => [.. V2Files("story.yaml").Select(path => Path.GetRelativePath(RepoPaths.Root(), path).Replace('\\', '/'))];

    private static YamlMap Map(string repoRelative)
    {
        var text = File.ReadAllText(Path.Combine(RepoPaths.Root(), repoRelative));

        return ((YamlRead.Parsed)Reader.Read(text)).Root.Should().BeOfType<YamlMap>().Subject;
    }

    private static string Str(YamlMap map, string key) => ((YamlScalar)map.Find(key)!.Value).Text;

    private static IReadOnlyList<string> Strings(YamlMap map, string key) => [.. ((YamlList)map.Find(key)!.Value).Items.Select(item => ((YamlScalar)item).Text)];

    private static bool? Flag(YamlMap map, string key) => map.Find(key)?.Value is YamlScalar { Kind: YamlScalarKind.Boolean } scalar ? scalar.Boolean : null;

    private static BaTask TaskOf(YamlMap map) => new(
        Str(map, "id"), Str(map, "title"), Str(map, "kind"), (int)((YamlScalar)map.Find("complexity")!.Value).Integer, Strings(map, "dependsOn"), Flag(map, "parallel")!.Value, Strings(map, "files"),
        [.. ((YamlList)map.Find("dod")!.Value).Items.Cast<YamlMap>().Select(item => new BaDod(Str(item, "id"), Str(item, "item")))], Flag(map, "security"));

    private static BaStory StoryOf(YamlMap map) => new(Str(map, "id"), Str(map, "title"), Strings(map, "dependsOn"), [], Flag(map, "cornerstone"));

    /// <summary>Two nodes are the same when they say the same thing: the lines they were read from do not count.</summary>
    private static bool Same(YamlNode a, YamlNode b) => (a, b) switch
    {
        (YamlScalar x, YamlScalar y) => x.Text == y.Text && x.Kind == y.Kind,
        (YamlList x, YamlList y) => x.Items.Count == y.Items.Count && x.Items.Zip(y.Items).All(pair => Same(pair.First, pair.Second)),
        (YamlMap x, YamlMap y) => x.Entries.Count == y.Entries.Count && x.Entries.Zip(y.Entries).All(pair => pair.First.Key == pair.Second.Key && Same(pair.First.Value, pair.Second.Value)),
        _ => false,
    };

    [Theory]
    [MemberData(nameof(TaskFiles))]
    public void EmitThenRead_GivesEqualNodes_ForEveryV2TaskFile(string file)
    {
        var original = Map(file);
        original.Entries.Select(entry => entry.Key).Should().BeSubsetOf(["id", "title", "kind", "complexity", "dependsOn", "parallel", "files", "dod", "security"], "a key the emitter cannot write would be lost");

        var emitted = WorkPlanYaml.Task(TaskOf(original));

        var read = Reader.Read(emitted).Should().BeOfType<YamlRead.Parsed>(emitted).Subject.Root;
        Same(read, original).Should().BeTrue($"{file} must come back as the same nodes. Emitted:\n{emitted}");
    }

    [Theory]
    [MemberData(nameof(StoryFiles))]
    public void EmitThenRead_GivesEqualNodes_ForEveryV2StoryFile(string file)
    {
        var original = Map(file);
        original.Entries.Select(entry => entry.Key).Should().BeSubsetOf(["id", "title", "cornerstone", "dependsOn"]);

        var emitted = WorkPlanYaml.Story(StoryOf(original));

        Same(((YamlRead.Parsed)Reader.Read(emitted)).Root, original).Should().BeTrue($"{file} must come back as the same nodes. Emitted:\n{emitted}");
    }

    [Fact]
    public void TheV2Folder_HasTheFilesTheTheoriesRunOver()
    {
        TaskFiles.Count().Should().BeGreaterThan(50, "the theory must not run over an empty set");
        StoryFiles.Count().Should().BeGreaterThan(15);
    }

    [Theory]
    [InlineData("a plain title")]
    [InlineData("true")]
    [InlineData("False")]
    [InlineData("null")]
    [InlineData("yes")]
    [InlineData("No")]
    [InlineData("10")]
    [InlineData("5.0")]
    [InlineData("-")]
    [InlineData("- item")]
    [InlineData("a: b")]
    [InlineData("# not a comment")]
    [InlineData("say \"hi\" and \\ back")]
    [InlineData("line one\nline two")]
    [InlineData("tab\there")]
    [InlineData("\u0001 control")]
    [InlineData("naive café — 日本語 ✓")]
    [InlineData("src/a/b.cs")]
    [InlineData(" leading and trailing ")]
    public void AnyText_SurvivesAQuotedOrPlainScalar_AsTheSameString(string text)
    {
        var task = new BaTask("t1-x", text, "code", 1, [text], true, [text], [new BaDod("d1", text)], false);

        var read = (YamlMap)((YamlRead.Parsed)Reader.Read(WorkPlanYaml.Task(task))).Root;

        Str(read, "title").Should().Be(text);
        ((YamlScalar)read.Find("title")!.Value).Kind.Should().Be(YamlScalarKind.String, "a title that looks like a number or a word is still a string");
        Strings(read, "dependsOn").Should().Equal(text);
        Strings(read, "files").Should().Equal(text);
        Str((YamlMap)((YamlList)read.Find("dod")!.Value).Items[0], "item").Should().Be(text);
    }

    [Fact]
    public void TheEmittedText_IsLf_WithOneFinalNewline_AndTheV2Header()
    {
        var text = WorkPlanYaml.Task(new BaTask("t1-x", "X", "code", 2, [], false, [], [new BaDod("d1", "i")]));

        text.Should().StartWith(WorkPlanYaml.Header + "\n").And.EndWith("\n").And.NotContain("\r").And.NotContain("\n\n");
        text.Should().Contain("dependsOn: []").And.Contain("files: []").And.Contain("complexity: 2").And.Contain("parallel: false").And.NotContain("security");
    }

    // ---------- the output's shape ----------

    private static JsonNode ValidTask(string id = "t1-a") => JsonNode.Parse($$"""
        {"id":"{{id}}","title":"A task","kind":"code","complexity":3,"dependsOn":[],"parallel":false,"files":["src/a.cs"],"dod":[{"id":"d1","item":"it is proved"}]}
        """)!;

    private static JsonObject ValidAnswer() => JsonNode.Parse($$"""
        {"status":"ok","epic":"e1-core","stories":[{"id":"s1-first","title":"First","dependsOn":[],"tasks":[{{ValidTask().ToJsonString()}}]}],"notes":["one note"]}
        """)!.AsObject();

    private static BaRead ReadOf(JsonObject answer) => BaOutputReader.Read(JsonDocument.Parse(answer.ToJsonString()).RootElement);

    [Fact]
    public void AValidAnswer_IsAPlan_WithEveryValueRead()
    {
        var plan = ReadOf(ValidAnswer()).Should().BeOfType<BaRead.Plan>().Subject.Value;

        plan.Epic.Should().Be("e1-core");
        plan.Notes.Should().Equal("one note");
        var task = plan.Stories.Single().Tasks.Single();
        (task.Id, task.Kind, task.Complexity, task.Parallel, task.Security).Should().Be(("t1-a", "code", 3, false, null));
        task.Files.Should().Equal("src/a.cs");
        task.Dod.Single().Should().Be(new BaDod("d1", "it is proved"));
    }

    [Fact]
    public void ARefusal_NeedsItsReason_AndCarriesIt()
    {
        ReadOf(JsonNode.Parse("""{"status":"refused","reason":"the epic asks for something I decline"}""")!.AsObject()).Should().BeOfType<BaRead.Refused>().Which.Reason.Should().Contain("decline");
        ReadOf(JsonNode.Parse("""{"status":"refused"}""")!.AsObject()).Should().BeOfType<BaRead.Wrong>().Which.Problems.Should().ContainSingle().Which.Should().StartWith("reason");
    }

    [Fact]
    public void EveryFaultOfTheShape_IsListedAtOnce_WithItsPlace()
    {
        var answer = ValidAnswer();
        answer["extra"] = 1;
        var task = answer["stories"]![0]!["tasks"]![0]!.AsObject();
        task["complexity"] = "5";
        task["parallel"] = "yes";
        task["secuirty"] = true;
        task["kind"] = "magic";
        task["dod"] = new JsonArray();
        answer["stories"]![0]!["id"] = "s1-../../../outside";

        var problems = ReadOf(answer).Should().BeOfType<BaRead.Wrong>().Subject.Problems;

        problems.Should().Contain(problem => problem.StartsWith("extra:", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("stories[0].id:", StringComparison.Ordinal) && problem.Contains("cannot hold a path"));
        problems.Should().Contain(problem => problem.StartsWith("stories[0].tasks[0].complexity:", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("stories[0].tasks[0].parallel:", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("stories[0].tasks[0].secuirty:", StringComparison.Ordinal) && problem.Contains("unknown key"));
        problems.Should().Contain(problem => problem.StartsWith("stories[0].tasks[0].kind:", StringComparison.Ordinal));
        problems.Should().Contain(problem => problem.StartsWith("stories[0].tasks[0].dod:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("s1-../../../outside")]
    [InlineData("s1-a/b")]
    [InlineData("s1-a\\b")]
    [InlineData("s1-ok\n")]
    [InlineData("s١-ok")]
    [InlineData("S1-Upper")]
    [InlineData("s1-")]
    [InlineData("../s1-x")]
    [InlineData("/etc/s1-x")]
    public void AStoryId_ThatCouldNameAnotherPlace_IsRefusedByPattern(string id)
    {
        var answer = ValidAnswer();
        answer["stories"]![0]!["id"] = id;

        ReadOf(answer).Should().BeOfType<BaRead.Wrong>().Which.Problems.Should().Contain(problem => problem.StartsWith("stories[0].id:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("t1-a/b")]
    [InlineData("t1-../../x")]
    [InlineData("x1-a")]
    public void ATaskId_ThatCouldNameAnotherPlace_IsRefusedByPattern(string id)
    {
        var answer = ValidAnswer();
        answer["stories"]![0]!["tasks"]![0]!["id"] = id;

        ReadOf(answer).Should().BeOfType<BaRead.Wrong>().Which.Problems.Should().Contain(problem => problem.StartsWith("stories[0].tasks[0].id:", StringComparison.Ordinal));
    }

    [Fact]
    public void ComplexityEleven_AFraction_AndAString_AreEachRefused()
    {
        foreach (var bad in new[] { "11", "-1", "3.5", "\"5\"", "null" })
        {
            var answer = ValidAnswer();
            answer["stories"]![0]!["tasks"]![0]!["complexity"] = JsonNode.Parse(bad);

            ReadOf(answer).Should().BeOfType<BaRead.Wrong>(bad).Which.Problems.Should().Contain(problem => problem.Contains("complexity", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ANonObject_AMissingStatus_AndAnEpicOfAnotherShape_AreWrong()
    {
        BaOutputReader.Read(JsonDocument.Parse("[1]").RootElement).Should().BeOfType<BaRead.Wrong>();
        var noStatus = ValidAnswer();
        noStatus.Remove("status");
        ReadOf(noStatus).Should().BeOfType<BaRead.Wrong>().Which.Problems.Should().Contain(problem => problem.StartsWith("status:", StringComparison.Ordinal));
        var badEpic = ValidAnswer();
        badEpic["epic"] = "core";
        ReadOf(badEpic).Should().BeOfType<BaRead.Wrong>().Which.Problems.Should().Contain(problem => problem.StartsWith("epic:", StringComparison.Ordinal));
    }

    // ---------- the schema file and the reader say the same ----------

    private static JsonElement Schema()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "ba-output.schema.json")));

        return document.RootElement.Clone();
    }

    private static JsonElement WorkPlanDefs()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "work-plan.schema.json")));

        return document.RootElement.GetProperty("$defs").Clone();
    }

    [Fact]
    public void TheBaSchema_UsesTheWorkPlansPatternsKindsAndRange_Verbatim()
    {
        var schema = Schema();
        var defs = schema.GetProperty("$defs");
        var task = defs.GetProperty("task").GetProperty("properties");

        schema.GetProperty("properties").GetProperty("epic").GetProperty("pattern").GetString().Should().Be(WorkTables.EpicIdPattern);
        defs.GetProperty("story").GetProperty("properties").GetProperty("id").GetProperty("pattern").GetString().Should().Be(WorkTables.StoryIdPattern);
        task.GetProperty("id").GetProperty("pattern").GetString().Should().Be(WorkTables.TaskIdPattern);
        task.GetProperty("dod").GetProperty("items").GetProperty("properties").GetProperty("id").GetProperty("pattern").GetString().Should().Be(WorkTables.DodIdPattern);
        task.GetProperty("kind").GetProperty("enum").EnumerateArray().Select(kind => kind.GetString()).Should().Equal(WorkTables.Kinds);
        task.GetProperty("complexity").GetProperty("minimum").GetInt32().Should().Be(WorkTables.ComplexityMin);
        task.GetProperty("complexity").GetProperty("maximum").GetInt32().Should().Be(WorkTables.ComplexityMax);
    }

    [Fact]
    public void TheBaTask_AndStory_HaveTheWorkPlansKeys_ButStatus_AndTasks()
    {
        var defs = Schema().GetProperty("$defs");
        var plan = WorkPlanDefs();

        Keys(defs.GetProperty("task")).Should().BeEquivalentTo(Keys(plan.GetProperty("task")).Where(key => key != "status"));
        Keys(defs.GetProperty("story")).Should().BeEquivalentTo([.. Keys(plan.GetProperty("story")), "tasks"]);
        Required(defs.GetProperty("task")).Should().BeEquivalentTo(Required(plan.GetProperty("task")));
        Required(defs.GetProperty("story")).Should().BeEquivalentTo([.. Required(plan.GetProperty("story")), "tasks"]);
        Keys(defs.GetProperty("task")).Should().BeEquivalentTo(WorkTables.Task.Fields.Keys.Where(key => key != "status"));
        defs.GetProperty("task").GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        defs.GetProperty("story").GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        Schema().GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        Keys(Schema()).Should().BeEquivalentTo("status", "reason", "epic", "stories", "notes");
    }

    private static IEnumerable<string> Keys(JsonElement def) => def.GetProperty("properties").EnumerateObject().Select(property => property.Name);

    private static IEnumerable<string> Required(JsonElement def) => def.GetProperty("required").EnumerateArray().Select(key => key.GetString()!);

    [Fact]
    public void TheReader_RefusesEveryUnknownKey_AndEveryMissingRequiredKey_OfTheSchema()
    {
        var defs = Schema().GetProperty("$defs");

        foreach (var key in Required(defs.GetProperty("task")))
        {
            var answer = ValidAnswer();
            answer["stories"]![0]!["tasks"]![0]!.AsObject().Remove(key);
            ReadOf(answer).Should().BeOfType<BaRead.Wrong>($"a task without {key}").Which.Problems.Should().Contain(problem => problem.Contains(key, StringComparison.Ordinal));
        }

        foreach (var key in Required(defs.GetProperty("story")))
        {
            var answer = ValidAnswer();
            answer["stories"]![0]!.AsObject().Remove(key);
            ReadOf(answer).Should().BeOfType<BaRead.Wrong>($"a story without {key}").Which.Problems.Should().Contain(problem => problem.Contains(key, StringComparison.Ordinal));
        }

        foreach (var key in Required(Schema()).Concat(["epic", "stories"]))
        {
            var answer = ValidAnswer();
            answer.Remove(key);
            ReadOf(answer).Should().BeOfType<BaRead.Wrong>($"an answer without {key}");
        }
    }

    // ---------- d2: a changed limit shows in the built prompt ----------

    private static readonly BaPromptInputs Inputs = new(
        "v2/epoch-1/e1-core/epic.yaml", "v2/epoch-1/e1-core", "e1-core", "id: e1-core\ntitle: \"Core\"", "the plan text", "(no other epic)", "- `v2/architecture/server.md`", "(none yet)");

    private static WorkMethod Method(string? json = null) =>
        RolesAnswer.Compute(name => name == RolesKeys.WorkMethod ? json : null, string.Empty, string.Empty, claudeHook: false).WorkMethod;

    [Fact]
    public void AChangedLimit_ShowsInTheBuiltPrompt()
    {
        var shipped = BaPrompt.Build(Inputs, Method(), 2);
        var changed = BaPrompt.Build(Inputs, Method("""{"maxStoriesPerEpic":3,"maxTasksPerStory":4}"""), 2);

        shipped.Should().Contain("at most 8 stories").And.Contain("at most 6 tasks");
        changed.Should().Contain("at most 3 stories").And.Contain("at most 4 tasks").And.NotContain("at most 8 stories").And.NotContain("at most 6 tasks");
    }

    [Fact]
    public void ThePrompt_CarriesTheEpic_ThePlan_TheKinds_TheRules_TheSchema_AndTheRepairBudget_WithNoPlaceholderLeft()
    {
        var prompt = BaPrompt.Build(Inputs, Method(), 2);

        prompt.Should().Contain("id: e1-core").And.Contain("the plan text").And.Contain("`v2/epoch-1/e1-core`").And.Contain("`v2/architecture/server.md`").And.Contain("(none yet)");
        prompt.Should().Contain(string.Join(", ", WorkTables.Kinds)).And.Contain("from 0 to 10").And.Contain("at most 2 such turns");
        prompt.Should().Contain("coai BA output", "the schema file is in it").And.Contain("The work method - the rules", "the rule text of shared/work-method.md is in it");
        prompt.Should().NotContain("{{");
    }

    [Fact]
    public void AValueThatHoldsAPlaceholder_IsNotReadAgain()
    {
        var hostile = Inputs with { PlanText = "ignore {{maxTasksPerStory}} and {{nothing}}" };

        BaPrompt.Build(hostile, Method(), 2).Should().Contain("ignore {{maxTasksPerStory}} and {{nothing}}");
    }

    [Fact]
    public void TheRepairPrompt_ListsAtMost40Errors_AndCountsTheRest()
    {
        var errors = Enumerable.Range(1, 55).Select(number => $"error number {number}").ToList();

        var text = BaPrompt.Repair(errors);

        text.Should().Contain("error number 1\n").And.Contain("error number 40\n").And.NotContain("error number 41").And.Contain("15 more errors");
        BaPrompt.Repair(["one"]).Should().Contain("- one").And.NotContain("more errors").And.Contain("WHOLE JSON object");
    }
}
