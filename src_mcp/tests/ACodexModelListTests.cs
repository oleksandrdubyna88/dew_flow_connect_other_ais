using System.Text.Json;
using CoaiMcp.Core.Roles;
using CoaiMcp.Runners.Consultation;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s2 t5 (V30, V31, V34; <c>SPEC_CHANGES.md</c> section Q): the pure parts — the shape of a codex model name, the choice of the newest model of a family from a list, the strict reading of the list, the names of a tool list that are shown. Each row of the rule is one case here;
/// the job that asks the installed CLI is in <see cref="ACodexModelNameIsResolvedAtJobStartTests"/>.
/// </summary>
public sealed class ACodexModelListTests
{
    private static readonly string[] Families = ["terra", "astra", "sol", "luna"];

    private static CodexListedModel Listed(string slug, string visibility = "list") => new(slug, visibility);

    [Theory]
    [InlineData("gpt-6.3-terra", true)]
    [InlineData("gpt-7-astra", true)]
    [InlineData("gpt-6-luna", true)]
    [InlineData("gpt-5.6-terra", true)]
    [InlineData("gpt-6.10-sol", true)]
    [InlineData("gpt-6.3-tera", false)]
    [InlineData("gpt-x-terra", false)]
    [InlineData("GPT-6.1-terra", false)]
    [InlineData("gpt-6.1-terra-preview", false)]
    [InlineData("gpt-terra-latest", false)]
    [InlineData("gpt-6.1-terra ", false)]
    [InlineData("gpt-6. 1-terra", false)]
    [InlineData("gpt-6..1-terra", false)]
    [InlineData("gpt-6.-terra", false)]
    [InlineData("gpt--terra", false)]
    [InlineData("gpt-6.1-Terra", false)]
    [InlineData("gpt-6.1-terra\n", false)]
    [InlineData("gpt-1234567890-terra", false)]
    [InlineData("gpt-06-terra", false)]
    [InlineData("gpt-6.01-terra", false)]
    [InlineData("gpt-6.1.00-terra", false)]
    [InlineData("gpt-0-terra", true)]
    [InlineData("gpt-6.0-terra", true)]
    [InlineData("terra", false)]
    [InlineData("", false)]
    public void TheExactShape_IsGptVersionFamily_AndNothingElseHasIt(string name, bool shaped) =>
        CodexModelShape.Has(name, Families).Should().Be(shaped);

    [Theory]
    [InlineData("7", "6.2", 1)]
    [InlineData("6.10", "6.9", 1)]
    [InlineData("6.1", "6", 1)]
    [InlineData("6", "6.0", 0)]
    [InlineData("6.0.1", "6", 1)]
    [InlineData("5.6", "6.1", -1)]
    public void VersionsAreComparedNumberByNumber_AMissingNumberIsZero(string left, string right, int sign)
    {
        IReadOnlyList<int> Of(string version) => [.. version.Split('.').Select(int.Parse)];

        Math.Sign(CodexModelShape.Compare(Of(left), Of(right))).Should().Be(sign);
    }

    [Fact]
    public void TheNewestOfAFamily_IsTheHighestVersionOfTheExactShapeWithVisibilityList()
    {
        CodexModelList.Newest([Listed("gpt-5.6-terra"), Listed("gpt-6.1-terra"), Listed("gpt-6-terra")], "terra", Families).Should().Be("gpt-6.1-terra");
        CodexModelList.Newest([Listed("gpt-6.2-terra"), Listed("gpt-7-terra")], "terra", Families).Should().Be("gpt-7-terra", "7 is newer than 6.2");
        CodexModelList.Newest([Listed("gpt-6.9-terra"), Listed("gpt-6.10-terra")], "terra", Families).Should().Be("gpt-6.10-terra", "6.10 is newer than 6.9");
        CodexModelList.Newest([Listed("gpt-6-terra"), Listed("gpt-6.1-terra")], "terra", Families).Should().Be("gpt-6.1-terra", "6.1 is newer than 6");
        CodexModelList.Newest([Listed("gpt-6.2-astra"), Listed("gpt-6.1-terra")], "astra", Families).Should().Be("gpt-6.2-astra", "only the family asked for");
    }

