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

    /// <summary>
    /// A configured directory is used exactly as configured — the per-side partition is opt-in.
    /// </summary>
    /// <remarks>
    /// Worth a word, because issue #115 nearly changed this. The first build of the side partition
    /// applied it to EVERY override, which moved this directory to <c>&lt;dir&gt;/&lt;side&gt;</c> and
    /// turned six scenario tests red — they set <c>COAI_DATA_DIR</c> and then read files from that
    /// exact path, which is what a script, the bench, and anybody who set the variable last year
    /// also do. The partition is asked for with <c>COAI_DATA_SIDE</c> now, so this guarantee is
    /// exactly what it always was.
    /// </remarks>
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
