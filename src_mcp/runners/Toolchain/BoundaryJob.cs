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
