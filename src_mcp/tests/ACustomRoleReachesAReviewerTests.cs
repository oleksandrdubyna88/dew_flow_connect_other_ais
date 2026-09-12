using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A role nobody compiled in is dealt real work, with its own text, through the ordinary path.
/// </summary>
/// <remarks>
/// <para>This is the whole point of retiring <c>ReviewRole</c>, and it is the one thing the
/// mechanical half of that change could not prove on its own: every existing test would have stayed
/// green with the enum replaced by a string and the round still asking a compiled-in list. So the
/// catalog is composed with a role this build has never heard of, its prompt is written where the
/// override layer looks, and the assertion is on what the vendor adapter was actually handed.</para>
/// <para>It stops at <c>BuildWork</c> rather than running a round: what B1 changed is which roles a
/// round can be built FROM and which text they resolve to. Reading <c>COAI_ROLES</c> from a settings
/// file is story B2's, and a custom role whose text is MISSING is B2's too — here the text exists,
/// because the question is whether the path reaches it at all.</para>
/// </remarks>
public sealed class ACustomRoleReachesAReviewerTests : IDisposable
{
    private readonly string _dataDir = Directory.CreateTempSubdirectory("coai-customrole-").FullName;

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

    private const string TheText = "You review one thing: whether the requirement is actually met.";

    private PanelService Service(RoleCatalog catalog)
    {
        Directory.CreateDirectory(Path.Combine(_dataDir, "prompts"));
        File.WriteAllText(Path.Combine(_dataDir, "prompts", "req-general.md"), TheText);

        var settings = new PanelSettings
        {
            DataDir = _dataDir,
            CodeWorkspace = "none",
            Rounds = new PanelConfig(Roles: null, StagePolicy.Human) { Catalog = catalog },
            Providers = [new ProviderSettings("local") { Enabled = true, Runtime = "local", Model = "m" }],
        };

        return new PanelService(settings, VaultKeys.None("no vault"), default,
            new Runners.Processes.ProcessLauncher(), Serilog.Core.Logger.None);
    }

    private static RoleCatalog WithRequirements() =>
        RoleComposition.Compose([
            new RoleEntry("Requirements", Name: "Requirements", Stage: RoleStages.Result,
                Prompts: [new PromptEntry("req-general", "General", "Whether the requirement is met.")]),
        ]);

    private static string Scratch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coai-custom-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void ItIsBuiltIntoTheRound_UnderItsOwnName()
    {
        var work = Service(WithRequirements())
            .BuildWork(["Requirements"], Scratch(), "ctx", round: 1, isPlanStage: false);

        work.Should().ContainSingle().Which.Invocation.Role.Should().Be("Requirements",
            "the role travels as the person spelled it — there is no enum left to fold it into");
    }

    [Fact]
    public void ItsOwnPromptTextIsWhatTheReviewerIsHanded()
    {
        var work = Service(WithRequirements())
            .BuildWork(["Requirements"], Scratch(), "ctx", round: 1, isPlanStage: false);

        Handed(work[0]).Should().Contain(TheText,
            "the text comes from <dataDir>/prompts/<prompt id>.md, which is where a role nobody "
            + "compiled in can have one at all");
        work[0].Prompt.Should().Be("req-general", "the round records WHICH prompt it asked");
    }

    [Fact]
    public void AShippedRoleIsUnaffected_AndStillAsksItsOwnQuestion()
    {
        // The other half of the claim: making the catalog data changed nothing about the five roles
        // that were already there.
        var work = Service(WithRequirements())
            .BuildWork([RoleCatalog.ArchitectureRole], Scratch(), "ctx", round: 1, isPlanStage: false);

        work[0].Invocation.Role.Should().Be(RoleCatalog.ArchitectureRole);
        work[0].Prompt.Should().Be("architecture");
        Handed(work[0]).Should().Contain("ARCHITECTURE reviewer", "the shipped text, from the binary");
    }

