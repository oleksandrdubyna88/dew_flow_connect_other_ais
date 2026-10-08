using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Feature;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The <c>coai-lookup</c> block an antigravity consultant writes to ask coai to list a folder or search for text
/// (research/PLAN_agy_searches_through_coai.md §3): read from prose, never a regex, at most
/// <see cref="LookupBudget.RequestsPerTurn"/> lines, every other line named as refused.
/// </summary>
public sealed class LookupRequestsTests
{
    private static string Block(params string[] lines) =>
        "```" + LookupRequests.Fence + "\n" + string.Join("\n", lines) + "\n```";

    [Fact]
    public void ABlockOfListAndSearch_IsRead_AndTheProseAroundItIsKeptAsTheDraft()
    {
        var ask = LookupRequests.Read("I need to look first.\n\n" + Block("list projB", "search \"SMTP retry\" in D:/rsd/projB") + "\n\nThen I will answer.");

        ask.HadBlock.Should().BeTrue();
        ask.Requests.Should().HaveCount(2);
        ask.Requests[0].Should().Be(new LookupRequest.List("list projB", "projB"));
        ask.Requests[1].Should().Be(new LookupRequest.Search("search \"SMTP retry\" in D:/rsd/projB", "SMTP retry", "D:/rsd/projB"));
        ask.Refused.Should().BeEmpty();
        ask.Prose.Should().Be("I need to look first.\n\nThen I will answer.");
    }

    [Fact]
    public void AnAnswerWithNoBlock_IsTheAnswer_AndAsksForNothing()
    {
        var ask = LookupRequests.Read("No helper exists; mailer.ts sends once.");

        ask.HadBlock.Should().BeFalse();
        ask.Requests.Should().BeEmpty();
        ask.Prose.Should().Be("No helper exists; mailer.ts sends once.");
    }

    [Fact]
    public void ASearchWithoutAFolder_HasAnEmptyPath_AndItsTextIsKeptLiterally()
    {
        var ask = LookupRequests.Read(Block("search \"a.c(\" "));

        ask.Requests.Should().ContainSingle().Which.Should().Be(new LookupRequest.Search("search \"a.c(\"", "a.c(", string.Empty));
    }

    [Fact]
    public void APathWithSpaces_MayBeQuoted()
    {
        var ask = LookupRequests.Read(Block("list \"My Projects/app one\"", "search \"x\" in \"My Projects/app one\""));

        ask.Requests.Should().Equal(
            new LookupRequest.List("list \"My Projects/app one\"", "My Projects/app one"),
            new LookupRequest.Search("search \"x\" in \"My Projects/app one\"", "x", "My Projects/app one"));
    }

    [Fact]
    public void LinesPastTheCap_AndLinesThatAreNoRequest_AreRefusedByName()
    {
        var lines = Enumerable.Range(1, LookupBudget.RequestsPerTurn).Select(n => $"list p{n}").ToList();
        lines.Add("list p-too-many");
        lines.Add("rm -rf /");
        lines.Add("search unquoted");

        var ask = LookupRequests.Read(Block([.. lines]));

        ask.Requests.Should().HaveCount(LookupBudget.RequestsPerTurn);
        ask.Refused.Should().HaveCount(3);
        ask.Refused[0].Should().Contain("list p-too-many").And.Contain($"{LookupBudget.RequestsPerTurn} requests");
        ask.Refused[1].Should().Contain("rm -rf /").And.Contain("not a lookup");
        ask.Refused[2].Should().Contain("search unquoted").And.Contain("quote");
    }

    [Fact]
    public void TwoBlocks_AreReadTogether_UnderOneCap()
    {
        var ask = LookupRequests.Read(Block("list a") + "\nand\n" + Block("list b"));

        ask.Requests.Select(r => ((LookupRequest.List)r).Path).Should().Equal("a", "b");
        ask.Prose.Should().Be("and");
    }

    [Fact]
    public void AnUnclosedBlock_IsStillARequest_SoAModelThatRanOutOfTokensIsNotTakenAsAnAnswer()
    {
        var ask = LookupRequests.Read("Looking:\n```" + LookupRequests.Fence + "\nlist projA");

        ask.HadBlock.Should().BeTrue();
        ask.Requests.Should().ContainSingle();
        ask.Prose.Should().Be("Looking:");
    }

    [Fact]
    public void AFenceGluedToTheProse_IsStillABlock_AsTheRealModelWroteIt()
    {
        // Live, 2026-10-08, Windows run 2 (research/RESULTS_agy_searches_through_coai.md): agy wrote the fence at the end
        // of a sentence, and the row's ANSWER was the block itself.
        var ask = LookupRequests.Read(
            "We have 1 lookup left. Let's do a lookup to see what exists.```" + LookupRequests.Fence + "\n"
            + "list D:\\rsd\\ClaudeRag\\email-switcher\n"
            + "search \"smtp\" in D:\\rsd\\dew_flow_connect_other_ais```\nThen I will answer.");

        ask.HadBlock.Should().BeTrue();
        ask.Requests.Should().HaveCount(2, "a closing fence glued to the last line closes the block and keeps the line");
        ((LookupRequest.Search)ask.Requests[1]).Path.Should().Be("D:\\rsd\\dew_flow_connect_other_ais");
        ask.Prose.Should().Be("We have 1 lookup left. Let's do a lookup to see what exists.\nThen I will answer.");
        ask.Refused.Should().BeEmpty();
    }

    [Fact]
    public void AnEmptyBlock_IsABlock_WithNothingAsked()
    {
        var ask = LookupRequests.Read("Draft.\n" + Block());

        ask.HadBlock.Should().BeTrue();
        ask.Requests.Should().BeEmpty();
        ask.Prose.Should().Be("Draft.");
    }
}