    [Fact]
    public void OfTwoEqualVersions_TheOneListedFirstIsTaken()
    {
        CodexModelList.Newest([Listed("gpt-6.1-terra"), Listed("gpt-6.1.0-terra")], "terra", Families).Should().Be("gpt-6.1-terra");
        CodexModelList.Newest([Listed("gpt-6.1.0-terra"), Listed("gpt-6.1-terra")], "terra", Families).Should().Be("gpt-6.1.0-terra");
    }

    [Fact]
    public void AHiddenName_AndANameWithOtherWords_AreNeverTaken_SoTheTextAfterDashMAlwaysHasTheShape()
    {
        CodexModelList.Newest([Listed("gpt-9-terra", "hide"), Listed("gpt-8-terra-preview"), Listed("gpt-terra-latest"), Listed("gpt-6.1-terra")], "terra", Families).Should().Be("gpt-6.1-terra");
        CodexModelList.Newest([Listed("gpt-9-terra", "hide"), Listed("gpt-terra-latest")], "terra", Families).Should().BeNull("a family with no model of the exact shape and visibility list has no model");
        CodexModelList.Newest([], "terra", Families).Should().BeNull();
    }

    [Fact]
    public void TheList_IsReadOnlyWhenItIsOneJsonObjectWithModelsThatHaveASlug_AndWithinTheLimit()
    {
        CodexModelList.Read("""{"models":[{"slug":"gpt-6.1-terra","visibility":"list","extra":1},{"slug":"codex-auto-review","visibility":"hide"}]}""")!.Select(model => (model.Slug, model.Visibility)).Should().Equal(("gpt-6.1-terra", "list"), ("codex-auto-review", "hide"));
        CodexModelList.Read("""{"models":[]}""").Should().BeEmpty();
        foreach (var notAList in new[] { string.Empty, "this is not a list", "[]", """{"models":{}}""", """{"models":[{"visibility":"list"}]}""", """{"models":[{"slug":5}]}""", """{"other":[]}""", """{"models":[""" })
        {
            CodexModelList.Read(notAList).Should().BeNull(notAList);
        }

        CodexModelList.Read("{\"models\":[" + new string(' ', CodexModelList.MostChars) + "]}").Should().BeNull("over the size limit");
    }

    [Fact]
    public void TheRecordingOfTheRealList_ResolvesTheDefaultsToTheNewestListedModelOfTheirFamily()
    {
        var fixture = RoleStopFixtures.Load("codex-debug-models");
        var list = CodexModelList.Read(fixture.StdOut)!;

        list.Should().NotBeEmpty();
        list.Should().Contain(model => model.Slug == "gpt-6.1-terra");
        CodexModelList.Newest(list, "terra", Families).Should().Be("gpt-6.1-terra");
        CodexModelList.Newest(list, "astra", Families).Should().Be("gpt-6.2-astra");
    }

    [Fact]
    public void TheFileHoldsTheFamilies_AsData() =>
        RoleModelNames.Current.CodexFamilies.Should().Equal("terra", "astra", "sol", "luna");

    [Fact]
    public void TheImportantList_AFamilyOnTheList_AndTheShapedNameOfIt_TheRefusalSaysToNameTheFamilyOrChangeTheList()
    {
        var table = new RouteTable([("important", [new ModelRef("astra", "codex")]), ("ba", [new ModelRef("opus", "claude")])], []);

        var refused = RoleRouter.Route(new RouteRequest("ba", "gpt-6.2-astra", "feature", 8, true), table).Should().BeOfType<RouteResult.Refused>().Subject;

        refused.Reason.Should().Contain("name 'astra' or change the list").And.Contain("lists are compared by the name as written");
    }

    // ---------- V34: the names of a tool list that may be shown ----------

    [Fact]
    public void ANameIsShownOnlyWhenItIsOneToSixtyFourSafeCharacters_AnyOtherNameIsSaidWithItsLength()
    {
        string[] outside = ["collaboration.spawn_agent", "bad\nname", "control\u0001char", "sk-ant-oat01-abcdefghij/klmnop+qrstuv==", new string('a', 300), new string('b', 64), new string('c', 65)];

        var shown = CodexToolList.Shown(outside);

        shown.Should().Contain("collaboration.spawn_agent").And.Contain("a name that cannot be shown (8 characters)").And.Contain("a name that cannot be shown (12 characters)").And.Contain("a name that cannot be shown (39 characters)").And.Contain("a name that cannot be shown (300 characters)");
        shown.Should().NotContain("\n").And.NotContain("\u0001").And.NotContain("sk-ant").And.NotContain(new string('a', 300));
        shown.Should().Contain("and 2 more (7 in all)").And.NotContain(new string('b', 64), "at most five names are shown");
        CodexToolList.CanBeShown(new string('b', 64)).Should().BeTrue();
        CodexToolList.CanBeShown(new string('c', 65)).Should().BeFalse();
        CodexToolList.CanBeShown(string.Empty).Should().BeFalse();
    }

