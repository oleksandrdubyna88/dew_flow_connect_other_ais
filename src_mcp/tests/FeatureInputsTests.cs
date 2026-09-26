using System.Text.Json;
using CoaiMcp.Core.Feature;
using CoaiMcp.Core.Rounds;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>review_feature</c>'s two hand-written arguments are refused before any work, and every refusal of
/// <c>lessons</c> asks the four questions the gate actually needs answered (plan §4.5).
/// </summary>
/// <remarks>
/// A file of refusals needs one positive (testing.md, <i>a fixture the code REJECTS proves nothing</i>):
/// <see cref="ASubstantialAccount_IsAccepted"/> is built from the same pieces the refusals break, so a
/// fixture the parser dislikes for the wrong reason shows up there as red.
/// </remarks>
public sealed class FeatureInputsTests
{
    private const string Pitfall = "the api shim retried a 429 with no backoff until S1.2 added the ladder; the fix is in AskApiMode";
    private const string Blocker = "none — every epic merged without a blocker; the one risk was the vault, and it was configured in time";
    private const string Finding = "the outline cap of 400 files was never reached; the widest range read 258 files, measured in S0.2";

    private static string Lessons(object pitfalls, object blockers, object findings) =>
        JsonSerializer.Serialize(new Dictionary<string, object> { ["pitfalls"] = pitfalls, ["blockers"] = blockers, ["findings"] = findings });

    private static readonly string Good = Lessons(new[] { Pitfall }, new[] { Blocker }, new[] { Finding });

    [Fact]
    public void ASubstantialAccount_IsAccepted()
    {
        var parsed = FeatureInputs.ParseLessons(Good);

        var lessons = parsed.Should().BeOfType<FeatureInput<FeatureLessons>.Accepted>().Subject.Value;
        lessons.Pitfalls.Should().Equal(Pitfall);
        lessons.Blockers.Should().Equal(Blocker);
        lessons.Findings.Should().Equal(Finding);
    }

    /// <summary>Every shape of "not a usable account" — each one refused, each one asking all four questions.</summary>
    public static TheoryData<string, string, string> EmptyShapes => new()
    {
        { "absent", string.Empty, "not given" },
        { "blank", "   ", "not given" },
        { "not JSON", "{pitfalls: nope", "not JSON" },
        { "an array", "[]", "not an object" },
        { "pitfalls empty", Lessons(Array.Empty<string>(), new[] { Blocker }, new[] { Finding }), "'pitfalls' is empty" },
        { "blockers empty", Lessons(new[] { Pitfall }, Array.Empty<string>(), new[] { Finding }), "'blockers' is empty" },
        { "findings empty", Lessons(new[] { Pitfall }, new[] { Blocker }, Array.Empty<string>()), "'findings' is empty" },
        { "all empty", Lessons(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()), "'pitfalls' is empty" },
        { "a key missing", JsonSerializer.Serialize(new { pitfalls = new[] { Pitfall }, blockers = new[] { Blocker } }), "'findings' is missing" },
        { "a bare none", Lessons(new[] { Pitfall }, new[] { "none" }, new[] { Finding }), "'blockers' entry 1 says \"none\" without saying why" },
        { "none and one word", Lessons(new[] { Pitfall }, new[] { Blocker }, new[] { "no findings." }), "'findings' entry 1 says \"no findings.\"" },
        { "a number entry", Lessons(new object[] { 3 }, new[] { Blocker }, new[] { Finding }), "'pitfalls' entry 1 is not a non-empty string" },
        { "a blank entry", Lessons(new[] { "  " }, new[] { Blocker }, new[] { Finding }), "'pitfalls' entry 1 is not a non-empty string" },
        { "too thin", Lessons(new[] { "a flaky test in the api shim" }, new[] { "none — nothing blocked any epic at all" }, new[] { "all green" }), $"under the {ReviewScope.Floor}" },
    };

    [Theory]
    [MemberData(nameof(EmptyShapes))]
    public void EveryUnusableShapeOfLessons_IsRefused_NamingWhatIsWrong_AndAskingAllFourQuestions(string shape, string json, string names)
    {
        var refused = FeatureInputs.ParseLessons(json).Should().BeOfType<FeatureInput<FeatureLessons>.Refused>(shape).Subject;

        refused.Sentence.Should().Contain(names, shape);
        foreach (var question in FeatureInputs.LessonQuestions)
        {
            refused.Sentence.Should().Contain(question, $"the {shape} refusal must ask every question");
        }
    }

    [Fact]
    public void AnUnknownKey_IsRefused_NamingItAndTheLegalKeys()
    {
        var json = JsonSerializer.Serialize(new { pitfalls = new[] { Pitfall }, blockers = new[] { Blocker }, findings = new[] { Finding }, notes = new[] { "x" } });

        var refused = FeatureInputs.ParseLessons(json).Should().BeOfType<FeatureInput<FeatureLessons>.Refused>().Subject;

        refused.Sentence.Should().Contain("'notes' is not a key of lessons").And.Contain("pitfalls, blockers, findings");
    }

