using CoaiMcp.Core.Rounds;
using CoaiServer;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// Which review roles this server will run, and the one place that decides.
/// </summary>
/// <remarks>
/// <para><b>Why it is a type rather than two checks.</b> <c>RoleCatalog.Builtin.Roles</c> was
/// enumerated in TWO places already — <see cref="ReviewEndpoints"/>' unknown-role refusal and
/// <see cref="JobKinds"/>' missing-role refusal — both answering the same question in their own
/// words. Adding a configured list would have written the third. Plan 2 shipped with three
/// independent counts of "which code roles will run" and the one nobody updated promised four
/// reviewers under a page drawing five boxes; this is that shape, refused before it is built.</para>
/// <para>Every rule lives here and is reachable without a host: the shape of an id, its length, what
/// an operator may add, what "any" means, and what a refusal says.</para>
/// </remarks>
public sealed class AcceptedRolesTests
{
    private static readonly string Shipped = RoleCatalog.Builtin.Roles[0].Id;

    /// <summary>A server nobody reconfigured. Everything below is measured against this.</summary>
    private static AcceptedRoles Default => AcceptedRoles.From([], allowAny: false);

    // ---------- the default is what happens today ----------

    [Fact]
    public void AServerNobodyConfiguredKnowsTheFiveItShipsWith()
    {
        foreach (var role in RoleCatalog.Builtin.Roles)
        {
            Default.Knows(role.Id).Should().BeTrue(role.Id);
        }

        Default.Names.Should().BeEquivalentTo(RoleCatalog.Builtin.Roles.Select(r => r.Id));
        Default.Knows("Requirements").Should().BeFalse("nobody configured it");
    }

    [Fact]
    public void AShippedRoleIsKnownWhateverCaseItArrivesIn()
    {
        // A client that lower-cases its roles is a client, not a mistake — and the canonicalisation
        // that would reconcile the two never runs if membership refuses it first.
        Default.Knows(Shipped.ToUpperInvariant()).Should().BeTrue();
        Default.Knows(Shipped.ToLowerInvariant()).Should().BeTrue();
    }

    // ---------- what an operator may add ----------

    [Fact]
    public void AConfiguredRoleIsKnownBesideTheShippedOnes()
    {
        var roles = AcceptedRoles.From(["Requirements"], allowAny: false);

        roles.Knows("Requirements").Should().BeTrue();
        roles.Knows(Shipped).Should().BeTrue("the shipped five never go away");
        roles.Names.Should().Contain("Requirements");
    }

    [Fact]
    public void AConfiguredRoleIsMatchedWithoutCaseAndRecordedAsTheOperatorSpelledIt()
    {
        var roles = AcceptedRoles.From(["Requirements"], allowAny: false);

        roles.Knows("requirements").Should().BeTrue();
        roles.Canonical("requirements").Should().Be("Requirements");
        roles.Canonical("REQUIREMENTS").Should().Be("Requirements");
    }

    // ---------- a configured id that cannot work is refused AT BOOT ----------

    [Theory]
    [InlineData("My-Role")]      // a hyphen: COAI_ROUNDS_MY-ROLE is not exportable
    [InlineData("123Role")]      // starts with a digit
    [InlineData("my role")]      // a space
    [InlineData("")]
    public void AConfiguredIdThatCouldNeverRunStopsTheServer(string bad)
    {
        // At boot, naming it — not on every request, where the operator is not looking. A server
        // whose configuration would be refused on every request is misconfigured, and a misconfigured
        // server must not start quietly. (local, the plan round, Blocking.)
        var boom = () => AcceptedRoles.From([bad], allowAny: false);

        boom.Should().Throw<InvalidOperationException>()
            .WithMessage("*Coai:ExtraRoles*");
    }

    [Fact]
    public void AConfiguredIdLongerThanTheBoundStopsTheServerToo()
    {
        var boom = () => AcceptedRoles.From([new string('R', AcceptedRoles.MaxIdLength + 1)], allowAny: false);

        boom.Should().Throw<InvalidOperationException>().WithMessage("*Coai:ExtraRoles*");
    }

    [Fact]
    public void AConfiguredIdExactlyAtTheBoundIsFine()
    {
        var longest = new string('R', AcceptedRoles.MaxIdLength);

        AcceptedRoles.From([longest], allowAny: false).Knows(longest).Should().BeTrue();
    }

