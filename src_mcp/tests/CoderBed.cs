using System.Security.Cryptography;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;

namespace CoaiMcp.Tests;

/// <summary>
/// A <see cref="BoundaryBed"/> with a stand-in for the person's real home, for the suites of the coder's OS layer (epoch 3 E2 s2 t5). The stand-in holds a sign-in file (<c>.claude/.credentials.json</c>, a fixed made-up line) and a few files a real home
/// has, so the layer's one bind from it can be seen working and everything else of it can be seen closed. It is a temp folder: nothing real is ever named, and the real home is never read or written.
/// </summary>
/// <remarks>
/// The program of a launch is the fake CLI (<see cref="FakeCliInvocations.Exe"/>) or a system program; a real claude is never started by a test.
/// </remarks>
internal sealed class CoderBed : IAsyncDisposable
{
    /// <summary>The line the stand-in's sign-in file holds; a test looks for it inside the unit, and never in a log.</summary>
    internal const string SignInLine = "made-up-sign-in-line-not-a-credential";

    private readonly TempDir _realHome;

    private CoderBed(TempDir realHome, BoundaryBed inner)
    {
        _realHome = realHome;
        Inner = inner;
        Host = inner.Host with { RealHome = _realHome.Path };
        Evidence = new FilesystemInvariant(inner.Launcher);
    }

    public BoundaryBed Inner { get; }

    /// <summary>The host as the layer sees it: the stand-in as the real home, no PATH to look programs up in.</summary>
    public BoundaryHost Host { get; }

    public FilesystemInvariant Evidence { get; }

    public BoundaryJob Job => Inner.Job;

    public string Main => Inner.Main;

    public string Worktree => Inner.Worktree;

    public string JobId => Inner.JobId;

    public WatchedLauncher Launcher => Inner.Launcher;

    /// <summary>The stand-in home's folder.</summary>
    public string RealHome => _realHome.Path;

    /// <summary>The stand-in's sign-in file.</summary>
    public string SignIn => Path.Combine(_realHome.Path, ".claude", ".credentials.json");

    /// <summary>The folder that holds the worktree: the coder root.</summary>
    public string CoderRoot => Job.CoderRoot;

    /// <summary>
    /// The bed. <paramref name="toolchainOf"/> makes the toolchain file the base commit holds from the stand-in's path — a fixture check that names it must be written before the commit that holds it.
    /// </summary>
    public static async Task<CoderBed> CreateAsync(Func<string, string>? toolchainOf = null)
    {
        var home = TempDir.For("cb5-home-");
        Directory.CreateDirectory(Path.Combine(home.Path, ".claude"));
        await File.WriteAllTextAsync(Path.Combine(home.Path, ".claude", ".credentials.json"), SignInLine + "\n");
        await File.WriteAllTextAsync(Path.Combine(home.Path, ".claude.json"), "{}\n");
        await File.WriteAllTextAsync(Path.Combine(home.Path, ".bashrc"), "# a file of the stand-in's home\n");

        return new CoderBed(home, await BoundaryBed.CreateAsync(toolchainOf?.Invoke(home.Path) ?? string.Empty));
    }

    public CoderBoundary Boundary(Func<IReadOnlyList<string>, IReadOnlyList<string>>? degrade = null, BoundaryHost? host = null) =>
        new(Launcher, host ?? Host) { Degrade = degrade };

    public CoderTurns Turns(CoderBoundary? boundary = null) => new(boundary ?? Boundary(), Evidence);

    /// <summary>A launch of <paramref name="program"/> as a coder would be launched: in the worktree, with a minute to live.</summary>
    public ProcessRequest Request(string program, params string[] arguments) =>
        new(program, arguments, Worktree) { Timeout = TimeSpan.FromSeconds(60), InheritsEnvironment = false, Passthrough = ProcessEnvironment.Minimal };

    /// <summary>A launch of the fake CLI.</summary>
    public ProcessRequest Fake(params string[] arguments) => Request(FakeCliInvocations.Exe, arguments);

    /// <summary>A launch of one <c>sh -c</c> script, the way the unit tests of the checks probe their unit.</summary>
    public ProcessRequest Shell(string script) => Request("/bin/sh", "-c", script);

    /// <summary>
    /// What a person looking at the main checkout, its whole <c>.git</c> included, would call its bytes: every file under it by relative path, hashed. <c>git status</c> is run once first so the index's stat cache is already as git will leave it —
    /// the snapshot is of the checkout and not of one refresh.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> MainBytesAsync()
    {
        await Launcher.RunAsync(new ProcessRequest("git", ["status", "--porcelain"], Main));
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Main, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            seen[Path.GetRelativePath(Main, file)] = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(file)));
        }

        return seen;
    }

    public ValueTask DisposeAsync()
    {
        _realHome.Dispose();

        return Inner.DisposeAsync();
    }
}
