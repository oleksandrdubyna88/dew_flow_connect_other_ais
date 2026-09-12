using CoaiMcp.Core.Rounds;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a person's <c>COAI_ROLES</c> does to the shipped catalog — and what it is refused for.
/// </summary>
/// <remarks>
/// <para>Nothing here throws, and that is the contract being tested as much as any single rule: a
/// row this code cannot use becomes a sentence in <c>Dropped</c> and the round runs without it.
/// The opposite case — a seed the binary ships — is <c>BuiltinRoleCatalogTests.ABrokenSeed</c>,
/// where the same mistakes are exceptions, because there is nobody to tell and nothing to salvage.</para>
/// <para>Every refusal is asserted through the SENTENCE a person reads, not through a count: a
/// dropped row whose reason does not name the row is a row that vanished.</para>
/// </remarks>
public sealed class RoleCompositionTests
{
    private static readonly RoleDefinition Architecture =
        RoleCatalog.Builtin.Roles.Single(r => r.Id == RoleCatalog.ArchitectureRole);

    private static RoleEntry Custom(string id, string stage = RoleStages.Result, params string[] promptIds) =>
        new(id, Name: $"The {id} role", Stage: stage,
            Prompts: [.. (promptIds.Length == 0 ? [$"{id.ToLowerInvariant()}-general"] : promptIds)
                .Select(p => new PromptEntry(p, "General", "What it is for."))]);

    private static RoleCatalog Composed(params RoleEntry[] entries) => RoleComposition.Compose(entries);

    [Fact]
    public void NoEntriesAtAll_ComposeToExactlyTheShippedCatalog()
    {
        var catalog = Composed();

        catalog.Roles.Should().BeEquivalentTo(RoleCatalog.Builtin.Roles, o => o.WithStrictOrdering(),
            "a machine nobody has configured runs what the binary ships");
        catalog.Dropped.Should().BeEmpty();
    }

    /// <summary>
    /// The machine nobody configured, asserted as the thing it is — not as a suite that stayed green.
    /// </summary>
    /// <remarks>
    /// "Nothing observable changes" was the promise of the whole plan, and until this test it was
    /// carried by the absence of failures elsewhere: a catalog that came back in another order, with
    /// another role switched on, or with another budget would have left every existing test passing
    /// and changed what every round runs. Asked for by codex on this story's plan round.
    /// </remarks>
    [Fact]
    public void WithNothingConfigured_TheRoundIsExactlyWhatTheProductShips()
    {
        var config = new PanelConfig(Roles: null, StagePolicy.Human) { Catalog = Composed() };

        config.RolesForRound(Stage.PlanReview, round: 1).Should().Equal("PlanCritique");
        config.RolesForRound(Stage.CodeReview, round: 1).Should().Equal(
            "Conventions", "Architecture", "SecurityReliability", "UxDxPerformance");

        config.For("PlanCritique").Should().Be(PanelConfig.PlanDefault);
        foreach (var role in config.RolesForRound(Stage.CodeReview, round: 1))
        {
            config.For(role).Should().Be(PanelConfig.CodeDefault, $"{role}'s shipped budget");
        }

        config.Catalog.Roles.Should().OnlyContain(r => r.Active && r.BuiltIn && r.ProgrammingTask);
        config.Catalog.Dropped.Should().BeEmpty();
    }

    /// <summary>
    /// One input carrying an override, a role of the person's own and a capped one — asserted as a
    /// whole catalog rather than as three sentences.
    /// </summary>
    /// <remarks>
    /// A composition could produce every refusal correctly and still fail to append a role, lose an
    /// override's prompts or reorder the built-ins, and a suite that only read `Dropped` would agree
    /// with it. Asked for by codex on this story's plan round.
    /// </remarks>
    [Fact]
    public void AMixedConfiguration_ProducesTheWholeCatalog_NotJustItsRefusals()
    {
        var catalog = Composed(
            new RoleEntry(RoleCatalog.ConventionsRole, Active: false,
                Prompts: [new PromptEntry("conv-ours", "Ours", "The rules we wrote.")]),
            Custom("Requirements", RoleStages.Result, "req-general"),
            Custom("Risks", RoleStages.Result, "risks-general"),
            Custom("Brief", RoleStages.Plan, "brief-general"));

        catalog.Roles.Select(r => (r.Id, r.Active, r.BuiltIn, r.Stage, r.ProgrammingTask)).Should().Equal(
            ("PlanCritique", true, true, RoleStages.Plan, true),
            ("Conventions", false, true, RoleStages.Result, true),
            ("Architecture", true, true, RoleStages.Result, true),
            ("SecurityReliability", true, true, RoleStages.Result, true),
            ("UxDxPerformance", true, true, RoleStages.Result, true),
            ("Requirements", true, false, RoleStages.Result, true),
            ("Risks", true, false, RoleStages.Result, true),
            ("Brief", true, false, RoleStages.Plan, true));

        catalog.ById(RoleCatalog.ConventionsRole)!.Prompts.Select(p => p.Id)
            .Should().Equal(["conventions", "conv-ours"], "an override keeps the shipped prompts and adds to them");
        catalog.RolesOf(RoleStages.Result).Should().Equal(
            "Architecture", "SecurityReliability", "UxDxPerformance", "Requirements", "Risks");
        catalog.RolesOf(RoleStages.Plan).Should().Equal("PlanCritique", "Brief");
        catalog.Dropped.Should().BeEmpty("every row here is usable — unticking one built-in made room for two");
    }

