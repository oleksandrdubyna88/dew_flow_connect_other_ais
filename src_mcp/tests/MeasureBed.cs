using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Runners.Worktrees;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// The harness of the recorded measures of epoch 3 E4 story s1: a scratch repository on a real disk, its coder worktree, and the PRODUCT's own check boundary (<see cref="CheckBoundary"/> with a <see cref="BoundaryJob"/>, so the unit's property list
/// and its environment are exactly the ones a coder job's step gets), driving one command at a time inside a unit and writing down what it did. It changes no property of the unit (SPEC rule J1).
/// </summary>
/// <remarks>
/// <para><b>The real home is not used.</b> <see cref="BoundaryHost.RealHome"/> is a STAND-IN folder of the bed: when node is wanted it holds a COPY of the node install of the machine (<c>.nvm/versions/node/&lt;v&gt;</c>), found read-only through coai's own PATH, so the
/// unit's one bind under the home is of the copy. It has no <c>.nuget</c>, so no fallback package folder is given and the restore is a cold one. <see cref="StandInManifest"/> is a hash of every file and time of the stand-in, taken before and after a run.</para>
/// <para><b>One folder, one run.</b> The folder must not exist; it is made on a real disk with a short path (a tmpfs under the product's free-space floor reddened tests, <c>RESULTS_s1_toolchain.md</c>). Nothing of it is removed: it is the evidence, and the operator removes it.</para>
/// <para><b>Working folder.</b> A step of the product runs from the worktree root; a project folder is reached here by <c>/bin/sh -c 'cd DIR &amp;&amp; exec "$@"'</c> INSIDE the unit, so the unit is the same and only the program the unit runs differs. A product step with a working folder is a task of story s3.</para>
/// </remarks>
internal sealed class MeasureBed : IAsyncDisposable
{
    private static readonly IReadOnlyDictionary<string, string?> DotnetSwitches = new Dictionary<string, string?>
    {
        ["NO_COLOR"] = "1",
        ["DOTNET_NOLOGO"] = "1",
        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
        ["MSBUILDDISABLENODEREUSE"] = "1",
    };

    private static readonly IReadOnlyDictionary<string, string?> NodeSwitches = new Dictionary<string, string?>
    {
        ["NO_COLOR"] = "1",
        ["CI"] = "true",
    };

    private readonly ProcessLauncher _launcher = new();

    private readonly List<string> _lines = [];

    private BoundaryAdmission.Admitted? _admitted;

    private MeasureBed(string folder)
    {
        Folder = folder;
        StandIn = Path.Combine(folder, "home");
        Main = Path.Combine(folder, "m");
        Logs = Path.Combine(folder, "logs");
        Fixtures = Path.Combine(folder, "fixtures");
    }

    public string Folder { get; }

    /// <summary>The stand-in of the person's home: what the unit sees as the real home's read-only caches.</summary>
    public string StandIn { get; }

    public string Main { get; }

    public string Logs { get; }

    public string Fixtures { get; }

    public string Worktree { get; private set; } = string.Empty;

    public BoundaryJob Job { get; private set; } = null!;

    public BoundaryHost Host { get; private set; } = null!;

    public CheckBoundary Boundary { get; private set; } = null!;

    public IProcessLauncher Launcher => _launcher;

    /// <summary>The hash of the stand-in before any step ran.</summary>
    public string StandInBefore { get; private set; } = string.Empty;

