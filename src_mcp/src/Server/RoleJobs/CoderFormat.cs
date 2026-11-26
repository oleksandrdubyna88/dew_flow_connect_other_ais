using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Runners.Worktrees;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The formatter turn of a coder job (E4 s5 t1, t2; SPEC D): after a turn's commit, and before the checks of that commit, the formatter runs on the files that commit added or changed — in a unit of its own, after the restore step (prettier lives in <c>node_modules</c>) — and what it changed is committed as
/// <c>style: format (coai)</c>. No model takes part.
/// </summary>
/// <remarks>
/// <para><b>What it may change:</b> only the files it was given, and only TRACKED changes are taken. After the run the worktree is read: a changed path that was not given, a new file git would add, or a failed formatter puts the worktree back to the turn's commit (<see cref="CoderRestore"/>) and the state is <c>refused</c> or <c>failed</c>. A formatter can also change IGNORED files (a runner under <c>node_modules</c>); git does not show them, so
/// the checks of the round begin with <c>git clean -ffdx</c> and an empty <c>status --ignored</c> (<see cref="CoderChecks"/>) and then restore again: nothing the formatter did to an ignored file survives into them (re-review finding F2).</para>
/// <para><b>It is never a verdict.</b> The state is in the envelope's <c>format</c> and makes no round red or green; a file the formatter cannot parse fails the build or the test next, and that digest is the useful one.</para>
/// </remarks>
internal sealed class CoderFormat(IRoleRun run, GitHistory git, CoderOsLayer layer, CoderTree tree, string baseSha)
{
    /// <summary>The message of the format commit.</summary>
    public const string Message = "style: format (coai)";

    /// <summary>The most the formatter may take (SPEC D: 5 minutes). Settable only for the tests, which run one at a time.</summary>
    internal static TimeSpan FormatTimeout { get; set; } = TimeSpan.FromMinutes(5);

    private const int NamedPaths = 3;

    /// <summary>What <see cref="PrepareAsync"/> found: the state to report and whether tracked changes of the given files wait to be committed.</summary>
    internal sealed record Prepared(FormatResult Result, bool Changed)
    {
        /// <summary>The envelope's rows of the units this turn ran (S5 of the s5 verify).</summary>
        public IReadOnlyList<CoderCheckStep> Steps { get; init; } = [];

        /// <summary>Set when a unit of the turn could not be stopped (M4 of the s5 verify): the job ends <c>coder-boundary</c> with this sentence, and the worktree is not touched while the process may still write.</summary>
        public string? EndsJob { get; init; }
    }

    /// <summary>A project with a formatter and the files this turn gives it.</summary>
    private sealed record Work(PlannedStep? Restore, PlannedStep Format, IReadOnlyList<string> Files);

    public async Task<Prepared> PrepareAsync(string head, int turn, CancellationToken ct)
    {
        run.Phase("format", turn);
        var checks = new ChecksAtBase(git, new CheckRunner(layer.Launcher, layer.ChecksBoundary()));
        if (await checks.ResolveForFormatAsync(layer.Job, baseSha, ct) is not ToolchainResolution.Resolved { Toolchain: var toolchain })
        {
            return new Prepared(FormatResult.NotConfigured, false);
        }

        var changed = await ChangedByTheCommitAsync(head, ct);
        var (work, tooMany) = WorkOf(toolchain, changed);
        if (tooMany)
        {
            return new Prepared(new FormatResult("too-many-files", 0, string.Empty, $"the turn changed more than {FileArguments.MaxFiles} files for the formatter, so it was not run"), false);
        }

        if (work.Count == 0)
        {
            return new Prepared(HasAFormatter(toolchain) ? new FormatResult("unchanged", 0, string.Empty, "no file of the turn is one the formatter is given") : FormatResult.NotConfigured, false);
        }

        var logs = Path.Combine(run.Folder, "checks", $"format-{turn}");
        var plan = work.SelectMany(item => item.Restore is null ? [item.Format] : new[] { item.Restore, item.Format }).ToList();
        var ran = await new CheckRunner(layer.Launcher, layer.ChecksBoundary()).RunStepsAsync(plan, new CheckRequest(tree.Root, logs, CoderChecks.Jobs) { Job = layer.Job, StepFinished = step => CoderChecks.RecordStep(run, layer, logs, step) }, ct);
        var given = work.SelectMany(item => item.Files).ToHashSet(StringComparer.Ordinal);
        var steps = CoderChecks.StepsOf(run, ran.Steps);
        if (ran.State == CheckState.BoundaryRefused)
        {
            return new Prepared(new FormatResult("failed", given.Count, string.Empty, "a unit of the formatter turn could not be stopped, so the job ends here"), false) { Steps = steps, EndsJob = ran.Note };
        }

        if (ran.State != CheckState.Green)
        {
            var reset = await CoderRestore.RestoreAsync(git, tree, head, ct);

            return new Prepared(new FormatResult("failed", given.Count, string.Empty, reset.Length == 0 ? "the formatter, or the restore before it, did not pass; the worktree is back at the turn's commit" : reset), false) { Steps = steps };
        }

        return (await VerifyAsync(head, given, ct)) with { Steps = steps };
    }

