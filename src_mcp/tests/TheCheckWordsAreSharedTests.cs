using System.Reflection;
using System.Text.Json;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The words a consultant check is written in are the words <c>shared/consult-check-words.json</c> lists — read from the
/// server by REFLECTION, so a state or a canary reading added on one side fails the build on the other.
/// </summary>
/// <remarks>
/// The whole-branch review of PLAN_the_consultant_works_on_every_vendor.md (2026-10-03), finding J: the failure kinds had
/// a shared catalogue both halves were held to, and the check's states and canary readings — the same kind of vocabulary,
/// crossing the same seam — had none; the panel kept its own list and a fourth reading reached it only by a person
/// remembering. <c>consultantHealth.test.ts</c> holds the extension to the same file.
/// </remarks>
public sealed class TheCheckWordsAreSharedTests
{
    private static JsonElement Shared()
    {
        var path = Path.Combine(ProductionSources.RepositoryRoot(), "shared", "consult-check-words.json");
        using var parsed = JsonDocument.Parse(File.ReadAllBytes(path));

        return parsed.RootElement.Clone();
    }

    private static IReadOnlyList<string> Words(string list) =>
        [.. Shared().GetProperty(list).EnumerateArray().Select(entry => entry.GetProperty("word").GetString()!)];

    /// <summary>Every public string constant of <paramref name="type"/> — the server's own catalogue.</summary>
    private static IReadOnlyList<string> Constants(Type type) =>
        [.. type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)];

    [Fact]
    public void TheCheckStates_AreTheSharedStates_OneForOne()
    {
        var shared = Words("states");

        shared.Should().HaveCount(7, "the plan names seven check states, and a file that did not load names none");
        Constants(typeof(ConsultCheckStates)).Should().BeEquivalentTo(shared);
        shared.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void TheCanaryReadings_AreTheSharedReadings_AndOnlyDeniedByCliIsObservedConfinement()
    {
        var shared = Words("canaryReadings");

        shared.Should().HaveCount(4);
        Constants(typeof(CanaryReadings)).Should().BeEquivalentTo(shared);
        Shared().GetProperty("canaryReadings").EnumerateArray()
            .Where(entry => entry.GetProperty("observedConfinement").GetBoolean())
            .Select(entry => entry.GetProperty("word").GetString())
            .Should().Equal([CanaryReadings.DeniedByCli], "a model declining on its own proves compliance, and an unattributed refusal proves nothing about the canary");
    }
}
