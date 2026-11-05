using System.Diagnostics;
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

    // ---- the folders and the baseline (the layout the template does not have) ----------------------------------------------------------------------

    /// <summary>
    /// R4, must-not-copy #1 (the audit's sibling). Two children of ONE run share a live <c>TESTSTODELETE_RUN</c> record and one private root. The child that finishes first runs its audit while the other still works: the other's
    /// folder and claim are untouched, byte for byte. What this proves is the sweep and the audit's bound: child a's audit reads a's own run folder, so it cannot reach b's folder with or without the run-record clause, and the RED recorded for R4 needed two plants together. The clause itself is proved by the template's in-process twin, <c>A_sibling_childs_folder_survives_the_audit_of_a_run_whose_record_it_shares</c>.
    /// </summary>
    [Fact]
    public async Task TwoChildSuitesUnderOneRun_TheFirstToFinish_LeavesTheSecondsFolder()
    {
        var root = _owned.Dir("s6-siblings");
        var work = _owned.Dir("s6-siblings-work");
        var ready = Path.Combine(work, "ready");
        var go = Path.Combine(work, "go");
        Dictionary<string, string?> EnvFor(string role) => new(AllThreeAt(root))
        {
            ["TESTSTODELETE_RUN"] = Owner.This.Format(),
            ["COAI_S6_SIBLING"] = role,
            ["COAI_S6_SHARED_ROOT"] = root,
            ["COAI_S6_READY"] = ready,
            ["COAI_S6_GO"] = go,
        };

        using var b = Process.Start(SuiteChild.StartInfoFor(Environment.ProcessPath!, ["--filter-class", "*SiblingChild"], EnvFor("b")))!;
        var bOut = b.StandardOutput.ReadToEndAsync();
        var bErr = b.StandardError.ReadToEndAsync();
        try
        {
            SpinWait.SpinUntil(() => File.Exists(ready) || b.HasExited, TimeSpan.FromSeconds(45));
            File.Exists(ready).Should().BeTrue($"child b made its folder: {(b.HasExited ? await bOut + await bErr : string.Empty)}");
            var folder = File.ReadAllText(ready);
            var claim = folder + ".owner";
            var before = File.ReadAllBytes(claim);

            var a = SuiteChild.Run(["--filter-class", "*SiblingChild"], EnvFor("a"), ChildTimeout);

            a.TimedOut.Should().BeFalse(a.StdErr);
            a.ExitCode.Should().Be(0, $"a's audit found nothing of its own: {a.StdOut} / {a.StdErr}");
            Directory.Exists(folder).Should().BeTrue("b's folder is still there after a's audit");
            File.ReadAllBytes(claim).Should().Equal(before, "b's claim is byte for byte what b wrote");

            File.WriteAllText(go, "go");
            b.WaitForExit(TimeSpan.FromSeconds(60)).Should().BeTrue("b ends once it is let go");
            b.ExitCode.Should().Be(0, await bOut + await bErr);
        }
        finally
        {
            File.WriteAllText(go, "go");
            if (!b.HasExited)
            {
                b.Kill(entireProcessTree: true);
            }
        }
    }

    /// <summary>R12 (the re-review's finding e2). The repository root is found by walking up from the binary, whatever the working folder of the process is, so the baseline file at the root is found from the project folder and from the output folder alike. Measured 2026-11-05: the xUnit host moves the working folder to the binary's folder before test code runs, so all the children see one working folder, and a walk up from it also passes. This test goes red when the file is absent; it does not prove the walk's starting point.</summary>
    [Fact]
    public async Task TheBaselineIsFound_FromBothPlaces()
    {
        var work = _owned.Dir("s6-baseline-found");
        var places = new[] { Path.GetFullPath(Path.Combine(RepositoryRoot.Path, "src_mcp", "tests")), AppContext.BaseDirectory };
        for (var index = 0; index < places.Length; index++)
        {
            var outFile = Path.Combine(work, $"found-{index}");
            var start = SuiteChild.StartInfoFor(Environment.ProcessPath!, ["--filter-class", "*BaselineLocationChild"], new Dictionary<string, string?> { ["COAI_S6_BASELINE_OUT"] = outFile });
            start.WorkingDirectory = places[index];
            using var process = Process.Start(start)!;
            var text = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            process.WaitForExit(ChildTimeout).Should().BeTrue("the child ends by itself");
            process.ExitCode.Should().Be(0, await text + await error);

            File.ReadAllText(outFile).Should().Be("True", $"the baseline is found when the working folder is {places[index]}");
        }
    }

    /// <summary>R11. The scan sees every root of this repository's layout, and only them.</summary>
    [Fact]
    public void The_scan_sees_the_coai_roots()
    {
        string[] seen =
        [
            "src_mcp/tests_leftovers/X.cs", "src_mcp/tests_fakecli/X.cs", "src_server/tests/X.cs", "src_bugs/tests/X.cs", "src_bench/CoaiBench.Tests/X.cs", "src_mcp/tests/X.cs",
        ];
        string[] unseen = [".agents/conventions/x.sh", ".claude/hooks/x.sh", "src_server/install-cli.sh", "src_mcp/core/X.cs"];
        var root = _owned.Dir("s6-roots");
        var violation = PlantedLines.CSharp("x");
        foreach (var file in seen.Concat(unseen).Append("src_vs_code/scripts/x.mjs"))
        {
            var text = (file.EndsWith(".sh", StringComparison.Ordinal) ? PlantedLines.Shell() : file.EndsWith(".mjs", StringComparison.Ordinal) ? PlantedLines.Mjs() : violation) + "\n";
            var target = Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, text);
        }

        var report = LeftoversGuard.Scan(root, RepositoryRoot.Path);

        report.FilesScanned.Should().Be(seen.Length + 1, "six C# roots and the script folder are read; the mount, a hidden adapter folder, the frozen list and product code are not");
        report.Findings.Select(f => f.File).Should().BeEquivalentTo([.. seen, "src_vs_code/scripts/x.mjs"]);
        report.Findings.Should().OnlyContain(f => f.Class == "raw-temp");
    }

    /// <summary>R18. <c>RunTempFixture</c> ends nothing: ending the run is the sweep fixture's disposal, after the audit.</summary>
    [Fact]
    public void RunTempFixture_EndsNothing() =>
        typeof(RunTempFixture).GetInterfaces().Should().NotContain(typeof(IDisposable), "its disposal would end the run folder before the audit reads it");
}
