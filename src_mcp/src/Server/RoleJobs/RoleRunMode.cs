
namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// <c>coai-mcp --role-run &lt;jobId&gt; [--data-dir &lt;dir&gt;]</c>: the one-shot a role start launches DETACHED (epoch 3 E1 s1) — it takes the job's lease, runs the role's work and ends. Never speaks JSON-RPC;
/// its stdout is empty and its sentences go to stderr and to the run's log file. See <see cref="RoleRunner"/> for what it does.
/// </summary>
/// <remarks>
/// <para><b>Why <c>--data-dir</c>.</b> A <c>systemd-run --user</c> unit starts from the user manager's environment, so <c>COAI_DATA_DIR</c> is not there when the runner needs it — to find the job folder that
/// holds the very <c>env.json</c> which would carry it (<c>v2/spikes/RESULTS_detach.md</c> §3). The launcher therefore names the folder on the command line (a folder is not a secret); everything else
/// travels in <c>env.json</c>. Without the option the folder is resolved as every mode resolves it.</para>
/// <para><b>Exit codes</b> (never 64 — this binary KNOWS the mode): 0 the job reached a terminal state through this runner; <b>65</b> a wrong argument or no such job (an unknown id makes no folder);
/// 70 an unexpected fault, the job closed as failed; <b>75</b> not the writer (the lease is held elsewhere, the job is over, or it was taken over). A NAMED data folder that is confirmed absent is
/// refused by the dispatch before this runs (<c>Program.WriterModes</c>, exit 74).</para>
/// </remarks>
internal static class RoleRunMode
{
    internal const string Usage = "--role-run takes a job id, then at most `--data-dir <folder>`";

    internal static async Task<int> RunAsync(string[] args, Func<string, string?> env, Action<string> note, CancellationToken stop) =>
        await RunAsync(args, env, note, TimeProvider.System, RoleDescriptors.Shipped, stop);

    internal static async Task<int> RunAsync(
        string[] args, Func<string, string?> env, Action<string> note, TimeProvider clock, IReadOnlyList<RoleDescriptor> descriptors, CancellationToken stop)
    {
        if (Parsed(args) is not { } request)
        {
            // 65, NOT 64: the binary HAS this mode and is refusing the request.
            note(Usage);

            return RoleRunner.NoSuchJob;
        }

        var dataDir = request.DataDir.Length > 0 ? request.DataDir : PanelSettings.DataDirectoryFor(env).Path;
        if (!Directory.Exists(RoleJobStoreFor(dataDir).JobsRoot))
        {
            note($"--role-run: there is no job '{request.JobId}' in {dataDir} — no job has ever been started there");

            return RoleRunner.NoSuchJob;
        }

        var log = ServiceDefaults.CoaiLogging.CreateDewFlowLogger(
            Runners.Reviewers.ShimNotes.AppName + "-role",
            consoleToStdErr: true,
            logsRoot: ServiceDefaults.CoaiLogPath.RootFor(dataDir));
        try
        {
            var store = new RoleJobStore(dataDir, clock, log, RoleJobProjection.For(dataDir, log));

            // The notices go where the server's go: the data directory the restored environment names (env.json carries COAI_DATA_DIR and COAI_DATA_SIDE), resolved when a notice is written — after the environment is back.
            var runner = new RoleRunner(store, descriptors, clock, note, log)
            {
                Notice = notice => ServerNotices.Append(PanelSettings.DataDirectoryFor(Environment.GetEnvironmentVariable), notice),
                SettingsOf = () => Roles.RolesMode.Layered(Environment.GetEnvironmentVariable),
            };

            return await runner.RunAsync(request.JobId, stop);
        }
        finally
        {
            HostCrash.Flushed(log);
        }
    }

    private static RoleJobStore RoleJobStoreFor(string dataDir) => new(dataDir, TimeProvider.System, Serilog.Core.Logger.None);

    /// <summary>The job id and the folder from argv, or null for any other shape. <c>args[0]</c> is <c>--role-run</c>.</summary>
    internal static (string JobId, string DataDir)? Parsed(string[] args)
    {
        if (args.Length is not (2 or 4) || !RoleJobIds.IsValid(args[1]))
        {
            return null;
        }

        if (args.Length == 2)
        {
            return (args[1], string.Empty);
        }

        return args[2] == "--data-dir" && args[3].Length > 0 ? (args[1], args[3]) : null;
    }

    /// <summary>The argv of the runner a start launches: the job, and the data folder it must open.</summary>
    internal static IReadOnlyList<string> ArgumentsFor(string jobId, string dataDir) => ["--role-run", jobId, "--data-dir", dataDir];
}
