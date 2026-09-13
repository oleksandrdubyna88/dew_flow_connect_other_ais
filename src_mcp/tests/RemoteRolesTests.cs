using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a Team server told this machine it will run, and what a round does with each answer.
/// </summary>
/// <remarks>
/// <para><b>Three states, not two.</b> A server that ANSWERED and a server that could not be REACHED
/// both leave a client without a list, and reading them alike is how a network blip becomes a
/// silently smaller round. The fallback — the five this product ships — is right for a server that
/// answered without the field, and it is a fabrication for one that never answered at all: the round
/// still runs the same reviewers either way, but only one of the two can honestly say why.</para>
/// <para><b>The absent-field rule this family has paid for twice.</b> `remoteVendor` was dropped by
/// every component written before it existed and the signature was a 400 nobody could read. Absent
/// means what was true before the field: never "none", which would silently empty every round, and
/// never "any", which would send custom roles to a server certain to refuse them.</para>
/// </remarks>
public sealed class RemoteRolesTests
{
    private static RemoteRoles Answered(bool allowAny, params string[] names) =>
        new(names, allowAny, RemoteRolesSource.Answered);

    // ---------- a server that answered ----------

    [Fact]
    public void ARoleTheServerNAMEDIsCarried()
    {
        Answered(false, "Architecture", "Requirements")
            .WhyNot("Requirements", "Requirements", builtIn: false).Should().BeNull();
    }

    [Fact]
    public void ARoleTheServerDidNotNameIsRefusedInTheServersOwnWords()
    {
        var why = Answered(false, "Architecture", "Requirements").WhyNot("Brief", "Brief", builtIn: false);

        why.Should().NotBeNull();
        why.Should().Contain("Architecture").And.Contain("Requirements").And.Contain("Brief");
    }

    [Fact]
    public void AShippedRoleTheServerDidNotNameIsStillRefused()
    {
        // If the server said which roles it runs, that list is the whole truth about it — including
        // about the five. A server configured not to run one of them is a server that will 400 it,
        // and sending it anyway to be polite about the shipped catalog would waste a round.
        Answered(false, "Requirements").WhyNot("Architecture", "Architecture", builtIn: true).Should().NotBeNull();
    }

    [Fact]
    public void TheListIsMatchedWithoutCase()
    {
        // The server matches without case and canonicalises what it stores. A client refusing
        // `requirements` against a catalog naming `Requirements` would never reach that.
        // (gemini, plan 3's plan round.)
        Answered(false, "Requirements").WhyNot("requirements", "requirements", builtIn: false).Should().BeNull();
        Answered(false, "Requirements").WhyNot("REQUIREMENTS", "REQUIREMENTS", builtIn: false).Should().BeNull();
    }

    [Fact]
    public void AServerThatRunsAnythingCarriesARoleItNeverNamed()
    {
        Answered(allowAny: true, "Architecture").WhyNot("Invented", "Invented", builtIn: false).Should().BeNull();
    }

    // ---------- a server that answered WITHOUT the field ----------

    [Fact]
    public void AnOlderServerCarriesTheFiveThisProductShips()
    {
        var older = new RemoteRoles([], false, RemoteRolesSource.Shipped);

        older.WhyNot("Architecture", "Architecture", builtIn: true).Should().BeNull("exactly what happened before this plan");
    }

    [Fact]
    public void AnOlderServerRefusesARoleYouAddedAndSaysItIsOlder()
    {
        var why = new RemoteRoles([], false, RemoteRolesSource.Shipped).WhyNot("Requirements", "Requirements", builtIn: false);

        why.Should().NotBeNull();
        why.Should().Contain("older").And.Contain("Requirements");
    }

    // ---------- a server that could not be asked ----------

    [Fact]
    public void AnUnreachableServerStillCarriesTheShippedFive()
    {
        // The round must not get smaller because a catalog fetch timed out. The five have always
        // worked on every Team server; nothing about a failed request makes that less true.
        new RemoteRoles([], false, RemoteRolesSource.Unreachable)
            .WhyNot("Architecture", "Architecture", builtIn: true).Should().BeNull();
    }

    [Fact]
    public void AnUnreachableServerSaysSoRatherThanClaimingTheServerRefused()
    {
        // The distinction the plan round insisted on: "could not be asked" is not "said no". One is
        // fixed by looking at the network, the other by configuring the server.
        var why = new RemoteRoles([], false, RemoteRolesSource.Unreachable).WhyNot("Requirements", "Requirements", builtIn: false);

        why.Should().Contain("could not be asked");
        why.Should().NotContain("older", "nothing here knows how old it is");
    }

    [Fact]
    public void AServerNobodyHasAskedYetSaysThatInsteadOfGuessing()
    {
        var why = RemoteRoles.Unknown.WhyNot("Requirements", "Requirements", builtIn: false);

        why.Should().Contain("not been asked yet");
        RemoteRoles.Unknown.WhyNot("Architecture", "Architecture", builtIn: true)
            .Should().BeNull("the shipped five are carried by every server, asked or not");
    }

    // ---------- every state agrees about the five ----------

    [Theory]
    [InlineData(RemoteRolesSource.NotAsked)]
    [InlineData(RemoteRolesSource.Unreachable)]
    [InlineData(RemoteRolesSource.Shipped)]
    public void NothingButAnAnswerCanTakeAShippedRoleOutOfARound(RemoteRolesSource source)
    {
        // The floor, stated once: whatever went wrong, a round keeps the reviewers it has always
        // had. Only a server that actually SAID otherwise can change that.
        new RemoteRoles([], false, source).WhyNot("Architecture", "Architecture", builtIn: true).Should().BeNull(source.ToString());
    }
}