    private static CodexToolList.ToolRead Whole(IEnumerable<string> names) => new([.. names], true);

    [Fact]
    public void TheJudgement_PassesFewerNames_AndRefusesAnOutsideName_TwoReadsThatDiffer_AndAnEmptyList()
    {
        var all = CodexToolList.Known;
        CodexToolList.Judge(Whole(all), Whole(all)).ToolList.Should().BeEmpty();
        CodexToolList.Judge(Whole(all.Take(9)), Whole(all.Take(9))).Should().Match<CodexToolList.Judgement>(j => j.ToolList.Length == 0 && j.NoProjectTools.Length == 0 && j.Absent.Count == 2 && j.Reason.Length == 0);
        CodexToolList.Judge(Whole(all.Take(1)), Whole(all.Take(1))).Absent.Should().HaveCount(10);
        var outside = CodexToolList.Judge(Whole([.. all, "collaboration.spawn_agent"]), Whole(all));
        outside.ToolList.Should().Contain("1 name(s) outside the known set").And.NotContain("collaboration.spawn_agent", "the two reads differ: a count and no name (A4-M2)");
        outside.Reason.Should().Be("unknown-tool");
        outside.ToolListUnavailable.Should().BeTrue("entry 2.2: before the first launch a tool outside the set fails over");
        outside.NoProjectTools.Should().NotBeEmpty("the two reads differ here, so the boundary row is red too and wins");
        var agents = CodexToolList.Judge(Whole([.. all, "collaboration.spawn_agent"]), Whole([.. all, "collaboration.spawn_agent"]));
        agents.ToolList.Should().Contain("collaboration.spawn_agent").And.Contain("an agent tool");
        var same = CodexToolList.Judge(Whole([.. all, "mcp__x__y"]), Whole([.. all, "mcp__x__y"]));
        same.ToolList.Should().Contain("an MCP tool");
        same.NoProjectTools.Should().BeEmpty("the same names in both reads: only the tool row is red");
        CodexToolList.Judge(Whole(all), Whole(all.Take(9))).NoProjectTools.Should().Contain("not the one of the worktree");
        CodexToolList.Judge(Whole([]), Whole([])).Should().Match<CodexToolList.Judgement>(j => j.ToolListUnavailable && j.ToolList.Length > 0 && j.NoProjectTools.Length == 0 && j.Reason == "inventory-not-whole");
        CodexToolList.Judge(CodexToolList.ToolRead.Unreadable, CodexToolList.ToolRead.Unreadable).ToolListUnavailable.Should().BeTrue();
        CodexToolList.Judge(Whole(all), Whole([])).Should().Match<CodexToolList.Judgement>(j => !j.ToolListUnavailable && j.NoProjectTools.Length > 0 && j.Reason == "inventory-not-whole");
        CodexToolList.Judge(new(all, false), Whole(all)).ToolList.Should().NotBeEmpty("a read that is not whole passes nothing, even when it holds only known names");
    }

    // ---------- V36: the inventory is read whole, or it is not read ----------

    private static string Report(string execDescription, params string[] more)
    {
        var tools = new List<object> { new { name = "functions", tools = new object[] { new { name = "exec", description = execDescription }, new { name = "wait", description = "" } } } };
        tools.AddRange(more.Select(name => (object)new { name }));

        return JsonSerializer.Serialize(new object[] { new { type = "message", role = "developer", content = "x" }, new { type = "additional_tools", tools } });
    }

    [Fact]
    public void AnInventoryInTheGrammar_IsReadWhole_AndItsNamesAreTheNestedOnes()
    {
        var read = CodexToolList.Read(Report("Run JavaScript.\n### `apply_patch`\nmore text\n### `exec_command`\n## clock\n### `clock__curr_time`", "apply_patch"));

        read.Whole.Should().BeTrue();
        read.Names.Should().Contain(["functions.exec", "functions.wait", "apply_patch", "exec_command", "clock__curr_time"]);
    }

