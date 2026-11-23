using System.Diagnostics;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Runners.Worktrees;
using CoaiMcp.Work;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s2 t4, DoD d6: does THIS repository's build and whole test suite pass inside the check boundary, and under <c>PrivateNetwork=yes</c>? The recorded run puts a clean clone of HEAD on a real disk under a short path, makes the coder worktree
/// there with <see cref="CoderWorktree"/>, and runs <see cref="ChecksAtBase.RunCoderJobAsync"/> — the product's own road — on it, once. The numbers are in <c>v2/epoch-3/e2-coder-one-task/RESULTS_s2_check_boundary.md</c>.
/// </summary>
/// <remarks>
/// <para>Skipped unless <c>COAI_RECORD_BOUNDARY_RUN</c> names an EMPTY or absent folder on a real disk with a short path (<c>RESULTS_s1_toolchain.md</c>: a tmpfs under the 5 GiB floor and a temp root of 33 characters each reddened tests).
/// It uses the person's real home on purpose — the shared NuGet folder and node come from it, read-only, and the preflight asks the real <c>ProtectHome</c> — so it is an operator's measurement and never part of the ordinary run.</para>
/// <para><c>COAI_RECORD_BOUNDARY_BUILD_NETWORK</c> and <c>COAI_RECORD_BOUNDARY_TEST_NETWORK</c> take <c>none</c> (default) or <c>host</c>; <c>COAI_RECORD_BOUNDARY_MEMORY_MB</c> the unit's <c>MemoryMax</c> (default 8000).</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class AThisRepositoryUnderTheBoundaryTests
{
    private const string RecordVariable = "COAI_RECORD_BOUNDARY_RUN";

    private static string Setting(string name, string fallback) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

    private static NetworkChoice Network(string name) =>
        Setting(name, "none") == "host" ? NetworkChoice.OnHost($"{name} = host for this measurement") : NetworkChoice.None($"{name} = none for this measurement");

    private static async Task RunAsync(IProcessLauncher launcher, string folder, string verb, params string[] args)
    {
        var done = await launcher.RunAsync(new ProcessRequest(verb, args, folder) { Timeout = TimeSpan.FromMinutes(10) });
        done.ExitCode.Should().Be(0, $"{verb} {string.Join(' ', args)}: {done.StdErr}");
    }

    [Fact]
    public async Task ARecordedRunUnderTheBoundary()
    {
        var folder = Environment.GetEnvironmentVariable(RecordVariable) ?? string.Empty;
        Assert.SkipWhen(folder.Length == 0, $"set {RecordVariable} to a new folder on a real disk with a short path to record the checks of this repository inside the boundary (it builds and runs the whole suite)");
        Assert.SkipUnless(SystemdUser.Works(out var why), why);
        Directory.Exists(folder).Should().BeFalse($"{folder} must not exist: the run makes it");
        var launcher = new ProcessLauncher();
        var main = Path.Combine(folder, "m");
        Directory.CreateDirectory(folder);

        // What CI does around a checkout (ci.yml), done by the harness BEFORE the job: a clean clone of HEAD, the rules mount and the node packages the tests that read it need.
        await RunAsync(launcher, folder, "git", "clone", "--no-hardlinks", "--quiet", RepoPaths.Root(), main);
        await RunAsync(launcher, main, "git", "submodule", "update", "--init", "--depth", "1", ".agents/conventions");
        var head = (await launcher.RunAsync(new ProcessRequest("git", ["rev-parse", "HEAD"], main))).StdOut.Trim();
        var started = await new CoderWorktree(launcher, Path.Combine(folder, "c")).StartAsync(main, "m1", head);
        var tree = started.Should().BeOfType<CoderWorktreeOutcome.Started>(started.ToString()).Subject;
        await RunAsync(launcher, tree.Path, "git", "submodule", "update", "--init", "--depth", "1", ".agents/conventions");
        await RunAsync(launcher, tree.Path, "npm", "ci", "--ignore-scripts", "--prefix", ".agents/conventions");
        var opened = CoderTree.Open(tree.Path, main).Should().BeOfType<CoderTreeOpening.Opened>().Subject.Tree;
        var job = new BoundaryJob("m1", opened, Path.Combine(folder, "j"), Network("COAI_RECORD_BOUNDARY_BUILD_NETWORK"), Network("COAI_RECORD_BOUNDARY_TEST_NETWORK"),
            int.Parse(Setting("COAI_RECORD_BOUNDARY_MEMORY_MB", "8000"), System.Globalization.CultureInfo.InvariantCulture));
        var checks = new ChecksAtBase(new GitHistory(launcher), new CheckRunner(launcher, new CheckBoundary(launcher, BoundaryHost.Current())));
        var clock = Stopwatch.StartNew();

        var result = await checks.RunCoderJobAsync(new DiskWorkFolder(tree.Path), head, job, Path.Combine(folder, "logs"), 4);

        var lines = new List<string> { $"recorded run of .coai/toolchain.yaml at {head}, worktree {tree.Path}, {clock.Elapsed}: {result.State.Wire()} — {result.Note}" };
        foreach (var step in result.Steps)
        {
            var log = File.ReadAllText(step.LogPath);
            lines.Add($"  {step.Project} {step.Step}: exit {step.ExitCode}, {step.Duration}, network {step.Boundary.Network} ({step.Boundary.NetworkReason}), log {step.LogPath}");
            lines.AddRange(log.Split('\n').Where(line => line.Contains("Test run summary", StringComparison.Ordinal) || line.TrimStart().StartsWith("total:", StringComparison.Ordinal) || line.TrimStart().StartsWith("failed:", StringComparison.Ordinal) || line.TrimStart().StartsWith("succeeded:", StringComparison.Ordinal) || line.TrimStart().StartsWith("skipped:", StringComparison.Ordinal) || line.TrimStart().StartsWith("duration:", StringComparison.Ordinal)).Select(line => "    " + line.Trim()));
        }

        lines.AddRange(result.Boundary.Preflight.Concat(result.Steps.Count > 0 ? result.Steps[0].Boundary.Preflight : []).Select(row => $"  preflight {row.Item}: {(row.Held ? "held" : "FAILED")} — {row.Detail}"));
        await File.WriteAllLinesAsync(Path.Combine(folder, "RESULT.txt"), lines);
        foreach (var line in lines)
        {
            Console.Error.WriteLine(line);
        }

        result.Steps.Should().NotBeEmpty($"the run was refused or did not start: {result.Note}");
    }
}