    [Fact]
    public void LessonsOverTheCap_AreRefusedBeforeTheyAreParsed()
    {
        var json = Lessons(new[] { new string('x', FeatureInputs.LessonsMaxBytes) }, new[] { Blocker }, new[] { Finding });

        FeatureInputs.ParseLessons(json).Should().BeOfType<FeatureInput<FeatureLessons>.Refused>()
            .Which.Sentence.Should().Contain($"over the {FeatureInputs.LessonsMaxBytes}-byte cap");
    }

    [Theory]
    [InlineData("none", true)]
    [InlineData("None.", true)]
    [InlineData("n/a", true)]
    [InlineData("nothing to report", true)]
    [InlineData("no blockers", true)]
    [InlineData("none — every epic merged without a blocker", false)]
    [InlineData("no retry on a 429 in the api shim, fixed in S1.2", false)]
    [InlineData("nonexistent path handling was the first defect", false)]
    public void ANoneNeedsItsReason(string entry, bool bare) =>
        FeatureInputs.IsBareNone(entry).Should().Be(bare);

    private static string Epic(string title = "E1 — foundation", string summary = "honest stages, the api runtime, the outliner", string? branch = null, object? pr = null)
    {
        var epic = new Dictionary<string, object> { ["title"] = title, ["summary"] = summary };
        if (branch is not null)
        {
            epic["branch"] = branch;
        }

        if (pr is not null)
        {
            epic["pr"] = pr;
        }

        return JsonSerializer.Serialize(epic);
    }

    private static string Epics(int count) => "[" + string.Join(",", Enumerable.Range(1, count).Select(i => Epic($"E{i}"))) + "]";

    [Fact]
    public void Epics_AreAccepted_WithTheirCountAsData_AndOptionalFieldsEmptyWhenAbsent()
    {
        var json = $"[{Epic("E1", branch: "feat/e1", pr: 512)},{Epic("E2")},{Epic("E3", pr: "#530")}]";

        var epics = FeatureInputs.ParseEpics(json).Should().BeOfType<FeatureInput<FeatureEpics>.Accepted>().Subject.Value;

        epics.Count.Should().Be(3, "the D17 count is data; the threshold is the stage's decision");
        epics.Items[0].Should().Be(new FeatureEpic("E1", "honest stages, the api runtime, the outliner", "feat/e1", "512"));
        epics.Items[1].Branch.Should().BeEmpty();
        epics.Items[1].Pr.Should().BeEmpty();
        epics.Items[2].Pr.Should().Be("#530");
    }

    [Fact]
    public void TwoEpics_AreNotRefused_TheThresholdIsNotTheParsersToApply() =>
        FeatureInputs.ParseEpics(Epics(2)).Should().BeOfType<FeatureInput<FeatureEpics>.Accepted>()
            .Which.Value.Count.Should().Be(2);

    [Fact]
    public void TwentyEpics_AreTheMost() =>
        FeatureInputs.ParseEpics(Epics(FeatureInputs.MaxEpics)).Should().BeOfType<FeatureInput<FeatureEpics>.Accepted>();

    public static TheoryData<string, string, string> BadEpics => new()
    {
        { "absent", string.Empty, "not given" },
        { "not JSON", "[{", "not JSON" },
        { "an object", Epic(), "not an array" },
        { "none", "[]", "it has 0 entries" },
        { "twenty-one", Epics(FeatureInputs.MaxEpics + 1), "it has 21 entries" },
        { "no title", $"[{Epic(title: " ")}]", "entry 1 has no title" },
        { "no summary", $"[{Epic()},{Epic(summary: "")}]", "entry 2 has no summary" },
        { "an unknown key", "[{\"title\":\"E1\",\"summary\":\"s\",\"owner\":\"me\"}]", "'owner', which is not a key of an epic" },
        { "a string entry", "[\"E1\"]", "entry 1 is a JSON string, not an object" },
        { "over the cap", $"[{Epic(summary: new string('s', FeatureInputs.EpicsMaxBytes))}]", $"over the {FeatureInputs.EpicsMaxBytes}-byte cap" },
    };

    [Theory]
    [MemberData(nameof(BadEpics))]
    public void EveryUnusableShapeOfEpics_IsRefused_NamingWhatIsWrong(string shape, string json, string names) =>
        FeatureInputs.ParseEpics(json).Should().BeOfType<FeatureInput<FeatureEpics>.Refused>(shape)
            .Which.Sentence.Should().Contain(names, shape).And.Contain("title and summary required");
}
