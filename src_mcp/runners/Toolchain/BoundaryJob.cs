using CoaiMcp.Runners.Worktrees;

namespace CoaiMcp.Runners.Toolchain;

/// <summary>Whether a step has a network of its own — a namespace with a loopback device only — or the host's.</summary>
public enum CheckNetwork
{
    /// <summary><c>PrivateNetwork=yes</c>: only <c>lo</c>.</summary>
    Private,

    /// <summary>The host's network; the step's record says why.</summary>
    Host,
}

/// <summary>The network state of a step and the reason for it — recorded in every step's result, never implied.</summary>
public sealed record NetworkChoice(CheckNetwork Mode, string Reason)
{
    /// <summary>A private network: no interface but the loopback one.</summary>
    public static NetworkChoice None(string reason) => new(CheckNetwork.Private, reason);

    /// <summary>The host's network, with the measured reason.</summary>
    public static NetworkChoice OnHost(string reason) => new(CheckNetwork.Host, reason);

    /// <summary>The word in a result: <c>none</c> or <c>host</c>.</summary>
    public string Wire => Mode == CheckNetwork.Private ? "none" : "host";

    /// <summary>
    /// The build step's measured default: the HOST network. This repository's build restores packages and NuGet audits them online; <c>TreatWarningsAsErrors</c> makes an unreachable audit service (<c>NU1900</c>) an error, and
    /// with only a loopback device the build failed in 8 s with 30 of them. A step with the host network still has no credential to send and no folder to write but its own (<c>RESULTS_s2_check_boundary.md</c>).
    /// </summary>
    public static NetworkChoice DefaultBuild { get; } = OnHost("the build restores and audits NuGet packages online (NU1900 is an error here); with only a loopback device it failed in 8 s — measured in RESULTS_s2_check_boundary.md");

    /// <summary>
    /// The test step's measured default: a PRIVATE network. This repository's whole suite (15,730 tests) passed with only a loopback device (<c>RESULTS_s2_check_boundary.md</c>), so a test run, which executes the coder's code, gets no network.
    /// A repository whose suite needs one declares it on its own job and the result says so.
    /// </summary>
    public static NetworkChoice DefaultTest { get; } = None("the whole suite of this repository passed with only a loopback device — measured in RESULTS_s2_check_boundary.md");
}

