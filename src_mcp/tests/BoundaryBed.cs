using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Runners.Worktrees;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// A real coder worktree of a real (dirty) repository, a job folder, a shared NuGet package folder of its own (a stand-in for ~/.nuget/packages), and the one launcher with every request written down — the common ground of the check-boundary suites
/// (epoch 3 E2 s2 t4). Nothing here touches the real HOME or a real data folder: every folder is a temp folder, and <see cref="BoundaryHost.RealHome"/> is a path that is not there.
/// </summary>
/// <remarks>
/// <b>It stops every unit its job started.</b> A unit left behind is a process of a test that outlived it, so <see cref="DisposeAsync"/> asks the user manager for <c>coai-check-&lt;job&gt;-*</c> and <c>coai-coder-&lt;job&gt;-*</c>, stops what is there and then FAILS the test that left it.
/// The unit names carry a random job id, so two runs of this suite on one user manager (another worktree's build, say) never meet.
/// </remarks>
internal sealed class BoundaryBed : IAsyncDisposable
{
    private readonly CoderTestBed _bed;

    private readonly TempDir _home = TempDir.For("cb-home-");

    private BoundaryBed(CoderTestBed bed, CoderTree tree, string jobId, string baseSha)
    {
        _bed = bed;
        Tree = tree;
        JobId = jobId;
        BaseSha = baseSha;
        JobRoot = Path.Combine(bed.Parent, "job");
        // The "real" home of a test is a path that is not there: nothing real is ever named, and a write to it can only fail. The shared package folder is a temp folder.
        Host = BoundaryHost.Current() with { RealHome = "/nonexistent/coai-real-home-" + jobId, NuGetPackages = SharedPackages, Path = string.Empty };
        Job = new BoundaryJob(jobId, tree, JobRoot, NetworkChoice.None("test"), NetworkChoice.None("test"), 2000);
    }

    public CoderTree Tree { get; }

    public string JobId { get; }

    /// <summary>The commit the worktree was made at.</summary>
    public string BaseSha { get; }

    public string JobRoot { get; }

    public BoundaryHost Host { get; }

    public BoundaryJob Job { get; private set; }

