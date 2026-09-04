using Xunit;
using FluentAssertions;
using CoaiMcp.Server;

namespace CoaiMcp.Tests;

/// <summary>
/// A relative data directory must become an absolute one before anything is built from it.
/// </summary>
/// <remarks>
/// <para>Found by this repository's own bench on its FIRST real run, which is the nicest way for a
/// tool to justify itself. Given <c>COAI_DATA_DIR=artifacts/bench/smoke</c> the server started
/// happily, wrote its schema file, and handed the reviewer a RELATIVE path — and a vendor CLI is
/// launched in a directory of its own, so it answered:</para>
/// <code>
/// codex/PlanCritique: exit 1: Failed to read output schema file
/// artifacts/bench/smoke\data\...\finding-schema.json: The system cannot find the path specified.
/// </code>
/// <para>Every reviewer in the round failed the same way, so the round came back `call_human` with
/// nothing reviewed. The setting was accepted and the rounds were unrunnable — the worst shape a
/// configuration error can take, because everything reports success until the answer is empty.</para>
/// </remarks>
public sealed class DataDirIsAbsoluteTests
{
    private static PanelSettings From(string dataDir) =>
        PanelSettings.FromEnvironment(name => name == "COAI_DATA_DIR" ? dataDir : null);

    [Fact]
    public void ARelativeDataDir_IsResolvedAgainstTheWorkingDirectory()
    {
        var settings = From(Path.Combine("artifacts", "bench", "smoke"));

        Path.IsPathRooted(settings.DataDir).Should().BeTrue(
            "a reviewer subprocess launches somewhere else and cannot resolve our relative path");
        settings.DataDir.Should().Be(
            Path.GetFullPath(Path.Combine("artifacts", "bench", "smoke")));
    }

    [Fact]
    public void AnAbsoluteOne_IsLeftExactlyAsItIs()
    {
        var absolute = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "coai-data"));

        From(absolute).DataDir.Should().Be(absolute);
    }

    [Fact]
    public void TheDefault_IsAbsoluteToo() =>
        Path.IsPathRooted(PanelSettings.FromEnvironment(_ => null).DataDir).Should().BeTrue();
}
