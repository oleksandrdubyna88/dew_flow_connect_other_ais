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
}