    /// <summary>The lines of RESULT.txt written so far.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>
    /// Makes the folder, the scratch repository (<paramref name="populate"/> writes its files into the main checkout, which is then one commit), the stand-in home, the worktree and the job, and admits the boundary for both network states.
    /// </summary>
    /// <param name="folder">An absent folder on a real disk.</param>
    /// <param name="populate">Writes the repository's files into the folder it is given. For a clone of this repository the function clones into it instead (<paramref name="cloneOf"/>).</param>
    /// <param name="withNode">Whether the stand-in holds a copy of node.</param>
    /// <param name="cloneOf">A repository to clone (<c>--no-hardlinks</c>) instead of writing files; empty for a scratch repository.</param>
    public static async Task<MeasureBed> CreateAsync(string folder, Func<string, Task> populate, bool withNode, string cloneOf = "")
    {
        Directory.Exists(folder).Should().BeFalse($"{folder} must not exist: the run makes it");
        var bed = new MeasureBed(folder);
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(bed.StandIn);
        if (cloneOf.Length > 0)
        {
            await bed.GitAsync(folder, "clone", "--no-hardlinks", "--quiet", cloneOf, bed.Main);
        }
        else
        {
            Directory.CreateDirectory(bed.Main);
            await bed.GitAsync(bed.Main, "init", "--quiet", "--initial-branch=main");
            await populate(bed.Main);
            await bed.GitAsync(bed.Main, "add", "-A");
            await bed.GitAsync(bed.Main, "-c", "user.name=measure", "-c", "user.email=measure@example.invalid", "commit", "--quiet", "-m", "scratch repository");
        }

        var node = withNode ? await bed.CopyNodeIntoStandInAsync() : string.Empty;
        var head = (await bed.GitAsync(bed.Main, "rev-parse", "HEAD")).StdOut.Trim();
        var started = await new CoderWorktree(bed._launcher, Path.Combine(folder, "c")).StartAsync(bed.Main, "m1", head);
        var path = started.Should().BeOfType<CoderWorktreeOutcome.Started>(started.ToString()).Subject.Path;
        bed.Worktree = path;
        var tree = CoderTree.Open(path, bed.Main).Should().BeOfType<CoderTreeOpening.Opened>().Subject.Tree;
        bed.Host = BoundaryHost.Current() with
        {
            RealHome = bed.StandIn,
            NuGetPackages = Path.Combine(bed.StandIn, ".nuget", "packages"),
            Path = node,
        };
        bed.Job = new BoundaryJob("m1", tree, Path.Combine(folder, "j"), NetworkChoice.DefaultBuild, NetworkChoice.DefaultTest, 8000);
        bed.Boundary = new CheckBoundary(bed._launcher, bed.Host);
        bed.StandInBefore = StandInManifest(bed.StandIn);
        var admission = await bed.Boundary.AdmitAsync(bed.Job, [CheckNetwork.Host, CheckNetwork.Private]);
        bed._admitted = admission.Should().BeOfType<BoundaryAdmission.Admitted>(admission.ToString()).Subject;
        bed.Note($"folder {folder}; worktree {path}; head {head}; stand-in home {bed.StandIn}; node path in the unit {node.Length > 0}");
        bed.Note($"properties (host network): {string.Join(' ', bed.Boundary.PropertiesFor(bed.Job, CheckNetwork.Host, TimeSpan.FromMinutes(20) + UnitControl.Backstop))}");
        bed.Note($"properties (private network): {string.Join(' ', bed.Boundary.PropertiesFor(bed.Job, CheckNetwork.Private, TimeSpan.FromMinutes(20) + UnitControl.Backstop))}");
        foreach (var row in admission is BoundaryAdmission.Admitted a ? a.Records.Values.SelectMany(record => record.Preflight).DistinctBy(row => row.Item) : [])
        {
            bed.Note($"preflight {row.Item}: {(row.Held ? "held" : "FAILED")} — {row.Detail}");
        }

        return bed;
    }

    /// <summary>The files a scratch project writes: relative path to text.</summary>
    public static async Task WriteAsync(string root, IReadOnlyDictionary<string, string> files)
    {
        foreach (var (name, text) in files)
        {
            var path = Path.Combine(root, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, text);
        }
    }

    /// <summary>What one step did.</summary>
    public sealed record Measured(string Id, int ExitCode, bool TimedOut, bool Gone, TimeSpan Time, string Text, string Network);

    /// <summary>
    /// Runs one command inside a unit of the job's own boundary, from <paramref name="folder"/> (relative to the worktree; empty is the worktree root), and writes <c>logs/&lt;id&gt;.log</c> and a line of RESULT.txt.
    /// </summary>
    public async Task<Measured> StepAsync(string id, bool host, IReadOnlyList<string> argv, string folder = "", bool node = false, IReadOnlyDictionary<string, string?>? extra = null, TimeSpan? timeout = null)
    {
        var switches = new Dictionary<string, string?>(node ? NodeSwitches : DotnetSwitches);
        foreach (var (name, value) in extra ?? new Dictionary<string, string?>())
        {
            switches[name] = value;
        }

        IReadOnlyList<string> command = folder.Length == 0 ? argv : ["/bin/sh", "-c", "cd \"$1\" && shift && exec \"$@\"", "sh", folder, .. argv];
        var choice = host ? NetworkChoice.OnHost("measure") : NetworkChoice.None("measure");
        var clock = Stopwatch.StartNew();
        var run = await Boundary.RunAsync(_admitted!, Job, choice, id.Replace('.', '-'), command, switches, timeout ?? TimeSpan.FromMinutes(20), 8 * 1024 * 1024);
        clock.Stop();
        var text = run.Result.StdOut + (run.Result.StdErr.Length > 0 ? "\n[stderr]\n" + run.Result.StdErr : string.Empty);
        Directory.CreateDirectory(Logs);
        await File.WriteAllTextAsync(Path.Combine(Logs, id + ".log"), text);
        var measured = new Measured(id, run.Result.ExitCode, run.Result.TimedOut, run.Gone, clock.Elapsed, text, choice.Wire);
        Note($"{id}: exit {measured.ExitCode}, {measured.Time.TotalSeconds:0.0} s, network {measured.Network}, timedOut {measured.TimedOut}, unitGone {measured.Gone}, cwd '{(folder.Length == 0 ? "." : folder)}', argv {string.Join(' ', argv)}");

        return measured;
    }

