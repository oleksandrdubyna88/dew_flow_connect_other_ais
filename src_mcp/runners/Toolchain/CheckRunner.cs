using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Toolchain;

/// <summary>Where and how a run of the checks happens.</summary>
/// <param name="Tree">The folder the steps run in and the repository root their paths are relative to: the coder's worktree at its head.</param>
/// <param name="LogFolder">Where each step's log is written; made if it is not there.</param>
/// <param name="Jobs">What <c>{jobs}</c> becomes in an argv: 4 for a heavy job alone, 2 when two run (server.md § 9). At least 1.</param>
public sealed record CheckRequest(string Tree, string LogFolder, int Jobs);

/// <summary>
/// Runs the build and then the test command of each project of a toolchain, in a tree, and writes down what each step did (server.md § 10, § 12).
/// </summary>
/// <remarks>
/// <para><b>Every process goes through <see cref="IProcessLauncher"/></b> — the one launcher, so the deadline and the kill of the WHOLE process tree on expiry are the tested ones. The argv is started with no
/// shell: an argument with <c>;</c>, <c>$(…)</c> or a backtick reaches the program as text. The environment is the minimal list (<see cref="ProcessEnvironment.Minimal"/>) and a few switches that keep a build quiet
/// and its nodes from lingering; none of this product's own variables, and no vendor key, reaches a build.</para>
/// <para><b>Order and stop.</b> Projects in the file's order, each build then its test; the first step that does not pass ends the run, so a build that fails is never followed by its test (a test of a tree that
/// does not build says nothing). <b>No command to run is never green</b>: a file whose projects name neither a build nor a test is <c>not_configured</c>.</para>
/// <para><b>This runs the toolchain it is GIVEN.</b> Which file that is — the tree's, or for a coder job the one at the base commit — is decided before, by the caller; a coder job goes through
/// <see cref="ChecksAtBase"/>, never through a file the coder could have written.</para>
/// </remarks>
public sealed class CheckRunner(IProcessLauncher launcher)
{
    private static readonly IReadOnlyDictionary<string, string?> Quiet = new Dictionary<string, string?> { ["NO_COLOR"] = "1" };

    private static readonly IReadOnlyDictionary<string, string?> DotnetSwitches = new Dictionary<string, string?>
    {
        ["NO_COLOR"] = "1",
        ["DOTNET_NOLOGO"] = "1",
        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
        ["MSBUILDDISABLENODEREUSE"] = "1",
    };

    /// <summary>Runs the checks of <paramref name="toolchain"/> in <paramref name="request"/>'s tree.</summary>
    public async Task<CheckResult> RunAsync(ToolchainRead toolchain, CheckRequest request, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(request.Jobs, 1);

        return toolchain switch
        {
            ToolchainRead.Configured configured => await RunStepsAsync(configured.Toolchain, request, ct),
            ToolchainRead.Refused refused => new CheckResult(CheckState.ToolchainRefused, [], $"the toolchain file is refused, so nothing was run: {refused.Sentence}"),
            _ => new CheckResult(CheckState.NotConfigured, [], $"there is no {ToolchainTables.FilePath}, so nothing was run; checks that did not run are never green"),
        };
    }

    private async Task<CheckResult> RunStepsAsync(RepoToolchain toolchain, CheckRequest request, CancellationToken ct)
    {
        var plan = Plan(toolchain);
        if (plan.Count == 0)
        {
            return new CheckResult(CheckState.NotConfigured, [], "no project of the toolchain file names a build or a test command, so nothing was run; checks that did not run are never green");
        }

        Directory.CreateDirectory(request.LogFolder);
        var done = new List<StepRecord>();
        foreach (var (step, number) in plan.Select((step, index) => (step, index + 1)))
        {
            var record = await RunStepAsync(step, number, request, ct);
            done.Add(record);
            if (!record.Passed)
            {
                return new CheckResult(CheckState.Failed, done, $"{Describe(record)}; the steps after it were not run");
            }
        }

        return new CheckResult(CheckState.Green, done, $"{done.Count} step(s) ran and every one exited 0");
    }