    // ---------- "any" means any ROLE, not any string ----------

    [Fact]
    public void AllowAnyRoleAcceptsAWellFormedIdNobodyConfigured()
    {
        AcceptedRoles.From([], allowAny: true).Knows("Requirements").Should().BeTrue();
    }

    [Theory]
    [InlineData("My-Role")]
    [InlineData("123Role")]
    [InlineData("role with spaces")]
    [InlineData("role:123")]
    [InlineData("Requirements\n")]
    public void AllowAnyRoleStillRefusesSomethingThatIsNotAnId(string bad)
    {
        // The regex bounds the ALPHABET; an id also becomes COAI_ROUNDS_<ID>, reaches JobRecord, the
        // idempotency fingerprint and an append-only ledger. "Any" is the escape hatch for the
        // NAMING of a role, never for its shape. (local and codex, the plan round.)
        AcceptedRoles.From([], allowAny: true).Knows(bad).Should().BeFalse(bad);
    }

    [Fact]
    public void AllowAnyRoleRefusesAnIdLongerThanTheBound()
    {
        var roles = AcceptedRoles.From([], allowAny: true);

        roles.Knows(new string('R', AcceptedRoles.MaxIdLength)).Should().BeTrue("the boundary itself");
        roles.Knows(new string('R', AcceptedRoles.MaxIdLength + 1)).Should().BeFalse("one past it");
    }

    // ---------- one role is one ledger row, on every server ----------

    [Fact]
    public void AllowAnyRoleFoldsTheCaseSoOneRoleIsOneRow()
    {
        // The first draft of this plan wrote the SPLIT down as an acceptable price and asked the
        // operator about it. Three reviewers refused that independently, and one named the answer:
        // fold the id. Nobody has to configure anything for two clients to agree.
        var roles = AcceptedRoles.From([], allowAny: true);

        roles.Canonical("Requirements").Should().Be(roles.Canonical("requirements"));
        roles.Canonical("REQUIREMENTS").Should().Be(roles.Canonical("requirements"));
    }

    [Fact]
    public void AShippedRoleKeepsTheCatalogsSpellingEvenUnderAllowAnyRole()
    {
        // Folding everything would rename the five this product ships, in the ledger and in the log.
        AcceptedRoles.From([], allowAny: true).Canonical(Shipped.ToLowerInvariant()).Should().Be(Shipped);
    }

    [Fact]
    public void AConfiguredSpellingWinsOverFoldingWhenBothCouldApply()
    {
        var roles = AcceptedRoles.From(["Requirements"], allowAny: true);

        roles.Canonical("requirements").Should().Be("Requirements", "an operator said how to spell it");
    }

    [Fact]
    public void ARoleNothingKnowsIsHandedBackUnchanged()
    {
        // It is about to be refused; changing it on the way to the refusal would name the wrong id.
        Default.Canonical("Requirements").Should().Be("Requirements");
    }

    // ---------- a refusal names the rule it broke ----------

    [Fact]
    public void AnUnknownRoleIsRefusedByNamingWhatIsAccepted()
    {
        var why = Default.Refusal("Requirements");

        why.Should().NotBeNull();
        why.Should().Contain("Requirements").And.Contain(Shipped);
    }

    [Fact]
    public void AnIdThatIsNotAnIdIsRefusedByNamingTHATRatherThanListingRoles()
    {
        // Under AllowAnyRole a shape failure answering "Allowed: PlanCritique, Conventions, …" tells
        // an operator the server accepts five when it accepts anything — which is the opposite of
        // what is wrong. (gemini and local, the plan round.)
        var why = AcceptedRoles.From([], allowAny: true).Refusal("My-Role");

        why.Should().NotBeNull();
        why.Should().NotContain(Shipped, "the accepted set is not what this one broke");
        why.Should().Contain("COAI_ROUNDS_", "the reason a role id has this shape at all");
    }

    [Fact]
    public void AnAcceptedRoleIsRefusedByNothing()
    {
        Default.Refusal(Shipped).Should().BeNull();
        Default.Refusal(Shipped.ToLowerInvariant()).Should().BeNull();
        AcceptedRoles.From(["Requirements"], allowAny: false).Refusal("requirements").Should().BeNull();
        AcceptedRoles.From([], allowAny: true).Refusal("Anything").Should().BeNull();
    }