    private async Task<Prepared> VerifyAsync(string head, HashSet<string> given, CancellationToken ct)
    {
        var status = await git.InCoderTreeAsync(tree, ["--no-optional-locks", "status", "--porcelain=v1", "-z", "--untracked-files=all"], ct);
        if (!status.Ran || !status.Ok)
        {
            await CoderRestore.RestoreAsync(git, tree, head, ct);

            return new Prepared(new FormatResult("failed", given.Count, string.Empty, "git could not read the worktree after the formatter, so the worktree was put back"), false);
        }

        var entries = status.Out.Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(entry => entry.Length > 3).Select(entry => (Code: entry[..2], Path: entry[3..])).ToList();
        var foreign = entries.Where(entry => !given.Contains(entry.Path) || entry.Code is not (" M" or "M " or "MM")).Select(entry => entry.Path).ToList();
        if (foreign.Count > 0)
        {
            var reset = await CoderRestore.RestoreAsync(git, tree, head, ct);

            return new Prepared(new FormatResult("refused", given.Count, string.Empty, $"the formatter changed {foreign.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} path(s) it was not given or a new file ({PathSpelling.SpellMany(foreign.Take(NamedPaths))}); the worktree was put back{(reset.Length > 0 ? ": " + reset : string.Empty)}"), false);
        }

        return entries.Count == 0
            ? new Prepared(new FormatResult("unchanged", given.Count, string.Empty, "the formatter changed no file"), false)
            : new Prepared(new FormatResult("formatted", given.Count, string.Empty, $"the formatter changed {entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} of the {given.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} file(s) it was given"), true);
    }

    /// <summary>The paths the turn's commit added or changed (against its parent).</summary>
    private async Task<IReadOnlyList<string>> ChangedByTheCommitAsync(string head, CancellationToken ct)
    {
        var listed = await git.InCoderTreeAsync(tree, ["diff-tree", "--no-commit-id", "--name-only", "-r", "-z", "--no-renames", "--diff-filter=AM", head], ct);

        return listed.Ran && listed.Ok ? [.. listed.Out.Split('\0', StringSplitOptions.RemoveEmptyEntries)] : [];
    }

    private static bool HasAFormatter(ResolvedToolchain toolchain) => toolchain.Projects.Any(project => (project.Detected?.Steps.Any(step => step.Name == "format") ?? false) || (project.FileProject?.Format.Runs ?? false));

    /// <summary>The projects with a formatter and the files each is given; <c>tooMany</c> when a project's list is over the limit.</summary>
    private static (List<Work> Work, bool TooMany) WorkOf(ResolvedToolchain toolchain, IReadOnlyList<string> changed)
    {
        var table = ToolchainDefaults.Current;
        var restores = StepPlan.Of(toolchain, table, CoderChecks.Jobs).Where(step => step.Step == CheckStep.Restore).ToList();
        var work = new List<Work>();
        foreach (var project in toolchain.Projects)
        {
            var (argv, folder, workingFolder, extensions) = FormatOf(project, table);
            if (argv.Count == 0)
            {
                continue;
            }

            var files = FileArguments.Select(changed, folder, extensions, table);
            if (files.State == "too-many-files")
            {
                return ([], true);
            }

            if (files.State == "none")
            {
                continue;
            }

            var restore = restores.FirstOrDefault(step => step.Project == project.Id || (project.Detected is not null && step.Project == project.Detected.Id));
            var format = new PlannedStep(restore?.Project ?? project.Id, restore?.Adapter ?? project.FileProject?.Adapter ?? string.Empty, CheckStep.Format, FileArguments.Argv(argv, files.Files, workingFolder, CoderChecks.Jobs),
                new PlannedNetwork(DefaultNetwork.None, "the formatter writes files and needs no network (SPEC D)"), workingFolder, FormatTimeout, restore?.Origin ?? "file", project.Id, restore?.Switches ?? new Dictionary<string, string?> { ["NO_COLOR"] = "1" });
            work.Add(new Work(restore, format, files.Files));
        }

        return (work, false);
    }

    private static (IReadOnlyList<string> Argv, string Folder, string WorkingFolder, IReadOnlyList<string> Extensions) FormatOf(ResolvedProject project, ToolchainDefaults table)
    {
        if (project.Detected?.Steps.FirstOrDefault(step => step.Name == "format") is { } step)
        {
            return (step.Argv, project.Detected.Folder == "." ? string.Empty : project.Detected.Folder, step.WorkingFolder, step.Extensions);
        }

        if (project.FileProject is { Format.Runs: true } file)
        {
            var extensions = table.Stacks.TryGetValue(file.Adapter, out var stack) && stack.Steps.TryGetValue("format", out var defaults) && defaults is not null ? defaults.Extensions : [];

            return (file.Format.Argv, file.Path == "." ? string.Empty : file.Path, ".", extensions);
        }

        return ([], string.Empty, ".", []);
    }
}
