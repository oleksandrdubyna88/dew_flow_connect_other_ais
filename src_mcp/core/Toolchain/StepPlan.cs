namespace CoaiMcp.Core.Toolchain;

/// <summary>The network a planned step asks for, and the reason, which goes into the step's record. <c>null</c> in <see cref="PlannedStep"/> means "the job's own": E2's build and test defaults.</summary>
public sealed record PlannedNetwork(DefaultNetwork Mode, string Reason);

/// <summary>
/// One step of a round, decided before anything runs: what is started, from which folder, with which network and switches, under which timeout.
/// </summary>
/// <param name="Project">The project's id: the project path of the file, or the file that makes a detected project.</param>
/// <param name="Adapter">The stack (<c>dotnet</c>, <c>node</c>…): the failure digest chooses its reader by it.</param>
/// <param name="Step">Restore, build, typecheck or test.</param>
/// <param name="Argv">The command, executable first; <c>{jobs}</c> and <c>{unit}</c> are filled (a detected step has its unit already).</param>
/// <param name="Network">The network and its reason; null for a step of the file that takes the job's own state.</param>
/// <param name="WorkingFolder">Relative to the repository root; <c>.</c> is the root. A step of the file runs from the root, as before.</param>
/// <param name="Timeout">Build's timeout for restore, build and typecheck; the test timeout for a test step.</param>
/// <param name="Origin"><c>file</c>, <c>defaults</c> or <c>detected</c>.</param>
/// <param name="Subject">What the step is about: the test project of a .NET test step, else the project's id.</param>
/// <param name="Switches">The adapter's environment switches: names and constant values, never a value from coai's own environment.</param>
public sealed record PlannedStep(
    string Project,
    string Adapter,
    CheckStep Step,
    IReadOnlyList<string> Argv,
    PlannedNetwork? Network,
    string WorkingFolder,
    TimeSpan Timeout,
    string Origin,
    string Subject,
    IReadOnlyDictionary<string, string?> Switches);

/// <summary>
/// The step plan of a resolved toolchain (epoch 3 E4 s3 t2): the steps of every project in the project order and, in a project, in the order of a round — restore, build, typecheck, test — with the first step that does not pass ending the run. Pure.
/// </summary>
/// <remarks>
/// <para>The names that run in E4 are restore, build, typecheck and test (<c>format</c> is the formatter turn of s5, <c>lint</c>, <c>audit</c> and <c>coverage</c> run nowhere). <b>A plan with no build, typecheck and test step is not a plan</b>: it is <c>not_configured</c>, because a restore alone checks nothing.</para>
/// <para>A project of the file runs exactly the commands it states, from the repository root, with the job's own network states (E2); a detected or defaulted project runs the table's steps, from its project folder, with the table's network states, and <c>skip</c> of the file takes <c>restore</c> out of a defaulted project.</para>
/// </remarks>
public static class StepPlan
{
    private static readonly IReadOnlyDictionary<string, string?> Quiet = new Dictionary<string, string?> { ["NO_COLOR"] = "1" };

    private static readonly IReadOnlyDictionary<string, string?> DotnetSwitches = new Dictionary<string, string?>
    {
        ["NO_COLOR"] = "1",
        ["DOTNET_NOLOGO"] = "1",
        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
        ["MSBUILDDISABLENODEREUSE"] = "1",
    };

    /// <summary>The steps of <paramref name="toolchain"/>, in the order they run, with <c>{jobs}</c> replaced by <paramref name="jobs"/>.</summary>
    public static IReadOnlyList<PlannedStep> Of(ResolvedToolchain toolchain, ToolchainDefaults table, int jobs, AffectedSelection? selection = null)
    {
        var timeouts = toolchain.File?.Timeouts ?? ToolchainTimeouts.Default;
        var skip = toolchain.File?.Skip ?? [];
        var plan = new List<PlannedStep>();
        foreach (var project in toolchain.Projects)
        {
            if (selection is not null && !selection.Runs(project.Id))
            {
                continue;
            }

            if (project.FileProject is { } file)
            {
                plan.AddRange(FileSteps(file, timeouts, jobs));
            }
            else if (project.Detected is { } detected)
            {
                plan.AddRange(DetectedSteps(project, detected, table, timeouts, skip, jobs).Where(step => selection is null || step.Step != CheckStep.Test || selection.RunsTest(project.Id, step.Subject)));
            }
        }

        return plan;
    }

    /// <summary>The steps of a toolchain FILE read as it always was: each project's build and then its test, plus a restore when the file states one. Used for a file the caller has already resolved to <c>file</c>.</summary>
    public static IReadOnlyList<PlannedStep> OfFile(RepoToolchain toolchain, int jobs) =>
        [.. toolchain.Projects.SelectMany(project => FileSteps(project, toolchain.Timeouts, jobs))];

    /// <summary>Whether the plan has something that checks the work: a build, a typecheck or a test step.</summary>
    public static bool Checks(IReadOnlyList<PlannedStep> plan) => plan.Any(step => step.Step is CheckStep.Build or CheckStep.Typecheck or CheckStep.Test);

    private static IEnumerable<PlannedStep> FileSteps(ToolchainProject project, ToolchainTimeouts timeouts, int jobs)
    {
        var switches = project.Adapter == "dotnet" ? DotnetSwitches : Quiet;
        (CheckStep Step, ToolchainCommand Command, TimeSpan Timeout)[] steps =
        [
            (CheckStep.Restore, project.Restore, timeouts.Build),
            (CheckStep.Build, project.Build, timeouts.Build),
            (CheckStep.Test, project.Test, timeouts.Test),
        ];

        return steps.Where(step => step.Command.Runs).Select(step => new PlannedStep(project.Path, project.Adapter, step.Step, Fill(step.Command.Argv, jobs), null, ".", step.Timeout, "file", project.Path, switches));
    }

    private static IEnumerable<PlannedStep> DetectedSteps(ResolvedProject project, DetectedProject detected, ToolchainDefaults table, ToolchainTimeouts timeouts, IReadOnlyList<string> skip, int jobs)
    {
        var switches = table.Stacks.TryGetValue(detected.Stack, out var stack) ? stack.Switches.ToDictionary(pair => pair.Key, pair => (string?)pair.Value) : [];
        foreach (var step in detected.Steps)
        {
            var kind = step.Name switch
            {
                "restore" => CheckStep.Restore,
                "build" => CheckStep.Build,
                "typecheck" => CheckStep.Typecheck,
                "test" => CheckStep.Test,
                _ => (CheckStep?)null,
            };
            if (kind is null || (kind == CheckStep.Restore && skip.Contains("restore", StringComparer.Ordinal) && project.Origin == "defaults"))
            {
                continue;
            }

            var entry = stack?.Steps.GetValueOrDefault(step.Name);
            var reason = step.Network == DefaultNetwork.Host
                ? $"shared/toolchains.json: the {step.Name} step of a {detected.Stack} project needs the network; measured in {entry?.MeasuredIn?.File} ({entry?.MeasuredIn?.Heading})"
                : $"shared/toolchains.json: the {step.Name} step of a {detected.Stack} project has only a loopback device; story s1 ran it so inside the check unit";

            yield return new PlannedStep(detected.Id, detected.Stack, kind.Value, Fill(step.Argv, jobs), new PlannedNetwork(step.Network, reason), step.WorkingFolder, kind == CheckStep.Test ? timeouts.Test : timeouts.Build, project.Origin, step.Subject, switches);
        }
    }

    private static string[] Fill(IReadOnlyList<string> argv, int jobs) =>
        [.. argv.Select(item => item.Replace("{jobs}", jobs.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))];
}
