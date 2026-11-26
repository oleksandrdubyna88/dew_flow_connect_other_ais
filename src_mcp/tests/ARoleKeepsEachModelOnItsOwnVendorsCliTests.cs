using System.Text.Json;
using CoaiMcp.Core.Roles;
using CoaiMcp.Roles;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E3 plan gate, finding 4 — the vendor-routing rule in the roles table: a Claude model runs only through the <c>claude</c> CLI, a GPT model through <c>codex</c>, Gemini and GPT-OSS through <c>agy</c>
/// (<c>.agents/rules/common/vendor-routing.md</c>). A role or band entry that breaks it is named in <c>unrecognised</c> and its default stands — the wrong CLI is invisible in the output otherwise.
/// </summary>
public sealed class ARoleKeepsEachModelOnItsOwnVendorsCliTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] values) =>
        name => values.Where(one => one.Name == name).Select(one => one.Value).FirstOrDefault();

    private static JsonElement Answer(Func<string, string?> env) =>
        JsonDocument.Parse(RolesAnswer.Json(RolesAnswer.Compute(env, "claude-code", "2.1.330", claudeHook: false))).RootElement.Clone();

    private static string[] Models(JsonElement answer, string role) =>
        [.. answer.GetProperty("roles").GetProperty(role).GetProperty("models").EnumerateArray().Select(one => $"{one.GetProperty("model").GetString()}:{one.GetProperty("vendor").GetString()}")];

    private static string[] Problems(JsonElement answer) =>
        [.. answer.GetProperty("unrecognised").EnumerateArray().Select(problem => $"{problem.GetProperty("key").GetString()}|{problem.GetProperty("sentence").GetString()}")];

    [Theory]
    [InlineData("sonnet", "agy")]
    [InlineData("opus", "codex")]
    [InlineData("haiku", "agy")]
    [InlineData("fable", "codex")]
    [InlineData("claude-opus-4-6-thinking", "agy")]
    [InlineData("Claude-Sonnet-4-6", "codex")]
    [InlineData("gpt-5.5", "agy")]
    [InlineData("terra", "agy")]
    public void ARoleListThatBreaksTheRule_IsNamed_AndItsDefaultStands(string model, string vendor)
    {
        var plain = Answer(Env());
        var answer = Answer(Env((RolesKeys.Models, $$"""{"coder":[{"model":"{{model}}","vendor":"{{vendor}}"}],"scout":[{"model":"opus","vendor":"claude"}]}""")));

        Models(answer, "coder").Should().Equal(Models(plain, "coder"), "the default of that role stands");
        Models(answer, "scout").Should().Equal(["opus:claude"], "a role that keeps the rule is taken");
        Problems(answer).Should().ContainSingle().Which.Should().StartWith($"{RolesKeys.Models}|").And.Contain("coder").And.Contain(model).And.Contain(vendor).And.Contain("vendor-routing");
    }

    [Fact]
    public void AFailoverEntryThatBreaksTheRule_CostsTheWholeListOfItsRole()
    {
        var plain = Answer(Env());
        var answer = Answer(Env((RolesKeys.Models, """{"important":[{"model":"fable","vendor":"claude"},{"model":"sonnet","vendor":"agy"}]}""")));

        Models(answer, "important").Should().Equal(Models(plain, "important"), "a list with one wrong route is not half applied");
        Problems(answer).Should().ContainSingle();
    }

    [Theory]
    [InlineData("sonnet", "agy")]
    [InlineData("opus", "codex")]
    [InlineData("gpt-5.5", "agy")]
    public void ABandThatBreaksTheRule_IsNamed_AndTheDefaultBandsStand(string model, string vendor)
    {
        var plain = Answer(Env());
        var answer = Answer(Env((RolesKeys.Bands, $$"""[{"min":0,"max":4,"model":"haiku","vendor":"claude"},{"min":5,"max":10,"model":"{{model}}","vendor":"{{vendor}}"}]""")));

        answer.GetProperty("complexityBands").GetRawText().Should().Be(plain.GetProperty("complexityBands").GetRawText());
        Problems(answer).Should().ContainSingle().Which.Should().StartWith($"{RolesKeys.Bands}|").And.Contain(model).And.Contain(vendor);
    }

    [Theory]
    [InlineData("sonnet", "claude")]
    [InlineData("claude-opus-4-6", "claude")]
    [InlineData("gpt-5.5", "codex")]
    [InlineData("terra", "codex")]
    [InlineData("gemini-3-pro", "agy")]
    [InlineData("gpt-oss-120b", "agy")]
    [InlineData("astra", "codex")]
    [InlineData("some-new-model", "agy")]
    public void AnEntryThatKeepsTheRule_IsTaken(string model, string vendor)
    {
        var answer = Answer(Env(
            (RolesKeys.Models, $$"""{"coder":[{"model":"{{model}}","vendor":"{{vendor}}"}]}"""),
            (RolesKeys.Bands, $$"""[{"min":0,"max":10,"model":"{{model}}","vendor":"{{vendor}}"}]""")));

        Models(answer, "coder").Should().Equal($"{model}:{vendor}");
        answer.GetProperty("complexityBands")[0].GetProperty("model").GetString().Should().Be(model);
        Problems(answer).Should().BeEmpty();
    }

    [Fact]
    public void TheShippedDefaults_KeepTheRule()
    {
        var shipped = File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "role-defaults.json"));

        var defaults = RoleDefaults.Parse(shipped);

        defaults.MainAi.Values.SelectMany(vendor => vendor.Roles.SelectMany(role => role.Models.Select(model => (model.Model, model.Vendor)))
            .Concat(vendor.Bands.Select(band => (band.Model, band.Vendor)))).Should().OnlyContain(pair => VendorRouting.Refusal(pair.Model, pair.Vendor).Length == 0);
    }

    [Fact]
    public void ADefaultsFileThatBreaksTheRule_IsABuildFault()
    {
        var broken = File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "role-defaults.json"))
            .Replace("\"orchestrator\": [ { \"model\": \"opus\", \"vendor\": \"claude\" } ]", "\"orchestrator\": [ { \"model\": \"sonnet\", \"vendor\": \"agy\" } ]", StringComparison.Ordinal);
        broken.Should().Contain("\"vendor\": \"agy\" } ]", "positive control: the edit is in the text");

        var parse = () => RoleDefaults.Parse(broken);

        parse.Should().Throw<InvalidOperationException>().WithMessage("*sonnet*agy*");
    }
}