/// <summary>
/// What one coder job's checks need to run inside the boundary: the tree, the job's own writable folder on a real disk, the network state of each step, and the memory ceiling.
/// </summary>
/// <param name="JobId">The coder job id (<see cref="CoderWorktree.IsJobId"/>); the unit names are made from it.</param>
/// <param name="Tree">The worktree (the only writable source folder) and the main checkout (read-only).</param>
/// <param name="JobRoot">The job's own folder, on a real disk under a short path: its HOME, <c>/tmp</c>, <c>/var/tmp</c> and caches. Made by coai before the preflight; the caller removes it when the job is over.</param>
/// <param name="BuildNetwork">The build step's network state.</param>
/// <param name="TestNetwork">The test step's network state.</param>
/// <param name="MemoryMegabytes"><c>MemoryMax</c> of each unit, in MiB. With <c>MemorySwapMax=0</c> it is a kill, not a suggestion.</param>
public sealed record BoundaryJob(
    string JobId,
    CoderTree Tree,
    string JobRoot,
    NetworkChoice BuildNetwork,
    NetworkChoice TestNetwork,
    int MemoryMegabytes = 6000)
{
    /// <summary>A coder job with the measured network defaults (<see cref="NetworkChoice.DefaultBuild"/>, <see cref="NetworkChoice.DefaultTest"/>) and the default memory ceiling.</summary>
    public static BoundaryJob ForCoder(string jobId, CoderTree tree, string jobRoot) =>
        new(jobId, tree, jobRoot, NetworkChoice.DefaultBuild, NetworkChoice.DefaultTest);

    /// <summary>The job's <c>HOME</c> (and <c>XDG_*</c> by default): never the real one.</summary>
    public string HomeFolder => Path.Combine(JobRoot, "home");

    /// <summary>What the unit sees as <c>/tmp</c>.</summary>
    public string TempFolder => Path.Combine(JobRoot, "tmp");

    /// <summary>What the unit sees as <c>/var/tmp</c>.</summary>
    public string VarTempFolder => Path.Combine(JobRoot, "vartmp");

    /// <summary>The writable NuGet package folder (<c>NUGET_PACKAGES</c>).</summary>
    public string NuGetFolder => Path.Combine(JobRoot, "nuget-packages");

    /// <summary>The npm cache folder (<c>npm_config_cache</c>).</summary>
    public string NpmFolder => Path.Combine(JobRoot, "npm-cache");

    /// <summary>The folder of <c>DOTNET_CLI_HOME</c>.</summary>
    public string DotnetFolder => Path.Combine(JobRoot, "dotnet-home");

    /// <summary>Every per-job folder coai makes, in the order they are made.</summary>
    public IReadOnlyList<string> Folders => [HomeFolder, TempFolder, VarTempFolder, NuGetFolder, NpmFolder, DotnetFolder];

    /// <summary>
    /// Where the CODER's own process lives: a folder beside the checks' folders, never the same one. What the claude CLI writes (its config, its transcripts, its caches) must not be in the HOME the checks read, or a coder that writes a
    /// <c>.npmrc</c>, a <c>.gitconfig</c> or a NuGet source there would steer the build that runs after it.
    /// </summary>
    public string CoderFolder => Path.Combine(JobRoot, "coder");

    /// <summary>The coder's <c>HOME</c>: the job's own, with the sign-in file bound into it read-only — never the real one.</summary>
    public string CoderHomeFolder => Path.Combine(CoderFolder, "home");

    /// <summary>What the coder's unit sees as <c>/tmp</c>.</summary>
    public string CoderTempFolder => Path.Combine(CoderFolder, "tmp");

    /// <summary>What the coder's unit sees as <c>/var/tmp</c>.</summary>
    public string CoderVarTempFolder => Path.Combine(CoderFolder, "vartmp");

    /// <summary>Where the sign-in file is bound inside the coder's home.</summary>
    public string CoderSignInFile => Path.Combine(CoderHomeFolder, ".claude", ".credentials.json");

    /// <summary>
    /// The folder of the claude key pool of this device (<c>&lt;data&gt;/keys</c>), when the job runs on it (epoch 3 E2 s7): a coder launch with a pool key gets <c>InaccessiblePaths=</c> of it, and its preflight proves a read of it fails from inside the unit. Empty
    /// when there is none to hide (a job started without a data folder, in a test of the boundary alone).
    /// </summary>
    public string KeysFolder { get; init; } = string.Empty;

    /// <summary>The sentence that says the keys folder cannot be hidden from a unit — it is spelled into <c>InaccessiblePaths=</c>, whose value is split at a space, so a path with a space, a colon, a backslash or a control character is refused — or empty.</summary>
    public string KeysFolderProblem() =>
        KeysFolder.Length == 0 || IsPlain(KeysFolder)
            ? string.Empty
            : $"The key folder '{KeysFolder}' cannot be hidden from a unit: it must be absolute and hold no space, colon, backslash or control character.";

    /// <summary>Every per-job folder the coder's unit needs, in the order they are made.</summary>
    public IReadOnlyList<string> CoderFolders => [CoderHomeFolder, Path.Combine(CoderHomeFolder, ".claude"), CoderTempFolder, CoderVarTempFolder];

    /// <summary>
    /// The folder that holds the worktree, and every other coder worktree beside it: read-only to the coder, with the one worktree of the job bound writable on top. A coder that writes BESIDE its worktree writes into another job's.
    /// </summary>
    public string CoderRoot => Path.GetDirectoryName(Tree.Root) ?? string.Empty;

    /// <summary>The network state of a step.</summary>
    public NetworkChoice NetworkOf(Core.Toolchain.CheckStep step) => step == Core.Toolchain.CheckStep.Build ? BuildNetwork : TestNetwork;

    /// <summary>
    /// The sentence that says why this job cannot be bounded, or empty. The paths go into <c>BindPaths=</c>, whose value is split at a space and at a colon, so a path with either, a backslash or a control character is refused
    /// BEFORE a unit is described with it — not quoted, because there is no quote that systemd reads in that value.
    /// </summary>
    public string Problem()
    {
        if (!CoderWorktree.IsJobId(JobId))
        {
            return "The job id is not usable for a unit name: 1 to 40 characters of a-z, 0-9 and '-', starting with a letter or a digit.";
        }

        var odd = new[] { Tree.Root, Tree.MainCheckout, JobRoot }.FirstOrDefault(path => !IsPlain(path));

        return odd is null ? InsideProblem() : $"The path '{odd}' cannot be given to a unit: it must be absolute and hold no space, colon, backslash or control character.";
    }

    /// <summary>
    /// <see cref="Problem"/>, and what only a coder's unit needs: a coder root that can be given to a unit and is not the file system's root (a worktree directly under <c>/</c> would make the whole machine read-only).
    /// </summary>
    public string CoderProblem() =>
        Problem() is { Length: > 0 } problem
            ? problem
            : !IsPlain(CoderRoot) || CoderRoot == Path.GetPathRoot(CoderRoot)
                ? $"The coder root '{CoderRoot}' cannot be given to a unit: it must be an absolute folder that is not the root of the file system and holds no space, colon, backslash or control character."
                : string.Empty;

    private string InsideProblem()
    {
        var jobInTree = Inside(Tree.Root, JobRoot) || Inside(JobRoot, Tree.Root);
        var treeInMain = Inside(Tree.MainCheckout, Tree.Root) || Inside(Tree.Root, Tree.MainCheckout);
        var jobInMain = Inside(Tree.MainCheckout, JobRoot);

        return jobInTree || treeInMain || jobInMain
            ? "The worktree, the main checkout and the job folder must be three separate folders: a folder inside another would make the read-only one writable or the writable one read-only."
            : string.Empty;
    }

    private static bool Inside(string parent, string child) => Files.RealPath.IsInside(parent, child, StringComparison.Ordinal);

    private static bool IsPlain(string path) =>
        Path.IsPathFullyQualified(path) && !path.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is ':' or '\\');
}
