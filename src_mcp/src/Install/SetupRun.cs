using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Install;

/// <summary>One invocation of the installer — its arguments, its host, and the order of its steps (see <see cref="SetupClientsMode"/>).</summary>
internal sealed class SetupRun(string mode, SetupArgs args, InstallHost host, SetupDependencies deps, Action<string> note)
{
    private readonly ClientFinder _finder = new(host, ExecutableResolver.IsInstalledFile, deps.Launcher);

    internal async Task<int> ExecuteAsync(TextWriter output, CancellationToken ct)
    {
        var binary = await PrepareBinaryAsync(ct);
        if (binary.Error.Length > 0)
        {
            note(binary.Error);

            return SetupClientsMode.BadArguments;
        }

        var restore = args.Restore.Length > 0 ? InstallBackup.Find(host, args.Restore) : null;
        if (args.Restore.Length > 0 && restore is null)
        {
            note($"no backup '{args.Restore}' — they are named by id under {host.BackupsDir}, or 'latest'");

            return SetupClientsMode.BadArguments;
        }

        var found = await _finder.FindAsync(args.Only, ct);
        if (found.All(client => !client.Found))
        {
            return Report(output, binary.View, null, Plan(found, binary.Path, restore), SetupClientsMode.NoClientFound);
        }

        var runs = Plan(found, binary.Path, restore);
        if (args.DryRun || !runs.Any(Changes))
        {
            return Report(output, binary.View, null, runs, ExitOf(runs, failed: false));
        }

        return await ApplyAsync(output, binary, found, restore, ct);
    }

    private async Task<int> ApplyAsync(TextWriter output, BinaryChoice binary, IReadOnlyList<FoundClient> found, ManifestRecord? restore, CancellationToken ct)
    {
        InstallLock? held;
        try
        {
            held = InstallLock.TryTake(host);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            note($"the state folder {host.StateDir} cannot be used, so nothing was changed: {e.Message}");

            return SetupClientsMode.BackupNotWritten;
        }

        if (held is null)
        {
            note("another --setup-clients or --install is running (it holds the lock); nothing was changed — run this again when it ends");

            return SetupClientsMode.RunInProgress;
        }

        using (held)
        {
            // Planned AGAIN under the lock: the run that held it may just have done what this one meant to.
            var runs = Plan(found, binary.Path, restore);
            if (!runs.Any(Changes))
            {
                return Report(output, binary.View, null, runs, ExitOf(runs, failed: false));
            }

            return await ChangeAsync(output, binary, runs, ct);
        }
    }

    private async Task<int> ChangeAsync(TextWriter output, BinaryChoice binary, IReadOnlyList<ClientRun> runs, CancellationToken ct)
    {
        InstallBackup backup;
        try
        {
            backup = InstallBackup.Create(host, deps.Clock.GetUtcNow(), deps.Pid, binary.Path, Subjects(runs));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            note($"the backup could not be written, so nothing was changed: {e.Message}");

            return SetupClientsMode.BackupNotWritten;
        }

        var secrets = runs.SelectMany(run => run.Targets).SelectMany(target => new[] { target.Plan.Before, target.Plan.After }).OfType<McpEntry>().SelectMany(entry => entry.Env.Values).ToList();
        var context = new ApplyContext(host, string.Empty, new VendorCommands(host, deps.Launcher, deps.VendorTimeout, secrets), backup, deps.Faults);
        var done = new List<ClientRun>();
        foreach (var run in runs)
        {
            done.Add(await ApplyClientAsync(run, context, backup, ct));
        }

        InstallBackup.Prune(host);

        return Report(output, binary.View, new BackupView(backup.Id, backup.Dir), done, ExitOf(done, failed: done.SelectMany(run => run.Targets).Any(target => target.Status == "failed")));
    }

