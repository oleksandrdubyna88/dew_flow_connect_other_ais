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
    public void TheFileHoldsTheFamilies_AsData_AndAClaudeIdIsNeverAcceptedByItsShape()
    {
        RoleModelNames.Current.CodexFamilies.Should().Equal("terra", "astra", "sol", "luna");
        RoleModelNames.Refusal("claude-haiku-9-9", "claude", "k").Should().NotBeEmpty("a claude id that looks real but nobody verified");
        RoleModelNames.Refusal("gpt-6.3-terra", "claude", "k").Should().NotBeEmpty("the codex shape is for codex only");
        RoleModelNames.Refusal("gpt-6.3-terra", "agy", "k").Should().NotBeEmpty("the agy list does not change");
    }

    [Theory]
    [InlineData("gpt-6.3-terra")]
    [InlineData("gpt-7-astra")]
    [InlineData("terra")]
    [InlineData("luna")]
    [InlineData("gpt-5.5")]
    public void ACodexName_ThatIsAFamily_OrHasTheShape_OrIsListed_NeedsNoHandEdit(string name) =>
        RoleModelNames.Refusal(name, "codex", "COAI_ROLES_MODELS").Should().BeEmpty();

    [Theory]
    [InlineData("gpt-6.3-tera")]
    [InlineData("gpt-x-terra")]
    [InlineData("GPT-6.1-terra")]
    [InlineData("gpt-6.1-terra-preview")]
    [InlineData("gpt 6.1-terra")]
    public void ATypoOfACodexName_IsRouteInvalid_BeforeAnyFolder_AndTheSentenceNamesTheSetting(string name) =>
        RoleModelNames.Refusal(name, "codex", "COAI_ROLES_BANDS").Should().Contain($"model '{name}'").And.Contain("COAI_ROLES_BANDS");

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

    [Fact]
    public void TheJudgement_PassesFewerNames_AndRefusesAnOutsideName_TwoReadsThatDiffer_AndAnEmptyList()
    {
        var all = CodexToolList.Known;
        CodexToolList.Judge(all, all).ToolList.Should().BeEmpty();
        CodexToolList.Judge(all.Take(9).ToList(), all.Take(9).ToList()).Should().Match<CodexToolList.Judgement>(j => j.ToolList.Length == 0 && j.NoProjectTools.Length == 0 && j.Absent.Count == 2);
        CodexToolList.Judge(all.Take(1).ToList(), all.Take(1).ToList()).Absent.Should().HaveCount(10);
        CodexToolList.Judge([.. all, "collaboration.spawn_agent"], all).ToolList.Should().Contain("1 name(s) outside the known set").And.Contain("collaboration.spawn_agent").And.Contain("an agent tool");
        CodexToolList.Judge(all, [.. all, "mcp__x__y"]).ToolList.Should().Contain("an MCP tool");
        CodexToolList.Judge(all, all.Take(9).ToList()).NoProjectTools.Should().Contain("not the one of the worktree");
        CodexToolList.Judge([], []).Should().Match<CodexToolList.Judgement>(j => j.ToolListUnavailable && j.ToolList.Length > 0 && j.NoProjectTools.Length == 0);
        CodexToolList.Judge(null, null).ToolListUnavailable.Should().BeTrue();
        CodexToolList.Judge(all, []).Should().Match<CodexToolList.Judgement>(j => !j.ToolListUnavailable && j.NoProjectTools.Length > 0);
    }

    [Fact]
    public void TheMappingOfAnUnknownToolAtThePreflightIsOneConstant_AndTodayItIsABoundaryRow() =>
        CodexToolList.UnknownToolFailsOver.Should().BeFalse("the owner may decide that an unknown tool fails over: that is this one constant");
}
