using CoaiMcp.Core.Roles;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s2 t5 (V31; <c>SPEC_CHANGES.md</c> Q1, S4 items 4 and 6): which codex names <c>shared/role-model-names.json</c> accepts before a folder is made, with no hand edit; that a claude id is never accepted by its shape; and that lists are compared by the name as written. Only the public face of
/// the table and of the router — <see cref="RoleModelNames.Refusal"/> and <see cref="RoleRouter.Route"/> — so each test runs on the code of the plan stop too (the red run of d1 names which are red there and which are green guards).
/// </summary>
public sealed class ACodexNamesNeedNoEditTests
{
    [Theory]
    [InlineData("gpt-6.3-terra")]
    [InlineData("gpt-7-astra")]
    [InlineData("gpt-6.10-sol")]
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
    [InlineData("gpt-06-terra")]
    [InlineData("gpt-6.01-terra")]
    [InlineData("gpt 6.1-terra")]
    public void ATypoOfACodexName_IsRouteInvalid_BeforeAnyFolder_AndTheSentenceNamesTheSetting(string name) =>
        RoleModelNames.Refusal(name, "codex", "COAI_ROLES_BANDS").Should().Contain($"model '{name}'").And.Contain("COAI_ROLES_BANDS");

    [Theory]
    [InlineData("claude-haiku-9-9", "claude")]
    [InlineData("claude-opus-5-55", "claude")]
    [InlineData("gpt-6.3-terra", "claude")]
    [InlineData("gpt-6.3-terra", "agy")]
    [InlineData("gemini-4-argon-low2", "agy")]
    public void AClaudeId_IsNeverAcceptedByItsShape_AndTheAgyListDoesNotChange(string name, string vendor) =>
        RoleModelNames.Refusal(name, vendor, "COAI_ROLES_MODELS").Should().NotBeEmpty();

    private static RouteResult Route(string model, params string[] important)
    {
        var table = new RouteTable([("important", [.. important.Select(name => new ModelRef(name, "codex"))]), ("ba", [new ModelRef("opus", "claude")])], []);

        return RoleRouter.Route(new RouteRequest("ba", model, "feature", 8, true), table);
    }

    [Fact]
    public void TheImportantList_IsComparedByTheWrittenName_NotByWhatAFamilyResolvesTo()
    {
        Route("gpt-6.2-astra", "astra").Should().BeOfType<RouteResult.Refused>("a name that is not on the list as written is refused, whatever it would resolve to");
        Route("astra", "astra").Should().BeOfType<RouteResult.Routed>();
    }

    [Fact]
    public void AShapedNameThatIsWrittenOnTheImportantList_IsRouted_WithNoEditOfTheFile() =>
        Route("gpt-6.2-astra", "gpt-6.2-astra").Should().BeOfType<RouteResult.Routed>("the written name is on the list, and the shape is accepted");
}