    private static async Task<ClientRun> ApplyClientAsync(ClientRun run, ApplyContext context, InstallBackup backup, CancellationToken ct)
    {
        var applied = new List<TargetRun>();
        foreach (var target in run.Targets)
        {
            if (!target.Plan.Action.Changes())
            {
                applied.Add(target);
                continue;
            }

            TargetOutcome outcome;
            try
            {
                outcome = await target.Target.ApplyAsync(target.Plan, context with { CliPath = run.Found.Path }, ct);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                outcome = new TargetOutcome(OutcomeKind.Failed, $"a write failed after the backup: {e.Message}");
            }

            if (outcome.Kind == OutcomeKind.Done)
            {
                InstallBackup.SupersedeInterrupted(context.Host, target.Plan.Client, target.Plan.Kind, backup.Id);
            }

            applied.Add(target with { Status = outcome.Kind.ToString().ToLowerInvariant(), Reason = outcome.Reason.Length > 0 ? outcome.Reason : target.Plan.Reason });
        }

        return run with { Targets = applied };
    }

    private static IReadOnlyList<InstallBackup.Subject> Subjects(IReadOnlyList<ClientRun> runs) =>
        [.. runs.SelectMany(run => run.Targets).Where(target => target.Plan.Action.Changes()).Select(target => new InstallBackup.Subject(target.Plan.Client, target.Plan.Kind, target.Plan.File, target.Plan.Before, target.Plan.After))];

    private static bool Changes(ClientRun run) => run.Targets.Any(target => target.Plan.Action.Changes());

    /// <summary>The exit code of runs whose targets are decided: 74 a failed write, else 78 a refusal, else 0.</summary>
    private static int ExitOf(IReadOnlyList<ClientRun> runs, bool failed)
    {
        if (failed)
        {
            return SetupClientsMode.WriteFailed;
        }

        return runs.SelectMany(run => run.Targets).Any(target => target.Plan.Action == PlanAction.Refuse || target.Status == "refused") ? SetupClientsMode.Refused : 0;
    }

    // ---------- planning ----------

    private IReadOnlyList<ClientRun> Plan(IReadOnlyList<FoundClient> found, string binary, ManifestRecord? restore)
    {
        var inputs = new PlanInputs(args.Remove ? Wanted.Remove : Wanted.Install, binary, args.Binary.Length > 0, host.IsWindows, File.Exists, host.InClaudeSession);

        return [.. found.Select(client => new ClientRun(client, client.Found ? PlanTargets(client.Cli, inputs, restore) : []))];
    }

    private IReadOnlyList<TargetRun> PlanTargets(Client cli, PlanInputs inputs, ManifestRecord? restore) =>
        [.. InstallTargets.For(cli).Select(target => new TargetRun(target, PlanTarget(target, inputs, restore), "planned", string.Empty))];

    private PlannedTarget PlanTarget(IInstallTarget target, PlanInputs inputs, ManifestRecord? restore)
    {
        if (target.Cli == Client.Agy)
        {
            return ClientPlan.Unmeasured(target, host);
        }

        var read = ReadFile(target);
        if (restore is not null)
        {
            var line = restore.Targets.FirstOrDefault(one => one.Cli == target.Cli.Word() && one.Kind == target.Kind);

            return line is null
                ? PlannedTarget.Nothing(target.Cli, target.Kind, target.FileOf(host), "that install did not change this target")
                : Writable(ClientPlan.Restore(target, host, read, line.Before?.ToEntry(), line.After?.ToEntry(), host.InClaudeSession));
        }

        var interrupted = inputs.Wanted == Wanted.Install && read is ConfigRead.Found { Entry: null } ? InstallBackup.InterruptedEntry(host, target.Cli, target.Kind) : null;

        return Writable(ClientPlan.Plan(target, host, read, inputs, interrupted));
    }

