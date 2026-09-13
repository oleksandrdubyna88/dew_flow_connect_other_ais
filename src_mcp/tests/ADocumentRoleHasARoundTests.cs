using CoaiMcp.Core.Rounds;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The predicate plans 1, 2 and 3 all stopped at: a role marked <c>programmingTask: false</c> was
/// stored, shown, composed, budgeted and switched on, and took part in no round.
/// </summary>
/// <remarks>
/// <para>The whole selection change is one predicate — <c>RolesOf</c> filtered on
/// <c>ProgrammingTask</c> — so the way to get it wrong is to WIDEN it, and every test here comes in
/// a pair: the document role is found by its own stage, and the code roles are still found by
/// theirs and by nothing else.</para>
/// <para><c>Stage</c> and <c>RoleDefinition.Stage</c> are two different things and stay two
/// different things: a document role's stage is the STRING <c>result</c> with
/// <c>ProgrammingTask: false</c>, everywhere it is persisted, and <see cref="Stage.DocumentReview"/>
/// is an orchestration value that meets it in exactly one place. The plan round asked for that to be
/// said out loud; these tests are where it is said in code.</para>
/// </remarks>
public sealed class ADocumentRoleHasARoundTests
{
    private static RoleEntry Off(string id) => new(id, Active: false);

    /// <summary>The shipped five switched off, so a round is only what the test added.</summary>
    private static readonly RoleEntry[] NoShippedRoles =
    [
        Off(RoleCatalog.PlanRole),
        Off(RoleCatalog.ConventionsRole),
        Off(RoleCatalog.ArchitectureRole),
        Off(RoleCatalog.SecurityRole),
        Off(RoleCatalog.UxDxRole),
    ];

    private static RoleEntry Role(string id, string stage, bool programmingTask) =>
        new(id, Name: id, Stage: stage, ProgrammingTask: programmingTask,
            Prompts: [new PromptEntry($"{id.ToLowerInvariant()}-general", "General", "The whole thing.")]);

    private static RoleCatalog With(params RoleEntry[] extra) =>
        RoleComposition.Compose([.. NoShippedRoles, .. extra]);

    [Fact]
    public void ADocumentRole_IsInTheDocumentBucket_AndNoOther()
    {
        var catalog = With(Role("Requirements", RoleStages.Result, programmingTask: false));

        catalog.InBucket(RoleBuckets.ResultDocument).Should().Equal(["Requirements"]);
        catalog.InBucket(RoleBuckets.ResultCode).Should().BeEmpty();
        catalog.InBucket(RoleBuckets.PlanCode).Should().BeEmpty();
        catalog.InBucket(RoleBuckets.PlanDocument).Should().BeEmpty();
    }

    /// <summary>
    /// The regression guard. The change is one predicate, and widening it is how it goes wrong.
    /// </summary>
    [Fact]
    public void ACodeRole_IsStillInTheCodeBucket_AndNoOther()
    {
        var catalog = With(Role("Perf", RoleStages.Result, programmingTask: true));

        catalog.InBucket(RoleBuckets.ResultCode).Should().Equal(["Perf"]);
        catalog.InBucket(RoleBuckets.ResultDocument).Should().BeEmpty();
    }

    /// <summary>
    /// A plan-stage document role is storable today and this plan runs none. Asserted rather than
    /// assumed: "not built yet" and "does not exist" are different states, and the difference is
    /// only real if something checks it.
    /// </summary>
    [Fact]
    public void APlanStageDocumentRole_IsInTheCatalog_AndInNoStagesRound()
    {
        var catalog = With(Role("Brief", RoleStages.Plan, programmingTask: false));

        catalog.ById("Brief").Should().NotBeNull("it is stored — it is waiting, not dropped");
        catalog.InBucket(RoleBuckets.PlanDocument).Should().Equal(["Brief"]);

        foreach (var stage in (Stage[])[Stage.PlanReview, Stage.CodeReview, Stage.DocumentReview])
        {
            catalog.InBucket(PanelConfig.BucketFor(stage)).Should().NotContain("Brief",
                $"no round of {stage} runs a plan-stage document role");
        }
    }

    /// <summary>The one bridge between the orchestration stage and the persisted role stage.</summary>
    [Fact]
    public void EachStage_NamesItsOwnBucket()
    {
        PanelConfig.BucketFor(Stage.PlanReview).Should().Be(RoleBuckets.PlanCode);
        PanelConfig.BucketFor(Stage.CodeReview).Should().Be(RoleBuckets.ResultCode);
        PanelConfig.BucketFor(Stage.DocumentReview).Should().Be(RoleBuckets.ResultDocument);
    }

    /// <summary>
    /// A document role reaches the ROUND, not merely the catalog — budgets, enable switches and
    /// round rosters all read the same selection.
    /// </summary>
    [Fact]
    public void ADocumentRole_IsOnTheDocumentStagesRoster()
    {
        var catalog = With(Role("Requirements", RoleStages.Result, programmingTask: false));
        var config = new PanelConfig(
            catalog.Roles.ToDictionary(r => r.Id, _ => new RoleGate(2, 5)), StagePolicy.Human)
        {
            Catalog = catalog,
        };

        config.EnabledRolesOf(Stage.DocumentReview).Should().Equal(["Requirements"]);
        config.RolesForRound(Stage.DocumentReview, 1).Should().Equal(["Requirements"]);
        config.EnabledRolesOf(Stage.CodeReview).Should().BeEmpty();
        config.For(Stage.DocumentReview).Should().Be(new StageGate(2, 5));
    }

    /// <summary>
    /// A document role switched off is not on the roster, exactly like a code one. The switch is a
    /// property of the role, never of its kind.
    /// </summary>
    [Fact]
    public void ADocumentRoleSwitchedOff_RunsInNothing()
    {
        var catalog = With(Role("Requirements", RoleStages.Result, programmingTask: false));
        var config = new PanelConfig(
            catalog.Roles.ToDictionary(r => r.Id, _ => new RoleGate(2, 5, Enabled: false)), StagePolicy.Human)
        {
            Catalog = catalog,
        };

        config.EnabledRolesOf(Stage.DocumentReview).Should().BeEmpty();
        config.For(Stage.DocumentReview).Should().Be(PanelConfig.NoEnabledRoles);
    }
}
