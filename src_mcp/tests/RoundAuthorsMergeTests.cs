using System.Text.Json;
using CoaiMcp.Core.Authors;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using static CoaiMcp.Tests.RoundAuthorsFacts;
using static CoaiMcp.Tests.RoundAuthorsSeed;

namespace CoaiMcp.Tests;

/// <summary>
/// E6.S4: the default output is the SUMS as JSON — additive, so combining two halves equals reporting their union, without opening a store
/// across the drvfs boundary. Stores are seeded through the real writers; in-memory answers are built through S2 and S3.
/// </summary>
public sealed class RoundAuthorsMergeTests : IDisposable
{
    private readonly TempDir _root = TempDir.For("testsToDelete-coai-authors-merge-");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.Dispose();
    }

    private static readonly string ReadAt = Stamp(T0.AddDays(10));

    private string NewStore(string name) => Directory.CreateDirectory(_root.At(name)).FullName;

    /// <summary>Two code rounds per gate session: a first with a Sonnet coder, a corrective one with a recorded delta.</summary>
    private static void SeedSessions(string dir, params string[] sessions)
    {
        using var db = Open(dir);
        foreach (var session in sessions)
        {
            Round(
                db, session, "D:/repo", "feat/x", "CodeReview", 1, T0, reviewers: [Done("codex", "gpt-6-luna", "high")], findings: [Found("a"), Found("b")],
                size: Whole(Cell("shown", "code", 100)), authors: Snapshot("CodeReview", 1, rows: [Author(AgentKey.MainId, "claude-opus-5-5", Association.SameSession), Author("a1", "claude-sonnet-5-5")]), accept: [0], gating: 1);
            Round(
                db, session, "D:/repo", "feat/x", "CodeReview", 2, T0.AddHours(1), reviewers: [Done("codex", "gpt-6-luna", "high")], findings: [Found("c")],
                size: WithDelta([Cell("shown", "code", 120)], Cell("shown", "code", 20)), authors: Snapshot("CodeReview", 2, rows: [Author(AgentKey.MainId, "claude-opus-5-5", Association.SameSession), Author("a1", "claude-sonnet-5-5", number: 2)]), accept: [0]);
        }
    }

    private static RoundAuthorsSelection Selection() => Sel() with { SinceUtc = Stamp(T0.AddDays(-1)) };

    private static RoundAuthorsAnswer ReportOf(string dir) => RoundAuthorsMerge.Report(dir, Selection(), ReadAt);

    private static RoundAuthorsAnswer WithoutSources(RoundAuthorsAnswer answer) => answer with { Sources = [] };

    private static RoundAuthorsAnswer AnswerOf(string source, string[] sessions, params RoundFacts[] rounds)
    {
        var selection = Sel();
        var sums = RoundAuthorsCells.Count(RoundAuthorsPopulation.Of(rounds, selection), selection);

        return new RoundAuthorsAnswer(1, selection, [new RoundAuthorsSource(source, "windows", "t", true, sessions)], sums.Cells, sums.Exclusions, true);
    }

    [Fact]
    public void TwoHalves_AreTheUnion()
    {
        var (a, b, c) = (NewStore("a"), NewStore("b"), NewStore("c"));
        SeedSessions(a, "x1", "x2");
        SeedSessions(b, "y1", "y2");
        SeedSessions(c, "x1", "x2", "y1", "y2");

        var merged = RoundAuthorsMerge.Add(ReportOf(a), ReportOf(b));
        var union = ReportOf(c);

        merged.Ok.Should().BeTrue(merged.Why);
        merged.Answer.Cells.Should().NotBeEmpty();
        merged.Answer.Cells.Should().BeEquivalentTo(union.Cells, options => options.WithStrictOrdering());
        merged.Answer.Exclusions.Should().BeEquivalentTo(union.Exclusions, options => options.WithStrictOrdering());
        merged.Answer.Sources.Should().HaveCount(2).And.OnlyContain(source => source.Sessions.Count == 2);
    }

    [Fact]
    public void Adding_IsCommutative_OnceTheSourcesAreSetAside()
    {
        var (a, b) = (NewStore("a"), NewStore("b"));
        SeedSessions(a, "x1");
        SeedSessions(b, "y1", "y2");

        var one = RoundAuthorsMerge.Add(ReportOf(a), ReportOf(b)).Answer;
        var other = RoundAuthorsMerge.Add(ReportOf(b), ReportOf(a)).Answer;

        RoundAuthorsMerge.Serialize(WithoutSources(one)).Should().Be(RoundAuthorsMerge.Serialize(WithoutSources(other)));
    }

    [Fact]
    public void Marks_AreRecomputedFromTheSums_NeverAdded()
    {
        RoundFacts[] Half(string tag, int onMain) => [.. Enumerable.Range(0, 6).Select(at => Round(1, session: tag + at, branch: at < onMain ? "main" : tag + "-b" + at))];
        string[] Sessions(string tag) => [.. Enumerable.Range(0, 6).Select(at => tag + at)];
        var first = AnswerOf("one", Sessions("p"), Half("p", 4));
        var second = AnswerOf("two", Sessions("q"), Half("q", 0));

        var merged = RoundAuthorsMerge.Add(first, second).Answer.Cells.Single();

        RoundAuthorsCells.Marks(first.Cells.Single()).Should().Equal(["small", "dominated"], "6 rounds, 4 of them on one branch");
        merged.Rounds.Should().Be(12);
        RoundAuthorsCells.Marks(merged).Should().BeEmpty("12 rounds is not small, and 4 of 12 is not more than half — marks are never added");
    }

    [Fact]
    public void SessionSets_AreUnioned_NeverSummedAsCounts()
    {
        var one = AnswerOf("one", ["p"], Round(1, session: "p"));
        var two = AnswerOf("two", ["q"], Round(1, session: "p"));

        var cell = RoundAuthorsMerge.Add(one, two).Answer.Cells.Single();

        cell.Rounds.Should().Be(2);
        cell.Sessions.Should().Equal(["p"], "one session id seen by both halves is one session");
    }

    [Fact]
    public void TwoReportsThatAreNotOnePopulation_AreRefused_NamingTheField()
    {
        var one = AnswerOf("one", ["p"], Round(1, session: "p"));
        var split = one with { Selection = one.Selection with { SplitUtc = Stamp(T0.AddHours(3)) }, Sources = [one.Sources[0] with { SourceId = "other", Sessions = ["z"] }] };

        var differing = RoundAuthorsMerge.Add(one, split);
        var format = RoundAuthorsMerge.Add(one, one with { Format = 2, Sources = [one.Sources[0] with { SourceId = "other", Sessions = ["z"] }] });
        var twice = RoundAuthorsMerge.Add(one, one);

        differing.Ok.Should().BeFalse();
        differing.Why.Should().Contain("`split`").And.Contain(Stamp(T0.AddHours(3)));
        format.Why.Should().Contain("formats").And.Contain("1").And.Contain("2");
        twice.Ok.Should().BeFalse("one source added twice would double every count");
        twice.Why.Should().Contain("same store");
        RoundAuthorsMerge.Parse("this is not json").Ok.Should().BeFalse();
        RoundAuthorsMerge.Parse("{\"format\":1,\"selection\":{},\"sources\":[],\"exclusions\":[],\"effortMarkerKnown\":true}").Ok.Should().BeFalse("`cells` is missing");
        RoundAuthorsMerge.Parse("null").Ok.Should().BeFalse();
    }

    [Fact]
    public void ACopyOfAStore_IsNotASecondStore_AndOneDirectoryUnderTwoSpellingsIsOneSource()
    {
        var (a, copy) = (NewStore("a"), NewStore("copy"));
        SeedSessions(a, "x1", "x2");
        SqliteConnection.ClearAllPools();
        File.Copy(Path.Combine(a, "coai.db"), Path.Combine(copy, "coai.db"));

        var original = ReportOf(a);
        var copied = ReportOf(copy);
        var refused = RoundAuthorsMerge.Add(original, copied);

        original.Sources[0].SourceId.Should().NotBe(copied.Sources[0].SourceId, "two paths are two sources");
        refused.Ok.Should().BeFalse("a copy would double every count");
        refused.Why.Should().Contain("2 gate session");
        RoundAuthorsMerge.SourceIdOf(a + Path.DirectorySeparatorChar).Should().Be(RoundAuthorsMerge.SourceIdOf(a), "a trailing separator is the same directory");
        if (OperatingSystem.IsWindows())
        {
            RoundAuthorsMerge.SourceIdOf(a.ToUpperInvariant()).Should().Be(RoundAuthorsMerge.SourceIdOf(a.ToLowerInvariant()), "Windows folds case");
        }

        original.Sources[0].SourceId.Should().MatchRegex("^[0-9a-f]{32}$").And.NotContain(":").And.NotContain("\\");
    }

    [Fact]
    public void TheSameSelectionInADifferentSpelling_MergesAndKeepsBothReadInstants()
    {
        var (a, b) = (NewStore("a"), NewStore("b"));
        SeedSessions(a, "x1");
        SeedSessions(b, "y1");
        var early = RoundAuthorsMerge.Report(a, Selection() with { Kinds = ["code", "test"] }, Stamp(T0.AddDays(10)));
        var later = RoundAuthorsMerge.Report(b, Selection() with { Kinds = ["code", "test"] }, Stamp(T0.AddDays(10).AddSeconds(7)));

        var merged = RoundAuthorsMerge.Add(early, later);

        early.Selection.UntilUtc.Should().BeEmpty("an unset --until is stored as empty, never as the instant it resolved to");
        merged.Ok.Should().BeTrue(merged.Why);
        merged.Answer.Sources.Select(source => source.ReadUtc).Should().Equal([Stamp(T0.AddDays(10)), Stamp(T0.AddDays(10).AddSeconds(7))]);
    }

    [Fact]
    public void AnOlderStoreBesideACurrentOne_Merges_AndTheEffortMarkerIsKnownNoMore()
    {
        var (current, old) = (NewStore("current"), NewStore("old"));
        SeedSessions(current, "x1");
        OlderFile(old, StepIndex(Schema.TheRoundsSize), OlderRound("old1", "D:/r", "b", T0));
        var now = ReportOf(current);
        var before = ReportOf(old);

        var merged = RoundAuthorsMerge.Add(now, before);

        before.EffortMarkerKnown.Should().BeFalse("a step-21 file has no reviewer effort");
        merged.Ok.Should().BeTrue(merged.Why);
        merged.Answer.EffortMarkerKnown.Should().BeFalse();
        merged.Answer.Cells.Should().BeEquivalentTo(now.Cells, options => options.WithStrictOrdering(), "each cell's reviewer-set key is the one it had before the merge");
    }

    [Fact]
    public void TheAnswer_RoundTrips_CarriesNoPath_AndNoFigure()
    {
        var store = NewStore("a");
        SeedSessions(store, "x1", "x2");
        var answer = ReportOf(store);

        var text = RoundAuthorsMerge.Serialize(answer);
        var parsed = RoundAuthorsMerge.Parse(text);

        parsed.Ok.Should().BeTrue(parsed.Why);
        parsed.Answer.Should().BeEquivalentTo(answer, options => options.WithStrictOrdering());
        text.Should().NotContain(store).And.NotContain("D:/repo").And.NotContain("D:\\\\", "the document carries no absolute path: no user name, no workspace layout");
        using var document = JsonDocument.Parse(text);
        var numbers = Numbers(document.RootElement).ToList();
        numbers.Should().NotBeEmpty();
        numbers.All(IsInteger).Should().BeTrue("a ratio cannot hide as a sum: every number is an integer");
    }

    [Fact]
    public void AnUnknownPropertyOrAWrongType_IsARefusal()
    {
        var text = RoundAuthorsMerge.Serialize(AnswerOf("one", ["p"], Round(1, session: "p")));

        RoundAuthorsMerge.Parse(text.Replace("\"format\": 1", "\"format\": 1, \"figure\": 0.5")).Ok.Should().BeFalse("an unknown property is not ignored");
        RoundAuthorsMerge.Parse(text.Replace("\"format\": 1", "\"format\": \"one\"")).Ok.Should().BeFalse("a wrong type");
    }

    private static bool IsInteger(JsonElement number) => number.TryGetInt64(out _);

    private static IEnumerable<JsonElement> Numbers(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => [element],
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Numbers),
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => Numbers(property.Value)),
        _ => [],
    };
}