    [Theory]
    [InlineData("Run JavaScript. The tools are apply_patch and exec_command.")]
    [InlineData("Run JavaScript.\n### apply_patch")]
    [InlineData("Run JavaScript.\n### `apply_patch` and more")]
    [InlineData("Run JavaScript.\n### `apply patch`")]
    [InlineData("Run JavaScript.\n### `apply_patch`\n### `bad name!`")]
    [InlineData("Run JavaScript.\n###  `apply_patch`")]
    [InlineData("")]
    public void AnInventoryInAnotherForm_IsNotReadWhole_SoNoShorterListPasses(string description)
    {
        var read = CodexToolList.Read(Report(description));

        read.Whole.Should().BeFalse(description);
        CodexToolList.Judge(read, read).Should().Match<CodexToolList.Judgement>(j => j.ToolList.Length > 0 && j.ToolListUnavailable && j.Reason == "inventory-not-whole");
        CodexToolList.Judge(read, Whole(CodexToolList.Known)).NoProjectTools.Should().NotBeEmpty("in one read only it is the boundary row");
    }

    [Theory]
    [InlineData("this is not a report")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""[{"type":"additional_tools"}]""")]
    [InlineData("""[{"type":"additional_tools","tools":[{"description":"no name"}]}]""")]
    [InlineData("""[{"type":"additional_tools","tools":[{"name":5}]}]""")]
    [InlineData("""[{"type":"additional_tools","tools":["apply_patch"]}]""")]
    public void AReportThatIsNotTheExpectedJson_OrHasAnEntryWithNoName_IsNotWhole(string text) =>
        CodexToolList.Read(text).IsAList.Should().BeFalse();

    [Fact]
    public void AToolListWithNoExecTool_IsWhole_AndOneWithExecButNoNestedLineIsNot()
    {
        CodexToolList.Read("""[{"type":"additional_tools","tools":[{"name":"apply_patch"}]}]""").Should().Match<CodexToolList.ToolRead>(read => read.IsAList);
        CodexToolList.Read("""[{"type":"additional_tools","tools":[{"name":"functions","tools":[{"name":"exec"}]}]}]""").Whole.Should().BeFalse("exec with no description holds no inventory");
    }

    private static string RecordedReport(string model)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "codex-tool-reports", model + ".json")));

        return document.RootElement.GetProperty("report").GetRawText();
    }

    [Theory]
    [InlineData("gpt-6.1-terra", 11)]
    [InlineData("gpt-5.6-terra", 9)]
    public void TheRecordedReportOfARealModel_IsReadWhole_UnderTheGrammar_AndHoldsTheKnownSet(string model, int count)
    {
        var read = CodexToolList.Read(RecordedReport(model));

        read.Whole.Should().BeTrue("a recorded report that is not whole under the grammar stops the task: the grammar is not widened in silence");
        read.Names.Should().HaveCount(count);
        CodexToolList.Outside(read.Names).Should().BeEmpty();
        CodexToolList.Absent(read.Names).Should().HaveCount(11 - count);
        CodexToolList.Judge(read, read).Should().Match<CodexToolList.Judgement>(j => j.ToolList.Length == 0 && j.NoProjectTools.Length == 0);
    }

    [Fact]
    public void TheRecordedReportOfGpt56Terra_HasNotTheTwoToolsTheMeasuredModelHas()
    {
        CodexToolList.Absent(CodexToolList.Read(RecordedReport("gpt-5.6-terra")).Names).Should().BeEquivalentTo("functions.request_user_input_async", "clock__curr_time");
        CodexToolList.Absent(CodexToolList.Read(RecordedReport("gpt-6.1-terra")).Names).Should().BeEmpty();
    }

    [Theory]
    [InlineData("gpt-6.1-terra")]
    [InlineData("gpt-5.6-terra")]
    public void TheRecordedReport_WithItsNestedInventoryInAnotherForm_InBothCopies_DoesNotHold(string model)
    {
        var recorded = RecordedReport(model);
        // The same report, the one list of tools in a sentence: what the code before read as a list of FOUR known names, with both copies the same.
        var changed = Changed(model, report => Exec(report)["description"] = System.Text.RegularExpressions.Regex.Replace(Exec(report)["description"]!.GetValue<string>(), @"### `([A-Za-z0-9_.-]+)`", "the tool $1"));
        changed.Should().NotBe(System.Text.Json.Nodes.JsonNode.Parse(recorded)!.ToJsonString());
        var read = CodexToolList.Read(changed);

        read.Whole.Should().BeFalse();
        CodexToolList.Judge(read, read).Should().Match<CodexToolList.Judgement>(j => j.ToolList.Length > 0 && j.ToolListUnavailable && j.Reason == "inventory-not-whole");
    }

    private static string Changed(string model, Action<System.Text.Json.Nodes.JsonArray> change)
    {
        var report = System.Text.Json.Nodes.JsonNode.Parse(RecordedReport(model))!.AsArray();
        change(report);

        return report.ToJsonString();
    }

    private static System.Text.Json.Nodes.JsonObject ToolsItem(System.Text.Json.Nodes.JsonArray report) => report.Select(item => item!.AsObject()).Single(item => item["type"]?.GetValue<string>() == "additional_tools");

    private static System.Text.Json.Nodes.JsonObject Exec(System.Text.Json.Nodes.JsonArray report) =>
        ToolsItem(report)["tools"]!.AsArray().Select(item => item!.AsObject()).Single(item => item["name"]!.GetValue<string>() == "functions")["tools"]!.AsArray().Select(item => item!.AsObject()).Single(item => item["name"]!.GetValue<string>() == "exec");

    public static TheoryData<string, Action<System.Text.Json.Nodes.JsonArray>> OneChange => new()
    {
        { "1: a second additional_tools item", report => report.Add(System.Text.Json.Nodes.JsonNode.Parse(ToolsItem(report).ToJsonString())) },
        { "1: a member tools of another item", report => report.Add(new System.Text.Json.Nodes.JsonObject { ["type"] = "message", ["tools"] = new System.Text.Json.Nodes.JsonArray() }) },
        { "1: tools of the item is not an array", report => ToolsItem(report)["tools"] = "none" },
        { "2: an element that is not an object", report => ToolsItem(report)["tools"]!.AsArray().Add("apply_patch") },
        { "2: an element with no name", report => ToolsItem(report)["tools"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject { ["type"] = "custom" }) },
        { "2: a nested element with no name", report => ToolsItem(report)["tools"]!.AsArray().Select(item => item!.AsObject()).Single(item => item["name"]!.GetValue<string>() == "functions")["tools"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject()) },
        { "2: a member tools that is not an array", report => ToolsItem(report)["tools"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject { ["name"] = "x", ["tools"] = "no" }) },
        { "3: functions.exec twice", report => ToolsItem(report)["tools"]!.AsArray().Select(item => item!.AsObject()).Single(item => item["name"]!.GetValue<string>() == "functions")["tools"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse(Exec(report).ToJsonString())) },
        { "3: the description of functions.exec is not a string", report => Exec(report)["description"] = 5 },
        { "3: functions.exec has no description", report => Exec(report).Remove("description") },
        { "4: a carriage return after the name", report => Exec(report)["description"] = Exec(report)["description"]!.GetValue<string>().Replace("### `write_stdin`", "### `write_stdin`\r", StringComparison.Ordinal) },
        { "4: a space after the name", report => Exec(report)["description"] = Exec(report)["description"]!.GetValue<string>().Replace("### `write_stdin`", "### `write_stdin` ", StringComparison.Ordinal) },
        { "4: spaces before the heading", report => Exec(report)["description"] = Exec(report)["description"]!.GetValue<string>().Replace("### `write_stdin`", "  ### `write_stdin`", StringComparison.Ordinal) },
        { "4: a tab before the heading", report => Exec(report)["description"] = Exec(report)["description"]!.GetValue<string>().Replace("### `write_stdin`", "\t### `write_stdin`", StringComparison.Ordinal) },
        { "4: a heading of another level with a backtick", report => Exec(report)["description"] = Exec(report)["description"]!.GetValue<string>() + "\n## `hidden_tool`" },
        { "4: a heading of the fourth level", report => Exec(report)["description"] = Exec(report)["description"]!.GetValue<string>() + "\n#### `hidden_tool`" },
        { "5: no line matches", report => Exec(report)["description"] = "Run JavaScript." },
    };

    [Theory]
    [MemberData(nameof(OneChange))]
    public void ARecordedReportChangedInOnePlace_IsNotWhole_InBothCopiesTheEntryIsModelUnavailable_InOneCopyItIsTheBoundary(string what, Action<System.Text.Json.Nodes.JsonArray> change)
    {
        var read = CodexToolList.Read(Changed("gpt-6.1-terra", change));
        var good = CodexToolList.Read(RecordedReport("gpt-6.1-terra"));

        read.IsAList.Should().BeFalse(what);
        CodexToolList.Judge(read, read).Should().Match<CodexToolList.Judgement>(j => j.ToolList.Length > 0 && j.ToolListUnavailable && j.Reason == "inventory-not-whole" && j.ShownNames.Length == 0, what);
        var one = CodexToolList.Judge(good, read);
        one.NoProjectTools.Should().NotBeEmpty(what + ": in one copy the two reads are not the same, and that row is the boundary row");
    }

    [Fact]
    public void TheRecordedReports_AreReadWhole_AndTheScrubChangedNoByteOfTheirToolsItem()
    {
        foreach (var model in new[] { "gpt-6.1-terra", "gpt-5.6-terra" })
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "codex-tool-reports", model + ".json")));
            document.RootElement.GetProperty("report").GetArrayLength().Should().Be(1, "only the additional_tools item is kept");
            CodexToolList.Read(document.RootElement.GetProperty("report").GetRawText()).Whole.Should().BeTrue(model);
        }

        // The harness compared the item as the CLI printed it with the item as saved, byte for byte, and wrote it into its results (RESULTS_e3_codex_model_name.md).
    }

    // ---------- A4-M4: the known set grows only by an addendum ----------

    [Fact]
    public void TheKnownSetOfTheProduct_IsEqualToTheListOfSection133OfTheJudgement_SoOneEditAloneIsRed()
    {
        // Section 13.3 of v2/spikes/RESULTS_codex_agy_write_boundary_judgement.md names these 11. A name joins this list only after a measure of that tool and an addendum of that file (A4-M4), never by one commit alone.
        string[] section133 =
        [
            "functions.exec", "functions.wait", "functions.request_user_input", "functions.request_user_input_async",
            "apply_patch", "exec_command", "write_stdin", "list_mcp_resources", "list_mcp_resource_templates", "read_mcp_resource", "clock__curr_time",
        ];

        CodexToolList.Known.Should().BeEquivalentTo(section133).And.HaveCount(11);
    }

    [Fact]
    public void ANameThatOneReadOnlyHolds_IsCountedAndNeverShown_AndNamesBothReadsHoldAreShown()
    {
        var all = CodexToolList.Known;
        var only = CodexToolList.Judge(Whole([.. all, "worktree_only_tool"]), Whole(all));
        only.ShownNames.Should().BeEmpty();
        (only.ToolList + only.NoProjectTools).Should().NotContain("worktree_only_tool").And.Contain("1 name(s)");
        var standOnly = CodexToolList.Judge(Whole(all), Whole([.. all, "standin_only_tool"]));
        (standOnly.ToolList + standOnly.NoProjectTools + standOnly.ShownNames).Should().NotContain("standin_only_tool");
        var both = CodexToolList.Judge(Whole([.. all, "shared_new_tool"]), Whole([.. all, "shared_new_tool"]));
        both.ShownNames.Should().Be("shared_new_tool");
        both.ToolList.Should().Contain("shared_new_tool");
    }

    [Fact]
    public void ANameThatHasTheShapeOfASecretButOnlyAllowedCharacters_IsNeverShown_BecauseTheSharedRedactorWouldChangeIt()
    {
        const string Secret = "sk-ant-oat01-abcdefghijklmnopqrstuvwxyz0123456789ABCDEF";
        CodexToolList.CanBeShown(Secret).Should().BeFalse();
        CoaiMcp.Core.Notices.Redaction.SafeText(Secret, 64).Should().NotBe(Secret, "the redactor does change it: that is what makes the rule");
        CodexToolList.Shown([Secret, "apply_patch2"]).Should().NotContain("sk-ant").And.Contain("a name that cannot be shown (").And.Contain("apply_patch2");
    }

    [Fact]
    public void TheMappingOfAnUnknownToolAtTheFirstLaunchIsOneConstant_AndItFailsOver_EntryTwoPointTwo() =>
        CodexToolList.UnknownToolFailsOver.Should().BeTrue("the owner, 2026-11-27: move to the next model");
}