    /// <summary>
    /// Gives the job ONE read-only mount (epoch 3 E2 s6, the recorded change): a folder of coai's own with the given files, bound read-only at <paramref name="relative"/> in the worktree. The source is in the job's mounts folder, not inside the job folder (a unit
    /// binds the job folder writable, and a mount's source must not be reachable through it); the target is made as the empty folder git leaves for a gitlink.
    /// </summary>
    public ReadOnlyMount Mount(string relative, params (string Name, string Text)[] files)
    {
        var source = Path.Combine(Job.MountsFolder, $"m{Job.Mounts.Count}");
        Directory.CreateDirectory(source);
        foreach (var (name, text) in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(source, name))!);
            File.WriteAllText(Path.Combine(source, name), text);
        }

        var target = Path.Combine(Worktree, relative);
        Directory.CreateDirectory(target);
        var mount = new ReadOnlyMount(source, target);
        Job = Job with { Mounts = [.. Job.Mounts, mount] };

        return mount;
    }

    /// <summary>Moves the job's folder to <paramref name="root"/> (a folder of the test's own); the folders under the old one are not carried over.</summary>
    public void UseJobRoot(string root) => Job = Job with { JobRoot = root };

    /// <summary>
    /// Puts the job's folder into a folder of its own that holds another job's folder too (what the coder-jobs folder is): <c>&lt;parent&gt;/jobs/job-a</c>, with a sibling <c>job-b</c> that has a coder folder and a file in it. Returns that sibling's file, which a unit of this job must not see.
    /// </summary>
    public string UseJobsFolderWithAnotherJob()
    {
        var jobs = Path.Combine(Parent(), "jobs");
        UseJobRoot(Path.Combine(jobs, "job-a"));
        var other = Path.Combine(jobs, "job-b", "coder", "home");
        Directory.CreateDirectory(other);
        var secret = Path.Combine(other, "transcript.txt");
        File.WriteAllText(secret, "another job's coder home\n");

        return secret;
    }

    public WatchedLauncher Launcher => _bed.Launcher;

    public string Main => _bed.Repo.Path;

    public string Worktree => Tree.Root;

    /// <summary>The folder that holds this bed's main checkout's siblings: where a job folder of a test may be.</summary>
    public string Parent() => _bed.Parent;

    /// <summary>The shared NuGet package folder, with one file in it.</summary>
    public string SharedPackages => Path.Combine(_home.Path, "shared-packages");

    /// <summary>A random job id of the strict shape — never the same twice, so two runs never share a unit name.</summary>
    public static string NewJobId() => "t4-" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>The bed. With <paramref name="toolchain"/> the repository gets one more commit that holds <c>.coai/toolchain.yaml</c>, and that commit is the base of the worktree.</summary>
    public static async Task<BoundaryBed> CreateAsync(string toolchain = "", IReadOnlyDictionary<string, string>? files = null)
    {
        var bed = await CoderTestBed.CreateAsync();
        var baseSha = toolchain.Length == 0 && files is null ? bed.Shas[2] : await CommitFilesAsync(bed, toolchain, files);
        var started = await bed.Coder.StartAsync(bed.Repo.Path, CoderTestBed.JobId(1), baseSha);
        var tree = CoderTree.Open(started.Should().BeOfType<CoderWorktreeOutcome.Started>(started.ToString()).Subject.Path, bed.Repo.Path);
        var opened = tree.Should().BeOfType<CoderTreeOpening.Opened>(tree.ToString()).Subject.Tree;
        var made = new BoundaryBed(bed, opened, NewJobId(), baseSha);
        Directory.CreateDirectory(made.SharedPackages);
        await File.WriteAllTextAsync(Path.Combine(made.SharedPackages, "seed.txt"), "shared\n");

        return made;
    }


    private static async Task<string> CommitFilesAsync(CoderTestBed bed, string toolchain, IReadOnlyDictionary<string, string>? files)
    {
        if (toolchain.Length > 0)
        {
            Directory.CreateDirectory(Path.Combine(bed.Repo.Path, ".coai"));
            await bed.Repo.WriteAsync(".coai/toolchain.yaml", toolchain);
        }

        foreach (var (name, text) in files ?? new Dictionary<string, string>())
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(bed.Repo.Path, name))!);
            await bed.Repo.WriteAsync(name, text);
        }

        await bed.Repo.CommitAsync("c4 toolchain");

        return await bed.Repo.HeadAsync();
    }

    public CheckBoundary Boundary(Func<IReadOnlyList<string>, IReadOnlyList<string>>? degrade = null, BoundaryHost? host = null) =>
        new(Launcher, host ?? Host) { Degrade = degrade };

    /// <summary>One <c>sh -c</c> script run as a step of this job in its own unit; the unit's name is <c>part</c>. The preflight runs first, as it does before a job, and must admit it.</summary>
    public Task<BoundaryRun> ShellAsync(string script, string part = "s1", CheckNetwork network = CheckNetwork.Private, CheckBoundary? boundary = null, TimeSpan? timeout = null, CancellationToken ct = default, bool restore = false) =>
        CommandAsync(["/bin/sh", "-c", script], new Dictionary<string, string?>(), part, network, boundary, timeout, ct, restore);

    public async Task<BoundaryRun> CommandAsync(IReadOnlyList<string> command, IReadOnlyDictionary<string, string?> extra, string part = "s1", CheckNetwork network = CheckNetwork.Private, CheckBoundary? boundary = null, TimeSpan? timeout = null, CancellationToken ct = default, bool restore = false)
    {
        boundary ??= Boundary();
        var admission = await boundary.AdmitAsync(Job, [network], ct);
        var admitted = admission.Should().BeOfType<BoundaryAdmission.Admitted>(admission.ToString()).Subject;
        var choice = network == CheckNetwork.Private ? NetworkChoice.None("test") : NetworkChoice.OnHost("test");

        return await boundary.RunAsync(admitted, Job, choice, part, command, extra, timeout ?? TimeSpan.FromSeconds(60), 1024 * 1024, ct, restore: restore);
    }

    /// <summary>The units of this job that are still loaded — a check's (<c>coai-check-</c>) or the coder's (<c>coai-coder-</c>). Empty where there is no systemctl at all: then no unit can have been started.</summary>
    public async Task<string> ActiveUnitsAsync()
    {
        try
        {
            var listed = await Launcher.RunAsync(new ProcessRequest("systemctl", ["--user", "list-units", $"coai-check-{JobId}-*", $"coai-coder-{JobId}-*", "--all", "--no-legend", "--plain"], Worktree) { Timeout = TimeSpan.FromSeconds(20) });

            return listed.StdOut.Trim();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var left = await ActiveUnitsAsync();
        if (left.Length > 0)
        {
            foreach (var line in left.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                await Launcher.RunAsync(new ProcessRequest("systemctl", ["--user", "stop", line.Split(' ')[0]], Worktree) { Timeout = TimeSpan.FromSeconds(20) });
            }
        }

        await _bed.DisposeAsync();
        _home.Dispose();
        left.Should().BeEmpty("a test of the check boundary or the coder boundary must stop every unit it starts");
    }
}
