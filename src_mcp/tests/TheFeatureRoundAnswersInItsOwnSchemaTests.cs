using System.Collections.Immutable;
using CoaiMcp.Core.Feature;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A feature reviewer is handed the feature schema and the outline truth; every other reviewer keeps the
/// finding schema and the sentence it always had — and a source request is recorded on the note, never lost.
/// </summary>
/// <remarks>
/// <para>The schema and the material come from the stage's row (<see cref="StageDescriptor.Answers"/>,
/// <see cref="StageDescriptor.Reads"/>), so this reads what the roster actually BUILDS for two stages side
/// by side: a code reviewer offered <c>sourceRequests</c> would be offered a field nobody serves, and a
/// feature reviewer quoted the plain schema could not ask at all.</para>
/// </remarks>
public sealed class TheFeatureRoundAnswersInItsOwnSchemaTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-feature-schema-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    private PanelService Service() =>
        new(
            new PanelSettings
            {
                Providers = [new("codex") { ExecutablePath = FakeCliExe, Feature = true }],
                Rounds = new PanelConfig(PanelConfig.AllRoles.ToDictionary(r => r, _ => new RoleGate(1, 5)), StagePolicy.Human),
                DataDir = _data,
            },
            VaultKeys.None("no vault in tests"),
            default,
            new ProcessLauncher(),
            Logger.None, Noticing.None);

    private static IReadOnlyList<string> Launch(PanelService service, Stage stage)
    {
        var roles = service.Settings.Rounds.RolesForRound(stage, 1);
        var work = service.Roster.BuildWork(roles, Path.GetTempPath(), "## the context", 1, stage, readsCheckout: false);

        return [.. work.Reviewers.Select(w => string.Join('\n', [.. w.Invocation.Request.Arguments, w.Invocation.Request.StdIn]))];
    }

    [Fact]
    public void AFeatureReviewer_IsQuotedTheFeatureSchema_AndHandedItsOwnSchemaFile()
    {
        var launch = Launch(Service(), Stage.FeatureReview).Should().ContainSingle("one vendor, the one feature role").Which;

        launch.Should().Contain("\"sourceRequests\"", "the feature schema is the one quoted");
        launch.Should().Contain(SchemaFile.FeatureName, "and the feature schema's own file is the one handed to the CLI");
        launch.Should().Contain("Of the code you have an OUTLINE and the CHANGED HUNKS");
    }

    [Fact]
    public void ACodeReviewer_IsNeverOfferedSourceRequests()
    {
        var launches = Launch(Service(), Stage.CodeReview);

        launches.Should().NotBeEmpty("the fixture must build code work, or the negative below proves nothing");
        launches.Should().OnlyContain(l => !l.Contains("sourceRequests", StringComparison.Ordinal) && !l.Contains(SchemaFile.FeatureName, StringComparison.Ordinal));
        launches.Should().OnlyContain(l => l.Contains("The material below — the change, the plan"), "the change truth, as before");
    }

    [Fact]
    public void EveryStageRow_NamesItsSchemaAndMaterial_AndOnlyTheFeatureStageAsksForSource()
    {
        foreach (var stage in Enum.GetValues<Stage>())
        {
            var row = Stages.Of(stage);
            (row.Answers == SchemaShape.Feature).Should().Be(stage == Stage.FeatureReview, $"{stage}'s schema");
            (row.Reads == ReaderMaterial.Outline).Should().Be(stage == Stage.FeatureReview, $"{stage}'s material");
            row.Reads.Should().NotBe(ReaderMaterial.Checkout, "a checkout is a fact about the launch, never a stage's default");
        }
    }

    [Fact]
    public void ASourceRequest_IsRecordedOnTheNote_AndARefusedOneIsNamed()
    {
        var review = new NormalisedReview([], []) with
        {
            Notes = "The epics add up.",
            SourceRequests = [new SourceRequest("src/Shop.cs", "Shop.Sell", 0, 0, "rounding"), new SourceRequest("src/Cart.cs", string.Empty, 10, 20, string.Empty)],
            RejectedSourceRequests = [new RejectedEntry(2, "the path leaves the repository")],
        };

        var note = SourceRequestNote.With(review);

        note.Should().StartWith("The epics add up.");
        note.Should().Contain(SourceRequestNote.Heading);
        note.Should().Contain("- src/Shop.cs `Shop.Sell` — rounding");
        note.Should().Contain("- src/Cart.cs lines 10–20");
        note.Should().Contain("- request 2 could not be read: the path leaves the repository");
    }

    [Fact]
    public void AReviewerThatAskedForNothing_KeepsItsNotesExactlyAsTheyWere()
    {
        SourceRequestNote.With(new NormalisedReview([], []) with { Notes = "prose" }).Should().Be("prose");
        SourceRequestNote.With(new NormalisedReview([], [])).Should().BeEmpty("no notes and no requests is no note at all");
        SourceRequestNote.With(new NormalisedReview([], []) with { SourceRequests = [new SourceRequest("a.cs", string.Empty, 0, 0, "why")] })
            .Should().StartWith(SourceRequestNote.Heading).And.Contain("a.cs (whole file) — why");
    }
}
