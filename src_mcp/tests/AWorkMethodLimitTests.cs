using System.Text.Json;
using CoaiMcp.Roles;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E1 s2 t2 — the work method answers its two new limits, an unknown key costs only itself (risk 3 of the spec), and the <c>ba</c> role has a second model to fail over to (risk 8).
/// </summary>
public sealed class AWorkMethodLimitTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] values) =>
        name => values.Where(one => one.Name == name).Select(one => one.Value).FirstOrDefault();

    private static JsonElement Answer(params (string Name, string Value)[] values) =>
        JsonDocument.Parse(RolesAnswer.Json(RolesAnswer.Compute(Env(values), "claude-code", "2.1.330", claudeHook: false))).RootElement.Clone();

    private static int Number(JsonElement method, string name) => method.GetProperty(name).GetInt32();

    [Fact]
    public void TheDefaults_AreEightStoriesPerEpic_AndSixTasksPerStory()
    {
        var method = Answer().GetProperty("workMethod");

        Number(method, "maxStoriesPerEpic").Should().Be(8);
        Number(method, "maxTasksPerStory").Should().Be(6);
        Number(method, "maxEpicsPerEpoch").Should().Be(5);
    }

    [Fact]
    public void ASetting_ReplacesTheTwoNewLimits()
    {
        var answer = Answer((RolesKeys.WorkMethod, """{"maxStoriesPerEpic":4,"maxTasksPerStory":3}"""));
        var method = answer.GetProperty("workMethod");

        Number(method, "maxStoriesPerEpic").Should().Be(4);
        Number(method, "maxTasksPerStory").Should().Be(3);
        Number(method, "maxEpicsPerEpoch").Should().Be(5, "a number the setting did not name keeps its default");
        answer.GetProperty("unrecognised").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void AnUnknownKey_KeepsTheOtherKeys_AndIsNamed()
    {
        var answer = Answer((RolesKeys.WorkMethod, """{"maxEpicsPerEpoch":3,"invented":1,"maxTasksPerStory":2}"""));
        var method = answer.GetProperty("workMethod");

        Number(method, "maxEpicsPerEpoch").Should().Be(3, "the unknown key must not reset the keys this server knows");
        Number(method, "maxTasksPerStory").Should().Be(2);
        Number(method, "maxStoriesPerEpic").Should().Be(8);
        var problems = answer.GetProperty("unrecognised").EnumerateArray().ToList();
        problems.Should().ContainSingle();
        problems[0].GetProperty("sentence").GetString().Should().Contain("invented");
    }

    [Fact]
    public void ABadValueOfAKnownKey_CostsThatKeyOnly()
    {
        var answer = Answer((RolesKeys.WorkMethod, """{"maxEpicsPerEpoch":"five","maxStoriesPerEpic":2,"mvpEpics":{"min":3,"max":2}}"""));
        var method = answer.GetProperty("workMethod");

        Number(method, "maxEpicsPerEpoch").Should().Be(5, "its own default stands");
        Number(method, "maxStoriesPerEpic").Should().Be(2, "a good key beside two bad ones is kept");
        method.GetProperty("mvpEpics").GetProperty("min").GetInt32().Should().Be(1);
        answer.GetProperty("unrecognised").GetArrayLength().Should().Be(2);
    }

    [Theory]
    [InlineData("maxStoriesPerEpic", "0")]
    [InlineData("maxTasksPerStory", "0")]
    [InlineData("maxStoriesPerEpic", "1.5")]
    public void ALimitBelowOne_OrNotWhole_IsRefusedNamingTheKey(string key, string value)
    {
        var answer = Answer((RolesKeys.WorkMethod, $$"""{"{{key}}":{{value}}}"""));

        Number(answer.GetProperty("workMethod"), key).Should().BeGreaterThan(0);
        answer.GetProperty("unrecognised")[0].GetProperty("sentence").GetString().Should().Contain(key);
    }

    [Fact]
    public void TheBaRole_FailsOverFromFableToOpus_BothOnTheClaudeCli()
    {
        var models = Answer().GetProperty("roles").GetProperty("ba").GetProperty("models").EnumerateArray()
            .Select(one => $"{one.GetProperty("model").GetString()}:{one.GetProperty("vendor").GetString()}").ToList();

        models.Should().Equal("fable:claude", "opus:claude");
    }

    [Fact]
    public void TheRevision_ChangesWhenANewLimitDoes()
    {
        var plain = Answer().GetProperty("settingsRevision").GetString();

        Answer((RolesKeys.WorkMethod, """{"maxTasksPerStory":5}""")).GetProperty("settingsRevision").GetString().Should().NotBe(plain);
    }

    [Fact]
    public void TheHookSentence_NamesTheTwoNewLimits()
    {
        var text = RolesAnswer.HookText(RolesAnswer.Compute(Env(), string.Empty, string.Empty, claudeHook: true));

        text.Should().Contain("at most 8 stories per epic").And.Contain("6 tasks per story");
    }
}
