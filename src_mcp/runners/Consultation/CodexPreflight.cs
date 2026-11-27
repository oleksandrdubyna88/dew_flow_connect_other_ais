using CoaiMcp.Runners.Toolchain;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// The preflight of a codex coder job (epoch 3 E3 s5 t3; SPEC C2 item 3, judgement sections 8, 13.3 and 13.11): with no model, inside the EXACT launch unit, one row for every id of class <c>pre</c> of the codex row of <c>shared/coder-boundary-releases.json</c>.
/// </summary>
internal static class CodexPreflight
{
    /// <summary>The rows, in the order they are answered, each with the id of the table it answers. Several rows may answer one id; no row answers none (a test holds the two lists equal).</summary>
    internal static readonly IReadOnlyList<(string Item, string Id)> Rows =
    [
        (BoundaryPreflight.WorktreeWrite, "U1"),
        ("main-hidden", "U2"),
        ("coder-root-hidden", "U3"),
        (BoundaryPreflight.GitReadOnly, "U4"),
        (BoundaryPreflight.NoUserManager, "U5"),
        (BoundaryPreflight.WindowsProgram, "U5"),
        (BoundaryPreflight.NoSocketsNoInterop, "U5"),
        (BoundaryPreflight.HomeIsTheJobs, "U6"),
        (BoundaryPreflight.TempIsTheJobs, "U6"),
        (BoundaryPreflight.SignInAbsent, "U6"),
        (BoundaryPreflight.KeysHidden, "U6"),
        ("environment-exact", "U7"),
        ("coder-other-job-hidden", "U8"),
        ("program-unwritable", "U9"),
        ("vendor-sandbox-starts", "CX-V1"),
        ("vendor-sandbox-writes", "CX-V2"),
        ("vendor-sandbox-reads", "CX-V3"),
        ("vendor-sandbox-network", "CX-V4"),
        ("vendor-sandbox-processes", "CX-V5"),
        ("vendor-sandbox-protected-names", "CX-V6"),
        ("tool-list", "CX-T1"),
        ("config-read-back", "CX-C1"),
        ("no-project-tools", "CX-C3"),
        ("permission-profile", "CX-C4"),
        ("refresh-override", "CX-R2"),
        ("sign-in-is-the-copy", "CX-R3"),
        ("copy-has-the-placeholder", "CX-R4"),
    ];

    /// <summary>The row of a job that has submodule mounts: the contents bound read-only (it answers U4, the read-only half of git's own files).</summary>
    internal const string SubmodulesRow = BoundaryPreflight.SubmodulesReadOnly;

    /// <summary>The id each row answers, by the row's item (the submodule row included).</summary>
    internal static IReadOnlyDictionary<string, string> RowIds { get; } = new Dictionary<string, string>([.. Rows.Select(row => new KeyValuePair<string, string>(row.Item, row.Id)), new(SubmodulesRow, "U4")], StringComparer.Ordinal);

    /// <summary>The items of a codex preflight for <paramref name="job"/>, in order: <see cref="Rows"/>, and the submodule row when the job has a mount.</summary>
    internal static IReadOnlyList<string> Items(BoundaryJob job) => [.. Rows.Select(row => row.Item), .. job.Mounts.Count == 0 ? Array.Empty<string>() : [SubmodulesRow]];

    /// <summary>How long the whole unit may run: the vendor sandbox, two tool lists and the doctor report each have a deadline of their own inside it.</summary>
    internal static readonly TimeSpan Budget = TimeSpan.FromMinutes(4);

    /// <summary>
    /// The plan for one launch (<see cref="ICoderVendor.PreflightPlan"/>; <see cref="CoderPreflightPlan.Budget"/> and <see cref="CoderPreflightPlan.Rows"/> are what codex adds to the record): the shell program and its arguments, the rows of its answer, and what the host makes for it — a listener on the loopback (CX-V4) and the stand-in folder of another job (U8) — removed when the unit has ended.
    /// The model comes from the job's own <c>config.toml</c>, which the vendor wrote from the launch's argv just before the preflight, so the probes are asked for the job's own model.
    /// </summary>
    internal static CoderPreflightPlan Plan(BoundaryHost host, BoundaryJob job, CoderProgram program, string windowsCopy)
    {
        var vendor = CodexCoderVendor.Instance;
        var model = CodexCoderVendor.ModelOfConfig(job);
        var folder = CodexCoderVendor.ProgramFolder(program);
        var listener = LoopbackListener.Start();
        var other = CheckBoundary.PlantAnotherJob(job);
        try
        {
            return Made(host, job, program, windowsCopy, vendor, model, folder, listener, other);
        }
        catch
        {
            // A plan that cannot be finished must not leave the listener open or the stand-in of another job on the disk.
            listener.Dispose();
            CheckBoundary.RemoveAnotherJob(other);

            throw;
        }
    }

    private static CoderPreflightPlan Made(BoundaryHost host, BoundaryJob job, CoderProgram program, string windowsCopy, CodexCoderVendor vendor, string model, string folder, LoopbackListener listener, string other)
    {
        var override_ = new Uri(CodexCoderVendor.RefreshOverrideUrl);
        var environment = string.Join('\n', vendor.Environment(host, job).Select(pair => $"{pair.Key}={pair.Value}"));
        var port = listener.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        IReadOnlyList<string> command =
        [
            "/bin/sh", "-c", CodexPreflightScript.For(job), "coai-preflight",
            job.Tree.Root, job.Tree.MainCheckout, host.ResolvedHome, job.JobId, windowsCopy, host.ResolverFile, job.CoderRoot, program.Real, folder,
            job.CoderVendorFolder(CodexCoderVendor.VendorId), CodexCoderVendor.CodexHome(job), vendor.Home(job).SignInFile, other, port, SignInCopy.Placeholder, CodexCoderVendor.ConfigText(model, folder), environment, override_.Host,
            override_.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), CodexCoderVendor.RefreshOverrideUrl, string.Join(' ', new[] { job.KeysFolder, job.KeysFile }.Where(path => path.Length > 0)), string.Join('\n', CodexCoderVendor.ProbeFlags(model)),
            .. BoundaryPreflight.MountArguments(job),
        ];

        return new CoderPreflightPlan(command, Items(job), () =>
        {
            listener.Dispose();
            CheckBoundary.RemoveAnotherJob(other);
        })
        {
            Budget = Budget,
            Rows = (started, gone) => CodexPreflightRows.Build(started, gone, job),
        };
    }
}
