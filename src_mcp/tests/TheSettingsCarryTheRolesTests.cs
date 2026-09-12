using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// `COAI_ROLES` — a person's own roles, arriving through the channel every other setting uses.
/// </summary>
/// <remarks>
/// This is the story where the feature becomes reachable: the catalog was data from A1, composition
/// was a rule from A2, the round asked it from B1, and until now nothing put a person's rows into
/// it. The panel writes the key (story C1); a hand-written `settings.json` or a pasted `mcpServers`
/// block writes it today, which is how `COAI_VENDORS` was driven before the panel knew vendors.
/// </remarks>
public sealed class TheSettingsCarryTheRolesTests : IDisposable
{
    private readonly string _dataDir = Directory.CreateTempSubdirectory("coai-roles-settings-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dataDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private const string OneCustomRole = """
        [{"id":"Requirements","name":"Requirements we wrote","stage":"result",
          "prompts":[{"id":"req-general","label":"General","purpose":"Whether the requirement is met."}]}]
        """;

    private static PanelSettings From(string? roles, Func<string, string?>? more = null) =>
        PanelSettings.FromEnvironment(name => name == "COAI_ROLES" ? roles : more?.Invoke(name));

    [Fact]
    public void ARoleAPersonWrote_IsInTheCatalogTheRoundReads()
    {
        var settings = From(OneCustomRole);

        settings.Rounds.Catalog.ById("Requirements")!.Name.Should().Be("Requirements we wrote");
        settings.Rounds.RolesForRound(Stage.CodeReview, round: 1).Should().Contain("Requirements");
        settings.Unrecognised.Should().BeEmpty();
    }

    [Fact]
    public void ItGetsItsOwnBudgetKeys_LikeAnyOtherRole()
    {
        var settings = From(OneCustomRole, name => name switch
        {
            "COAI_ROUNDS_REQUIREMENTS" => "3",
            "COAI_THRESHOLD_REQUIREMENTS" => "1",
            _ => null,
        });

        settings.Rounds.For("Requirements").Should().Be(new RoleGate(3, 1),
            "the id becomes COAI_ROUNDS_<ID>, which is why it is latin and carries no hyphen");
    }

    [Fact]
    public void ItCanBeSwitchedOff_LikeAnyOtherRole()
    {
        var settings = From(OneCustomRole, name => name == "COAI_ENABLED_REQUIREMENTS" ? "false" : null);

        settings.Rounds.RolesForRound(Stage.CodeReview, round: 1).Should().NotContain("Requirements");
    }

    [Fact]
    public void WithNoKeyAtAll_TheShippedFiveAreWhatRuns()
    {
        var settings = From(null);

        settings.Rounds.Catalog.Roles.Should().BeEquivalentTo(RoleCatalog.Builtin.Roles, o => o.WithStrictOrdering());
        settings.Rounds.For(RoleCatalog.ArchitectureRole).Should().Be(PanelConfig.CodeDefault);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"id\":\"Requirements\"}")]
    [InlineData("[")]
    public void MalformedJson_IsNoCustomRolesRatherThanHalfOfThem(string json)
    {
        // The reflex COAI_VENDORS and COAI_PROMPTS_PER_ROUND have had since they shipped: a
        // configuration this build cannot read leaves the product running what it ships.
        var settings = From(json);

        settings.Rounds.Catalog.Roles.Should().BeEquivalentTo(RoleCatalog.Builtin.Roles, o => o.WithStrictOrdering());
    }

    [Fact]
    public void ARowThatCompositionRefused_IsASentenceInUnrecognised()
    {
        var settings = From("""[{"id":"1bad","stage":"result","prompts":[{"id":"bad-general"}]}]""");

        settings.Rounds.Catalog.Roles.Should().HaveCount(RoleCatalog.Builtin.Roles.Count);
        settings.Unrecognised.Should().ContainSingle().Which.Should().Contain("1bad").And.Contain("not a usable id");
    }

    [Fact]
    public void ThePanelsOtherComplaintsAreStillThere()
    {
        // `Unrecognised` had one job before this and still has it: an unreadable VALUE of a key this
        // build knows. The dropped rows join that list rather than replacing it.
        var settings = PanelSettings.FromEnvironment(name => name switch
        {
            "COAI_ROLES" => """[{"id":"1bad","stage":"result","prompts":[{"id":"bad-general"}]}]""",
            "COAI_ON_EXHAUSTED" => "whenever",
            _ => null,
        });

        settings.Unrecognised.Should().HaveCount(2)
            .And.Contain(u => u.Contains("COAI_ON_EXHAUSTED"))
            .And.Contain(u => u.Contains("1bad"));
    }

    /// <summary>
    /// A role a person added but never wrote the prompt for.
    /// </summary>
    /// <remarks>
    /// It is the one failure only a CUSTOM role can have: a shipped prompt's text is embedded in the
    /// binary. The round must run without it and SAY so — a reviewer that silently does not run is a
    /// round that reviewed less than it reported, which is the defect
    /// `PLAN_a_skipped_role_reaches_the_ai` was written for.
    /// </remarks>
    [Fact]
    public void ARoleWhosePromptHasNoText_IsNamedAsNotAsked_AndTheRoundStillRuns()
    {
        var settings = new PanelSettings
        {
            DataDir = _dataDir,
            CodeWorkspace = "none",
            Rounds = Composed("Requirements"),
            Providers = [new ProviderSettings("local") { Enabled = true, Runtime = "local", Model = "m" }],
        };
        var service = new PanelService(settings, VaultKeys.None("no vault"), default,
            new Runners.Processes.ProcessLauncher(), Serilog.Core.Logger.None);

        var work = service.BuildWork(
            [RoleCatalog.ArchitectureRole, "Requirements"], Scratch(), "ctx", round: 1, isPlanStage: false);

        work.Reviewers.Should().ContainSingle()
            .Which.Invocation.Role.Should().Be(RoleCatalog.ArchitectureRole, "the shipped role still runs");
        work.NotAsked.Should().ContainSingle().Which.Should().Match<SkippedRole>(
            s => s.Role == "Requirements" && s.Reason.Contains("req-general") && s.Reason.Contains("no text"));
    }

    [Fact]
    public void ItIsOneSentence_HoweverManyVendorsWouldHaveCarriedIt()
    {
        var settings = new PanelSettings
        {
            DataDir = _dataDir,
            CodeWorkspace = "none",
            Rounds = Composed("Requirements"),
            Providers =
            [
                new ProviderSettings("local") { Enabled = true, Runtime = "local", Model = "m" },
                new ProviderSettings("second") { Enabled = true, Runtime = "local", Model = "m" },
            ],
        };
        var service = new PanelService(settings, VaultKeys.None("no vault"), default,
            new Runners.Processes.ProcessLauncher(), Serilog.Core.Logger.None);

        var work = service.BuildWork(["Requirements"], Scratch(), "ctx", round: 1, isPlanStage: false);

        work.NotAsked.Should().ContainSingle("a person needs to know the ROLE did not run");
    }

    /// <summary>
    /// A Team server accepts the roles it was compiled with, so a custom one is refused BEFORE it is
    /// sent.
    /// </summary>
    /// <remarks>
    /// The refusal would otherwise arrive as a 400 naming roles the person never asked about, after
    /// a round-trip — the signature this family already paid for once, when a Team-server reviewer
    /// was dropped from every round for three releases and nothing said so. Widening the server is
    /// plan 3 of this feature; until then the round names the vendor it could not use and runs the
    /// ones it can.
    /// </remarks>
    [Fact]
    public void ATeamServerVendor_IsNamedAsExcludedForACustomRole_AndStillRunsTheShippedOnes()
    {
        File.WriteAllText(Path.Combine(Prompts(), "req-general.md"), "whether the requirement is met");

        // Signed in, so the Team-server vendor is eligible at all: without a token it is excluded
        // for its own sake long before a role is chosen, and this test would pass having exercised
        // nothing. (It did, on its first run.)
        TeamServerAuth.WriteToken(
            TeamServerAuth.TokenPath(_dataDir, "https://coai.example.com"), "a-token");

        var settings = new PanelSettings
        {
            DataDir = _dataDir,
            CodeWorkspace = "none",
            Rounds = Composed("Requirements"),
            Providers =
            [
                new ProviderSettings("local") { Enabled = true, Runtime = "local", Model = "m" },
                new ProviderSettings("company-codex")
                {
                    Enabled = true, Runtime = "remote", Model = "m", RemoteVendor = "codex",
                    BaseUrl = "https://coai.example.com",
                },
            ],
        };
        var service = new PanelService(settings, VaultKeys.None("no vault"), default,
            new Runners.Processes.ProcessLauncher(), Serilog.Core.Logger.None);

        var work = service.BuildWork(
            [RoleCatalog.ArchitectureRole, "Requirements"], Scratch(), "ctx", round: 1, isPlanStage: false);

        work.Reviewers.Where(w => w.Invocation.Role == "Requirements").Select(w => w.Invocation.Provider)
            .Should().Equal(["local"], "the custom role goes to the vendors this machine runs itself");
        work.Reviewers.Where(w => w.Invocation.Role == RoleCatalog.ArchitectureRole)
            .Should().HaveCount(2, "a shipped role goes to the Team server as it always did");
        work.Excluded.Should().ContainSingle().Which.Should()
            .Contain("company-codex").And.Contain("Requirements");
    }

    private string Prompts()
    {
        var path = Path.Combine(_dataDir, "prompts");
        Directory.CreateDirectory(path);

        return path;
    }

    private static PanelConfig Composed(string id)
    {
        var catalog = RoleComposition.Compose([
            new RoleEntry(id, Name: id, Stage: RoleStages.Result,
                Prompts: [new PromptEntry("req-general", "General", "Whether the requirement is met.")]),
        ]);

        return new PanelConfig(Roles: null, StagePolicy.Human) { Catalog = catalog };
    }

    private static string Scratch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coai-rolesettings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        return path;
    }
}
