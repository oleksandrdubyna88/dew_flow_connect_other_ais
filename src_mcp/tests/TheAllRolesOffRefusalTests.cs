using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The sentence a person reads when they have switched every code role off.
/// </summary>
/// <remarks>
/// It is the one message in the product that is only ever seen in a state where NOTHING is
/// configured to run, so it has to name what to switch back on — and it has to name it from the
/// catalog, because a person who added a role of their own is looking at a panel with five boxes.
/// The first version of that change asked the catalog for the roles of the stage, which filters to
/// the ACTIVE ones; in the only state this sentence appears in, none are, and it read "Tick at least
/// one of a role". Found on this story's code round.
/// </remarks>
public sealed class TheAllRolesOffRefusalTests
{
    private static PanelService Service(RoleCatalog catalog) =>
        new(new PanelSettings
            {
                DataDir = Directory.CreateTempSubdirectory("coai-refusal-").FullName,
                Rounds = new PanelConfig(
                    catalog.Roles.ToDictionary(r => r.Id, r => new RoleGate(1, 5, Enabled: false)),
                    StagePolicy.Human)
                {
                    Catalog = catalog,
                },
            },
            VaultKeys.None("no vault"), default,
            new Runners.Processes.ProcessLauncher(), Serilog.Core.Logger.None);

    [Fact]
    public void WithEveryRoleOff_ItStillNamesTheBoxesAPersonCanTick()
    {
        var refusal = Service(RoleCatalog.Builtin).NoCodeRolesRefusal;

        refusal.Should().Contain("Conventions").And.Contain("Architecture")
            .And.Contain("Security & reliability").And.Contain("Performance & UX-DX",
                "this sentence exists precisely for the state where none of them is on");
        refusal.Should().NotContain("a role", "that is what an empty list reads as");
    }

    [Fact]
    public void ARoleAPersonAdded_IsOfferedToo()
    {
        var catalog = RoleComposition.Compose([
            new RoleEntry("Requirements", Name: "Requirements we wrote", Stage: RoleStages.Result,
                Prompts: [new PromptEntry("req-general")]),
        ]);

        Service(catalog).NoCodeRolesRefusal.Should().Contain("Requirements we wrote",
            "a person is told to tick the box they can actually see, under the name they gave it");
    }

    [Fact]
    public void ADocumentRoleIsNotOffered_BecauseTickingItCannotSatisfyACodeRound()
    {
        // It would be an instruction that leaves the person exactly as blocked as before, having
        // done what they were told. (codex, this story's code round.)
        var catalog = RoleComposition.Compose([
            new RoleEntry("Brief", Name: "The brief", Stage: RoleStages.Result, ProgrammingTask: false,
                Prompts: [new PromptEntry("brief-general")]),
        ]);

        Service(catalog).NoCodeRolesRefusal.Should().NotContain("The brief");
    }

    [Fact]
    public void APlanRoleIsNotOffered_ForTheSameReason() =>
        Service(RoleCatalog.Builtin).NoCodeRolesRefusal.Should().NotContain("Plan review");

    [Fact]
    public void ARoleSwitchedOffInTheCATALOG_IsStillOfferedAsSomethingToTick()
    {
        // Two switches, and this sentence must read the right one. `RoleGate.Enabled` is the panel's
        // tick; `RoleDefinition.Active` is the catalog's. A person who turned every role off in
        // COAI_ROLES and then asked for a round was told to "tick at least one of a role", because
        // the list offered was the ACTIVE ones — of which, in the only state this message appears
        // in, there can be none.
        var catalog = RoleComposition.Compose([
            new RoleEntry(RoleCatalog.ArchitectureRole, Active: false),
            new RoleEntry(RoleCatalog.ConventionsRole, Active: false),
            new RoleEntry(RoleCatalog.SecurityRole, Active: false),
            new RoleEntry(RoleCatalog.UxDxRole, Active: false),
        ]);

        Service(catalog).NoCodeRolesRefusal.Should()
            .Contain("Architecture").And.Contain("Conventions")
            .And.NotContain("a role", "an empty list is not an instruction");
    }

    /// <summary>
    /// A session file must not carry the catalog it ran with.
    /// </summary>
    /// <remarks>
    /// `PanelConfig` is persisted inside `SessionState`, and it gained a `Catalog` when roles became
    /// data. If that rode along, every session file would carry twenty-five prompts and their prose —
    /// and, worse, a resumed session would be read back with the catalog it was OPENED with rather
    /// than the one configured now, so editing a role would not reach a session already open. The
    /// gates are the session's; the catalog is the server's.
    /// </remarks>
    /// <summary>
    /// A session read back runs against the catalog configured NOW, not the shipped default.
    /// </summary>
    /// <remarks>
    /// The catalog is not persisted (the test below says why), so a deserialized config carries the
    /// init default until the store reattaches the live one. It matters quietly: `PanelConfig.For(Stage)`
    /// asks the catalog which roles a stage HAS, so a session resumed after a restart would take its
    /// stage budget from the shipped roles alone and miss the rounds a person's own role was given.
    /// Raised by codex on this story's second code round.
    /// </remarks>
    [Fact]
    public void ASessionReadBack_RunsAgainstTheCatalogTheServerHasNow()
    {
        var dir = Directory.CreateTempSubdirectory("coai-resume-").FullName;
        var catalog = RoleComposition.Compose([
            new RoleEntry("Requirements", Name: "Requirements", Stage: RoleStages.Result,
                Prompts: [new PromptEntry("req-general")]),
        ]);

        // Written by a server that knew the role, read by one configured with the same catalog.
        new SessionStore(dir).Save(new PersistedSession(
            new SessionState("s1", "D:/repo", "main", new PanelConfig(
                catalog.Roles.ToDictionary(r => r.Id, _ => new RoleGate(3, 5)), StagePolicy.Human)),
            []));

        var loaded = new SessionStore(dir, catalog).Load("D:/repo", "main")!;

        loaded.State.Config.Catalog.ById("Requirements").Should().NotBeNull();
        loaded.State.Config.RolesForRound(Stage.CodeReview, round: 3).Should().Contain("Requirements",
            "a role a person defined keeps the rounds it was given across a restart");
    }

    [Fact]
    public void ASessionFileCarriesTheGates_AndNotTheCatalog()
    {
        var session = new PersistedSession(
            new SessionState("s1", "D:/repo", "main", new PanelConfig(Roles: null, StagePolicy.Human)),
            []);

        var json = JsonSerializer.Serialize(session, ServerJsonContext.Default.PersistedSession);

        json.Should().Contain("\"roles\"", "the budgets are the session's own");
        json.Should().NotContain("\"catalog\"").And.NotContain("\"purpose\"").And.NotContain("\"programmingTask\"",
            "the catalog belongs to the server that is running now, not to a session opened yesterday");
    }
}