    /// <summary><c>git status --porcelain --ignored</c> of the worktree, read OUTSIDE the unit (the harness, not a check step).</summary>
    public async Task<string> StatusAsync(bool ignored = true) =>
        (await (ignored ? GitAsync(Worktree, "status", "--porcelain", "--ignored") : GitAsync(Worktree, "status", "--porcelain"))).StdOut;

    public async Task<ProcessResult> GitAsync(string folder, params string[] args)
    {
        var done = await _launcher.RunAsync(new ProcessRequest("git", args, folder) { Timeout = TimeSpan.FromMinutes(10) });
        done.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {done.StdErr}");

        return done;
    }

    /// <summary>Writes a line to RESULT.txt and to stderr.</summary>
    public void Note(string line)
    {
        _lines.Add(line);
        Console.Error.WriteLine(line);
    }

    /// <summary>
    /// A copy of a recorded output for a fixture: the checkout path, the scratch folder, the home and the user name of this machine replaced. The scan of story s2 t6 (and <see cref="LeaksInFixture"/>) is what proves it.
    /// </summary>
    public async Task SaveFixtureAsync(string name, string text)
    {
        Directory.CreateDirectory(Fixtures);
        await File.WriteAllTextAsync(Path.Combine(Fixtures, name), Sanitise(text));
    }

    /// <summary>The names of this machine that must not be in a recording: the user, the host, the home and the scratch folder.</summary>
    public string Sanitise(string text)
    {
        var result = text.Replace(Folder, "/work", StringComparison.Ordinal);
        foreach (var name in new[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName, Environment.MachineName })
        {
            if (name.Length >= 3)
            {
                result = result.Replace(name, name.StartsWith('/') ? "/home/user" : "user", StringComparison.Ordinal);
            }
        }

        return result;
    }

    /// <summary>True for a line that still names the user, the host or the home of this machine.</summary>
    public bool LeaksInFixture(string text) =>
        new[] { Environment.UserName, Environment.MachineName, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) }.Any(name => name.Length >= 3 && text.Contains(name, StringComparison.Ordinal));

    /// <summary>Hashes every file of the stand-in (path, size, time, bytes) and its folders: equal before and after means nothing was written.</summary>
    public static string StandInManifest(string root)
    {
        var lines = new List<string>();
        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            var info = new FileInfo(path);
            var kind = Directory.Exists(path) ? "d" : "f";
            var hash = kind == "f" && info.LinkTarget is null ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))[..16] : "-";
            lines.Add($"{kind} {Path.GetRelativePath(root, path)} {(kind == "f" ? info.Length : 0)} {info.LastWriteTimeUtc.Ticks} {hash}");
        }

        return $"{lines.Count} entries, sha256 {Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))))[..24]}";
    }

    /// <summary>Writes the summary of the stand-in's before and after, and RESULT.txt.</summary>
    public async Task FinishAsync()
    {
        var after = StandInManifest(StandIn);
        Note($"stand-in home before: {StandInBefore}");
        Note($"stand-in home after:  {after} — {(after == StandInBefore ? "the same bytes and times" : "CHANGED")}");
        await File.WriteAllLinesAsync(Path.Combine(Folder, "RESULT.txt"), _lines);
    }

    /// <summary>A directory listing of sizes: <c>du -sk</c> of a folder in KiB, or -1.</summary>
    public async Task<long> SizeKiBAsync(string folder)
    {
        var done = await _launcher.RunAsync(new ProcessRequest("du", ["-sk", folder], Folder) { Timeout = TimeSpan.FromMinutes(2) });

        return done.ExitCode == 0 && long.TryParse(done.StdOut.Split('\t')[0], out var kib) ? kib : -1;
    }

    private async Task<string> CopyNodeIntoStandInAsync()
    {
        var real = Environment.GetEnvironmentVariable("PATH")?.Split(':').FirstOrDefault(entry => entry.Contains("/.nvm/versions/node/", StringComparison.Ordinal) && Directory.Exists(entry));
        real.Should().NotBeNull("node must be on coai's PATH under ~/.nvm to measure the node steps");
        var versionFolder = Path.GetDirectoryName(real!.TrimEnd('/'))!;
        var home = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(versionFolder)!)!)!)!;
        var target = Path.Combine(StandIn, Path.GetRelativePath(home, versionFolder));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var copied = await _launcher.RunAsync(new ProcessRequest("cp", ["-a", versionFolder, target], Folder) { Timeout = TimeSpan.FromMinutes(5) });
        copied.ExitCode.Should().Be(0, copied.StdErr);

        return Path.Combine(target, "bin");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
