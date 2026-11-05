using CoaiMcp.Tests.Leftovers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests.Leftovers;

/// <summary>
/// What is coai's own about the end of the run and, further down, about the guard's layout (E5 S6, <c>todo/PLAN_test_leftovers_E5_S6_brief.md</c> § 11 and § 14). The template has no run folder, no
/// second helper folder and no baseline, so none of this is in its tests: it is HERE, in a file that is not a copy.
/// </summary>
/// <remarks>
/// <para>The first group is condition (o) of the plan: the end-of-run audit reads the run folder while it exists, then the run ends. <c>RunTempFixture</c> ends nothing; the sweep fixture's disposal
/// calls the audit, then <see cref="RunTemp.EndOfRun"/> in a <c>finally</c>, in ONE ordered call that only the assembly fixture's own constructor sets.</para>
/// </remarks>
public sealed class LeftoversCoaiLayoutTests : IDisposable
{
    private static readonly TimeSpan ChildTimeout = TimeSpan.FromSeconds(60);

    private readonly OwnedScope _owned = DarwinLayout.FreshOwnedScope();

    public void Dispose() => _owned.Dispose();

    private static Dictionary<string, string?> AllThreeAt(string path) => RunTempFolder.Variables.ToDictionary(name => name, _ => (string?)path);

    /// <summary>R5. A child run that leaves nothing reports no leak and ends its own run folder. A child of the audit that read the machine's temp root and the registry whole would name the run folder as "never disposed".</summary>
    [Fact]
    public void AGreenRun_ReportsNoLeak_AndEndsTheRunFolder()
    {
        var root = _owned.Dir("s6-green");
        var env = AllThreeAt(root);
        env["TESTSTODELETE_RUN"] = null;

        var result = SuiteChild.Run(["--filter-class", "*DiscoverySentinel"], env, ChildTimeout);

        result.TimedOut.Should().BeFalse(result.StdErr);
        result.ExitCode.Should().Be(0, $"stdout: {result.StdOut} / stderr: {result.StdErr}");
        (result.StdOut + result.StdErr).Should().NotContain("this run left", "a run that left nothing is not named by the audit");
        Directory.GetFileSystemEntries(root).Should().BeEmpty("the run folder and its claim are gone after the audit");
    }

    /// <summary>R7. The audit reads the registry WITHOUT the run folder (it ends right after) and WITH a folder a test leaked.</summary>
    [Fact]
    public void TheRegistry_LeavesOutTheRunFolder_AndKeepsALeak()
    {
        OwnedDir.Unreclaimed().Select(dir => dir.Path).Should().Contain(RunTemp.Folder, "the run folder is an OwnedDir of this process: a registry read whole would name it");
        var leak = OwnedDir.Create("s6-registry", _owned.Dir("s6-registry-parent"));
        try
        {
            var seen = RunTemp.RegistryWithoutTheRunFolder().Select(dir => dir.Path).ToList();

            seen.Should().NotContain(RunTemp.Folder, "the run folder is ended by the fixture's disposal right after the audit");
            seen.Should().Contain(leak.Path, "a folder a test made and never disposed is what the registry check is for");
        }
        finally
        {
            leak.Dispose();
        }
    }

    /// <summary>R17. A sweep fixture built through the seam constructor (every test that builds one) never ends the REAL run: only the assembly fixture's own constructor wires the ending.</summary>
    [Fact]
    public async Task DisposingAFixtureBuiltThroughTheSeam_DoesNotEndTheRun()
    {
        var fixture = new LeftoversSweepFixture(() => SweepResult.Empty, _ => { }, () => { });

        await fixture.DisposeAsync();

        Directory.Exists(RunTemp.Folder).Should().BeTrue("the run folder is still there: a test built a fixture and disposed it");
        RunTemp.Refusal().Should().BeNull("the three variables still name the run folder");
    }

    /// <summary>R18. <c>RunTempFixture</c> ends nothing: ending the run is the sweep fixture's disposal, after the audit.</summary>
    [Fact]
    public void RunTempFixture_EndsNothing() =>
        typeof(RunTempFixture).GetInterfaces().Should().NotContain(typeof(IDisposable), "its disposal would end the run folder before the audit reads it");
}