    [Fact]
    public void NamesIsWhatEveryMessageDerivesFrom()
    {
        // One list. A message that built its own would drift from the gate that refuses.
        var roles = AcceptedRoles.From(["Requirements"], allowAny: false);

        roles.Names.Should().BeEquivalentTo(
            RoleCatalog.Builtin.Roles.Select(r => r.Id).Append("Requirements"));
    }

    // ---------- what a configured entry may NOT quietly change ----------

    [Fact]
    public void AConfiguredEntryNamingAShippedRoleKeepsTheCatalogsSpelling()
    {
        // An operator writing `architecture` has named a role this product already ships, and a
        // built-in's identity is FIXED — its id keys settings, open sessions and every recorded
        // round, which is the rule plan 2 settled on the page. The entry is accepted, because it
        // names a role this server runs; the catalog's spelling is what gets recorded.
        var roles = AcceptedRoles.From([Shipped.ToLowerInvariant()], allowAny: false);

        roles.Knows(Shipped).Should().BeTrue();
        roles.Canonical(Shipped.ToLowerInvariant()).Should().Be(Shipped);
        roles.Names.Should().ContainSingle(n => string.Equals(n, Shipped, StringComparison.OrdinalIgnoreCase),
            "it is one role, listed once");
    }

    [Fact]
    public void TwoConfiguredEntriesDifferingOnlyInCaseAreOneRoleAndTheFirstWins()
    {
        var roles = AcceptedRoles.From(["Requirements", "requirements"], allowAny: false);

        roles.Names.Should().ContainSingle(n => string.Equals(n, "Requirements", StringComparison.OrdinalIgnoreCase));
        roles.Canonical("REQUIREMENTS").Should().Be("Requirements", "the spelling that arrived first");
    }

    [Fact]
    public void NamesCannotBeEditedByWhoeverIsHandedIt()
    {
        // It is about to be served on the catalog endpoint. A consumer that downcast it and edited it
        // would make this server advertise a role `Knows` refuses. (codex, story 1's code round.)
        var roles = AcceptedRoles.From(["Requirements"], allowAny: false);

        (roles.Names as ICollection<string>)?.IsReadOnly.Should().NotBe(false);
        var edit = () => ((IList<string>)roles.Names).Add("Invented");
        edit.Should().Throw<NotSupportedException>();
    }

    // ---------- a refusal that is about a MISSING role, not a malformed one ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ARoleThatWasNotSentIsRefusedAsMissingRatherThanAsMalformed(string? nothing)
    {
        // "'' is not a role id" tells somebody who sent no role that the empty string was rejected —
        // true, and useless. (gemini, story 1's code round.)
        var why = Default.Refusal(nothing);

        why.Should().NotBeNull();
        why.Should().Contain("needs a role").And.Contain(Shipped);
        why.Should().NotContain("is not a role id");
    }

    // ---------- the hot path does not allocate to answer ----------

    [Fact]
    public void AMalformedIdIsHandedBackWithoutBeingFolded()
    {
        // It is about to be refused; folding a 4 KB id for a lookup that cannot match would allocate
        // a 4 KB copy per request on input nobody here controls. (codex, story 1's code round.)
        var huge = new string('R', 4096);

        Default.Canonical(huge).Should().BeSameAs(huge, "handed straight back, not copied");
    }

    [Fact]
    public void ARoleNOBODYSENTCanonicalisesToNothingRatherThanFailing()
    {
        // The old-client row reaches `Canonical` too: a request that named no kind and no role passes
        // BOTH gates, and its role is still canonicalised on the way into the JobRecord and the
        // fingerprint. Nothing about that path is allowed to throw, and it was never asserted — the
        // gates were tested, the ingress was not. (gemini, story 2's plan round.)
        Default.Canonical(null).Should().BeEmpty();
        Default.Canonical(string.Empty).Should().BeEmpty();

        // Whitespace is nothing too. It used to come back as "   ", which put three spaces into the
        // JobRecord and into the idempotency fingerprint — so the same review, from the same person,
        // was two jobs depending on which way they said nothing. Quoting the input back is the
        // refusal's job. (gemini, story 2's code round.)
        Default.Canonical("   ").Should().BeEmpty();
        Default.Canonical("\t\n").Should().BeEmpty();
    }
}
