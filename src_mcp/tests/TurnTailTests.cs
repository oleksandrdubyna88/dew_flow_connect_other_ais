using System.Collections.Immutable;
using CoaiMcp.Core.Feature;
using CoaiMcp.Core.Findings;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The tail a follow-up turn appends to the base prompt (plan §4.9, D25; S3.2): the plan's order, the
/// FINAL marker on the last turn, earlier turns' source kept, and "turn k of N" nowhere but here.
/// </summary>
public sealed class TurnTailTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    private static Finding Major(string file, int line, string title) =>
        new(Severity.Major, Category.Reliability, file, line, title, "why", "fix", ["codex"]);

    private static ServedSlice Slice(string file, string symbol, int from, int to, string text) =>
        new(file, symbol, from, to, 49, Sha, text, string.Empty, "why");

    private static TurnTailInput Input(
        int turn = 2, int turns = 4, bool final = false,
        IReadOnlyList<Finding>? findings = null, IReadOnlyList<SourceRequest>? requests = null,
        IReadOnlyList<ServedSlice>? earlier = null, ServedTurn? now = null) =>
        new(turn, turns,
            findings ?? [],
            requests ?? [new SourceRequest("src/Cart.cs", "Cart.Add", 0, 0, "does Add check?")],
            [],
            earlier ?? [],
            now ?? new ServedTurn([Slice("src/Cart.cs", "Add", 28, 35, "secretAdd(item);")], [], new SourceSpend(16)),
            final);

    [Fact]
    public void TheTail_IsInThePlansOrder_FindingsThenRequestsThenServedCodeThenRefusalsThenTheInstruction()
    {
        var tail = TurnTail.Render(Input(
            findings: [Major("src/Cart.cs", 28, "Add never checks")],
            now: new ServedTurn(
                [Slice("src/Cart.cs", "Add", 28, 35, "secretAdd(item);")],
                [new SourceRefusal("config/.env", string.Empty, "looks like a credential file (.env*)")],
                new SourceSpend(16))));

        var positions = new[]
        {
            tail.IndexOf("## Turn 2 of 4", StringComparison.Ordinal),
            tail.IndexOf("[major/reliability] src/Cart.cs:28 — Add never checks", StringComparison.Ordinal),
            tail.IndexOf("src/Cart.cs `Cart.Add` — does Add check?", StringComparison.Ordinal),
            tail.IndexOf("### src/Cart.cs lines 28-35 of 49 @ " + Sha + " — Add", StringComparison.Ordinal),
            tail.IndexOf("secretAdd(item);", StringComparison.Ordinal),
            tail.IndexOf("not served: config/.env — looks like a credential file", StringComparison.Ordinal),
            tail.IndexOf(TurnTail.OnlyThisTurnCounts, StringComparison.Ordinal),
            tail.IndexOf(TurnTail.AskAgain, StringComparison.Ordinal),
        };

        positions.Should().OnlyContain(at => at >= 0, "every part is there");
        positions.Should().BeInAscendingOrder("and in the plan's order");
        tail.Should().StartWith("\n\n", "the tail separates itself from a base that may end without a newline");
        tail.Should().NotContain("FINAL:", "turn 2 of 4 is not the last");
    }

    [Fact]
    public void TheLastTurn_IsToldFinal_AndNotToAskAgain()
    {
        var tail = TurnTail.Render(Input(turn: 4, turns: 4, final: true));

        tail.Should().Contain("## Turn 4 of 4").And.Contain(TurnTail.Final).And.NotContain(TurnTail.AskAgain);
    }

    [Fact]
    public void ASpentBudget_IsFinalBeforeTheCap()
    {
        TurnTail.Render(Input(turn: 2, turns: 4, final: true)).Should().Contain(TurnTail.Final);
    }

    [Fact]
    public void SourceServedInEarlierTurns_IsRepeatedBeforeThisTurns()
    {
        var tail = TurnTail.Render(Input(
            turn: 3,
            earlier: [Slice("src/Shop.cs", "Buy", 3, 7, "var total = n * 2;")],
            now: new ServedTurn([Slice("src/Cart.cs", "Add", 28, 35, "secretAdd(item);")], [], new SourceSpend(40))));

        tail.IndexOf("### Served in earlier turns", StringComparison.Ordinal).Should().BePositive();
        tail.IndexOf("var total = n * 2;", StringComparison.Ordinal).Should().BeLessThan(
            tail.IndexOf("### Served for turn 3", StringComparison.Ordinal), "what was read before is still in hand, before what is new");
        tail.Should().Contain("secretAdd(item);");
    }

    [Fact]
    public void NothingAskedAndNothingServed_AreSaidAsSuch_NeverAsEmptySections()
    {
        var tail = TurnTail.Render(Input(
            requests: [],
            now: new ServedTurn([], [], SourceSpend.None)));

        tail.Should().Contain("### What you asked for\n\nnothing").And.Contain("### Served for turn 2\n\nnothing");
        tail.Should().Contain("### Your findings from turn 1 (compact)\n\nnone");
    }

    [Fact]
    public void ACompactFinding_NamesItsPlaceOnlyWhenItHasOne()
    {
        TurnTail.Compact(Major("src/Cart.cs", 28, "Add never checks")).Should().Be("[major/reliability] src/Cart.cs:28 — Add never checks");
        TurnTail.Compact(Major("src/Cart.cs", 0, "somewhere in the file")).Should().Be("[major/reliability] src/Cart.cs — somewhere in the file");
        TurnTail.Compact(Major(string.Empty, 0, "the plan itself")).Should().Be("[major/reliability] the plan itself");
        TurnTail.Compact(Major("a.cs", 1, "a title\nover two lines")).Should().NotContain("\n", "one line per finding, or the list breaks");
    }

    [Fact]
    public void ARejectedRequest_IsNamedWithItsReason()
    {
        var tail = TurnTail.Render(Input() with { Rejected = ImmutableArray.Create(new RejectedEntry(2, "the path leaves the repository")) });

        tail.Should().Contain("- request 2 could not be read: the path leaves the repository");
    }
}
