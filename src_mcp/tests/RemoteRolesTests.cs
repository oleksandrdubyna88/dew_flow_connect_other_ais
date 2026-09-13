using CoaiMcp.Core.Rounds;
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

        why.Should().Be("'Requirements' is a role you added, and this Team server has not been asked which roles it runs yet");
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

    // ---------- a catalog is a promise, and both clients must break it the same way ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ARoleListCarryingNothingUsableIsReadAsSayingNothing(string? rubbish)
    {
        // `IReadOnlyList<string>?` is a nullable ANNOTATION, not a runtime check: `{"roles":[null]}`
        // deserialises to a list with a null in it. Counting that as an answer made this half exclude
        // every shipped role while the extension read the same response as "said nothing useful" —
        // one server, two clients, two different rounds. (codex, story 4's second code round.)
        var read = RemoteRoles.From([rubbish], allowAny: false);

        read.Source.Should().Be(RemoteRolesSource.Shipped);
        read.WhyNot("Architecture", "Architecture", builtIn: true)
            .Should().BeNull("the floor holds whatever arrives");
    }

    [Fact]
    public void TheUsableNamesSurviveAListThatIsOnlyPartlyRubbish()
    {
        var read = RemoteRoles.From([null, "Requirements", "", "Brief"], allowAny: false);

        read.Source.Should().Be(RemoteRolesSource.Answered);
        read.Names.Should().BeEquivalentTo(["Requirements", "Brief"]);
    }

    [Fact]
    public void AnAbsentListAndAnEmptyOneAreTheSameAnswer()
    {
        RemoteRoles.From(null, allowAny: false).Source.Should().Be(RemoteRolesSource.Shipped);
        RemoteRoles.From([], allowAny: false).Source.Should().Be(RemoteRolesSource.Shipped);
    }

    [Fact]
    public void AListThatSaysNothingDropsTheAllowAnyFlagWithIt()
    {
        // `allowAnyRole: true` with no usable list is a catalog contradicting itself. Carrying the
        // flag through would make one malformed response accept every role on that server.
        RemoteRoles.From([null], allowAny: true).AllowAny.Should().BeFalse();
    }

    [Fact]
    public void ARealAnswerKeepsTheFlagItSent()
    {
        RemoteRoles.From(["Requirements"], allowAny: true).AllowAny.Should().BeTrue();
    }

    // ---------- a DOCUMENT built-in is newer than any server that has not said (plan 4's tail) ----------

    /// <summary>
    /// A server that has NOT said its roles cannot be sent a document role, built-in or not.
    /// </summary>
    /// <remarks>
    /// <para>The fallback was <c>builtIn ? null : …</c> — "the five this product ships are carried by
    /// every Team server" — and it was true for exactly as long as there were five. Plan 4 added two
    /// document roles to the seed, so this client now calls seven roles built in while every deployed
    /// server knows five: the Team server deploy is MANUAL, so shipping a tag does not put the new
    /// roles on the box.</para>
    /// <para>What that costs is measured elsewhere in this product's history and is always the same
    /// shape: the round sends a role the server has never heard of, gets a 400 naming the roles it
    /// does run, and reports a failed reviewer — seconds per round and zero tokens, for a reviewer
    /// that was never going to answer.</para>
    /// <para>The rule that holds instead: a server that has not said its roles predates the field
    /// that carries them, which predates the document roles. So "would every server know this" is
    /// "is it one of the five" — built in AND a programming task.</para>
    /// </remarks>
    [Theory]
    [InlineData(RemoteRolesSource.Shipped, RoleCatalog.DocumentRole)]
    [InlineData(RemoteRolesSource.Unreachable, RoleCatalog.DocumentRole)]
    [InlineData(RemoteRolesSource.NotAsked, RoleCatalog.DocumentRole)]
    [InlineData(RemoteRolesSource.Shipped, RoleCatalog.DocumentSummaryRole)]
    [InlineData(RemoteRolesSource.Unreachable, RoleCatalog.DocumentSummaryRole)]
    [InlineData(RemoteRolesSource.NotAsked, RoleCatalog.DocumentSummaryRole)]
    public void ADocumentRoleIsNotSentToAServerThatHasNotSaid(RemoteRolesSource source, string role)
    {
        // BOTH shipped document roles, not just the first: codex pointed out on the plan round that
        // covering one of a pair leaves the other free to reach a legacy server while the suite stays
        // green.
        var why = new RemoteRoles([], false, source).WhyNot(role, "The document", builtIn: true);

        why.Should().NotBeNull("a deployed server runs the five and this is not one of them");
        why.Should().Contain("The document");
        why.Should().NotContain("you added", "it is a role this PRODUCT added, not the person");
    }

    /// <summary>
    /// The frozen list is exactly the five, and it is a fact about the past rather than a predicate.
    /// </summary>
    /// <remarks>
    /// Pinned as a literal because the two proxies that preceded it — <c>builtIn</c>, then
    /// <c>builtIn &amp;&amp; programmingTask</c> — were each correct about the seed of the day and
    /// silently wrong the next time it grew.
    /// </remarks>
    [Fact]
    public void TheRolesEveryServerRuns_AreTheFiveThatPredateTheCatalogField()
    {
        RemoteRoles.BeforeTheCatalog.Should().BeEquivalentTo([
            "PlanCritique", "Conventions", "Architecture", "SecurityReliability", "UxDxPerformance",
        ]);
        RemoteRoles.BeforeTheCatalog.Should().NotContain(RoleCatalog.DocumentRole);
        RemoteRoles.BeforeTheCatalog.Should().NotContain(RoleCatalog.DocumentSummaryRole);
    }

    /// <summary>
    /// A role a PERSON added and a role this PRODUCT added are different news, and say so.
    /// </summary>
    [Fact]
    public void WhoAddedTheRole_ChangesTheSentence()
    {
        var server = new RemoteRoles([], false, RemoteRolesSource.Shipped);

        server.WhyNot("Requirements", "Requirements", builtIn: false).Should().Contain("you added");
        server.WhyNot(RoleCatalog.DocumentRole, "The document", builtIn: true)
            .Should().Contain("this product added");
    }

    /// <summary>The regression guard: the five are still carried by all three of those states.</summary>
    [Theory]
    [InlineData(RemoteRolesSource.Shipped)]
    [InlineData(RemoteRolesSource.Unreachable)]
    [InlineData(RemoteRolesSource.NotAsked)]
    public void ACodeBuiltInIsStillCarriedByAServerThatHasNotSaid(RemoteRolesSource source)
    {
        foreach (var role in RemoteRoles.BeforeTheCatalog)
        {
            new RemoteRoles([], false, source).WhyNot(role, role, builtIn: true)
                .Should().BeNull($"{role} has been on every Team server since before it could say so");
        }
    }

    /// <summary>
    /// A server that HAS said decides for itself, about a document role like any other.
    /// </summary>
    [Fact]
    public void AServerThatNamedTheDocumentRoleRunsIt() =>
        Answered(false, RoleCatalog.DocumentRole)
            .WhyNot(RoleCatalog.DocumentRole, "The document", builtIn: true)
            .Should().BeNull();

    [Fact]
    public void AServerThatDidNotNameTheDocumentRoleRefusesIt() =>
        Answered(false, RoleCatalog.ArchitectureRole)
            .WhyNot(RoleCatalog.DocumentRole, "The document", builtIn: true)
            .Should().NotBeNull();

    /// <summary>
    /// A row a PERSON wrote that borrows a shipped name is not the shipped role.
    /// </summary>
    /// <remarks>
    /// The membership check was on the NAME alone, and codex pointed out what that claims: that
    /// anything called <c>Architecture</c> is the Architecture every Team server has run since before
    /// it could say so. Provenance decides with it now. Unreachable through the real caller today —
    /// a catalog row whose id names a built-in IS that built-in — and one <c>&amp;&amp;</c> is a
    /// cheaper guarantee than the argument that it cannot happen.
    /// </remarks>
    [Fact]
    public void ARoleOfYourOwnBorrowingAShippedName_IsNotCarried() =>
        new RemoteRoles([], false, RemoteRolesSource.Shipped)
            .WhyNot(RoleCatalog.ArchitectureRole, "Architecture", builtIn: false)
            .Should().NotBeNull("a name is not a claim about who shipped it");

    /// <summary>The fact about the past cannot be edited at runtime.</summary>
    /// <remarks>
    /// It was a <c>HashSet</c> behind an <c>IReadOnlySet</c>, which a caller can cast back and add
    /// to — and a role added there is a role sent to every old server for the life of the process.
    /// </remarks>
    [Fact]
    public void TheFrozenList_CannotBeAddedTo() =>
        RemoteRoles.BeforeTheCatalog.Should().BeAssignableTo<System.Collections.Frozen.FrozenSet<string>>(
            "a set somebody can Add to is not a fact about the past");

    /// <summary>The refusal says why the server cannot, not what else it could.</summary>
    [Fact]
    public void TheOldServerSentence_DoesNotReciteTheFive()
    {
        var why = new RemoteRoles([], false, RemoteRolesSource.Shipped)
            .WhyNot(RoleCatalog.DocumentRole, "The document", builtIn: true);

        why.Should().Contain("older than the setting that names roles");
        foreach (var role in RemoteRoles.BeforeTheCatalog)
        {
            why.Should().NotContain(role, "the ids of five roles nobody asked about are noise");
        }
    }
}
