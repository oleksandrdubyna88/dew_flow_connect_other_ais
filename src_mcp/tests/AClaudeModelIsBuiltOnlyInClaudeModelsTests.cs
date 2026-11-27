using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// No claude launch builds its own <c>--model</c>: the one place is <c>ClaudeModels</c>, because the CLI maps the bare alias
/// <c>haiku</c> to Sonnet in plan mode and every claude launch here is in plan mode (research/RESULTS_haiku_alias_in_plan_mode.md).
/// </summary>
/// <remarks>
/// <para><b>What the scan covers.</b> Every non-test <c>.cs</c> file under <c>src_mcp</c> (the reviewer, the stuck consultant, the question
/// consultant and anything added later) that is claude launch code — its file name starts with <c>Claude</c>, or it names
/// <c>--permission-mode</c> — and holds the literal <c>"--model"</c> anywhere but <c>ClaudeModels.cs</c> is a violation. Other vendors'
/// adapters (codex, agy, the api, remote and local rows) build their own <c>--model</c> and are not claude launch code.</para>
/// <para><b>What it does not cover.</b> <c>src_bench/CoaiBench/Judging/Judge.cs</c> builds <c>--permission-mode plan --model &lt;model&gt;</c>
/// itself: the benchmark project does not reference the runners, and its judge model is set by the person running the benchmark
/// (Fable by default). It is the one named exception below, and a test holds the exception to the file, so it cannot outlive a fix. Nothing
/// under <c>src_server</c> or <c>src_bugs</c> builds a claude argv itself: the Team server runs the runners' adapter.</para>
/// <para>A scan reads text, so it sees a spelling and not a behaviour: <see cref="AClaudePlanLaunchNeverSendsTheHaikuAliasTests"/>
/// is what asserts the argv. This scan is what says a NEW claude launch road cannot skip that.</para>
/// </remarks>
public sealed class AClaudeModelIsBuiltOnlyInClaudeModelsTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private const string Sanctioned = "ClaudeModels.cs";

    /// <summary>The one named exception, relative to the repository root, forward slashes.</summary>
    private const string BenchJudge = "src_bench/CoaiBench/Judging/Judge.cs";

    private static IEnumerable<string> SourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part is "obj" or "bin" or "tests" or "node_modules"));

    private static bool IsClaudeLaunchCode(string path, string text) =>
        Path.GetFileName(path).StartsWith("Claude", StringComparison.Ordinal) || text.Contains("\"--permission-mode\"", StringComparison.Ordinal);

    private static bool HoldsTheModelFlag(string text) => text.Contains("\"--model\"", StringComparison.Ordinal);

    /// <summary>The claude launch files under <paramref name="root"/> that hold a <c>"--model"</c>, relative and forward-slashed.</summary>
    internal static IReadOnlyList<string> BuildersOf(string root) =>
        [.. SourceFiles(root)
            .Where(path => IsClaudeLaunchCode(path, File.ReadAllText(path)) && HoldsTheModelFlag(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];

    [Fact]
    public void NoClaudeLaunchInSrcMcpBuildsItsOwnModelArgument()
    {
        var builders = BuildersOf(Path.Combine(RepoRoot, "src_mcp"));

        builders.Where(path => Path.GetFileName(path) != Sanctioned).Should().BeEmpty(
            "a claude `--model` built anywhere but ClaudeModels sends the bare alias `haiku`, which the CLI runs as Sonnet in plan mode");
    }

    /// <summary>The companion: the scan really sees the sanctioned construction, so an empty result above is not an empty scan.</summary>
    [Fact]
    public void TheScanFindsTheSanctionedConstructionInClaudeModels() =>
        BuildersOf(Path.Combine(RepoRoot, "src_mcp")).Should().ContainSingle(path => path.EndsWith("runners/Reviewers/" + Sanctioned, StringComparison.Ordinal));

    [Fact]
    public void TheScanFlagsAClaudeLaunchThatBuildsItsOwnModel()
    {
        var root = Path.Combine(TempDir.Root, "coai-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "runners"));
        File.WriteAllText(Path.Combine(root, "runners", "ClaudeSomethingNew.cs"), "var args = new[] { \"--model\", settings.Model };");
        File.WriteAllText(Path.Combine(root, "runners", "OtherLaunch.cs"), "var args = new[] { \"--permission-mode\", \"plan\", \"--model\", m };");
        File.WriteAllText(Path.Combine(root, "runners", "CodexRuntime.cs"), "var args = new[] { \"--model\", settings.Model };");
        try
        {
            BuildersOf(root).Should().Equal("runners/ClaudeSomethingNew.cs", "runners/OtherLaunch.cs");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The bench judge is the stated exception: it must still be there and still build its own argv, or this note is stale.</summary>
    [Fact]
    public void TheBenchJudgeExceptionIsStillTheCase()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, BenchJudge));

        text.Should().Contain("\"--permission-mode\"").And.Contain("\"--model\"",
            "the exception named in this class's remarks holds only while the judge builds its own plan-mode argv; delete the exception when it does not");
    }
}