    /// <summary>A target that would change a file this account cannot write is REFUSED with the OS's own words — at plan time, so no lock is taken and no backup made for it.</summary>
    private static PlannedTarget Writable(PlannedTarget plan) =>
        plan.Action.Changes() && InstallFiles.WriteRefusal(plan.File) is { Length: > 0 } refusal
            ? PlannedTarget.Refused(plan.Client, plan.Kind, plan.File, refusal, plan.ShadowedBy)
            : plan;

    private ConfigRead ReadFile(IInstallTarget target)
    {
        var file = target.FileOf(host);
        try
        {
            return target.Read(File.Exists(file) ? File.ReadAllText(file) : null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new ConfigRead.Unreadable(e.Message);
        }
    }

    // ---------- the binary to register ----------

    private sealed record BinaryChoice(string Path, BinaryView View, string Error);

    private async Task<BinaryChoice> PrepareBinaryAsync(CancellationToken ct)
    {
        if (args.Remove || args.Restore.Length > 0)
        {
            return new BinaryChoice(string.Empty, new BinaryView(string.Empty, string.Empty), string.Empty);
        }

        var path = args.Binary.Length > 0 ? args.Binary : deps.SelfPath;
        if (!Path.IsPathFullyQualified(path) && !(host.IsWindows && path.Contains('\\')))
        {
            return Bad($"the binary '{path}' is not a full path — name one with --binary <full path to coai-mcp>");
        }

        if (!ExecutableResolver.IsInstalledFile(path))
        {
            return Bad($"the binary '{path}' is not an executable file on this side");
        }

        return new BinaryChoice(path, new BinaryView(path, await VersionOfAsync(path, ct)), string.Empty);

        static BinaryChoice Bad(string error) => new(string.Empty, new BinaryView(string.Empty, string.Empty), error);
    }

    private async Task<string> VersionOfAsync(string path, CancellationToken ct)
    {
        if (path.Equals(deps.SelfPath, StringComparison.Ordinal))
        {
            return deps.SelfVersion;
        }

        var found = await new ClientFinder(host, ExecutableResolver.IsInstalledFile, deps.Launcher).VersionOfAsync(path, ct);

        return found.Length > 0 && found.Contains(' ') ? found[(found.LastIndexOf(' ') + 1)..] : found;
    }

    // ---------- the report ----------

    private int Report(TextWriter output, BinaryView binary, BackupView? backup, IReadOnlyList<ClientRun> runs, int exitCode)
    {
        output.WriteLine(SetupOutputJson.Line(new SetupOutput(mode, args.DryRun, binary, deps.HostWord, backup, [.. runs.Select(ClientViewOf)], exitCode)));

        return exitCode;
    }

    private ClientView ClientViewOf(ClientRun run)
    {
        var targets = run.Targets.Select(target => new TargetView(
            target.Plan.Kind,
            target.Plan.File,
            target.Plan.Action.Word(),
            target.Plan.Action.Changes() ? target.Plan.Via : string.Empty,
            EntryView.Of(target.Plan.Before),
            EntryView.Of(target.Plan.After),
            target.Plan.ShadowedBy,
            target.Status is "refused" or "failed" ? target.Reason : target.Plan.Reason)).ToList();

        return new ClientView(
            run.Found.Cli.Word(), run.Found.Found, run.Found.Path, run.Found.Version, ResultOf(run),
            run.Found.Found ? string.Join("; ", targets.Select(target => target.Reason).Where(reason => reason.Length > 0).Distinct()) : run.Found.Reason,
            targets);
    }

    private static string ResultOf(ClientRun run)
    {
        if (!run.Found.Found)
        {
            return "absent";
        }

        if (run.Found.Cli == Client.Agy)
        {
            return "unmeasured";
        }

        if (run.Targets.Any(target => target.Status == "failed"))
        {
            return "failed";
        }

        if (run.Targets.Any(target => target.Plan.Action == PlanAction.Refuse || target.Status == "refused"))
        {
            return "refused";
        }

        return run.Targets.Any(target => target.Plan.Action.Changes()) ? "changed" : "unchanged";
    }
}