    [Fact]
    public void ARowNamingABuiltIn_KeepsTheShippedIdentity_AndGainsOnlyItsExtraPrompts()
    {
        var catalog = Composed(new RoleEntry(
            RoleCatalog.ArchitectureRole,
            Name: "Renamed by hand",
            Stage: RoleStages.Plan,
            ProgrammingTask: false,
            Prompts: [new PromptEntry("arch-our-layering", "Our layering", "The one we wrote down.")]));

        var role = catalog.ById(RoleCatalog.ArchitectureRole)!;
        (role.Name, role.Stage, role.ProgrammingTask, role.BuiltIn)
            .Should().Be((Architecture.Name, Architecture.Stage, Architecture.ProgrammingTask, true),
                "a built-in cannot be renamed or moved: its id keys settings, sessions and every row of the rounds database");
        role.Prompts.Select(p => p.Id).Should().Equal([.. Architecture.Prompts.Select(p => p.Id), "arch-our-layering"]);
        role.General.Should().BeEquivalentTo(Architecture.General, "an added prompt never becomes the general one");
        role.Prompts.Last().Universal.Should().BeFalse();
        catalog.Dropped.Should().BeEmpty();
    }

    [Fact]
    public void ARowSpellingABuiltInIdInAnotherCase_IsThatBuiltIn_NotANewRole()
    {
        var catalog = Composed(new RoleEntry("architecture", Active: false));

        catalog.Roles.Should().HaveCount(RoleCatalog.Builtin.Roles.Count, "nothing was added");
        catalog.ById(RoleCatalog.ArchitectureRole)!.Active.Should().BeFalse();
    }

    [Fact]
    public void ARowThatOmitsActive_LeavesTheBuiltInAsShipped()
    {
        // The reason every field but the id is nullable. A non-nullable `Active` would read an
        // omitted one as false and switch a role off for somebody who only wanted to add a prompt.
        var catalog = Composed(new RoleEntry(
            RoleCatalog.ArchitectureRole,
            Prompts: [new PromptEntry("arch-our-layering")]));

        catalog.ById(RoleCatalog.ArchitectureRole)!.Active.Should().BeTrue();
    }

    [Fact]
    public void ARowThatSaysActiveFalse_SwitchesTheBuiltInOff_AndLeavesItInTheCatalog()
    {
        var catalog = Composed(new RoleEntry(RoleCatalog.ArchitectureRole, Active: false));

        catalog.ById(RoleCatalog.ArchitectureRole)!.Active.Should().BeFalse();
        catalog.RolesOf(RoleStages.Result).Should().NotContain(RoleCatalog.ArchitectureRole);
        catalog.Dropped.Should().BeEmpty("switching a role off is not a refusal");
    }

    [Fact]
    public void ANamedBuiltInRowRepeated_KeepsTheFirst_AndNamesTheSecond()
    {
        var catalog = Composed(
            new RoleEntry(RoleCatalog.ArchitectureRole, Active: false),
            new RoleEntry(RoleCatalog.ArchitectureRole, Active: true));

        catalog.ById(RoleCatalog.ArchitectureRole)!.Active.Should().BeFalse("the first row wins");
        catalog.Dropped.Should().ContainSingle().Which.Should().Contain("more than once");
    }

