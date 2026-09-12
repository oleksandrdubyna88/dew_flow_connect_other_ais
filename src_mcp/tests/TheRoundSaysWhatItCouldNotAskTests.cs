using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The two guards story B2 added, at the edges its first draft got wrong.
/// </summary>
/// <remarks>
/// Both guards were written against the case they were designed for and passed it. What this class
/// holds is the cases beside it, every one raised on B2's own code round: a prompt file that exists
/// and says nothing, a SHIPPED role whose prompt a person edited, a role that fails both guards at
/// once, and one role refused as many times as it had lenses.
/// </remarks>
public sealed class TheRoundSaysWhatItCouldNotAskTests : IDisposable
{
    private const string TeamServer = "https://coai.example.com";

    private readonly string _dataDir = Directory.CreateTempSubdirectory("coai-couldnotask-").FullName;

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

    private string Prompts()
    {
        var path = Path.Combine(_dataDir, "prompts");
        Directory.CreateDirectory(path);

        return path;
    }

    private void Prompt(string id, string text) => File.WriteAllText(Path.Combine(Prompts(), $"{id}.md"), text);

    private static string Scratch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coai-couldnotask-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        return path;
    }

    /// <summary>One custom result role with as many prompts as asked for.</summary>
    private static RoleCatalog With(params string[] promptIds) =>
        RoleComposition.Compose([
            new RoleEntry("Requirements", Name: "Requirements we wrote", Stage: RoleStages.Result,
                Prompts: [.. promptIds.Select(id => new PromptEntry(id, "General", "Whether the requirement is met."))]),
        ]);

    /// <summary>
    /// A custom role dealt to the one vendor that cannot run it must still be asked of one that can.
    /// </summary>
    /// <remarks>
    /// Dealing hands each question to ONE vendor. The exclusion was applied after the deal, in the
    /// leaf that builds a launch, so a custom role whose hand fell to the Team server was dropped
    /// from the round entirely — with a local vendor sitting there able to run it. The round then
    /// reported a vendor exclusion for a role that simply did not happen, which is the silent shrink
    /// this whole story is about. Walked over ten seeds because which vendor gets the hand is the
    /// seed's business, and one seed proves nothing either way. (gemini, story B2's second code
    /// round.)
    /// </remarks>
    [Fact]
    public void ACustomRoleDealtToATeamServer_GoesToAVendorThatCanRunIt()
    {
        Prompt("req-general", "Whether the requirement is met.");
        TeamServerAuth.WriteToken(TeamServerAuth.TokenPath(_dataDir, TeamServer), "a-token");

        var service = new PanelService(
            new PanelSettings
            {
                DataDir = _dataDir,
                CodeWorkspace = "none",
                Rounds = new PanelConfig(Roles: null, StagePolicy.Human) { Catalog = With("req-general") },
                Providers =
                [
                    new ProviderSettings("local") { Enabled = true, Runtime = "local", Model = "m" },
                    new ProviderSettings("company-codex")
                    {
                        Enabled = true, Runtime = "remote", Model = "m", RemoteVendor = "codex", BaseUrl = TeamServer,
                    },
                ],
            },
            VaultKeys.None("no vault"), default, new Runners.Processes.ProcessLauncher(), Serilog.Core.Logger.None);

        foreach (var seed in Enumerable.Range(0, 10))
        {
            var work = service.BuildWork(
                ["Requirements"], Scratch(), "ctx", round: 1, isPlanStage: false, seed: seed, deal: true);

            work.Reviewers.Should().ContainSingle($"seed {seed}: some vendor here can run this role")
                .Which.Invocation.Provider.Should().Be("local");
        }
    }

    private PanelService Service(RoleCatalog catalog, bool withTeamServer)
    {
        if (withTeamServer)
        {
            TeamServerAuth.WriteToken(TeamServerAuth.TokenPath(_dataDir, TeamServer), "a-token");
        }

        var settings = new PanelSettings
        {
            DataDir = _dataDir,
            CodeWorkspace = "none",
            Rounds = new PanelConfig(Roles: null, StagePolicy.Human) { Catalog = catalog },
            Providers = withTeamServer
                ?
                [
                    new ProviderSettings("company-codex")
                    {
                        Enabled = true, Runtime = "remote", Model = "m", RemoteVendor = "codex", BaseUrl = TeamServer,
                    },
                ]
                : [new ProviderSettings("local") { Enabled = true, Runtime = "local", Model = "m" }],
        };

        return new PanelService(settings, VaultKeys.None("no vault"), default,
            new Runners.Processes.ProcessLauncher(), Serilog.Core.Logger.None);
    }

    /// <summary>
    /// A prompt file that exists and says nothing is a prompt with no text.
    /// </summary>
    /// <remarks>
    /// The guard asked <c>File.Exists</c>, and a person who creates the file before writing it —
    /// which is the ordinary order of doing that — got a reviewer launched with an empty prompt
    /// instead of the sentence saying their role could not run. A reviewer given nothing to review
    /// by is a round that reviewed less than it reported, which is the whole defect the guard was
    /// written for. (codex, three times on this story's code round.)
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   \r\n\t  ")]
    public void APromptFileThatSaysNothing_IsAPromptWithNoText(string text)
    {
        Prompt("req-general", text);

        var work = Service(With("req-general"), withTeamServer: false)
            .BuildWork(["Requirements"], Scratch(), "ctx", round: 1, isPlanStage: false);

        work.Reviewers.Should().BeEmpty("a reviewer handed an empty prompt reviews nothing");
        work.NotAsked.Should().ContainSingle().Which.Reason.Should().Contain("req-general").And.Contain("no text");
    }

    /// <summary>
    /// A file naming the prompt a person has to go and write.
    /// </summary>
    /// <remarks>
    /// The sentence named the DIRECTORY, leaving somebody to guess both the file name and that it
    /// wants <c>.md</c> — and the id it quotes is not always the file name, because the prompt store
    /// sanitises an id before it becomes one. (gemini, this story's code round.)
    /// </remarks>
    [Fact]
    public void TheSentenceNamesTheFileToWrite_NotJustTheFolder()
    {
        var work = Service(With("req-general"), withTeamServer: false)
            .BuildWork(["Requirements"], Scratch(), "ctx", round: 1, isPlanStage: false);

        work.NotAsked.Should().ContainSingle().Which.Reason.Should()
            .Contain(Path.Combine(_dataDir, "prompts", "req-general.md"));
    }

    /// <summary>
    /// A SHIPPED role whose prompt somebody edited is still a shipped role.
    /// </summary>
    /// <remarks>
    /// The Team-server guard asked whether the PROMPT was built in, and a prompt stops being built
    /// in the moment a person overrides its text — which the prompt store exists to let them do. So
    /// editing the architecture prompt silently dropped Architecture from every Team-server vendor,
    /// for the reason "it is a role this Team server does not know" about a role it has shipped
    /// since the first release. The question is about the ROLE's provenance, and only the catalog
    /// answers it. (gemini, twice on this story's code round.)
    /// </remarks>
    [Fact]
    public void AShippedRoleWhosePromptWasEdited_StillGoesToTheTeamServer()
    {
        Prompt("architecture", "Review the architecture, but in our words.");

        var work = Service(With("req-general"), withTeamServer: true)
            .BuildWork([RoleCatalog.ArchitectureRole], Scratch(), "ctx", round: 1, isPlanStage: false);

        work.Excluded.Should().BeEmpty("the role is one of the five that server was compiled with");
        work.Reviewers.Should().ContainSingle().Which.Invocation.Role.Should().Be(RoleCatalog.ArchitectureRole);
    }

    /// <summary>
    /// A role that fails both guards is still named for the failure a person can fix.
    /// </summary>
    /// <remarks>
    /// The vendor check ran first and returned, so a custom role with no prompt text and none but a
    /// Team-server vendor was reported only as a vendor exclusion — the round said the server does
    /// not know the role, and never that the role had nothing to say in the first place. The second
    /// is the one the person can act on, and it is true whatever vendors are configured. (codex,
    /// this story's code round.)
    /// </remarks>
    [Fact]
    public void ARoleWithNoTextAndOnlyATeamServer_IsNamedForTheTextFirst()
    {
        var work = Service(With("req-general"), withTeamServer: true)
            .BuildWork(["Requirements"], Scratch(), "ctx", round: 1, isPlanStage: false);

        work.NotAsked.Should().ContainSingle().Which.Reason.Should().Contain("no text");
    }

    /// <summary>
    /// A round emptied by role exclusions says so, instead of blaming the vendors.
    /// </summary>
    /// <remarks>
    /// Every vendor a Team server and every scheduled role one a person defined: the vendors are
    /// configured perfectly, every role has its text, and the round still has nobody in it. The
    /// refusal carried the roles it could not ASK and not the roles a vendor could not TAKE, so the
    /// person was sent to check a configuration with nothing wrong with it. (CodeRabbit, this plan's
    /// pull request.)
    /// </remarks>
    [Fact]
    public void ARoundEmptiedByAVendorThatCannotTakeTheRole_SaysWhichRole()
    {
        Prompt("req-general", "Whether the requirement is met.");
        var service = Service(With("req-general"), withTeamServer: true);

        var work = service.BuildWork(["Requirements"], Scratch(), "ctx", round: 1, isPlanStage: false);
        work.Reviewers.Should().BeEmpty("the only vendor here cannot take this role");

        var refusal = service.NoReviewerRefusal(Stage.CodeReview, work);

        refusal.Should().Contain("Requirements", "the role is what a person is looking for")
            .And.Contain("company-codex", "and the vendor that could not take it");
    }

    /// <summary>
    /// One role refused once, however many questions it was going to be asked.
    /// </summary>
    /// <remarks>
    /// <c>NotAsked</c> has said one sentence per role since it shipped; the exclusion list beside it
    /// did not, so a role dealt four lenses produced four identical sentences in the round summary.
    /// (gemini, this story's code round.)
    /// </remarks>
    [Fact]
    public void OneRoleIsExcludedOnce_HoweverManyLensesItWouldHaveBeenDealt()
    {
        Prompt("req-general", "Whether the requirement is met.");
        Prompt("req-gaps", "What the requirement does not say.");

        var work = Service(With("req-general", "req-gaps"), withTeamServer: true).BuildWork(
            ["Requirements"], Scratch(), "ctx", round: 1, isPlanStage: false,
            planPrompts: ["req-general", "req-gaps"]);

        work.Excluded.Should().ContainSingle().Which.Role.Should().Be("Requirements");
    }
}