    private sealed record Planned(ToolchainProject Project, CheckStep Step, ToolchainCommand Command, TimeSpan Timeout);

    /// <summary>The steps to run, in order: each project's build, then its test, when the file names one that runs something.</summary>
    private static List<Planned> Plan(RepoToolchain toolchain) =>
    [
        .. toolchain.Projects.SelectMany(project => new[]
        {
            new Planned(project, CheckStep.Build, project.Build, toolchain.Timeouts.Build),
            new Planned(project, CheckStep.Test, project.Test, toolchain.Timeouts.Test),
        }).Where(step => step.Command.Runs),
    ];

    private async Task<StepRecord> RunStepAsync(Planned step, int number, CheckRequest request, CancellationToken ct)
    {
        var argv = Argv(step.Command.Argv, request.Jobs);
        var log = Path.Combine(request.LogFolder, $"{number:00}-{Slug(step.Project.Path)}-{step.Step.ToString().ToLowerInvariant()}.log");
        var clock = Stopwatch.StartNew();
        var launch = new ProcessRequest(Executable(argv[0], request.Tree), argv[1..], request.Tree)
        {
            Timeout = step.Timeout,
            InheritsEnvironment = false,
            Passthrough = ProcessEnvironment.Minimal,
            Environment = step.Project.Adapter == "dotnet" ? DotnetSwitches : Quiet,
            MaxOutputChars = CheckLimits.LogBytes,
        };

        ProcessResult result;
        try
        {
            result = await launcher.RunAsync(launch, ct);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or ArgumentException)
        {
            // A step that cannot start is a failed step with its reason in the log, not a fault of the run: the file named a program this machine does not have.
            var empty = new ProcessResult(StepRecord.NotStartedExitCode, string.Empty, string.Empty, TimedOut: false);
            await CheckLog.WriteAsync(log, empty, $"[coai: the process could not be started: {e.Message}]\n");

            return new StepRecord(step.Project.Path, step.Step, argv, Started: false, StepRecord.NotStartedExitCode, TimedOut: false, clock.Elapsed, log, LogCut: false);
        }

        ct.ThrowIfCancellationRequested();
        var trailer = result.TimedOut ? $"\n[coai: the step ran over its timeout of {step.Timeout} and its whole process tree was killed]\n" : string.Empty;
        var cut = await CheckLog.WriteAsync(log, result, trailer);

        return new StepRecord(step.Project.Path, step.Step, argv, Started: true, result.ExitCode, result.TimedOut, clock.Elapsed, log, cut);
    }

    /// <summary>The argv as it is started: <c>{jobs}</c> replaced inside every item, nothing else touched.</summary>
    private static string[] Argv(IReadOnlyList<string> argv, int jobs) =>
        [.. argv.Select(item => item.Replace("{jobs}", jobs.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))];

    /// <summary>A program named with a folder is relative to the repository root, the way the file writes it; a bare name is looked up on PATH by the launcher.</summary>
    private static string Executable(string first, string tree) =>
        first.Contains('/') || first.Contains(Path.DirectorySeparatorChar) ? Path.GetFullPath(Path.Combine(tree, first)) : first;

    /// <summary>A project path as a file-name part: letters and digits, everything else a dash; the repository root is <c>root</c>.</summary>
    private static string Slug(string path)
    {
        var slug = new string([.. path.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]).Trim('-');

        return slug.Length == 0 ? "root" : slug;
    }

    private static string Describe(StepRecord record)
    {
        var what = $"the {record.Step.ToString().ToLowerInvariant()} step of {record.Project}";
        if (!record.Started)
        {
            return $"{what} could not be started ({record.Argv[0]})";
        }

        return record.TimedOut ? $"{what} ran over its timeout and was stopped" : $"{what} exited {record.ExitCode}";
    }
}