    [Fact]
    public void ACustomRole_IsAddedAfterTheBuiltIns_WithItsFirstPromptAsItsGeneralOne()
    {
        var catalog = Composed(Custom("Requirements", RoleStages.Result, "req-general", "req-gaps"));

        catalog.Roles.Select(r => r.Id).Should().EndWith("Requirements");
        var role = catalog.ById("Requirements")!;
        (role.BuiltIn, role.Active, role.ProgrammingTask).Should().Be((false, true, true));
        role.General.Id.Should().Be("req-general");
        role.Prompts.Select(p => p.Universal).Should().Equal(true, false);
        catalog.RolesOf(RoleStages.Result).Should().Contain("Requirements");
    }

    [Fact]
    public void ACustomRoleMarkedNotAProgrammingTask_IsInTheCatalog_AndInNoRound()
    {
        var catalog = RoleComposition.Compose([
            Custom("SpecReview") with { ProgrammingTask = false },
        ]);

        catalog.ById("SpecReview").Should().NotBeNull();
        catalog.RolesOf(RoleStages.Result).Should().NotContain("SpecReview",
            "a document role is waiting for the round that reviews a document, not dropped");
        catalog.Dropped.Should().BeEmpty();
    }

    /// <remarks>
    /// The hyphen is the one worth explaining. A prompt id is a FILE name and wears hyphens like
    /// every shipped one does; a role id becomes the environment variable <c>COAI_ROUNDS_&lt;ID&gt;</c>,
    /// and <c>COAI_ROUNDS_MY-ROLE</c> is not a name a POSIX shell can export — so a role whose
    /// budget could be set through the settings file but never through the environment, or through
    /// the block a person pastes into an MCP client, would be a role that works in one of the two
    /// places its settings can come from. Raised by gemini on this story's plan round.
    /// </remarks>
    [Theory]
    [InlineData("1st-role", "not a usable id")]
    [InlineData("Проверка", "not a usable id")]
    [InlineData("my role", "not a usable id")]
    [InlineData("my-role", "not a usable id")]
    [InlineData("", "not a usable id")]
    public void AnIdThatCannotBecomeAnEnvironmentKey_IsDroppedAndNamed(string id, string expected)
    {
        var catalog = Composed(Custom(id));

        catalog.Roles.Should().HaveCount(RoleCatalog.Builtin.Roles.Count);
        var reason = catalog.Dropped.Should().ContainSingle().Which;
        reason.Should().Contain(expected);
        if (id.Length > 0)
        {
            reason.Should().Contain(id, "the sentence has to name the row a person wrote");
        }
    }

    [Fact]
    public void AnUnknownStage_IsDroppedAndNamed()
    {
        var catalog = Composed(Custom("Whenever", "sometime"));

        catalog.Dropped.Should().ContainSingle().Which.Should().Contain("sometime").And.Contain("Whenever");
    }

    [Fact]
    public void ACustomRoleWithNoPrompts_IsDroppedAndNamed()
    {
        var catalog = Composed(new RoleEntry("Bare", Stage: RoleStages.Result));

        catalog.Dropped.Should().ContainSingle().Which.Should().Contain("Bare").And.Contain("no prompts");
    }

    [Fact]
    public void TwoCustomRolesOfOneId_KeepTheFirst_AndNameTheSecond()
    {
        var catalog = Composed(
            Custom("Requirements", RoleStages.Result, "req-one"),
            Custom("requirements", RoleStages.Result, "req-two"));

        catalog.Roles.Count(r => !r.BuiltIn).Should().Be(1);
        catalog.ById("Requirements")!.General.Id.Should().Be("req-one");
        catalog.Dropped.Should().ContainSingle().Which.Should().Contain("COAI_ROUNDS_REQUIREMENTS",
            "the reason names the environment key, which is why ids are matched without case");
    }

    [Theory]
    [InlineData("../../secrets")]
    [InlineData("Rules")]
    [InlineData("two words")]
    public void APromptIdThatIsNotASlug_IsDroppedAndNamed(string promptId)
    {
        var catalog = Composed(Custom("Requirements", RoleStages.Result, promptId));

        catalog.ById("Requirements").Should().BeNull("its only prompt was refused, so the role has no general one");
        catalog.Dropped.Should().Contain(d => d.Contains(promptId) && d.Contains("names a file"));
    }

