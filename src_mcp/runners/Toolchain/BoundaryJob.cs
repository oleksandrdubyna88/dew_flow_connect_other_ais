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

    /// <summary>
    /// The submodule contents bound READ-ONLY into every unit of the job, each at its path in the worktree (epoch 3 E2 s6, the recorded change). Empty for a job with no submodule that could be provided; the property lists of the units are then exactly E2's.
    /// </summary>
    public IReadOnlyList<ReadOnlyMount> Mounts { get; init; } = [];

    /// <summary>
    /// The folder the mounts' sources are made in: a SIBLING of the job folder, never inside it — a unit may bind a folder of the job writable, and a source reachable through it could be changed from inside, read-only bind or not. No unit binds it; it is only a source.
    /// </summary>
    public string MountsFolder => JobRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar) + ".mounts";

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

    /// <summary>
    /// The folder a unit covers with an empty read-only tmpfs before it binds what it needs of the job (S2 of the security verify of the E2 follow-ups): the folder that HOLDS the job folders — the coder-jobs folder — so another job's folders (their coder homes) are not
    /// readable from this job's unit wherever that folder lies; or the job folder itself when the holding folder cannot be covered. It cannot when it is the root of the file system, or holds (or is) the worktree, the main checkout or the coder root — a tmpfs there
    /// would hide what the unit is given at those paths — or is a folder the units bind their own temp folders at.
    /// </summary>
    public string JobCover
    {
        get
        {
            var parent = System.IO.Path.GetDirectoryName(JobRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar)) ?? string.Empty;
            var unsafeToCover = parent.Length == 0 || parent == System.IO.Path.GetPathRoot(parent)
                || new[] { Tree.Root, Tree.MainCheckout, CoderRoot, "/tmp", "/var/tmp" }.Any(held => Inside(parent, held));

            return unsafeToCover ? JobRoot : parent;
        }
    }

    /// <summary>
    /// The sentence that says the job folder is not its own real path, or empty — asked once the folders are made. A job folder spelled through a link (the data folder, the home or <c>COAI_CODER_JOBS</c> behind one) would be covered and bound at its SPELLING, which inside a unit
    /// whose <c>/home</c> is an empty tmpfs is a path the link is not at: the real folder would be left under no cover, read-only, with the coder's home in it.
    /// </summary>
    public string JobRootOnDiskProblem()
    {
        var real = Files.RealPath.Resolve(JobRoot);

        return real.Ok && string.Equals(System.IO.Path.TrimEndingDirectorySeparator(real.Path), System.IO.Path.TrimEndingDirectorySeparator(JobRoot), StringComparison.Ordinal)
            ? string.Empty
            : $"The job folder '{JobRoot}' is not its own real path ({(real.Ok ? real.Path : real.Why)}): a link in its path would put the unit's cover and binds at a path the folder is not at, so the job was refused.";
    }

    /// <summary>The per-job folders a check unit binds at their own path (its <c>HOME</c>, the package and cache folders): every folder of <see cref="Folders"/> but the two temp folders, which are bound at <c>/tmp</c> and <c>/var/tmp</c>. The coder's folder is not one of them.</summary>
    public IReadOnlyList<string> CheckFolders => [HomeFolder, NuGetFolder, NpmFolder, DotnetFolder];

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

    /// <summary>The keys FILE the person named (<c>COAI_CLAUDE_KEYS_FILE</c> or the device setting <c>keysFile</c>), wherever it lies: hidden from a keyed coder's unit as the key folder is (second judgement) — a file outside <c>/home</c> would otherwise be readable there. Empty when none.</summary>
    public string KeysFile { get; init; } = string.Empty;

    /// <summary>The sentence that says the keys folder cannot be hidden from a unit — it is spelled into <c>InaccessiblePaths=</c>, whose value is split at a space, so a path with a space, a colon, a backslash or a control character is refused — or empty.</summary>
    public string KeysFolderProblem() =>
        new[] { KeysFolder, KeysFile }.FirstOrDefault(path => path.Length > 0 && !IsPlain(path)) is { } odd
            ? $"The key path '{odd}' cannot be hidden from a unit: it must be absolute and hold no space, colon, backslash or control character."
            : string.Empty;

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

        return odd is not null
            ? $"The path '{odd}' cannot be given to a unit: it must be absolute and hold no space, colon, backslash or control character."
            : InsideProblem() is { Length: > 0 } inside ? inside : MountsProblem();
    }

    /// <summary>
    /// The sentence that says a mount cannot be given to a unit, or empty — decided on the spelling alone (epoch 3 E2 s6): the source is a folder UNDER <see cref="MountsFolder"/> (never inside the writable worktree, the job folder or the main checkout, where it could be changed
    /// from inside), the target is a path UNDER the worktree and not its <c>.git</c>, both can be spelled into a bind (no space, colon, backslash or control character), and no two mounts share a source or lie one in the other.
    /// </summary>
    private string MountsProblem()
    {
        foreach (var mount in Mounts)
        {
            var odd = new[] { mount.Source, mount.Target }.FirstOrDefault(path => !IsPlain(path));
            if (odd is not null)
            {
                return $"The mount path '{odd}' cannot be given to a unit: it must be absolute and hold no space, colon, backslash or control character.";
            }

            if (!Inside(MountsFolder, mount.Source) || string.Equals(MountsFolder, mount.Source, StringComparison.Ordinal) || new[] { Tree.Root, JobRoot, Tree.MainCheckout }.Any(writable => Inside(writable, mount.Source)))
            {
                return $"The mount source '{mount.Source}' is not a folder under the mounts folder '{MountsFolder}', or lies where a unit could write it: a mount's source must be coai's own folder beside the job's.";
            }

            if (!Inside(Tree.Root, mount.Target) || string.Equals(Tree.Root, mount.Target, StringComparison.Ordinal) || Inside(Tree.GitEntry, mount.Target))
            {
                return $"The mount target '{mount.Target}' is not a path under the worktree '{Tree.Root}' (or it is the .git entry): a mount may only be bound onto a folder of the worktree.";
            }
        }

        for (var first = 0; first < Mounts.Count; first++)
        {
            for (var second = first + 1; second < Mounts.Count; second++)
            {
                if (Inside(Mounts[first].Target, Mounts[second].Target) || Inside(Mounts[second].Target, Mounts[first].Target) || string.Equals(Mounts[first].Source, Mounts[second].Source, StringComparison.Ordinal))
                {
                    return $"The mounts at '{Mounts[first].Target}' and '{Mounts[second].Target}' share a source or lie one in the other, so they were not given to a unit.";
                }
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// What only the disk can say about the mounts, asked at admission: every folder from the worktree down to a target, and the mounts folder and each source, is a real folder and not a link — a link in the path of a bind would carry the bind
    /// somewhere else (a target that is a link into <c>/etc</c> is the case). Empty when there is no mount or all hold. The preflight then asks the OS whether the bind is there and read-only.
    /// </summary>
    public string MountsOnDiskProblem()
    {
        foreach (var mount in Mounts)
        {
            var below = Path.GetRelativePath(Tree.Root, mount.Target).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            var walked = Tree.Root;
            foreach (var segment in below)
            {
                walked = Path.Combine(walked, segment);
                if (Files.RealPath.Lstat(walked).Kind != Files.EntryKind.Directory)
                {
                    return $"The mount target '{mount.Target}' is not a plain folder of the worktree ('{walked}' is missing, a link or a file), so no mount was made onto it.";
                }
            }

            if (Files.RealPath.Lstat(MountsFolder).Kind != Files.EntryKind.Directory || Files.RealPath.Lstat(mount.Source).Kind != Files.EntryKind.Directory)
            {
                return $"The mount source '{mount.Source}' is not a plain folder of coai's mounts folder (it is missing, a link or a file).";
            }
        }

        return string.Empty;
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

/// <summary>A folder of coai's own bound READ-ONLY at <paramref name="Target"/> (a path in the worktree) inside a unit.</summary>
public sealed record ReadOnlyMount(string Source, string Target);
