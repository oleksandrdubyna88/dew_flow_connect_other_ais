using CoaiMcp.Runners.Reviewers;
using CoaiServer;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The table that decides whether a submission said coherently what it is.
/// </summary>
/// <remarks>
/// Row by row, because it IS a table and the interesting row is the first one: a client that says
/// nothing must keep working, and that is the whole reason the field is nullable rather than
/// defaulted. Collapsing "said nothing" into "said review" would refuse every copy of the extension
/// and the shim already installed.
/// </remarks>
public sealed class JobKindTests
{
    [Fact]
    public void AClientThatSaysNothingIsAReviewAndIsNotJudgedOnIt()
    {
        // The old-client row. `coai-mcp --ask-remote` has never sent a kind and never will until it
        // is released again; refusing it would take every round on every machine down at once.
        JobKinds.Refusal(null, "Architecture").Should().BeNull();
        JobKinds.Refusal(null, null).Should().BeNull("an old client's role has always been optional");
        JobKinds.Refusal("", "").Should().BeNull("an empty string is a client saying nothing");
        JobKinds.Refusal("   ", null).Should().BeNull();

        JobKinds.TryRead(null, out var kind).Should().BeTrue();
        kind.Should().Be(JobKind.Review, "everything written before this field existed was one");
    }

    [Fact]
    public void AJobThatSaysItIsAReviewMustCarryARole()
    {
        JobKinds.Refusal("review", "Architecture").Should().BeNull();

        // It made a CLAIM, and a review is its role. This is the row the extension's `kind: chat`
        // was shipped ahead of the server for: the day this fires, a client sending a blank role and
        // no kind would have been refused with a message about roles, and every chat would stop.
        JobKinds.Refusal("review", null).Should().Contain("needs a role")
            .And.Contain("Architecture", "a refusal names what is allowed");
        JobKinds.Refusal("review", "  ").Should().Contain("needs a role");
    }

    [Fact]
    public void AChatCarriesNoRoleAndSayingOneIsRefused()
    {
        JobKinds.Refusal("chat", null).Should().BeNull();
        JobKinds.Refusal("chat", "").Should().BeNull();

        // Dropping it silently is how a field comes to mean something else: the job would run as a
        // chat while its usage line said Architecture, and the spending page would be wrong forever.
        JobKinds.Refusal("chat", "Architecture").Should().Contain("carries no review role")
            .And.Contain("Architecture", "it names the role it found");
    }

    [Fact]
    public void AKindThisServerDoesNotKnowIsRefusedNamingTheOnesItDoes()
    {
        var refusal = JobKinds.Refusal("conversation", null);

        refusal.Should().Contain("not a kind of job").And.Contain("review").And.Contain("chat");
    }

    [Fact]
    public void SpellingIsNotAContract()
    {
        // Trimmed and case-insensitive, exactly as the role check beside it already is. Refusing
        // `"Chat"` would be a rule about capitalisation wearing a contract's clothes. (gemini.)
        foreach (var said in new[] { "Chat", "CHAT", " chat ", "\tchat\n" })
        {
            JobKinds.TryRead(said, out var kind).Should().BeTrue(said);
            kind.Should().Be(JobKind.Chat, said);
            JobKinds.Refusal(said, null).Should().BeNull(said);
        }
    }

    [Fact]
    public void TheWireSpellingIsWhatEveryClientSends()
    {
        JobKinds.Wire(JobKind.Review).Should().Be("review");
        JobKinds.Wire(JobKind.Chat).Should().Be("chat");
    }

    [Fact]
    public void ANumberIsNotAKindHoweverWellItParses()
    {
        // Enum.TryParse also accepts the underlying numbers, so a kind of "1" arrived as a chat and
        // "0" as a review — values no contract mentions, from a caller who cannot have meant them,
        // deciding what a spending row says. (codex, the code round.)
        foreach (var number in new[] { "0", "1", "-1", "99" })
        {
            JobKinds.TryRead(number, out _).Should().BeFalse(number);
            JobKinds.Refusal(number, null).Should().Contain("not a kind of job", number);
        }
    }

    [Fact]
    public void TheServerAndTheLedgerSpellTheKindsTheSameWay()
    {
        // Two vocabularies for one field is how a writer and a reader come to disagree about what a
        // spending row means — and the reader mapping anything it does not know to "review" would
        // HIDE that disagreement rather than report it. The ledger lives in src_mcp, which cannot
        // see this enum, so the two are held together here. (codex, the code round.)
        Enum.GetValues<JobKind>().Select(JobKinds.Wire)
            .Should().BeEquivalentTo(UsageKinds.Known);

        foreach (var kind in Enum.GetValues<JobKind>())
        {
            UsageKinds.IsKnown(JobKinds.Wire(kind)).Should().BeTrue(JobKinds.Wire(kind));
        }

        UsageKinds.IsKnown("").Should().BeTrue("an empty kind is a line that did not say, which is fine");
        UsageKinds.IsKnown("rehearsal").Should().BeFalse();
    }
}
