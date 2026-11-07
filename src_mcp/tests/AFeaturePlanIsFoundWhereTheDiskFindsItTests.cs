using CoaiMcp.Core.Feature;
using CoaiMcp.Runners.Files;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>review_feature</c> finds <c>planPath</c> the way the disk does: through its links, not by folding <c>..</c> as text.
/// </summary>
/// <remarks>
/// The existence check folded <c>alias/../plan.md</c> to <c>plan.md</c> next to the repository's top, while the reader resolved
/// <c>alias</c> first — so a plan the reader would have read was refused as missing, and one it would not have read was said to
/// be there.
/// </remarks>
public sealed class AFeaturePlanIsFoundWhereTheDiskFindsItTests : IDisposable
{
    private const string Plan = "# The plan\n\nBuild it.\n";

    private readonly TempDir _work = TempDir.For("featureplan-link-");
    private readonly string _repo;

    public AFeaturePlanIsFoundWhereTheDiskFindsItTests()
    {
        _repo = RealPath.Resolve(_work.At("repo")) is { Ok: true } real ? real.Path : _work.At("repo");
        Directory.CreateDirectory(Path.Combine(_repo, "real", "sub"));
        File.WriteAllText(Path.Combine(_repo, "real", "plan.md"), Plan);
    }

    public void Dispose() => _work.Dispose();

    private void Alias(string name, string target)
    {
        try
        {
            File.CreateSymbolicLink(Path.Combine(_repo, name), target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"this machine cannot create a symbolic link: {e.Message}");
        }
    }

    [Fact]
    public void ADotDotAfterALink_IsAppliedToTheRealFolder_SoThePlanBesideItsTargetIsFound()
    {
        Alias("alias", Path.Combine(_repo, "real", "sub"));

        var outcome = FeaturePlan.Read(_repo, "alias/../plan.md");

        var accepted = outcome.Should().BeOfType<FeatureInput<FeaturePlanText>.Accepted>(
            (outcome as FeatureInput<FeaturePlanText>.Refused)?.Sentence).Subject;
        accepted.Value.Identity.Should().Be("real/plan.md", "the identity is where the plan really is");
        accepted.Value.Text.Should().Be(Plan);
    }

    [Fact]
    public void APlanThatIsNotWhereTheDiskResolvesIt_IsRefusedAsMissing_InTheFeatureStagesWords()
    {
        // Folded as text `alias/../plan.md` is `plan.md` at the top, which EXISTS; on the disk it is `real/plan.md`, which does not.
        // The check must ask the disk's answer, so the refusal is the stage's own — not the reader's, which speaks of documentPath.
        Alias("alias", Path.Combine(_repo, "real", "sub"));
        File.WriteAllText(Path.Combine(_repo, "plan.md"), Plan);
        File.Delete(Path.Combine(_repo, "real", "plan.md"));

        var outcome = FeaturePlan.Read(_repo, "alias/../plan.md");

        var sentence = outcome.Should().BeOfType<FeatureInput<FeaturePlanText>.Refused>().Subject.Sentence;
        sentence.Should().Contain("there is no plan file").And.NotContain("documentPath");
    }
}