    [Fact]
    public void APromptIdAlreadyUsedByAShippedRole_IsDroppedAndNamed()
    {
        var catalog = Composed(Custom("Requirements", RoleStages.Result, "arch-naming", "req-general"));

        catalog.ById("Requirements")!.Prompts.Select(p => p.Id).Should().Equal(
            ["req-general"], "the role survives on the prompts that were usable");
        catalog.ById(RoleCatalog.ArchitectureRole)!.Prompts.Select(p => p.Id).Should().Contain("arch-naming");
        catalog.Dropped.Should().ContainSingle().Which.Should().Contain("arch-naming").And.Contain("already in use");
    }

    [Fact]
    public void OnePromptIdUnderTwoCustomRoles_IsKeptOnTheFirst_AndNamedOnTheSecond()
    {
        var catalog = Composed(
            Custom("Alpha", RoleStages.Result, "shared-id", "alpha-own"),
            Custom("Beta", RoleStages.Result, "shared-id", "beta-own"));

        catalog.ById("Alpha")!.Prompts.Select(p => p.Id).Should().Equal("shared-id", "alpha-own");
        catalog.ById("Beta")!.Prompts.Select(p => p.Id).Should().Equal(["beta-own"]);
        catalog.ById("Beta")!.General.Id.Should().Be("beta-own", "the general prompt is whichever one survived first");
        // Beta is also the sixth active result role, so it carries a second sentence about the cap.
        catalog.Dropped.Should().Contain(d => d.Contains("Beta") && d.Contains("shared-id"));
    }

    [Fact]
    public void ASixthActiveRoleInABucket_IsInTheCatalogSwitchedOff_AndNamed()
    {
        // Four shipped result roles are active, so exactly one custom result role fits.
        var catalog = Composed(
            Custom("First", RoleStages.Result, "first-general"),
            Custom("Second", RoleStages.Result, "second-general"));

        catalog.ById("First")!.Active.Should().BeTrue();
        catalog.ById("Second")!.Active.Should().BeFalse("five is the limit and the built-ins were there first");
        catalog.RolesOf(RoleStages.Result).Should().HaveCount(RoleComposition.MaxActivePerBucket);
        catalog.Dropped.Should().ContainSingle().Which.Should().Contain("Second").And.Contain("switched off");
    }

    [Fact]
    public void TheCapNeverTrimsABuiltIn()
    {
        var catalog = Composed(
            Custom("First", RoleStages.Result, "first-general"),
            Custom("Second", RoleStages.Result, "second-general"),
            Custom("Third", RoleStages.Result, "third-general"));

        catalog.Roles.Where(r => r.BuiltIn && r.Stage == RoleStages.Result)
            .Should().OnlyContain(r => r.Active, "a person's row can never switch a shipped role off by arriving");
        catalog.Roles.Count(r => !r.BuiltIn && r.Active).Should().Be(1);
    }

    [Fact]
    public void RoomMadeByUntickingABuiltIn_GoesToACustomRole()
    {
        var catalog = Composed(
            new RoleEntry(RoleCatalog.ArchitectureRole, Active: false),
            Custom("First", RoleStages.Result, "first-general"),
            Custom("Second", RoleStages.Result, "second-general"));

        catalog.RolesOf(RoleStages.Result).Should().Equal(
            RoleCatalog.ConventionsRole, RoleCatalog.SecurityRole, RoleCatalog.UxDxRole, "First", "Second");
    }

    [Fact]
    public void TheOtherBucketHasItsOwnFive()
    {
        // A document role does not spend a code role's slot: they never share a round.
        var catalog = RoleComposition.Compose([
            Custom("First", RoleStages.Result, "first-general"),
            Custom("Doc", RoleStages.Result, "doc-general") with { ProgrammingTask = false },
        ]);

        catalog.ById("First")!.Active.Should().BeTrue();
        catalog.ById("Doc")!.Active.Should().BeTrue("it is the first role of its own bucket");
        catalog.Dropped.Should().BeEmpty();
    }

    [Fact]
    public void APlanStageCustomRole_JoinsThePlanRound()
    {
        var catalog = Composed(Custom("Assumptions", RoleStages.Plan, "assumptions-general"));

        catalog.RolesOf(RoleStages.Plan).Should().Equal(RoleCatalog.PlanRole, "Assumptions");
    }

    [Fact]
    public void EveryRefusal_NamesTheRowItRefused()
    {
        var catalog = Composed(
            Custom("1bad"),
            Custom("Whenever", "sometime"),
            new RoleEntry("Bare", Stage: RoleStages.Result));

        catalog.Dropped.Should().HaveCount(3)
            .And.OnlyContain(d => d.Contains(':'), "a reason is '<row>: why', so a person can find the row");
    }
}