    [Fact]
    public void ThePlanStagesLensPath_ResolvesThroughTheCatalogToo()
    {
        // `Items` has a second branch — the plan stage dealing unspent lenses — that takes its
        // prompt ids from the caller rather than from `ChoiceFor`, and `Add` then looks each one up.
        // A custom role reaching one and not the other would be a round that works until somebody
        // ticks "deal the lenses". (codex, B1's plan round.)
        var catalog = RoleComposition.Compose([
            new RoleEntry("Brief", Name: "The brief", Stage: RoleStages.Plan,
                Prompts:
                [
                    new PromptEntry("brief-general", "General", "The whole brief."),
                    new PromptEntry("brief-gaps", "Gaps", "What it does not say."),
                ]),
        ]);
        var service = Service(catalog);
        File.WriteAllText(Path.Combine(_dataDir, "prompts", "brief-gaps.md"), "What does it leave out?");

        var work = service.BuildWork(["Brief"], Scratch(), "ctx", round: 1, isPlanStage: true,
            planPrompts: ["brief-gaps"]);

        work.Should().ContainSingle().Which.Prompt.Should().Be("brief-gaps");
        Handed(work[0]).Should().Contain("What does it leave out?");
    }

    [Fact]
    public void APromptIdNobodyKnows_FallsBackToTheRolesOwnGeneralPrompt()
    {
        // `Add`'s second lookup: a stale pick from the settings must not leave a round with no
        // prompt, and for a custom role the fallback has to come from the catalog rather than from
        // a compiled list that has never heard of it.
        var work = Service(WithRequirements()).BuildWork(
            ["Requirements"], Scratch(), "ctx", round: 1, isPlanStage: false,
            planPrompts: ["a-prompt-that-was-deleted"]);

        work[0].Prompt.Should().Be("req-general", "the role's first prompt is its general one");
        Handed(work[0]).Should().Contain(TheText);
    }

    [Fact]
    public void AShippedRolesAnswerFileIsNamedWithItsHistoricalSpelling()
    {
        // Asserted as a LITERAL rather than through the catalog's constant: an id renamed in the
        // seed and the constant together would pass every test that reads the constant, while every
        // answer file, ledger row and rounds-database row already written said the old word.
        // (codex, B1's plan round.)
        var work = Service(WithRequirements())
            .BuildWork([RoleCatalog.ArchitectureRole], Scratch(), "ctx", round: 1, isPlanStage: false);

        var arguments = work[0].Invocation.Request.Arguments;
        arguments[arguments.ToList().IndexOf("--out") + 1]
            .Should().Contain("Architecture", "the spelling every already-written record uses");
    }

    [Fact]
    public void ARoleWhoseNameIsAPath_CannotWriteOutsideTheOutputDirectory()
    {
        // Composition refuses an id like this, so nothing should ever build one — which is exactly
        // why the second lock is worth having: the rule lives in the core and the file name is built
        // in a vendor adapter, and a caller assembling an invocation by hand passes neither.
        // (codex, B1's plan round.)
        // Three levels, not two: the role sits in the MIDDLE of `local-<role>-<guid>.prompt`, so the
        // leading `local-` absorbs the first `..` and two of them land back where they started. That
        // was the first draft of this test, and it passed with the guard removed — which is what a
        // teeth check is for.
        var outputDir = Scratch();
        var invocation = new LocalRuntime("local", "http://127.0.0.1:11434/v1")
            .Build("../../../escaped", "p", Scratch(), "schema.json", outputDir, new ReviewerSettings("local"));

        var arguments = invocation.Request.Arguments;
        var promptFile = arguments[arguments.ToList().IndexOf("--prompt-file") + 1];

        // The DIRECTORY, not a prefix: a sibling called `<outputDir>-elsewhere` starts with the same
        // characters and is not the same place.
        Path.GetDirectoryName(Path.GetFullPath(promptFile))
            .Should().Be(Path.GetFullPath(outputDir).TrimEnd(Path.DirectorySeparatorChar),
                "a role's name reaches a path, and a path is not a place a name may decide");
    }

    /// <summary>What the launch would actually put in front of the model.</summary>
    /// <remarks>
    /// The local adapter writes the composed prompt to a file and passes <c>--prompt-file</c>, so
    /// the text is read back from the path the arguments name rather than from the record — which is
    /// the point: this asserts what a reviewer is HANDED, not what the server believes it composed.
    /// </remarks>
    private static string Handed(ReviewerWork work)
    {
        var arguments = work.Invocation.Request.Arguments;
        var file = arguments[arguments.ToList().IndexOf("--prompt-file") + 1];

        return File.ReadAllText(file);
    }
}
