using System.Diagnostics;
using CoaiMcp.Normalizer;
using CoaiMcp.Runners.Feature;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The outline a <c>none</c> row is given is built ONCE per question (S1's note for S2) — and this is what it
/// costs on a real repository: THIS checkout, outlined at its HEAD, with the time written to the test output.
/// </summary>
/// <remarks>
/// A measurement with a generous bound rather than a tight assertion: the number is recorded for the plan's
/// deviations block, and the bound is what stops an outline that takes minutes from reaching a row whose whole
/// budget is five. Git objects at HEAD, never the working tree — the suite's own uncommitted files cannot reach it.
/// </remarks>
public sealed class QuestionOutlineTimingTests
{
    [Fact]
    public async Task TheOutlineOfThisCheckout_IsBuiltAtHead_InBoundedTime_AndTheTimeIsRecorded()
    {
        var root = RepositoryRoot();
        var launcher = new ProcessLauncher();
        var head = (await launcher.RunAsync(new ProcessRequest("git", ["rev-parse", "HEAD"], root), TestContext.Current.CancellationToken)).StdOut.Trim();
        head.Should().MatchRegex("^[0-9a-f]{40,64}$");

        var started = Stopwatch.StartNew();
        var outline = await new FeatureOutlineBuilder(launcher, new TreeSitterOutliner()).BuildAtHeadAsync(root, head, ct: TestContext.Current.CancellationToken);
        started.Stop();

        // Both to the test output and to stderr: the runner shows a passing test's output nowhere a person
        // reading the console can see it, and the number is the point of this test.
        var measured =
            $"outline of {root} at {head[..8]}: {outline.Files.Count} file(s) changed against the empty tree, {outline.Outlined} outlined, "
            + $"{outline.Section.Length} bytes of section, built in {started.ElapsedMilliseconds} ms";
        TestContext.Current.TestOutputHelper?.WriteLine(measured);
        Console.Error.WriteLine("[measurement] " + measured);
        outline.Section.Should().NotBeEmpty();
        outline.Files.Count.Should().BeGreaterThan(100, "this checkout has hundreds of files");
        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(60), "an outline that takes a minute would eat a row's five-minute budget");
    }

    private static string RepositoryRoot()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && !File.Exists(Path.Combine(here.FullName, "src_mcp", "src", "Tools.cs")))
        {
            here = here.Parent;
        }

        here.Should().NotBeNull("the checkout was not found above the test binary");

        return here!.FullName;
    }
}
