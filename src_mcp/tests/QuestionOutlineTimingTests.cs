using System.Diagnostics;
using System.Text;
using CoaiMcp.Core.Feature;
using CoaiMcp.Normalizer;
using CoaiMcp.Runners.Feature;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The outline a <c>none</c> row is given is built ONCE per question (S1's note for S2) — and this is what it
/// costs on a real repository: THIS checkout, outlined at its HEAD, with the time written to the test output
/// SPLIT BY PHASE, and the product's caps asserted by count.
/// </summary>
/// <remarks>
/// <para><b>What this test used to claim, and why it no longer does (E5 S11, C36).</b> It bounded the wall time at one minute — "an outline that takes a minute
/// would eat a row's five-minute budget". That bound claimed two things: that the product's caps hold on a real tree, and that the cost of the outline is bounded.
/// A wall bound on a checkout that grows cannot claim either. The checkout doubled in size in three weeks (1,997 files on 2026-10-01, 3,561 on 2026-10-22) and a
/// machine running three suites multiplies a build by five, so the test went red ALONE, with the product unchanged — it measured the machine and the tree, not
/// the product. The bound is gone, and that removal is deliberate and said here, in the plan and in the PR, not hidden.</para>
/// <para><b>What pins the caps instead.</b> A COUNT, with no clock, on this checkout (below) and on a fixed fixture no tree growth can move
/// (<see cref="FeatureOutlineCapsTests"/>): never more than <see cref="FeatureBudget.MaxOutlinedFiles"/> files read, never a section over
/// <see cref="FeatureBudget.OutlineBytes"/>, and — what the minute was really afraid of — a number of git processes that does not grow with the tree, so
/// nothing in the build is one launch per file.</para>
/// <para><b>What is still measured, not asserted.</b> The time, by phase: the git listing and sizing (which scale with the TREE) against the git read and the in-process
/// outlining (which the caps bound), so the next reader knows which part a tree of ten thousand files would move. It goes to the test output and to stderr as a
/// <c>[measurement]</c> line. Git objects at HEAD, never the working tree — the suite's own uncommitted files cannot reach it.</para>
/// </remarks>
public sealed class QuestionOutlineTimingTests
{
    /// <summary>
    /// The most git processes one at-head build may start: the empty tree, the listing, the sizes and the read are four; anything near the number of files is a
    /// launch per file. Generous on purpose — the claim is "a small constant", not "exactly four".
    /// </summary>
    private const int MostGitProcessesAtHead = 8;

    [Fact]
    public async Task TheOutlineOfThisCheckout_IsBuiltAtHead_UnderTheProductsCaps_AndTheTimeIsRecordedByPhase()
    {
        var root = RepositoryRoot();
        var head = (await new ProcessLauncher().RunAsync(new ProcessRequest("git", ["rev-parse", "HEAD"], root), TestContext.Current.CancellationToken)).StdOut.Trim();
        head.Should().MatchRegex("^[0-9a-f]{40,64}$");
        var launcher = new OutlineGitPhases(new ProcessLauncher());
        var outliner = new TimedOutliner(new TreeSitterOutliner());

        var started = Stopwatch.StartNew();
        var outline = await new FeatureOutlineBuilder(launcher, outliner).BuildAtHeadAsync(root, head, ct: TestContext.Current.CancellationToken);
        started.Stop();

        // Both to the test output and to stderr: the runner shows a passing test's output nowhere a person
        // reading the console can see it, and the number is the point of this test.
        var measured =
            $"outline of {root} at {head[..8]}: {outline.Files.Count} file(s) changed against the empty tree, {outline.Outlined} outlined, "
            + $"{outline.Section.Length} bytes of section, built in {started.ElapsedMilliseconds} ms — {launcher.Describe(started.Elapsed, outliner)}";
        TestContext.Current.TestOutputHelper?.WriteLine(measured);
        Console.Error.WriteLine("[measurement] " + measured);

        outline.Section.Should().NotBeEmpty();
        outline.Files.Count.Should().BeGreaterThan(100, "this checkout has hundreds of files");
        outline.Outlined.Should().BeLessThanOrEqualTo(FeatureBudget.MaxOutlinedFiles, "no tree, however large, is outlined past the file cap");
        launcher.FilesRead.Should().BeLessThanOrEqualTo(FeatureBudget.MaxOutlinedFiles, "the ONE batch is never asked for more files than the cap");
        Encoding.UTF8.GetByteCount(outline.Section).Should().BeLessThanOrEqualTo(FeatureBudget.OutlineBytes, "the section holds its byte budget on a real tree");
        launcher.Processes.Count.Should().BeLessThanOrEqualTo(MostGitProcessesAtHead, "the build starts a small constant number of git processes — never one per file");
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
