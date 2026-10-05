using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--features</c>: the binary lists the capabilities it has, and the extension sends a field only when it is listed
/// (todo/PLAN_one_model_catalog.md, epic 2, as revised by its plan round — "capability, not version numbers").
/// </summary>
/// <remarks>
/// A constant such as <c>SYSTEM_PROMPT_SINCE = "0.44.0"</c> guesses the release number before release-please cuts it,
/// and a binary built from a branch has no number at all. The binary is the one thing that knows what it does; asked,
/// it says, and an older one exits 64 — the sanctioned "never heard of this mode" — which reads as an empty list.
/// </remarks>
public sealed class TheBinarySaysWhatItAcceptsTests
{
    [Fact]
    public void FeaturesIsAKnownMode_NotTheUsageAnOlderBinaryAnswers()
    {
        Program.Classify(["--features"]).Should().NotBe(Program.Startup.Usage,
            "64 is how the extension recognises a binary that predates the list");
    }

    [Fact]
    public void TheAnswerIsOneObject_ListingEachCapabilityOnce()
    {
        using var answer = System.Text.Json.JsonDocument.Parse(Server.FeaturesMode.Answer());
        var listed = answer.RootElement.GetProperty("features").EnumerateArray().Select(one => one.GetString()).ToList();

        listed.Should().OnlyHaveUniqueItems().And.Contain("bugzRuntime",
            "story 1 shipped --collect-bugs --runtime, and the extension passes it only when this says so");
    }

    [Fact]
    public void TheModeIsInTheHelp_AndInTheOneShotList()
    {
        Program.HelpText.Should().Contain("`--features`");
        File.ReadAllText(Path.Combine(ProductionSources.RepositoryRoot(), ".agents", "PROJECT.md"))
            .Should().Contain("`--features`", "adding a one-shot mode means adding it to PROJECT.md's list");
    }
}
